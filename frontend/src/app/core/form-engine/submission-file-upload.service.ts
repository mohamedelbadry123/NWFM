import { HttpClient, HttpEvent, HttpEventType, HttpRequest } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, map } from 'rxjs';
import { ApiResult } from '../api/api-result';
import { FORM_ENGINE_UPLOADS_PATH } from './form-engine.models';

/**
 * A file already stored on the server; this is what a media field value holds. The submit payload
 * carries these references, never the bytes.
 */
export interface UploadedFileRef {
  readonly fileId: string;
  readonly path: string;
  readonly name: string;
  readonly type: string;
  readonly size: number;
}

/** What the fill the file belongs to, when it is already known at pick time. */
export interface UploadContext {
  readonly versionNo?: number | null;
  readonly contextType?: string | null;
  readonly contextId?: string | null;
}

/** Upload lifecycle: percent while bytes move, then the stored reference. */
export type UploadEvent =
  | { readonly kind: 'progress'; readonly percent: number }
  | { readonly kind: 'done'; readonly file: UploadedFileRef };

/**
 * Real-time media uploads for forms — a file goes to the server as soon as it is picked, so the
 * submit payload only carries references and a long upload never blocks the submit button.
 *
 * Upload and download both go through `HttpClient` rather than a plain URL, for concrete reasons:
 * progress events are the whole point of uploading on pick, and a browser-issued asset request
 * never passes through `authInterceptor`, so it would arrive without the bearer token.
 */
@Injectable({ providedIn: 'root' })
export class SubmissionFileUploadService {
  private readonly http = inject(HttpClient);

  upload(formId: string, dataName: string, file: File, context?: UploadContext): Observable<UploadEvent> {
    const body = new FormData();
    body.append('file', file, file.name);
    body.append('formDefinitionId', formId);
    body.append('dataName', dataName);

    if (context?.versionNo != null) {
      body.append('versionNo', String(context.versionNo));
    }

    if (context?.contextType && context?.contextId) {
      body.append('contextType', context.contextType);
      body.append('contextId', context.contextId);
    }

    const request = new HttpRequest('POST', FORM_ENGINE_UPLOADS_PATH, body, { reportProgress: true });

    return this.http.request<ApiResult<UploadedFileRef>>(request).pipe(
      map((event) => this.toUploadEvent(event)),
      filter((event): event is UploadEvent => event !== null),
    );
  }

  /** Removes a file the user picked and then dropped before submitting. */
  delete(fileId: string): Observable<ApiResult<unknown>> {
    return this.http.delete<ApiResult<unknown>>(`${FORM_ENGINE_UPLOADS_PATH}/${fileId}`);
  }

  /**
   * Fetches a stored file as a blob object URL, ready for `<img>` / `<video>` / `<audio>`.
   * The caller owns the returned URL and must `URL.revokeObjectURL` it — unless it came from
   * `MediaObjectUrlService`, which owns its own.
   */
  downloadObjectUrl(fileId: string): Observable<string> {
    return this.http
      .get(`${FORM_ENGINE_UPLOADS_PATH}/${fileId}`, { responseType: 'blob' })
      .pipe(map((blob) => URL.createObjectURL(blob)));
  }

  private toUploadEvent(event: HttpEvent<ApiResult<UploadedFileRef>>): UploadEvent | null {
    if (event.type === HttpEventType.UploadProgress) {
      const percent = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
      return { kind: 'progress', percent };
    }

    if (event.type === HttpEventType.Response) {
      return { kind: 'done', file: this.toFileRef(event.body) };
    }

    return null;
  }

  private toFileRef(result: ApiResult<UploadedFileRef> | null): UploadedFileRef {
    const file = result?.value;

    if (!result?.isSuccess || !file?.fileId) {
      throw new Error(result?.error?.message ?? 'The file could not be uploaded.');
    }

    return {
      fileId: file.fileId,
      path: file.path ?? '',
      name: file.name ?? '',
      type: file.type ?? '',
      size: file.size ?? 0,
    };
  }
}
