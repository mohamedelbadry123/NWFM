import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { AuthStore } from '@core/auth/auth.store';
import { STEP_DRAFT_PREFIX, clearSessionDrafts } from '@core/auth/session-drafts';
import { AppContextService } from '@core/context/app-context.service';
import {
  StepMappingMemory,
  preparingReason,
  resolveStepMapping,
  withPrefill,
  type StepDraftScope,
} from './workflow-step-mapping';
import type { StepMappingSnapshot, StepMappingValue } from './mapping.models';

const pins = { formId: 'form-1', versionNo: 2, visitId: 'visit-1', stepId: 'step-1' };
const value = (
  fieldKey: string,
  mode: StepMappingValue['mode'],
  v: unknown,
  over: Partial<StepMappingValue> = {},
): StepMappingValue => ({
  bindingId: 'b-' + fieldKey,
  formStepId: 'step-1',
  formId: 'form-1',
  formVersion: 2,
  fieldKey,
  mode,
  value: v,
  ...over,
});
const snapshot = (values: StepMappingValue[], status = 'Completed'): StepMappingSnapshot => ({
  invocationId: 'inv-1',
  visitId: 'visit-1',
  status,
  values,
});

describe('resolveStepMapping', () => {
  it('has nothing to apply without a snapshot', () => {
    expect(resolveStepMapping('w', pins, null).state).toBe('none');
    expect(resolveStepMapping('w', pins, undefined).applyKey).toBe('');
  });

  it('waits until the snapshot is Completed and exposes the one-time key', () => {
    const waiting = resolveStepMapping('w', pins, snapshot([value('a', 'Prefill', 1)], 'Waiting'));
    expect(waiting.state).toBe('waiting');
    expect(waiting.prefill).toEqual({});
    expect(waiting.applyKey).toBe('w|visit-1|inv-1');
  });

  it('splits ReadOnly and Prefill values and keeps exact typed JSON, null included', () => {
    const result = resolveStepMapping(
      'w',
      pins,
      snapshot([
        value('a', 'ReadOnly', null),
        value('b', 'ReadOnly', [1, { x: true }]),
        value('c', 'Prefill', 0),
      ]),
    );
    expect(result.state).toBe('ready');
    expect(result.locked).toEqual({ a: null, b: [1, { x: true }] });
    expect(result.prefill).toEqual({ c: 0 });
  });

  it('is invalid, applying nothing, for another form, version, step, visit, duplicate key or unknown mode', () => {
    const bad = (v: StepMappingValue[], snap: Partial<StepMappingSnapshot> = {}) =>
      resolveStepMapping('w', pins, { ...snapshot(v), ...snap });
    for (const result of [
      bad([value('a', 'Prefill', 1, { formId: 'other' })]),
      bad([value('a', 'Prefill', 1, { formVersion: 1 })]),
      bad([value('a', 'Prefill', 1, { formStepId: 'other' })]),
      bad([value('a', 'Prefill', 1)], { visitId: 'visit-2' }),
      bad([value('A ', 'Prefill', 1), value('a', 'ReadOnly', 2)]),
      bad([value('a', 'Other' as never, 1)]),
    ]) {
      expect(result.state).toBe('invalid');
      expect(result.problems.length).toBeGreaterThan(0);
      expect(result.locked).toEqual({});
      expect(result.prefill).toEqual({});
    }
  });

  it('treats dotted field keys as one literal key', () => {
    const result = resolveStepMapping(
      'w',
      pins,
      snapshot([value('customer.id', 'ReadOnly', 'C-1')]),
    );
    expect(result.locked).toEqual({ 'customer.id': 'C-1' });
  });
});

describe('withPrefill', () => {
  it('never replaces a key the person already answered, whatever its spelling', () => {
    expect(withPrefill({ Notes: 'mine' }, { notes: 'mapped', other: 'x' })).toEqual({
      Notes: 'mine',
      other: 'x',
    });
  });
  it('prefills an explicit null and does not mutate its input', () => {
    const answers = {};
    expect(withPrefill(answers, { a: null })).toEqual({ a: null });
    expect(answers).toEqual({});
  });
});

