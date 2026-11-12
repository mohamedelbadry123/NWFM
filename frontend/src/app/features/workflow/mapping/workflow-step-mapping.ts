import { DestroyRef, Injectable, Injector, inject } from '@angular/core';
import { AuthStore } from '@core/auth/auth.store';
import { STEP_DRAFT_PREFIX, onSessionDraftsCleared } from '@core/auth/session-drafts';
import { AppContextService } from '@core/context/app-context.service';
import { normalizeFieldKey } from '@shared/components/dynamic-form/locked-values';
import type { StepMappingSnapshot } from './mapping.models';

/** The pins of the form a work item currently shows (`WorkItemDto.selectedForm`). */
export interface StepMappingFormPins {
  formId: string;
  versionNo: number;
  visitId?: string;
  stepId?: string;
}

/**
 * - `none`: no mapping applies to this work item.
 * - `waiting`: the server is still preparing (or the snapshot is not completed); nothing may be applied yet.
 * - `ready`: a completed snapshot whose every destination matches this exact form, version, Step and visit.
 * - `invalid`: a completed snapshot that does not match — apply nothing and refuse to proceed.
 */
export type StepMappingState = 'none' | 'waiting' | 'ready' | 'invalid';

export interface ResolvedStepMapping {
  state: StepMappingState;
  /** `workItemId|visitId|invocationId`; one application per key. Empty unless a snapshot exists. */
  applyKey: string;
  /** `fieldKey` → exact typed JSON (null, boolean, arrays and objects included) the server compares. */
  locked: Record<string, unknown>;
  /** Editable initial values, applied once per key. */
  prefill: Record<string, unknown>;
  problems: string[];
}

const same = (a: string | null | undefined, b: string | null | undefined) =>
  (a ?? '').toLowerCase() === (b ?? '').toLowerCase();

/**
 * Decides what a mapping snapshot means for the form being shown. Pure: it never calls the server and never
 * touches answers. Every destination is validated against the exact form id, version, Step and visit.
 */
export function resolveStepMapping(
  workItemId: string,
  form: StepMappingFormPins,
  snapshot: StepMappingSnapshot | null | undefined,
): ResolvedStepMapping {
  const empty = (state: StepMappingState, problems: string[] = []): ResolvedStepMapping => ({
    state,
    applyKey: '',
    locked: {},
    prefill: {},
    problems,
  });
  if (!snapshot) return empty('none');
  const applyKey = `${workItemId}|${snapshot.visitId}|${snapshot.invocationId}`;
  if (snapshot.status !== 'Completed') return { ...empty('waiting'), applyKey };
  const problems: string[] = [];
  if (form.visitId && !same(form.visitId, snapshot.visitId))
    problems.push('The mapped values belong to a different visit of this Step.');
  const locked: Record<string, unknown> = {};
  const prefill: Record<string, unknown> = {};
  const seen = new Set<string>();
  for (const value of snapshot.values ?? []) {
    if (!same(value.formId, form.formId) || value.formVersion !== form.versionNo)
      problems.push(`"${value.fieldKey}" is mapped to a different form or form version.`);
    else if (form.stepId && value.formStepId !== form.stepId)
      problems.push(`"${value.fieldKey}" is mapped to a different Step form.`);
    const normalized = normalizeFieldKey(value.fieldKey);
    if (!normalized || seen.has(normalized))
      problems.push(`"${value.fieldKey}" is mapped more than once.`);
    seen.add(normalized);
    if (value.mode === 'ReadOnly') locked[value.fieldKey] = value.value;
    else if (value.mode === 'Prefill') prefill[value.fieldKey] = value.value;
    else problems.push(`"${value.fieldKey}" has an unknown mapping mode.`);
  }
  if (problems.length) return { ...empty('invalid', problems), applyKey };
  return { state: 'ready', applyKey, locked, prefill, problems };
}

/**
 * Applies the Prefill half of a snapshot to the answers, once. A key the user already answered is never
 * replaced, and `null` is a real prefill (it clears a default), so presence — not truthiness — decides.
 */
export function withPrefill(
  answers: Record<string, unknown>,
  prefill: Record<string, unknown>,
): Record<string, unknown> {
  const next = { ...answers };
  const existing = new Set(Object.keys(answers).map(normalizeFieldKey));
  for (const [key, value] of Object.entries(prefill))
    if (!existing.has(normalizeFieldKey(key))) next[key] = value;
  return next;
}

