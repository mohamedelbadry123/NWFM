import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AppContextService } from '@core/context/app-context.service';
import { authInterceptor } from '../interceptors/auth.interceptor';
import {
  StepMappingMemory,
  type StepDraftScope,
} from '../../features/workflow/mapping/workflow-step-mapping';
import { STEP_DRAFT_PREFIX } from './session-drafts';
import { AuthStore } from './auth.store';
import { AuthService } from './auth.service';
import type { CurrentUser } from './auth.model';
import { repairSessionIdentity, repairSessionIdentityInitializer } from './session-repair';

const ME = '/api/v1/auth/me';
const profile = (userId: string): CurrentUser => ({
  userId,
  userName: 'sara',
  email: 'sara@example.test',
  roles: [],
  permissions: ['workflow.tasks'],
  teamId: null,
  isUnrestrictedScope: false,
});
const tokens = (accessToken: string, refreshToken: string) => ({
  isSuccess: true,
  value: {
    accessToken,
    refreshToken,
    expiresInSeconds: 60,
    userName: 'sara',
    roles: [],
    permissions: [],
  },
  error: { code: '', message: '' },
});
const KEY = 'work-1|visit-1|inv-1';
const scope: StepDraftScope = {
  formId: 'FORM-1',
  versionNo: 2,
  stepId: 'step-1',
  draftKey: 'attempt-1',
};
const stored = () =>
  Object.keys(sessionStorage).filter((name) => name.startsWith(STEP_DRAFT_PREFIX));