describe('StepMappingMemory', () => {
  const memoryOf = () => TestBed.inject(StepMappingMemory);
  beforeEach(() => TestBed.configureTestingModule({}));
  it('remembers per key and forgets one work item at a time', () => {
    const memory = memoryOf();
    memory.remember('w1|v|i', { a: 1 });
    memory.remember('w2|v|i', { a: 2 });
    memory.remember('', { a: 3 });
    expect(memory.isApplied('w1|v|i')).toBe(true);
    expect(memory.recall('')).toBeUndefined();
    memory.forget('w1');
    expect(memory.recall('w1|v|i')).toBeUndefined();
    expect(memory.recall('w2|v|i')).toEqual({ a: 2 });
  });
});

describe('preparingReason', () => {
  const t = (en: string, ar: string) => (document.dir === 'rtl' ? ar : en);
  it('explains attention, a waiting deadline and plain preparation differently', () => {
    expect(preparingReason(t, { status: 'Attention', error: 'No match' })).toContain('No match');
    expect(preparingReason(t, { status: 'Waiting', deadline: '2026-01-01T00:00:00Z' })).toContain(
      'Waiting for data',
    );
    expect(preparingReason(t, null)).toContain('Preparing');
  });
});

describe('StepMappingMemory persistent drafts', () => {
  const KEY = 'work-1|visit-1|inv-1';
  let who = { user: 'user-1', tenant: 'tenant-1', participant: 'p-1', signedIn: true };
  const scope = (over: Partial<StepDraftScope> = {}): StepDraftScope => ({
    formId: 'FORM-1',
    versionNo: 2,
    stepId: 'step-1',
    draftKey: 'attempt-1',
    ...over,
  });
  const stored = () =>
    Object.keys(sessionStorage).filter((name) => name.startsWith(STEP_DRAFT_PREFIX));

  /** A new injector is a new page load: the memory is empty, `sessionStorage` is what the tab kept. */
  const page = () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: AuthStore,
          useValue: {
            user: () => (who.user ? { userId: who.user } : null),
            isAuthenticated: () => who.signedIn,
          },
        },
        {
          provide: AppContextService,
          useValue: {
            tenant: () => (who.tenant ? { id: who.tenant } : undefined),
            selectedParticipantId: () => who.participant,
          },
        },
      ],
    });
    return TestBed.inject(StepMappingMemory);
  };

  beforeEach(() => {
    sessionStorage.clear();
    who = { user: 'user-1', tenant: 'tenant-1', participant: 'p-1', signedIn: true };
  });
  afterEach(() => {
    sessionStorage.clear();
    vi.restoreAllMocks();
  });

  it('restores the edited answers after a hard reload', () => {
    page().remember(KEY, { approved: 'edited-prefill' }, scope(), true);
    expect(page().recall(KEY, scope())).toEqual({ approved: 'edited-prefill' });
  });

  it('persists only the edits a person made, never the initial prefill', () => {
    page().remember(KEY, { approved: 'initial-prefill' }, scope());
    expect(stored()).toEqual([]);
    expect(page().recall(KEY, scope())).toBeUndefined();
  });

  it('keeps null, false, zero, arrays and objects exactly as typed', () => {
    const typed = { a: null, b: false, c: 0, d: '', e: [1, null, { x: true }], f: { g: [] } };
    page().remember(KEY, typed, scope(), true);
    const restored = page().recall(KEY, scope());
    expect(restored).toEqual(typed);
    expect(Object.keys(restored!)).toEqual(Object.keys(typed));
  });

  it('never stores or restores a ReadOnly key, whatever its spelling, and the cache cannot lock', () => {
    const locked = { Notes: null };
    page().remember(KEY, { notes: 'mine', approved: 'x', other: 1 }, scope({ locked }), true);
    const [name] = stored();
    expect(sessionStorage.getItem(name)).not.toContain('notes');
    const tampered = JSON.parse(sessionStorage.getItem(name)!);
    tampered.answers.NOTES = 'forged';
    sessionStorage.setItem(name, JSON.stringify(tampered));
    expect(page().recall(KEY, scope({ locked }))).toEqual({ approved: 'x', other: 1 });
  });

  it('does not share a draft across user, tenant, participant or effective actor', () => {
    page().remember(KEY, { a: 'mine' }, scope({ actorKey: 'demo-1' }), true);
    const other = (
      over: Partial<typeof who>,
      extra: Partial<StepDraftScope> = { actorKey: 'demo-1' },
    ) => {
      who = { ...who, ...over };
      return page().recall(KEY, scope(extra));
    };
    expect(other({ user: 'user-2' })).toBeUndefined();
    expect(other({ user: 'user-1', tenant: 'tenant-2' })).toBeUndefined();
    expect(other({ tenant: 'tenant-1', participant: 'p-2' })).toBeUndefined();
    expect(other({ participant: 'p-1' }, { actorKey: 'demo-2' })).toBeUndefined();
    expect(other({}, {})).toBeUndefined();
    expect(other({})).toEqual({ a: 'mine' });
  });

  describe('when the identity changes inside one service instance (no page rebuild)', () => {
    const verified = { user: 'user-1', tenant: 'tenant-1', participant: 'p-1', signedIn: true };

    it.each([
      ['unknown user (e.g. right after a token refresh)', { user: '' }],
      ['unknown tenant', { tenant: '' }],
      ['signed out', { signedIn: false }],
      ['another user', { user: 'user-2' }],
      ['another tenant', { tenant: 'tenant-2' }],
      ['another participant', { participant: 'p-2' }],
    ])(
      'does not serve a verified owner draft to %s, and gives it back to that owner',
      (_n, over) => {
        const memory = page();
        memory.remember(KEY, { a: 'mine' }, scope(), true);
        expect(memory.recall(KEY, scope())).toEqual({ a: 'mine' });

        who = { ...verified, ...over };
        expect(memory.recall(KEY, scope())).toBeUndefined();

        who = { ...verified };
        // The other identity never overwrote the owner's tab draft.
        expect(memory.recall(KEY, scope())).toEqual({ a: 'mine' });
      },
    );

    it('does not let an identity typing into the same activation leak into or erase the owner draft', () => {
      const memory = page();
      memory.remember(KEY, { a: 'mine' }, scope(), true);
      who = { ...verified, user: 'user-2' };
      memory.remember(KEY, { a: 'theirs' }, scope(), true);
      expect(memory.recall(KEY, scope())).toEqual({ a: 'theirs' });
      who = { ...verified };
      expect(memory.recall(KEY, scope())).toEqual({ a: 'mine' });
    });

    it('does not hand an unverified in-memory draft to a verified identity', () => {
      who = { ...verified, user: '' };
      const memory = page();
      memory.remember(KEY, { a: 'anonymous' }, scope(), true);
      expect(memory.recall(KEY, scope())).toEqual({ a: 'anonymous' });
      who = { ...verified };
      expect(memory.recall(KEY, scope())).toBeUndefined();
    });

    it('never caches a ReadOnly value as an editable one across the transition', () => {
      const locked = { notes: null };
      const memory = page();
      memory.remember(KEY, { notes: 'typed', a: 'mine' }, scope({ locked }), true);
      who = { ...verified, user: '' };
      expect(memory.recall(KEY, scope({ locked }))).toBeUndefined();
      who = { ...verified };
      expect(memory.recall(KEY, scope({ locked }))).toEqual({ a: 'mine' });
      expect(stored().every((n) => !sessionStorage.getItem(n)!.includes('notes'))).toBe(true);
    });
  });

  it('keeps nothing in storage while the tenant or user is unverified (e.g. after a token refresh)', () => {
    for (const unverified of [{ user: '' }, { tenant: '' }, { signedIn: false }]) {
      who = {
        user: 'user-1',
        tenant: 'tenant-1',
        participant: 'p-1',
        signedIn: true,
        ...unverified,
      };
      const memory = page();
      memory.remember(KEY, { a: 'edit' }, scope(), true);
      expect(stored()).toEqual([]);
      // In-memory editing is unaffected.
      expect(memory.recall(KEY, scope())).toEqual({ a: 'edit' });
    }
  });

  it('drops a draft saved for another form, version or Step', () => {
    page().remember(KEY, { a: 1 }, scope(), true);
    for (const other of [{ formId: 'FORM-2' }, { versionNo: 3 }, { stepId: 'step-2' }]) {
      expect(page().recall(KEY, scope(other))).toBeUndefined();
      expect(stored()).toEqual([]);
      page().remember(KEY, { a: 1 }, scope(), true);
    }
    // The form id compares case-insensitively, like the mapping contract.
    expect(page().recall(KEY, scope({ formId: 'form-1' }))).toEqual({ a: 1 });
  });

  it('survives claiming within the same attempt, including a reload, and ends when the attempt changes', () => {
    // The draft identity is stable across pending -> claimed, so one key covers both.
    page().remember(KEY, { a: 'pending' }, scope(), true);
    expect(page().recall(KEY, scope())).toEqual({ a: 'pending' });
    page().remember(KEY, { a: 'claimed' }, scope(), true);
    expect(page().recall(KEY, scope())).toEqual({ a: 'claimed' });
    // Release / reassign / delegate-to-another keeps the visit but rotates the key.
    expect(page().recall(KEY, scope({ draftKey: 'attempt-2' }))).toBeUndefined();
    expect(stored()).toEqual([]);
  });

  it('cannot restore a pending draft after the attempt rotated while the view was absent', () => {
    page().remember(KEY, { a: 'pending edit' }, scope({ draftKey: 'attempt-1' }), true);
    // Claimed and released from another tab: pending again, but under a new attempt key.
    expect(page().recall(KEY, scope({ draftKey: 'attempt-2' }))).toBeUndefined();
    expect(stored()).toEqual([]);
    // The earlier attempt's draft is gone for good, even if its key were presented again.
    expect(page().recall(KEY, scope({ draftKey: 'attempt-1' }))).toBeUndefined();
  });

  it('does not let the same page recall an earlier attempt after the key rotated', () => {
    const memory = page();
    memory.remember(KEY, { a: 'earlier' }, scope({ draftKey: 'attempt-1' }), true);
    expect(memory.recall(KEY, scope({ draftKey: 'attempt-2' }))).toBeUndefined();
    expect(memory.recall(KEY, scope({ draftKey: 'attempt-1' }))).toBeUndefined();
  });

  it('fails closed without a draft key: nothing is persisted or restored, and null is not continuity', () => {
    for (const missing of [null, undefined, '']) {
      const memory = page();
      memory.remember(KEY, { a: 'legacy' }, scope({ draftKey: missing }), true);
      expect(stored()).toEqual([]);
      expect(memory.recall(KEY, scope({ draftKey: missing }))).toBeUndefined();
      expect(page().recall(KEY, scope({ draftKey: missing }))).toBeUndefined();
    }
    // A stored draft is never handed to a response that lost the key, and that view does not erase it.
    page().remember(KEY, { a: 'kept' }, scope(), true);
    expect(page().recall(KEY, scope({ draftKey: null }))).toBeUndefined();
    expect(page().recall(KEY, scope())).toEqual({ a: 'kept' });
  });

  it('keeps attempt keys isolated per work item', () => {
    const memory = page();
    memory.remember('work-1|visit-1|inv-1', { a: 'one' }, scope({ draftKey: 'attempt-1' }), true);
    memory.remember('work-2|visit-1|inv-1', { a: 'two' }, scope({ draftKey: 'attempt-9' }), true);
    const next = page();
    expect(next.recall('work-2|visit-1|inv-1', scope({ draftKey: 'attempt-1' }))).toBeUndefined();
    expect(next.recall('work-1|visit-1|inv-1', scope({ draftKey: 'attempt-1' }))).toEqual({
      a: 'one',
    });
  });

  it('discards the previous visit and invocation drafts when a newer one is edited', () => {
    const memory = page();
    memory.remember('work-1|visit-1|inv-1', { a: 'old visit' }, scope(), true);
    memory.remember('work-2|visit-1|inv-1', { a: 'other item' }, scope(), true);
    memory.remember('work-1|visit-2|inv-1', { a: 'new visit' }, scope(), true);
    const next = page();
    expect(next.recall('work-1|visit-1|inv-1', scope())).toBeUndefined();
    expect(next.recall('work-1|visit-2|inv-1', scope())).toEqual({ a: 'new visit' });
    expect(next.recall('work-2|visit-1|inv-1', scope())).toEqual({ a: 'other item' });
  });

  it('forgets a submitted work item from memory and from the tab', () => {
    const memory = page();
    memory.remember(KEY, { a: 1 }, scope(), true);
    memory.remember('work-2|visit-1|inv-1', { a: 2 }, scope(), true);
    memory.forget('work-1');
    expect(memory.recall(KEY, scope())).toBeUndefined();
    expect(page().recall(KEY, scope())).toBeUndefined();
    expect(page().recall('work-2|visit-1|inv-1', scope())).toEqual({ a: 2 });
  });

  it('ignores a corrupt or foreign envelope without throwing', () => {
    page().remember(KEY, { a: 1 }, scope(), true);
    const [name] = stored();
    for (const raw of [
      '{broken',
      '"text"',
      '{"v":2}',
      '{"v":1,"form":"x","receipt":null,"answers":[]}',
      '{"v":2,"form":"form-1|2|step-1","draftKey":"attempt-1","answers":[]}',
      '{"v":2,"form":"form-1|2|step-1","draftKey":"attempt-1"}',
      '{"v":2,"form":"form-1|2|step-1","draftKey":"attempt-2","answers":{"a":1}}',
    ]) {
      sessionStorage.setItem(name, raw);
      expect(page().recall(KEY, scope())).toBeUndefined();
    }
  });

  it('keeps the in-memory edits when storage refuses the write', () => {
    const memory = page();
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('full', 'QuotaExceededError');
    });
    expect(() => memory.remember(KEY, { a: 'typed' }, scope(), true)).not.toThrow();
    expect(memory.recall(KEY, scope())).toEqual({ a: 'typed' });
  });

  it('keeps an oversized draft in memory only', () => {
    const memory = page();
    memory.remember(KEY, { blob: 'x'.repeat(300 * 1024) }, scope(), true);
    expect(stored()).toEqual([]);
    expect(memory.recall(KEY, scope())!['blob']).toHaveLength(300 * 1024);
  });

  it('is emptied by logout in memory and in the tab', () => {
    const memory = page();
    memory.remember(KEY, { a: 1 }, scope(), true);
    clearSessionDrafts();
    expect(stored()).toEqual([]);
    expect(memory.recall(KEY, scope())).toBeUndefined();
  });

  it('AuthStore.clearSession is that logout, and a refreshed session (no user id) persists nothing', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: AppContextService,
          useValue: { tenant: signal({ id: 'tenant-1' }), selectedParticipantId: () => '' },
        },
      ],
    });
    // `tenant` above is a signal so the fake matches the real computed's call shape.
    const auth = TestBed.inject(AuthStore);
    const memory = TestBed.inject(StepMappingMemory);
    auth.setSession({
      isSuccess: true,
      value: {
        accessToken: 'a',
        refreshToken: 'r',
        expiresInSeconds: 60,
        userName: 'u',
        roles: [],
        permissions: [],
      },
      error: { code: '', message: '' },
    });
    memory.remember(KEY, { a: 1 }, scope(), true);
    expect(stored()).toEqual([]);
    auth.updateProfile({
      userId: 'user-1',
      userName: 'u',
      email: null,
      roles: [],
      permissions: [],
      teamId: null,
      isUnrestrictedScope: false,
    });
    memory.remember(KEY, { a: 2 }, scope(), true);
    expect(stored()).toHaveLength(1);
    auth.clearSession();
    expect(stored()).toEqual([]);
    expect(memory.recall(KEY, scope())).toBeUndefined();
    localStorage.clear();
  });
});
