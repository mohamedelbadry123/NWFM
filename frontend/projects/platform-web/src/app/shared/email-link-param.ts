import { ParamMap } from '@angular/router';

/** What a second query parameter becomes when an HTML-encoded link (`&amp;`) is pasted as-is. */
const HTML_ENCODED_PREFIX = 'amp;';

/**
 * Reads a query parameter from a link that arrived by e-mail. Links are HTML-encoded in the
 * message body; copied raw (from a plain-text view or the dev mail folder) `&amp;token=` reaches
 * the router as a parameter named `amp;token`, which would otherwise read as missing.
 */
export function emailLinkParam(params: ParamMap, name: string): string {
  return params.get(name) ?? params.get(HTML_ENCODED_PREFIX + name) ?? '';
}
