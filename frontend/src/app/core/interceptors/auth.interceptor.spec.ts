import { beforeEach, describe, expect, it, vi } from 'vitest';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AppContextService } from '@core/context/app-context.service';
import { StepMappingMemory } from '@features/workflow/mapping/workflow-step-mapping';
import { STEP_DRAFT_PREFIX } from '../auth/session-drafts';
import { AuthStore } from '../auth/auth.store';
import { authInterceptor } from './auth.interceptor';

const jwt = (sub: string) =>
  `${btoa('{"alg":"none"}')}.${btoa(JSON.stringify({ sub })).replace(/=+$/, '')}.sig`;

const tokens = (sub: string, refresh: string, userName = 'sara') => ({
  isSuccess: true,
  value: {
    accessToken: jwt(sub),
    refreshToken: refresh,
    expiresInSeconds: 60,
    userName,
    roles: [],
    permissions: [],
  },
  error: { code: '', message: '' },
});

const profile = (userId: string, userName = 'sara') => ({
  isSuccess: true,
  value: {
    userId,
    userName,
    email: null,
    roles: [],
    permissions: [],
    teamId: null,
    isUnrestrictedScope: false,
  },
});

const KEY = 'work-1|visit-1|inv-1';
const scope = { formId: 'FORM-1', versionNo: 1, stepId: 'step-1', draftKey: 'attempt-1' };
const stored = () =>
  Object.keys(sessionStorage).filter((name) => name.startsWith(STEP_DRAFT_PREFIX));

