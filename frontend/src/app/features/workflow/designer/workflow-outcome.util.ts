export const REDIRECT_OUTCOME_KEY = 'REDIRECT';

export function isRedirectOutcome(outcomeKey?: string | null, resultValue?: string | null): boolean {
  return matchesRedirect(outcomeKey) || matchesRedirect(resultValue);
}

function matchesRedirect(value?: string | null): boolean {
  if (!value) return false;
  return value.trim().replace(/-/g, '_').toUpperCase() === REDIRECT_OUTCOME_KEY;
}