/** What a remembered draft belongs to besides the activation key (`workItemId|visitId|invocationId`). */
export interface StepDraftScope {
  formId: string;
  versionNo: number;
  stepId?: string;
  /**
   * Server `WorkItemDto.formDraftKey`: an opaque identity of the ownership attempt that is stable across
   * pending -> claimed and changes on release, reassign and delegate-to-another. It is not a receipt and is never
   * submitted. A missing key is NOT continuity: without it nothing is persisted and nothing is restored.
   */
  draftKey?: string | null;
  /** The effective actor when acting as someone else (demo participant); part of the storage key. */
  actorKey?: string;
  /** ReadOnly values are server-authoritative: their keys are never persisted and never restored. */
  locked?: Record<string, unknown>;
}

interface StepDraft {
  form: string;
  actor: string;
  /** `tenant|user|participant` once all three are verified; null keeps the draft in memory only. */
  owner: string | null;
  /** The server attempt identity the draft was written under; null never matches anything. */
  draftKey: string | null;
  answers: Record<string, unknown>;
}

/** The persisted shape; the owner and activation live in the storage key. */
interface StepDraftEnvelope {
  v: 2;
  form: string;
  draftKey: string;
  answers: Record<string, unknown>;
}

/** A form submission can carry large values; beyond this a draft stays in memory only. */
const MAX_DRAFT_CHARS = 256 * 1024;

const formId = (scope: StepDraftScope) =>
  [scope.formId.toLowerCase(), scope.versionNo, scope.stepId ?? ''].join('|');

export const withoutLocked = (
  answers: Record<string, unknown>,
  locked?: Record<string, unknown>,
) => {
  const mandated = new Set(Object.keys(locked ?? {}).map(normalizeFieldKey));
  return Object.fromEntries(
    Object.entries(answers).filter(([key]) => !mandated.has(normalizeFieldKey(key))),
  );
};

/**
 * Remembers which snapshots were already applied and what the user did with them. A task page that is closed
 * and reopened (or one whose tab is switched) is rebuilt from server data that never saw the user's edits;
 * without this the prefill would either re-apply over them or vanish.
 *
 * Two layers, both draft-only (nothing here is ever submitted):
 * - process memory, for SPA navigation and Refresh;
 * - `sessionStorage` (this tab, survives a hard reload, gone when the tab closes), written only for edits the
 *   person made, only while tenant, user and participant are all verified, and wiped by logout.
 * The storage key carries tenant, user, participant, effective actor and the activation (work item, visit,
 * invocation); the entry carries the exact form, version and Step and the server `formDraftKey` (the ownership
 * attempt). Anything that does not match is discarded rather than reused. Storage failures never lose the in-memory draft.
 */
@Injectable({ providedIn: 'root' })
export class StepMappingMemory {
  private readonly injector = inject(Injector);
  private readonly drafts = new Map<string, StepDraft>();

  constructor() {
    const off = onSessionDraftsCleared(() => this.drafts.clear());
    inject(DestroyRef).onDestroy(off);
  }

  isApplied(key: string) {
    return this.drafts.has(key);
  }

  /** The remembered answers for this activation, or undefined when nothing valid is held for this scope. */
  recall(key: string, scope?: StepDraftScope): Record<string, unknown> | undefined {
    const owner = scope ? this.owner() : null;
    let draft = this.drafts.get(key);
    if (draft && scope && !this.fits(draft, scope, owner)) {
      this.drafts.delete(key);
      draft = undefined;
    }
    if (!draft && scope && owner) draft = this.read(key, scope, owner);
    return draft?.answers;
  }

  /**
   * `persist` is true only for edits made by the person, never for the initial prefill the server can
   * rebuild, so a reload without edits shows exactly what a fresh open shows.
   */
  remember(key: string, answers: Record<string, unknown>, scope?: StepDraftScope, persist = false) {
    if (!key) return;
    const owner = scope ? this.owner() : null;
    this.drafts.set(key, {
      form: scope ? formId(scope) : '',
      actor: scope?.actorKey ?? '',
      owner,
      draftKey: scope?.draftKey || null,
      answers,
    });
    // Nothing is persisted without a server attempt identity: a missing key is not continuity.
    if (persist && scope?.draftKey && owner) this.write(key, scope, owner, answers);
  }

  /** Drop everything remembered for a work item (memory and tab storage) once its form is submitted. */
  forget(workItemId: string) {
    this.prune(workItemId);
  }

  private prune(workItemId: string, keep?: string) {
    for (const key of [...this.drafts.keys()])
      if (key.startsWith(workItemId + '|') && key !== keep) this.drafts.delete(key);
    try {
      const prefix = STEP_DRAFT_PREFIX + workItemId + '|';
      const stale: string[] = [];
      for (let i = 0; i < sessionStorage.length; i++) {
        const name = sessionStorage.key(i);
        if (name?.startsWith(prefix) && (!keep || !name.startsWith(STEP_DRAFT_PREFIX + keep + '|')))
          stale.push(name);
      }
      for (const name of stale) sessionStorage.removeItem(name);
    } catch {
      // Unavailable storage: nothing persisted, nothing to drop.
    }
  }