describe('startup identity repair', () => {
  /** What an older build left behind: a stored token pair and a user whose id was blanked by its silent refresh. */
  const seedLegacySession = (userId = '') => {
    localStorage.setItem('nwfm_access_token', 'legacy-access');
    localStorage.setItem('nwfm_refresh_token', 'legacy-refresh');
    localStorage.setItem('nwfm_user', JSON.stringify(profile(userId)));
  };
  /** A new injector is a new page load: memory is empty, `localStorage`/`sessionStorage` is what the tab kept. */
  const page = () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        {
          provide: AppContextService,
          useValue: { tenant: () => ({ id: 'tenant-1' }), selectedParticipantId: () => 'p-1' },
        },
      ],
    });
    return {
      store: TestBed.inject(AuthStore),
      auth: TestBed.inject(AuthService),
      http: TestBed.inject(HttpTestingController),
      memory: TestBed.inject(StepMappingMemory),
    };
  };
  /** Lets the awaited request continuation (a microtask) run. */
  const tick = () => new Promise((resolve) => setTimeout(resolve));

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });
  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it('verifies a stored token with a blank user id through /auth/me, then persists drafts and restores them after a reload', async () => {
    seedLegacySession();
    const { store, memory, http } = page();
    expect(store.isAuthenticated()).toBe(true);
    expect(store.user()?.userId).toBe('');

    // Startup: the initializer holds editing back until the server has said who this is.
    let settled = false;
    const startup = TestBed.runInInjectionContext(repairSessionIdentityInitializer).then(() => {
      settled = true;
    });
    const request = http.expectOne(ME);
    expect(request.request.headers.get('Authorization')).toBe('Bearer legacy-access');
    await tick();
    expect(settled).toBe(false);
    memory.remember(KEY, { approved: 'typed-before' }, scope, true);
    expect(stored()).toEqual([]);

    request.flush({ isSuccess: true, value: profile('user-1') });
    await startup;
    expect(store.user()?.userId).toBe('user-1');
    expect(JSON.parse(localStorage.getItem('nwfm_user')!).userId).toBe('user-1');
    expect(store.accessToken()).toBe('legacy-access');
    http.verify();

    memory.remember(KEY, { approved: 'edited-after-repair' }, scope, true);
    expect(stored()).toHaveLength(1);

    // A hard reload: a new injector reads the repaired user and restores the edit; no further profile call.
    const reloaded = page();
    expect(reloaded.store.user()?.userId).toBe('user-1');
    expect(reloaded.memory.recall(KEY, scope)).toEqual({ approved: 'edited-after-repair' });
    await TestBed.runInInjectionContext(repairSessionIdentityInitializer);
    reloaded.http.expectNone(ME);
  });

  it('makes no profile call for a verified session or an unauthenticated visitor', async () => {
    seedLegacySession('user-1');
    const verified = page();
    await repairSessionIdentity(verified.store, verified.auth);
    verified.http.expectNone(ME);

    localStorage.clear();
    const anonymous = page();
    await repairSessionIdentity(anonymous.store, anonymous.auth);
    anonymous.http.expectNone(ME);
    expect(anonymous.store.isAuthenticated()).toBe(false);
  });

  it('ignores a profile that arrives after a logout', async () => {
    seedLegacySession();
    const { store, auth, http } = page();
    const startup = repairSessionIdentity(store, auth);
    const request = http.expectOne(ME);

    store.clearSession();
    request.flush({ isSuccess: true, value: profile('user-1') });
    await startup;

    expect(store.user()).toBeNull();
    expect(store.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('nwfm_user')).toBeNull();
    expect(localStorage.getItem('nwfm_access_token')).toBeNull();
  });

  it('ignores a profile that arrives after another user signed in', async () => {
    seedLegacySession();
    const { store, auth, http } = page();
    const startup = repairSessionIdentity(store, auth);
    const request = http.expectOne(ME);

    store.clearSession();
    store.setSession(tokens('new-access', 'new-refresh'));
    request.flush({ isSuccess: true, value: profile('user-1') });
    await startup;

    // The new login has not been verified yet; the old answer must not give it the previous user's identity.
    expect(store.user()?.userId).toBe('');
    expect(JSON.parse(localStorage.getItem('nwfm_user')!).userId).toBe('');
    expect(store.accessToken()).toBe('new-access');
  });

  it('starts anyway and stays memory-only when the profile cannot be loaded', async () => {
    seedLegacySession();
    const { store, memory, auth, http } = page();
    const startup = repairSessionIdentity(store, auth);
    http.expectOne(ME).error(new ProgressEvent('error'), { status: 0 });
    await startup;

    expect(store.isAuthenticated()).toBe(true);
    expect(store.user()?.userId).toBe('');
    memory.remember(KEY, { approved: 'typed' }, scope, true);
    expect(stored()).toEqual([]);
  });

  it('does not wait forever for a profile that never answers', async () => {
    seedLegacySession();
    const { store, auth, http } = page();
    await repairSessionIdentity(store, auth, 20);
    http.expectOne(ME);
    expect(store.isAuthenticated()).toBe(true);
    expect(store.user()?.userId).toBe('');
  });

  it('never stores an unusable profile', async () => {
    seedLegacySession();
    const { store, auth, http } = page();
    const rejected = repairSessionIdentity(store, auth);
    http.expectOne(ME).flush({ isSuccess: false, value: null });
    await rejected;
    const blank = repairSessionIdentity(store, auth);
    http.expectOne(ME).flush({ isSuccess: true, value: profile('  ') });
    await blank;

    expect(store.user()?.userId).toBe('');
    expect(JSON.parse(localStorage.getItem('nwfm_user')!).userId).toBe('');
  });

  describe('an expired token (the startup request is answered with 401 and refreshed)', () => {
    /** Brings startup to the point where the refresh succeeded and the refreshed identity is being verified. */
    const untilVerifying = () => {
      seedLegacySession();
      const env = page();
      let settled = false;
      const startup = repairSessionIdentity(env.store, env.auth).then(() => {
        settled = true;
      });
      env.http.expectOne(ME).flush(null, { status: 401, statusText: 'Unauthorized' });
      env.http.expectOne('/api/v1/auth/refresh').flush(tokens('fresh-access', 'fresh-refresh'));
      // The verification is the only profile request in flight: the replay waits for it.
      const verification = env.http.expectOne(ME);
      expect(verification.request.headers.get('Authorization')).toBe('Bearer fresh-access');
      return { ...env, startup, verification, settled: () => settled };
    };

    it('cannot finish startup before the refreshed identity is verified, so edits made meanwhile are not lost', async () => {
      const { store, memory, http, startup, verification, settled } = untilVerifying();

      await tick();
      expect(settled()).toBe(false);
      expect(store.user()?.userId).toBe('');
      memory.remember(KEY, { approved: 'typed-while-unverified' }, scope, true);
      expect(stored()).toEqual([]);

      verification.flush({ isSuccess: true, value: profile('user-1') });
      expect(store.user()?.userId).toBe('user-1');
      // Only now is the original profile request replayed; its answer is the last thing startup waits for.
      await tick();
      expect(settled()).toBe(false);
      const replay = http.expectOne(ME);
      expect(replay.request.headers.get('Authorization')).toBe('Bearer fresh-access');
      replay.flush({ isSuccess: true, value: profile('user-1') });
      await startup;

      expect(settled()).toBe(true);
      expect(store.user()?.userId).toBe('user-1');
      expect(store.accessToken()).toBe('fresh-access');
      memory.remember(KEY, { approved: 'edited-after-verification' }, scope, true);
      expect(stored()).toHaveLength(1);
      http.verify();

      const reloaded = page();
      expect(reloaded.store.user()?.userId).toBe('user-1');
      expect(reloaded.memory.recall(KEY, scope)).toEqual({ approved: 'edited-after-verification' });
    });

    it('ignores a verification that arrives after a logout and does not replay', async () => {
      const { store, http, startup, verification } = untilVerifying();

      store.clearSession();
      verification.flush({ isSuccess: true, value: profile('user-1') });
      await startup;

      expect(store.user()).toBeNull();
      expect(store.isAuthenticated()).toBe(false);
      expect(localStorage.getItem('nwfm_user')).toBeNull();
      expect(localStorage.getItem('nwfm_access_token')).toBeNull();
      http.verify();
    });

    it('ignores a verification that arrives after another user signed in and does not replay', async () => {
      const { store, http, startup, verification } = untilVerifying();

      store.clearSession();
      store.setSession(tokens('new-access', 'new-refresh'));
      verification.flush({ isSuccess: true, value: profile('user-1') });
      await startup;

      expect(store.user()?.userId).toBe('');
      expect(JSON.parse(localStorage.getItem('nwfm_user')!).userId).toBe('');
      expect(store.accessToken()).toBe('new-access');
      http.verify();
    });

    it('starts anyway, unverified, when the verification cannot be loaded', async () => {
      const { store, http, startup, verification } = untilVerifying();

      verification.error(new ProgressEvent('error'), { status: 0 });
      http.expectOne(ME).flush({ isSuccess: true, value: profile('user-1') });
      await startup;

      expect(store.isAuthenticated()).toBe(true);
      expect(store.accessToken()).toBe('fresh-access');
      // The replayed answer belongs to the refreshed session whose epoch moved on; it is not trusted blindly.
      expect(store.user()?.userId).toBe('');
      http.verify();
    });

    it('leaves the refresh latch free when startup times out while the refresh is pending', async () => {
      seedLegacySession();
      const { store, auth, http } = page();
      const client = TestBed.inject(HttpClient);
      const startup = repairSessionIdentity(store, auth, 20);
      http.expectOne(ME).flush(null, { status: 401, statusText: 'Unauthorized' });
      const pending = http.expectOne('/api/v1/auth/refresh');
      await startup;
      expect(pending.cancelled).toBe(true);
      expect(store.isAuthenticated()).toBe(true);

      // The next protected request's 401 must refresh, not sign the session out.
      const result = vi.fn();
      client.get('/api/data').subscribe({ next: result });
      http.expectOne('/api/data').flush({}, { status: 401, statusText: 'Unauthorized' });
      http.expectOne('/api/v1/auth/refresh').flush(tokens('fresh-access', 'fresh-refresh'));
      http.expectOne(ME).flush({ isSuccess: true, value: profile('user-1') });
      http.expectOne('/api/data').flush({ ok: true });

      expect(result).toHaveBeenCalled();
      expect(store.isAuthenticated()).toBe(true);
      expect(store.refreshToken()).toBe('fresh-refresh');
      expect(store.user()?.userId).toBe('user-1');
      http.verify();
    });
  });
});
