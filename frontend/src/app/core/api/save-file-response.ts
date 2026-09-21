import { HttpResponse } from '@angular/common/http';

/**
 * The file name a download response names in `Content-Disposition`, or `fallback`.
 *
 * Stops at the `;`: the header also carries a `filename*=UTF-8''…` copy, and swallowing it would
 * make the saved name carry the whole header fragment.
 */
export function fileNameFromDisposition(disposition: string | null, fallback: string): string {
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition ?? '');
  if (!match) {
    return fallback;
  }

  try {
    return decodeURIComponent(match[1].trim());
  } catch {
    return match[1].trim();
  }
}

/** Saves a blob response to disk under the server's file name. */
export function saveFileResponse(response: HttpResponse<Blob>, fallbackName: string): void {
  if (!response.body) {
    return;
  }

  const fileName = fileNameFromDisposition(response.headers.get('content-disposition'), fallbackName);
  const href = URL.createObjectURL(response.body);
  const anchor = document.createElement('a');
  anchor.href = href;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(href);
}
