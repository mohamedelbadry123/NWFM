import { Injectable, signal, computed } from '@angular/core';
import { AuthTokenResponse, CurrentUser } from './auth.model';

const ACCESS_TOKEN_KEY = 'nwfm_access_token';
const REFRESH_TOKEN_KEY = 'nwfm_refresh_token';
const USER_KEY = 'nwfm_user';

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

  setSession(response: AuthTokenResponse): void {
    if (!response.isSuccess || !response.value) return;
    const { accessToken, refreshToken, userName, roles, permissions } = response.value;
    this._accessToken.set(accessToken);
    this._refreshToken.set(refreshToken);
    localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);

    const user: CurrentUser = {
      userId: '', userName, email: null, roles, permissions,
      teamId: null, isUnrestrictedScope: false
    };
    this._user.set(user);
    localStorage.setItem(USER_KEY, JSON.stringify(user));
  }

  updateProfile(profile: CurrentUser): void {
    this._user.set(profile);
    localStorage.setItem(USER_KEY, JSON.stringify(profile));
  }

  clearSession(): void {
    this._accessToken.set(null);
    this._refreshToken.set(null);
    this._user.set(null);
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
  }

  hasPermission(permission: string): boolean {
    return this.permissions().includes(permission);
  }

  hasAnyPermission(...perms: string[]): boolean {
    const current = this.permissions();
    return perms.some(p => current.includes(p));
  }

  private loadUser(): CurrentUser | null {
    const json = localStorage.getItem(USER_KEY);
    if (!json) return null;
    try { return JSON.parse(json); } catch { return null; }
  }
}