  private fits(draft: StepDraft, scope: StepDraftScope, owner: string | null) {
    if (draft.form !== formId(scope) || draft.actor !== (scope.actorKey ?? '')) return false;
    // A draft belongs to exactly the identity that wrote it. When the verified identity becomes unknown (e.g.
    // right after a silent token refresh) or different, it is not served, whether or not the page was rebuilt.
    if (draft.owner !== owner) return false;
    // The attempt identity must be present on both sides and equal; null is never a wildcard.
    return !!draft.draftKey && draft.draftKey === (scope.draftKey || null);
  }

  /** Verified identity only: unknown tenant or user (e.g. right after a silent token refresh) means none. */
  private owner(): string | null {
    try {
      const auth = this.injector.get(AuthStore);
      const context = this.injector.get(AppContextService);
      const user = auth.user()?.userId?.trim();
      const tenant = context.tenant()?.id?.trim();
      if (!auth.isAuthenticated() || !user || !tenant) return null;
      return JSON.stringify([tenant, user, context.selectedParticipantId()]);
    } catch {
      return null;
    }
  }

  private storageKey(key: string, scope: StepDraftScope, owner: string) {
    return (
      STEP_DRAFT_PREFIX +
      key +
      '|' +
      encodeURIComponent(JSON.stringify([owner, scope.actorKey ?? '']))
    );
  }

  private read(key: string, scope: StepDraftScope, owner: string): StepDraft | undefined {
    // Without a server attempt identity nothing can be proven to belong to this attempt: restore nothing and
    // leave the tab entry alone (it may belong to a verified attempt this partial view cannot name).
    if (!scope.draftKey) return undefined;
    const name = this.storageKey(key, scope, owner);
    try {
      const raw = sessionStorage.getItem(name);
      if (!raw) return undefined;
      const envelope = JSON.parse(raw) as Partial<StepDraftEnvelope> | null;
      const valid =
        envelope?.v === 2 &&
        envelope.form === formId(scope) &&
        envelope.draftKey === scope.draftKey &&
        !!envelope.answers &&
        typeof envelope.answers === 'object' &&
        !Array.isArray(envelope.answers);
      if (!valid) {
        sessionStorage.removeItem(name);
        return undefined;
      }
      const draft: StepDraft = {
        form: envelope.form!,
        actor: scope.actorKey ?? '',
        owner,
        draftKey: scope.draftKey,
        answers: withoutLocked(envelope.answers!, scope.locked),
      };
      this.drafts.set(key, draft);
      return draft;
    } catch {
      return undefined;
    }
  }

  private write(
    key: string,
    scope: StepDraftScope,
    owner: string,
    answers: Record<string, unknown>,
  ) {
    const name = this.storageKey(key, scope, owner);
    try {
      // A newer visit or invocation of the same work item makes every other remembered draft stale.
      this.prune(key.split('|')[0], key);
      const envelope: StepDraftEnvelope = {
        v: 2,
        form: formId(scope),
        draftKey: scope.draftKey!,
        answers: withoutLocked(answers, scope.locked),
      };
      const json = JSON.stringify(envelope);
      if (json.length > MAX_DRAFT_CHARS) sessionStorage.removeItem(name);
      else sessionStorage.setItem(name, json);
    } catch {
      // Quota or a blocked storage: the in-memory draft above still holds the person's edits.
    }
  }
}

/** Human reason a Preparing work item cannot be acted on yet (the generic server reason says "completed or claimed"). */
export function preparingReason(
  t: (en: string, ar: string) => string,
  invocation?: { status: string; deadline?: string | null; error?: string | null } | null,
): string {
  if (invocation?.status === 'Attention')
    return t(
      'Needs attention: the mapped values could not be prepared. ' + (invocation.error ?? ''),
      'يحتاج انتباهاً: تعذر تجهيز القيم المرتبطة. ' + (invocation.error ?? ''),
    ).trim();
  if (invocation?.status === 'Waiting' && invocation.deadline)
    return t(
      `Waiting for data from related workflows until ${new Date(invocation.deadline).toLocaleString()}.`,
      `بانتظار بيانات من سير عمل مرتبط حتى ${new Date(invocation.deadline).toLocaleString()}.`,
    );
  return t(
    'Preparing the values for this Step. It becomes available automatically.',
    'جارٍ تجهيز قيم هذه الخطوة. ستتاح تلقائياً.',
  );
}
