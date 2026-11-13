/**
 * Step form drafts live in `sessionStorage` (this tab only, gone when the tab closes, survives a hard reload).
 * The logout boundary (`AuthStore.clearSession`) must wipe them, and the in-memory copies that mirror them.
 */
export const STEP_DRAFT_PREFIX = 'nwfm.stepDraft.v1.';

const listeners = new Set<() => void>();

/** Registers an in-memory cache to be emptied with the session; returns the unregister function. */
export function onSessionDraftsCleared(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function clearSessionDrafts(): void {
  try {
    const keys: string[] = [];
    for (let i = 0; i < sessionStorage.length; i++) {
      const key = sessionStorage.key(i);
      if (key?.startsWith(STEP_DRAFT_PREFIX)) keys.push(key);
    }
    for (const key of keys) sessionStorage.removeItem(key);
  } catch {
    // Storage may be unavailable; the in-memory copies below are still dropped.
  }
  for (const listener of [...listeners]) listener();
}