describe('authInterceptor silent refresh and the verified draft identity', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let store: AuthStore;
  let router: { navigate: ReturnType<typeof vi.fn> };

  /** A new injector is a page load: services start empty, `localStorage` and `sessionStorage` stay. */
  const boot = () => {
    TestBed.resetTestingModule();
    router = { navigate: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
        {
          provide: AppContextService,
          useValue: { tenant: signal({ id: 'tenant-1' }), selectedParticipantId: () => '' },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    store = TestBed.inject(AuthStore);
  };

  /** Sends a request that gets a 401 and answers the refresh it triggers; returns the pending refresh. */
  const expire = () => {
    const outcome = vi.fn();
    const failed = vi.fn();
    client.get('/api/data').subscribe({ next: outcome, error: failed });
    http.expectOne('/api/data').flush({}, { status: 401, statusText: 'Unauthorized' });
    return { outcome, failed, refresh: http.expectOne('/api/v1/auth/refresh') };
  };

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    boot();
    store.setSession(tokens('user-1', 'refresh-1'));
    store.updateProfile(profile('user-1').value);
  });

  it('keeps the verified user for the same subject, so drafts persist and survive a hard reload', () => {
    const memory = TestBed.inject(StepMappingMemory);
    const { outcome, refresh } = expire();
    refresh.flush(tokens('user-1', 'refresh-2'));

    // No profile reload is needed, and the original request is replayed with the new token.
    http.expectNone('/api/v1/auth/me');
    const replay = http.expectOne('/api/data');
    expect(replay.request.headers.get('Authorization')).toBe(`Bearer ${jwt('user-1')}`);
    replay.flush({ ok: true });
    expect(outcome).toHaveBeenCalled();
    expect(store.user()?.userId).toBe('user-1');

    memory.remember(KEY, { a: 'typed' }, scope, true);
    expect(stored()).toHaveLength(1);

    boot();
    expect(store.refreshToken()).toBe('refresh-2');
    expect(TestBed.inject(StepMappingMemory).recall(KEY, scope)).toEqual({ a: 'typed' });
    http.verify();
  });

  it('loads a fresh profile for a different subject and persists only once it lands', () => {
    const memory = TestBed.inject(StepMappingMemory);
    const { refresh } = expire();
    refresh.flush(tokens('user-2', 'refresh-2', 'sara')); // same user name, different subject

    // The request is replayed only after the refreshed identity is verified, never before.
    expect(store.user()?.userId).toBe('');
    http.expectNone('/api/data');
    memory.remember(KEY, { a: 'before profile' }, scope, true);
    expect(stored()).toEqual([]);

    const me = http.expectOne('/api/v1/auth/me');
    expect(me.request.headers.get('Authorization')).toBe(`Bearer ${jwt('user-2')}`);
    me.flush(profile('user-2'));
    expect(store.user()?.userId).toBe('user-2');
    const replay = http.expectOne('/api/data');
    expect(replay.request.headers.get('Authorization')).toBe(`Bearer ${jwt('user-2')}`);
    replay.flush({});
    memory.remember(KEY, { a: 'after profile' }, scope, true);
    expect(stored()).toHaveLength(1);
    http.verify();
  });

  it('drops a refresh that finishes after logout: no tokens, no replay, no navigation', () => {
    const { failed, refresh } = expire();
    store.clearSession();
    refresh.flush(tokens('user-1', 'refresh-2'));

    http.expectNone('/api/data');
    expect(failed).toHaveBeenCalled();
    expect(store.isAuthenticated()).toBe(false);
    expect(store.refreshToken()).toBeNull();
    expect(store.user()).toBeNull();
    http.verify();
  });

  it('drops a refresh that finishes after another user signed in, keeping the new session', () => {
    const { failed, refresh } = expire();
    store.setSession(tokens('user-9', 'refresh-9', 'omar'));
    store.updateProfile(profile('user-9', 'omar').value);
    refresh.flush(tokens('user-1', 'refresh-2'));

    http.expectNone('/api/data');
    expect(failed).toHaveBeenCalled();
    expect(store.refreshToken()).toBe('refresh-9');
    expect(store.user()?.userId).toBe('user-9');
    http.verify();
  });

  it('does not sign the new session out when the stale refresh is rejected', () => {
    const { failed, refresh } = expire();
    store.setSession(tokens('user-9', 'refresh-9', 'omar'));
    refresh.flush({ isSuccess: false }, { status: 401, statusText: 'Unauthorized' });

    expect(failed).toHaveBeenCalled();
    expect(store.isAuthenticated()).toBe(true);
    expect(router.navigate).not.toHaveBeenCalled();
    http.verify();
  });

  it('still signs out when the refresh of the current session is rejected', () => {
    const { failed, refresh } = expire();
    refresh.flush({ isSuccess: false }, { status: 401, statusText: 'Unauthorized' });

    expect(failed).toHaveBeenCalled();
    expect(store.isAuthenticated()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('ignores the profile of a refreshed session that ended before it answered, and does not replay', () => {
    const { failed, refresh } = expire();
    refresh.flush(tokens('user-2', 'refresh-2'));
    const me = http.expectOne('/api/v1/auth/me');

    store.clearSession();
    store.setSession(tokens('user-3', 'refresh-3', 'omar'));
    me.flush(profile('user-2'));

    expect(store.user()?.userId).toBe('');
    expect(store.refreshToken()).toBe('refresh-3');
    http.expectNone('/api/data');
    expect(failed).toHaveBeenCalled();
    http.verify();
  });

  it('drops the verification and the replay when the session was logged out meanwhile', () => {
    const { failed, refresh } = expire();
    refresh.flush(tokens('user-2', 'refresh-2'));
    const me = http.expectOne('/api/v1/auth/me');

    store.clearSession();
    me.flush(profile('user-2'));

    expect(store.user()).toBeNull();
    expect(store.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('nwfm_user')).toBeNull();
    http.expectNone('/api/data');
    expect(failed).toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
    http.verify();
  });

  it('still replays when the verification itself cannot be loaded, leaving the identity unverified', () => {
    const { outcome, refresh } = expire();
    refresh.flush(tokens('user-2', 'refresh-2'));
    http.expectOne('/api/v1/auth/me').error(new ProgressEvent('error'), { status: 0 });
    http.expectOne('/api/data').flush({ ok: true });

    expect(outcome).toHaveBeenCalled();
    expect(store.user()?.userId).toBe('');
    expect(store.isAuthenticated()).toBe(true);
    http.verify();
  });

  it('does not use the epoch advance to keep a session whose refreshed token is then rejected', () => {
    const { failed, refresh } = expire();
    refresh.flush(tokens('user-2', 'refresh-2'));
    http.expectOne('/api/v1/auth/me').flush(profile('user-2'));
    // The replay is rejected with a 401 as well: the refreshed session is still the current one and must end.
    http.expectOne('/api/data').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(failed).toHaveBeenCalled();
    expect(store.isAuthenticated()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
    http.expectNone('/api/v1/auth/refresh');
  });

  it('does not refresh again for a rejected verification request (its 401 goes to the caller)', () => {
    const { refresh } = expire();
    refresh.flush(tokens('user-2', 'refresh-2'));
    http.expectOne('/api/v1/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    http.expectNone('/api/v1/auth/refresh');
    // The replay follows and carries the fresh token.
    expect(http.expectOne('/api/data').request.headers.get('Authorization')).toBe(
      `Bearer ${jwt('user-2')}`,
    );
  });

  it('releases the refresh latch when the pending refresh is cancelled, so the next 401 can refresh', () => {
    const cancelled = client.get('/api/data').subscribe();
    http.expectOne('/api/data').flush({}, { status: 401, statusText: 'Unauthorized' });
    const first = http.expectOne('/api/v1/auth/refresh');
    cancelled.unsubscribe();
    expect(first.cancelled).toBe(true);

    const { outcome, refresh } = expire();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(store.isAuthenticated()).toBe(true);
    refresh.flush(tokens('user-1', 'refresh-2'));
    http.expectOne('/api/data').flush({ ok: true });
    expect(outcome).toHaveBeenCalled();
    expect(store.refreshToken()).toBe('refresh-2');
  });

  it('lets a concurrent request fail without ending the session that is being renewed', () => {
    const { outcome, refresh } = expire();
    const other = vi.fn();
    client.get('/api/other').subscribe({ error: other });
    http.expectOne('/api/other').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(other).toHaveBeenCalled();
    expect(store.isAuthenticated()).toBe(true);
    expect(router.navigate).not.toHaveBeenCalled();
    refresh.flush(tokens('user-1', 'refresh-2'));
    http.expectOne('/api/data').flush({ ok: true });
    expect(outcome).toHaveBeenCalled();
    expect(store.refreshToken()).toBe('refresh-2');
  });
});
