import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  OnInit,
  inject,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FieldType, FieldTypeConfig } from '@ngx-formly/core';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { ProgressBarModule } from 'primeng/progressbar';
import { TranslateService } from '@ngx-translate/core';
import { SubmissionFileUploadService, type UploadContext } from '../../../core/form-engine/submission-file-upload.service';
import { MediaThumbnailComponent } from '../media-viewer/media-thumbnail.component';
import { MediaViewerDialogComponent } from '../media-viewer/media-viewer-dialog.component';
import type { MediaItem } from '../media-viewer/media-kind';
import {
  FORM_STATE_CONTEXT_ID,
  FORM_STATE_CONTEXT_TYPE,
  FORM_STATE_FORM_ID,
  FORM_STATE_VERSION_NO,
  MEDIA_KINDS,
  type FormlyAttachment,
  type MediaKind,
} from './formly-preview.types';

const BYTES_PER_MB = 1024 * 1024;

/** `accept` attribute per media kind. */
const ACCEPT_BY_KIND: Readonly<Record<MediaKind, string>> = {
  [MEDIA_KINDS.Photo]: 'image/*',
  [MEDIA_KINDS.Video]: 'video/*',
  [MEDIA_KINDS.Audio]: 'audio/*',
  [MEDIA_KINDS.File]: '*/*',
};

/**
 * `capture` hints the device to open the camera/recorder directly.
 * Audio and documents have no capture mode, so they fall back to the file picker.
 */
const CAPTURE_BY_KIND: Readonly<Record<MediaKind, string | null>> = {
  [MEDIA_KINDS.Photo]: 'environment',
  [MEDIA_KINDS.Video]: 'environment',
  [MEDIA_KINDS.Audio]: null,
  [MEDIA_KINDS.File]: null,
};

const I18N_KEYS = {
  Add: 'formly.attachment.add',
  Empty: 'formly.attachment.empty',
  Remove: 'formly.attachment.remove',
  TooMany: 'formly.attachment.tooMany',
  TooLarge: 'formly.attachment.tooLarge',
  BadExtension: 'formly.attachment.badExtension',
  Limits: 'formly.attachment.limits',
  LimitsWithExtensions: 'formly.attachment.limitsWithExtensions',
  Uploading: 'formly.attachment.uploading',
  UploadFailed: 'formly.attachment.uploadFailed',
} as const;

/** Marks the control invalid while bytes are still moving, so the form cannot be submitted. */
const UPLOADING_ERROR_KEY = 'uploading';

/** `photo.final.JPG` -> `jpg`; '' when the name has no extension. */
function extensionOf(fileName: string): string {
  const dot = fileName.lastIndexOf('.');
  return dot >= 0 ? fileName.slice(dot + 1).toLowerCase() : '';
}

/**
 * Formly type `attachment` — backs the `photo`, `video` and `audio` elements
 * (`props.mediaKind` selects which).
 *
 * The control value is a `FormlyAttachment[]`. Each entry keeps a blob object
 * URL so previews render straight from the control value, with no component
 * state to fall out of sync when Formly resets the field.
 *
 * When the form knows which form definition it belongs to (`formState.formId`),
 * each picked file uploads immediately and the entry gains the `fileId` / `path`
 * the submission row will reference. Without one — the designer preview —
 * the field stays entirely local and nothing is sent.
 */
