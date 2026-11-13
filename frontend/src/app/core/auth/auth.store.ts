import { Injectable, signal, computed } from '@angular/core';
import { AuthTokenResponse, CurrentUser } from './auth.model';
import { clearSessionDrafts } from './session-drafts';
import { tokenSubject } from './token-subject';

const ACCESS_TOKEN_KEY = 'nwfm_access_token';
const REFRESH_TOKEN_KEY = 'nwfm_refresh_token';
const USER_KEY = 'nwfm_user';

/**
 * What refreshSession did: kept - same verified subject, identity retained; verify - the subject could not be
 * matched to a verified identity, so the caller must load /auth/me before drafts are owned; stale - the session
 * the refresh belonged to is gone (logout, new login, newer refresh) and nothing was changed.
 */
export type RefreshOutcome = 'kept' | 'verify' | 'stale';

@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly _accessToken = signal<string | null>(localStorage.getItem(ACCESS_TOKEN_KEY));
  private readonly _refreshToken = signal<string | null>(localStorage.getItem(REFRESH_TOKEN_KEY));
  private readonly _user = signal<CurrentUser | null>(this.loadUser());

  readonly accessToken = this._accessToken.asReadonly();
  readonly refreshToken = this._refreshToken.asReadonly();
  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => !!this._accessToken());
  readonly permissions = computed(() => this._user()?.permissions ?? []);
  readonly roles = computed(() => this._user()?.roles ?? []);
  readonly userName = computed(() => this._user()?.userName ?? null);
  /** Bumped whenever the signed-in identity may have changed (login, logout, unverified refresh). */
  private epoch = 0;

  /** Identifies the current session; async work captures it and hands it back so a stale result is dropped. */
  sessionEpoch(): number {
    return this.epoch;
  }

  setSession(response: AuthTokenResponse): void {
    if (!response.isSuccess || !response.value) return;
    this.epoch++;
    const { accessToken, refreshToken, userName, roles, permissions } = response.value;
    this._accessToken.set(accessToken);
    this._refreshToken.set(refreshToken);
    localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);

    const user: CurrentUser = {
      userId: '',
      userName,
      email: null,
      roles,
      permissions,
      teamId: null,
      isUnrestrictedScope: false,
    };
    this._user.set(user);
    localStorage.setItem(USER_KEY, JSON.stringify(user));
  }

  /**
   * Applies a silent refresh. The verified `userId` is retained only when the refresh belongs to the session that
   * requested it and the server-issued token's subject equals that verified id; otherwise the id is cleared (drafts
   * stay memory-only) until the caller loads a fresh profile for the returned epoch. Usernames are never matched.
   */
  refreshSession(
    response: AuthTokenResponse,
    expected: { epoch: number; refreshToken: string | null },
  ): RefreshOutcome {
    if (!response.isSuccess || !response.value) return 'stale';
    if (expected.epoch !== this.epoch || expected.refreshToken !== this._refreshToken())
      return 'stale';
    const { accessToken, refreshToken, userName, roles, permissions } = response.value;
    const previous = this._user();
    const verified = previous?.userId?.trim() || '';
    const same = !!verified && tokenSubject(accessToken) === verified;
    if (!same) this.epoch++;
    this._accessToken.set(accessToken);
    this._refreshToken.set(refreshToken);
    localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
    const user: CurrentUser = {
      userId: same ? verified : '',
      userName,
      email: same ? (previous?.email ?? null) : null,
      roles,
      permissions,
      teamId: same ? (previous?.teamId ?? null) : null,
      isUnrestrictedScope: same ? (previous?.isUnrestrictedScope ?? false) : false,
    };
    this._user.set(user);
    localStorage.setItem(USER_KEY, JSON.stringify(user));
    return same ? 'kept' : 'verify';
  }

  /** Stores the verified profile. With `epoch`, a result that belongs to an earlier session is ignored. */
  updateProfile(profile: CurrentUser, epoch?: number): boolean {
    if (epoch !== undefined && epoch !== this.epoch) return false;
    this._user.set(profile);
    localStorage.setItem(USER_KEY, JSON.stringify(profile));
    return true;
  }

  clearSession(): void {
    this.epoch++;
    this._accessToken.set(null);
    this._refreshToken.set(null);
    this._user.set(null);
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    clearSessionDrafts();
  }

  hasPermission(permission: string): boolean {
    return this.permissions().includes(permission);
  }

  hasAnyPermission(...perms: string[]): boolean {
    const current = this.permissions();
    return perms.some((p) => current.includes(p));
  }

  private loadUser(): CurrentUser | null {
    const json = localStorage.getItem(USER_KEY);
    if (!json) return null;
    try {
      return JSON.parse(json);
    } catch {
      return null;
    }
  }
}
