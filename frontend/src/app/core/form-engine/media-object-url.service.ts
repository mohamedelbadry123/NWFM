import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, shareReplay, tap } from 'rxjs';
import { FORM_ENGINE_UPLOADS_PATH } from './form-engine.models';

/**
 * How many blobs stay resident. A form rarely carries more than a handful of files, so this
 * is generous; the cap only exists so a long session browsing many forms cannot grow forever.
 */
const MAX_CACHED_FILES = 40;

/** One cached file: the blob itself plus the object URL handed to `<img>` / `<video>` / `<audio>`. */
interface CachedFile {
  readonly blob: Blob;
  readonly objectUrl: string;
}

/**
 * Shared, de-duplicated access to stored files.
 *
 * A stored file cannot be bound to an element by URL: `GET /api/v1/form-engine/uploads/{fileId}` is
 * authorized and the bearer token only exists in memory, so a browser-issued asset request is
 * rejected. Everything has to come down through `HttpClient` as a blob.
 *
 * That makes each fetch expensive, and the same file is wanted several times over — the grid
 * thumbnail, the full-size viewer, and the download button all point at one file id. This service
 * fetches once and replays: repeat callers get the same object URL, and downloads reuse the blob
 * that is already in memory.
 *
 * Object URLs are owned here, never by the caller — a component that revoked one would break
 * every other view of the same file. They are released only on eviction.
 */
@Injectable({ providedIn: 'root' })
export class MediaObjectUrlService {
  private readonly http = inject(HttpClient);


  /** Insertion-ordered, so the oldest key is the first one `keys()` yields. */
  private readonly cache = new Map<string, Observable<CachedFile>>();

  /** Object URLs that have actually been created, so eviction knows what to revoke. */
  private readonly objectUrls = new Map<string, string>();

  /**
   * A blob object URL for the file, ready to bind to a media element.
   *
   * The URL stays valid for as long as the file is cached; do **not** revoke it.
   */
  objectUrl(fileId: string): Observable<string> {
    return this.file(fileId).pipe(map((cached) => cached.objectUrl));
  }

  /** The raw bytes, for saving the file to disk. */
  blob(fileId: string): Observable<Blob> {
    return this.file(fileId).pipe(map((cached) => cached.blob));
  }

  /**
   * Saves a stored file to disk under its original name.
   *
   * The bytes come from the cache, so previewing a file and then downloading it costs one request.
   */
  download(fileId: string, fileName: string): Observable<void> {
    return this.blob(fileId).pipe(
      map((blob) => {
        // A short-lived URL of its own: revoking it must not affect the cached preview URL.
        const href = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = href;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);
        URL.revokeObjectURL(href);
      }),
    );
  }

  /** Drops everything and releases the object URLs — for sign-out. */
  clear(): void {
    [...this.cache.keys()].forEach((fileId) => this.evict(fileId));
  }

  private file(fileId: string): Observable<CachedFile> {
    const cached = this.cache.get(fileId);
    if (cached) {
      return cached;
    }

    const request = this.http
      .get(`${FORM_ENGINE_UPLOADS_PATH}/${fileId}`, { responseType: 'blob' })
      .pipe(
        map((blob) => ({ blob, objectUrl: URL.createObjectURL(blob) })),
        tap({
          next: (cached) => this.trackObjectUrl(fileId, cached.objectUrl),
          // Caching a failure would make the error permanent; drop it so a retry can re-fetch.
          error: () => this.cache.delete(fileId),
        }),
        // `refCount: false` keeps the replay alive after the last subscriber leaves, which is the
        // whole point: the thumbnail unsubscribes long before the viewer asks for the same file.
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    this.cache.set(fileId, request);
    this.evictOverflow();

    return request;
  }

  private evictOverflow(): void {
    while (this.cache.size > MAX_CACHED_FILES) {
      const oldest = this.cache.keys().next();
      if (oldest.done) {
        return;
      }

      this.evict(oldest.value);
    }
  }

  /** An entry evicted while its request was still in flight has nothing to keep — release at once. */
  private trackObjectUrl(fileId: string, objectUrl: string): void {
    if (this.cache.has(fileId)) {
      this.objectUrls.set(fileId, objectUrl);
      return;
    }

    URL.revokeObjectURL(objectUrl);
  }

  private evict(fileId: string): void {
    this.cache.delete(fileId);

    const objectUrl = this.objectUrls.get(fileId);
    if (objectUrl) {
      this.objectUrls.delete(fileId);
      URL.revokeObjectURL(objectUrl);
    }
  }
}
