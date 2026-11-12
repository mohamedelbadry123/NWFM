/**
 * The `sub` claim of a server-issued access token, or null when it cannot be read.
 *
 * This is only a continuity check: it lets the store keep a user id it already verified through `/auth/me` when
 * a silent refresh returns a token for that same subject. It is never an authorization decision and never
 * establishes an identity on its own; the server validates every token.
 */
export function tokenSubject(accessToken: string | null | undefined): string | null {
  const part = accessToken?.split('.')[1];
  if (!part) return null;
  try {
    const base64 = part.replace(/-/g, '+').replace(/_/g, '/');
    const bytes = Uint8Array.from(atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, '=')), (c) =>
      c.charCodeAt(0),
    );
    const sub = (JSON.parse(new TextDecoder().decode(bytes)) as { sub?: unknown }).sub;
    return typeof sub === 'string' && sub.trim() ? sub.trim() : null;
  } catch {
    return null;
  }
}