@Component({
  selector: 'formly-field-attachment',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ButtonModule,
    MessageModule,
    ProgressBarModule,
    MediaThumbnailComponent,
    MediaViewerDialogComponent,
  ],
  template: `
    <input
      #picker
      type="file"
      class="hidden"
      [accept]="accept"
      [attr.capture]="capture"
      [multiple]="maxFiles > 1"
      (change)="onFilesPicked($event)"
    />

    <div class="flex items-center gap-2 flex-wrap">
      <p-button
        [label]="addLabel"
        [icon]="addIcon"
        size="small"
        [outlined]="true"
        [disabled]="!!props.disabled || isFull || isBusy"
        (onClick)="picker.click()"
      />
      <small class="text-xs text-[var(--p-text-muted-color)]">{{ limitsLabel }}</small>
    </div>

    @if (rejection) {
      <p-message severity="warn" [text]="rejection" styleClass="mt-2 w-full" />
    }

    @if (attachments.length === 0) {
      <p class="text-xs text-[var(--p-text-muted-color)] mt-2">{{ emptyLabel }}</p>
    } @else {
      <div class="mt-2 flex flex-col gap-2">
        @for (item of attachments; track item.url || item.fileId; let i = $index) {
          <div class="flex items-center gap-3 p-2 rounded-lg border border-[var(--app-border)]">
            <app-media-thumbnail
              size="sm"
              [item]="mediaItems[i]"
              (open)="openViewer(i)"
            />
            <div class="flex-1 min-w-0">
              <div class="text-sm font-medium truncate">{{ item.name }}</div>
              <div class="text-[11px] text-[var(--p-text-muted-color)]">{{ sizeLabel(item.size) }}</div>
              @if (item.uploading) {
                <p-progressbar
                  [value]="item.progress ?? 0"
                  [showValue]="false"
                  styleClass="mt-1 h-1.5"
                />
                <div class="text-[11px] text-[var(--p-text-muted-color)] mt-0.5">
                  {{ uploadingLabel }}
                </div>
              }
            </div>
            <p-button
              icon="pi pi-trash"
              [text]="true"
              size="small"
              severity="danger"
              [disabled]="!!props.disabled || !!item.uploading"
              [loading]="isRemoving(item)"
              [ariaLabel]="removeLabel"
              (onClick)="remove(item)"
            />
          </div>
        }
      </div>
    }

    <app-media-viewer-dialog
      [(visible)]="viewerVisible"
      [items]="mediaItems"
      [startIndex]="viewerIndex"
    />
  `,
})
export class FormlyAttachmentComponent extends FieldType<FieldTypeConfig> implements OnInit, OnDestroy {
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly translate = inject(TranslateService);
  private readonly uploads = inject(SubmissionFileUploadService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly picker = viewChild.required<ElementRef<HTMLInputElement>>('picker');

  /** Object URLs this component created, so they can be revoked. */
  private readonly ownedUrls = new Set<string>();

  /** Object URLs whose server-side delete is still in flight. */
  private readonly removing = new Set<string>();

  protected rejection: string | null = null;

  protected viewerVisible = false;
  protected viewerIndex = 0;

  /**
   * `mediaItems` is rebuilt only when the control value changes identity.
   *
   * The thumbnails take it as an input, so handing back a fresh array on every change detection
   * pass would make them all look changed and re-resolve their sources for nothing.
   */
  private mediaItemsSource: FormlyAttachment[] | null = null;
  private mediaItemsCache: MediaItem[] = [];

  protected get mediaKind(): MediaKind {
    return (this.props['mediaKind'] as MediaKind | undefined) ?? MEDIA_KINDS.Photo;
  }

  /** Built by the mapper from the element's allowed extensions; wildcard fallback. */
  protected get accept(): string {
    return (this.props['accept'] as string | undefined) ?? ACCEPT_BY_KIND[this.mediaKind];
  }

  /** Lower-case, dot-less. Empty means every extension of this kind is fine. */
  protected get allowedExtensions(): string[] {
    const value = this.props['allowedExtensions'];
    return Array.isArray(value) ? (value as string[]) : [];
  }

  protected get capture(): string | null {
    return CAPTURE_BY_KIND[this.mediaKind];
  }

  protected get maxFiles(): number {
    return (this.props['maxFiles'] as number | undefined) ?? 1;
  }

  protected get maxFileSizeMb(): number | null {
    return (this.props['maxFileSizeMb'] as number | undefined) ?? null;
  }

  protected get attachments(): FormlyAttachment[] {
    const value: unknown = this.formControl.value;
    return Array.isArray(value) ? (value as FormlyAttachment[]) : [];
  }

  /**
   * The same files, described for the thumbnail and the viewer.
   *
   * A freshly picked file already has a local blob URL, so it previews without touching the
   * network; a file seeded from a previous fill carries only its `fileId` until `ngOnInit`
   * resolves it.
   */
  protected get mediaItems(): MediaItem[] {
    const source = this.attachments;
    if (source !== this.mediaItemsSource) {
      this.mediaItemsSource = source;
      this.mediaItemsCache = source.map((item) => ({
        fileId: item.fileId,
        url: item.url || undefined,
        name: item.name,
        contentType: item.type,
        sizeBytes: item.size,
      }));
    }

    return this.mediaItemsCache;
  }

  protected get isFull(): boolean {
    return this.attachments.length >= this.maxFiles;
  }

  /** Any upload still running — the picker stays closed until they settle. */
  protected get isBusy(): boolean {
    return this.attachments.some((item) => item.uploading);
  }

  protected get addLabel(): string {
    return this.translate.instant(I18N_KEYS.Add);
  }

  protected get addIcon(): string {
    return this.mediaKind === MEDIA_KINDS.Photo ? 'pi pi-camera' : 'pi pi-paperclip';
  }

  protected get emptyLabel(): string {
    return this.translate.instant(I18N_KEYS.Empty);
  }

  protected get removeLabel(): string {
    return this.translate.instant(I18N_KEYS.Remove);
  }

  protected get uploadingLabel(): string {
    return this.translate.instant(I18N_KEYS.Uploading);
  }

  protected get limitsLabel(): string {
    const allowed = this.allowedExtensions;
    const params = { count: this.maxFiles, size: this.maxFileSizeMb ?? '∞' };
    return allowed.length > 0
      ? this.translate.instant(I18N_KEYS.LimitsWithExtensions, {
          ...params,
          extensions: allowed.join(', '),
        })
      : this.translate.instant(I18N_KEYS.Limits, params);
  }

  /** The form being filled, or null in the designer preview — which uploads nothing. */
  private get formId(): string | null {
    const state = this.options?.formState as Record<string, unknown> | undefined;
    const value = state?.[FORM_STATE_FORM_ID];
    return typeof value === 'string' && value.length > 0 ? value : null;
  }

  /** What the upload is checked against and, later, what it is filed under. */
  private get uploadContext(): UploadContext {
    const state = this.options?.formState as Record<string, unknown> | undefined;
    const versionNo = state?.[FORM_STATE_VERSION_NO];

    return {
      versionNo: typeof versionNo === 'number' && Number.isFinite(versionNo) ? versionNo : null,
      contextType: asFormStateText(state?.[FORM_STATE_CONTEXT_TYPE]),
      contextId: asFormStateText(state?.[FORM_STATE_CONTEXT_ID]),
    };
  }

  ngOnInit(): void {
    // Seeded values from a previous fill have a fileId but no object URL yet.
    this.attachments.forEach((item) => {
      if (item.fileId && !item.url) {
        this.uploads
          .downloadObjectUrl(item.fileId)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: (url) => {
              this.ownedUrls.add(url);
              this.patchByFileId(item.fileId!, { url });
            },
            error: () => {
              // Ignore failure; it just means no thumbnail.
            },
          });
      }
    });
  }

  ngOnDestroy(): void {
    this.ownedUrls.forEach((url) => URL.revokeObjectURL(url));
    this.ownedUrls.clear();
  }

  protected sizeLabel(bytes: number): string {
    return `${(bytes / BYTES_PER_MB).toFixed(2)} MB`;
  }

  /** Opens the full-size viewer on the tile that was clicked, with the whole field as the set. */
  protected openViewer(index: number): void {
    this.viewerIndex = index;
    this.viewerVisible = true;
    this.changeDetector.markForCheck();
  }

  protected isRemoving(item: FormlyAttachment): boolean {
    return this.removing.has(item.url || item.fileId || '');
  }

  protected onFilesPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    const picked = Array.from(input.files ?? []);
    // Reset immediately so re-picking the same file still fires a change event.
    input.value = '';

    if (picked.length === 0) {
      return;
    }

    const current = this.attachments;
    const room = Math.max(0, this.maxFiles - current.length);
    const limit = this.maxFileSizeMb;
    const allowed = this.allowedExtensions;

    // `accept` is only a picker hint — drag-drop and "All files" can bypass it.
    const wrongType = allowed.length > 0
      ? picked.find((file) => !allowed.includes(extensionOf(file.name)))
      : undefined;
    if (wrongType) {
      this.rejection = this.translate.instant(I18N_KEYS.BadExtension, {
        name: wrongType.name,
        extensions: allowed.join(', '),
      });
      this.changeDetector.markForCheck();
      return;
    }

    const oversized = picked.find((file) => limit !== null && file.size > limit * BYTES_PER_MB);
    if (oversized) {
      this.rejection = this.translate.instant(I18N_KEYS.TooLarge, {
        name: oversized.name,
        size: limit,
      });
      this.changeDetector.markForCheck();
      return;
    }

    if (picked.length > room) {
      this.rejection = this.translate.instant(I18N_KEYS.TooMany, { count: this.maxFiles });
      this.changeDetector.markForCheck();
      return;
    }

    this.rejection = null;

    const formId = this.formId;
    const added = picked.map((file) => this.describe(file, formId !== null));
    this.setAttachments([...current, ...added]);
    this.formControl.markAsTouched();

    if (formId !== null) {
      picked.forEach((file, index) => {
        const url = added[index]?.url;
        if (url) {
          this.startUpload(formId, file, url);
        }
      });
    }

    this.changeDetector.markForCheck();
  }

  protected remove(item: FormlyAttachment): void {
    if (this.isRemoving(item)) {
      return;
    }

    // Nothing on the server yet (designer preview, or upload never completed).
    if (!item.fileId) {
      this.dropLocally(item);
      return;
    }

    const key = item.url || item.fileId || '';
    this.removing.add(key);
    this.changeDetector.markForCheck();

    this.uploads
      .delete(item.fileId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.removing.delete(key);
          this.dropLocally(item);
        },
        error: () => {
          // The row stays PENDING and is swept later; the user still gets the file removed.
          this.removing.delete(key);
          this.dropLocally(item);
        },
      });
  }

  private startUpload(formId: string, file: File, url: string): void {
    this.uploads
      .upload(formId, this.field.key as string, file, this.uploadContext)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (event) => {
          if (event.kind === 'progress') {
            this.patch(url, { progress: event.percent });
            return;
          }

          this.patch(url, {
            fileId: event.file.fileId,
            path: event.file.path,
            uploading: false,
            progress: 100,
          });
        },
        error: (error: unknown) => {
          // Drop the entry — a half-uploaded file must never reach the submit payload.
          const failed = this.attachments.find((item) => item.url === url);
          this.rejection = this.translate.instant(I18N_KEYS.UploadFailed, {
            name: failed?.name ?? '',
            reason: error instanceof Error ? error.message : '',
          });
          this.dropByUrl(url);
        },
      });
  }

  /** Replaces one entry, matched by its stable object URL. */
  private patch(url: string, changes: Partial<FormlyAttachment>): void {
    const next = this.attachments.map((item) => (item.url === url ? { ...item, ...changes } : item));
    this.setAttachments(next);
    this.changeDetector.markForCheck();
  }

  private patchByFileId(fileId: string, changes: Partial<FormlyAttachment>): void {
    const next = this.attachments.map((item) => (item.fileId === fileId ? { ...item, ...changes } : item));
    this.setAttachments(next);
    this.changeDetector.markForCheck();
  }

  private dropLocally(item: FormlyAttachment): void {
    if (item.url) {
      this.dropByUrl(item.url);
    } else if (item.fileId) {
      this.dropByFileId(item.fileId);
    }
    this.formControl.markAsTouched();
    this.rejection = null;
  }

  private dropByUrl(url: string | undefined): void {
    if (!url) return;
    this.setAttachments(this.attachments.filter((entry) => entry.url !== url));
    this.releaseUrl(url);
    this.changeDetector.markForCheck();
  }

  private dropByFileId(fileId: string): void {
    this.setAttachments(this.attachments.filter((entry) => entry.fileId !== fileId));
    this.changeDetector.markForCheck();
  }

  /**
   * Writes the value, then re-derives validity: while anything is uploading the control is
   * held invalid so the form cannot be submitted with a reference that does not exist yet.
   */
  private setAttachments(value: FormlyAttachment[]): void {
    this.formControl.setValue(value);

    if (value.some((item) => item.uploading)) {
      this.formControl.setErrors({ ...(this.formControl.errors ?? {}), [UPLOADING_ERROR_KEY]: true });
      return;
    }

    this.formControl.updateValueAndValidity();
  }

  private describe(file: File, uploading: boolean): FormlyAttachment {
    const url = URL.createObjectURL(file);
    this.ownedUrls.add(url);
    return { name: file.name, size: file.size, type: file.type, url, uploading, progress: 0 };
  }

  private releaseUrl(url: string): void {
    if (this.ownedUrls.delete(url)) {
      URL.revokeObjectURL(url);
    }
  }
}

/** A form-state value that is only meaningful as a non-empty string. */
function asFormStateText(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}
