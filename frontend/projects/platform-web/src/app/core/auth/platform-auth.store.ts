import { Injectable, computed, signal } from '@angular/core';
import { PLATFORM_ROLES, PlatformLogin } from '../api/platform-api.service';

const STORAGE_KEY = 'eye360.platform.session';

@Injectable({ providedIn: 'root' })
export class PlatformAuthStore {
  private readonly accessToken = signal<string | null>(null);
  private readonly _email = signal<string | null>(null);
  private readonly _displayName = signal<string | null>(null);
  private readonly _role = signal<string | null>(null);

  readonly isAuthenticated = computed(() => this.accessToken() !== null);
  readonly email = this._email.asReadonly();
  readonly displayName = this._displayName.asReadonly();
  readonly role = this._role.asReadonly();
  /** Admin-only screens and actions. The API enforces the same split; this only hides what would be refused. */
  readonly isAdmin = computed(() => this._role() === PLATFORM_ROLES.admin);

  constructor() {
    this.restore();
  }

  get currentAccessToken(): string | null {
    return this.accessToken();
  }

  setSession(login: PlatformLogin): void {
    this.accessToken.set(login.accessToken);
    this._email.set(login.email);
    this._displayName.set(login.displayName);
    this._role.set(login.role);
    sessionStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        accessToken: login.accessToken,
        email: login.email,
        displayName: login.displayName,
        role: login.role,
      }),
    );
  }

  clear(): void {
    this.accessToken.set(null);
    this._email.set(null);
    this._displayName.set(null);
    this._role.set(null);
    sessionStorage.removeItem(STORAGE_KEY);
  }

  private restore(): void {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return;
    }
    try {
      const parsed = JSON.parse(raw) as {
        accessToken: string;
        email: string;
        displayName: string;
        role: string;
      };
      this.accessToken.set(parsed.accessToken);
      this._email.set(parsed.email);
      this._displayName.set(parsed.displayName);
      this._role.set(parsed.role);
    } catch {
      sessionStorage.removeItem(STORAGE_KEY);
    }
  }
}
