import { beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { AuthStore } from './auth.store';
import { tokenSubject } from './token-subject';
import type { AuthTokenResponse, CurrentUser } from './auth.model';

/** An unsigned token shaped like the server's; the store only ever reads `sub` from it. */
const jwt = (sub: string | null) =>
  [
    btoa(JSON.stringify({ alg: 'none' })),
    btoa(JSON.stringify(sub === null ? {} : { sub }))
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, ''),
    'sig',
  ].join('.');

const response = (
  sub: string | null,
  refresh = 'refresh-2',
  userName = 'sara',
): AuthTokenResponse => ({
  isSuccess: true,
  value: {
    accessToken: jwt(sub),
    refreshToken: refresh,
    expiresInSeconds: 60,
    userName,
    roles: ['Reviewer'],
    permissions: ['Tasks.Read'],
  },
  error: { code: '', message: '' },
});

const profile = (userId: string, userName = 'sara'): CurrentUser => ({
  userId,
  userName,
  email: `${userName}@example.test`,
  roles: ['Reviewer'],
  permissions: ['Tasks.Read'],
  teamId: 7,
  isUnrestrictedScope: false,
});

describe('tokenSubject', () => {
  it('reads the sub claim from a base64url payload without padding', () => {
    expect(tokenSubject(jwt('11111111-2222-3333-4444-555555555555'))).toBe(
      '11111111-2222-3333-4444-555555555555',
    );
  });

  it('is null for anything it cannot read, never a guess', () => {
    for (const bad of [
      null,
      undefined,
      '',
      'abc',
      'a.b.c',
      'a..c',
      jwt(null),
      `a.${btoa('"text"')}.c`,
    ])
      expect(tokenSubject(bad)).toBeNull();
  });
});

describe('AuthStore session identity', () => {
  let store: AuthStore;

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    store = TestBed.inject(AuthStore);
    store.setSession(response('user-1', 'refresh-1'));
    store.updateProfile(profile('user-1'));
  });

  it('keeps the verified user id when the refreshed token is for the same subject', () => {
    const epoch = store.sessionEpoch();
    expect(store.refreshSession(response('user-1'), { epoch, refreshToken: 'refresh-1' })).toBe(
      'kept',
    );
    expect(store.user()?.userId).toBe('user-1');
    expect(store.user()?.teamId).toBe(7);
    expect(store.refreshToken()).toBe('refresh-2');
    expect(tokenSubject(store.accessToken())).toBe('user-1');
    // The identity is the same, so an in-flight profile for this session is still valid.
    expect(store.sessionEpoch()).toBe(epoch);
    expect(JSON.parse(localStorage.getItem('nwfm_user')!).userId).toBe('user-1');
  });

  it('does not keep the id for a different subject, even with the same user name', () => {
    const epoch = store.sessionEpoch();
    expect(
      store.refreshSession(response('user-2', 'refresh-2', 'sara'), {
        epoch,
        refreshToken: 'refresh-1',
      }),
    ).toBe('verify');
    expect(store.user()?.userId).toBe('');
    expect(store.user()?.email).toBeNull();
    expect(store.sessionEpoch()).toBe(epoch + 1);
    expect(JSON.parse(localStorage.getItem('nwfm_user')!).userId).toBe('');
  });

  it('asks for verification when the token carries no readable subject', () => {
    const epoch = store.sessionEpoch();
    expect(store.refreshSession(response(null), { epoch, refreshToken: 'refresh-1' })).toBe(
      'verify',
    );
    expect(store.user()?.userId).toBe('');
  });

  it('asks for verification when no identity was verified before the refresh', () => {
    store.setSession(response('user-1', 'refresh-1')); // unverified again: the login profile has not landed
    expect(
      store.refreshSession(response('user-1'), {
        epoch: store.sessionEpoch(),
        refreshToken: 'refresh-1',
      }),
    ).toBe('verify');
    expect(store.user()?.userId).toBe('');
  });

  it('ignores a refresh that belongs to a session that has since ended or been replaced', () => {
    const epoch = store.sessionEpoch();
    store.clearSession();
    expect(store.refreshSession(response('user-1'), { epoch, refreshToken: 'refresh-1' })).toBe(
      'stale',
    );
    expect(store.isAuthenticated()).toBe(false);
    expect(store.user()).toBeNull();

    store.setSession(response('user-9', 'refresh-9'));
    store.updateProfile(profile('user-9', 'omar'));
    expect(store.refreshSession(response('user-1'), { epoch, refreshToken: 'refresh-1' })).toBe(
      'stale',
    );
    expect(store.refreshToken()).toBe('refresh-9');
    expect(store.user()?.userId).toBe('user-9');
  });

  it('ignores a refresh issued for a token that a newer refresh already replaced', () => {
    const epoch = store.sessionEpoch();
    expect(
      store.refreshSession(response('user-1', 'refresh-2'), { epoch, refreshToken: 'refresh-1' }),
    ).toBe('kept');
    expect(
      store.refreshSession(response('user-1', 'refresh-3'), { epoch, refreshToken: 'refresh-1' }),
    ).toBe('stale');
    expect(store.refreshToken()).toBe('refresh-2');
  });

  it('ignores an unsuccessful refresh response', () => {
    const failed: AuthTokenResponse = { ...response('user-1'), isSuccess: false };
    expect(
      store.refreshSession(failed, { epoch: store.sessionEpoch(), refreshToken: 'refresh-1' }),
    ).toBe('stale');
    expect(store.refreshToken()).toBe('refresh-1');
  });

  it('applies a profile only for the session it was requested for', () => {
    store.setSession(response('user-2', 'refresh-b'));
    const epoch = store.sessionEpoch();
    store.clearSession();
    expect(store.updateProfile(profile('user-2'), epoch)).toBe(false);
    expect(store.user()).toBeNull();

    store.setSession(response('user-3', 'refresh-c'));
    expect(store.updateProfile(profile('user-2'), epoch)).toBe(false);
    expect(store.user()?.userId).toBe('');
    expect(store.updateProfile(profile('user-3'), store.sessionEpoch())).toBe(true);
    expect(store.user()?.userId).toBe('user-3');
  });
});
