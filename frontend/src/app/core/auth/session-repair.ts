import { inject } from '@angular/core';
import { firstValueFrom, timeout } from 'rxjs';
import { AuthService } from './auth.service';
import { AuthStore } from './auth.store';

/** Startup waits at most this long for the profile; after that the app starts and drafts stay memory-only. */
const REPAIR_TIMEOUT_MS = 5000;

/**
 * A session stored by an older build can hold a valid token with a blank `userId` (the old silent refresh blanked
 * it). Drafts need a verified identity, so such a session loads `/auth/me` once at startup, before editing is
 * available. The identity comes only from that server response, never from token claims. The result is applied for
 * the session epoch captured here, so a logout or new login that happens meanwhile discards it. An expired token is
 * refreshed by the auth interceptor, which verifies the refreshed identity before it replays this request, so
 * startup cannot finish ahead of that verification. A failure, a
 * timeout or an unusable profile never blocks startup and persists nothing: drafts simply stay memory-only.
 */
export async function repairSessionIdentity(
  store: AuthStore,
  authService: AuthService,
  timeoutMs = REPAIR_TIMEOUT_MS,
): Promise<void> {
  if (!store.accessToken() || store.user()?.userId?.trim()) return;
  const epoch = store.sessionEpoch();
  try {
    const res = await firstValueFrom(authService.getProfile().pipe(timeout(timeoutMs)));
    if (res.isSuccess && res.value?.userId?.trim()) store.updateProfile(res.value, epoch);
  } catch {
    // Offline, rejected or slow: the app starts anyway and the identity stays unverified.
  }
}

/** App initializer wiring for `repairSessionIdentity`. */
export const repairSessionIdentityInitializer = (): Promise<void> =>
  repairSessionIdentity(inject(AuthStore), inject(AuthService));
