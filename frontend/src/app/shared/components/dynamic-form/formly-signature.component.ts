import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  ElementRef,
  AfterViewInit,
  OnDestroy,
  inject,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FieldType, FieldTypeConfig } from '@ngx-formly/core';
import { ButtonModule } from 'primeng/button';
import { ProgressBarModule } from 'primeng/progressbar';
import { MessageModule } from 'primeng/message';
import { TranslateService } from '@ngx-translate/core';
import { SubmissionFileUploadService, type UploadContext } from '../../../core/form-engine/submission-file-upload.service';
import {
  FORM_STATE_CONTEXT_ID,
  FORM_STATE_CONTEXT_TYPE,
  FORM_STATE_FORM_ID,
  FORM_STATE_VERSION_NO,
  type FormlyAttachment,
} from './formly-preview.types';

const CANVAS = {
  Width: 600,
  Height: 200,
  LineWidth: 2,
  StrokeColor: '#0f172a',
} as const;

const SIGNATURE_MIME = 'image/png';
const SIGNATURE_FILE_NAME = 'signature.png';

const I18N_KEYS = {
  Clear: 'formly.signature.clear',
  Hint: 'formly.signature.hint',
  Uploading: 'formly.signature.uploading',
  UploadFailed: 'formly.signature.uploadFailed',
} as const;

/** Marks the control invalid while the PNG is still uploading. */
const UPLOADING_ERROR_KEY = 'uploading';

const DATA_URL_PREFIX = 'data:image/';

/**
 * Formly type `signature` — a draw-to-sign pad.
 *
 * The control value is a single-item {@link FormlyAttachment} array (same shape as photo/file),
 * or `null` when the pad is empty. When the form has a `formId`, each finished stroke uploads
 * the canvas as a PNG and the array carries the `fileId` the submission row will reference.
 */
@Component({
  selector: 'formly-field-signature',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ButtonModule, ProgressBarModule, MessageModule],
  template: `
    <div class="rounded-lg border border-[var(--app-border)] bg-white overflow-hidden relative">
      <canvas
        #pad
        class="block w-full touch-none cursor-crosshair"
        [class.opacity-60]="props.disabled || isUploading"
        [width]="canvasWidth"
        [height]="canvasHeight"
        (pointerdown)="startStroke($event)"
        (pointermove)="continueStroke($event)"
        (pointerleave)="endStroke()"
        (pointerup)="endStroke()"
      ></canvas>
      @if (isUploading) {
        <div class="absolute inset-x-0 bottom-0 px-2 pb-2">
          <p-progressbar
            [value]="uploadProgress"
            [showValue]="false"
            styleClass="h-1.5"
          />
          <div class="text-[11px] text-[var(--p-text-muted-color)] mt-0.5">
            {{ uploadingLabel }}
          </div>
        </div>
      }
    </div>
    @if (rejection) {
      <p-message severity="warn" [text]="rejection" styleClass="mt-2 w-full" />
    }
    <div class="flex items-center justify-between mt-1">
      <small class="text-xs text-[var(--p-text-muted-color)]">{{ hint }}</small>
      <p-button
        [label]="clearLabel"
        icon="pi pi-eraser"
        size="small"
        [text]="true"
        [disabled]="!!props.disabled || !hasSignature || isUploading"
        [loading]="isRemoving"
        (onClick)="clear()"
      />
    </div>
  `,
})
export class FormlySignatureComponent
  extends FieldType<FieldTypeConfig>
  implements AfterViewInit, OnDestroy
{
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly translate = inject(TranslateService);
  private readonly uploads = inject(SubmissionFileUploadService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly pad = viewChild.required<ElementRef<HTMLCanvasElement>>('pad');
  private context: CanvasRenderingContext2D | null = null;
  private drawing = false;

  /** Object URLs this component created, so they can be revoked. */
  private readonly ownedUrls = new Set<string>();

  /** Last source painted onto the canvas — avoids re-downloading the same file. */
  private paintedSource: string | null = null;

  protected readonly canvasWidth = CANVAS.Width;
  protected readonly canvasHeight = CANVAS.Height;

  protected rejection: string | null = null;
  protected isRemoving = false;
  protected uploadProgress = 0;

  protected get clearLabel(): string {
    return this.translate.instant(I18N_KEYS.Clear);
  }

  protected get hint(): string {
    return this.translate.instant(I18N_KEYS.Hint);
  }

  protected get uploadingLabel(): string {
    return this.translate.instant(I18N_KEYS.Uploading);
  }

  protected get hasSignature(): boolean {
    return this.attachments.length > 0 || this.isLegacyDataUrl(this.formControl.value);
  }

  protected get isUploading(): boolean {
    return this.attachments.some((item) => item.uploading);
  }

  private get attachments(): FormlyAttachment[] {
    const value: unknown = this.formControl.value;
    return Array.isArray(value) ? (value as FormlyAttachment[]) : [];
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

  private get dataName(): string {
    return String(this.field.key ?? '');
  }

  ngAfterViewInit(): void {
    const canvas = this.pad().nativeElement;
    this.context = canvas.getContext('2d');
    if (this.context) {
      this.context.lineWidth = CANVAS.LineWidth;
      this.context.lineCap = 'round';
      this.context.lineJoin = 'round';
      this.context.strokeStyle = CANVAS.StrokeColor;
    }

    this.restore();

    this.formControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.restore();
      this.changeDetector.markForCheck();
    });
  }

  ngOnDestroy(): void {
    this.drawing = false;
    this.ownedUrls.forEach((url) => URL.revokeObjectURL(url));
    this.ownedUrls.clear();
  }

  protected startStroke(event: PointerEvent): void {
    if (this.props.disabled || this.isUploading || !this.context) {
      return;
    }
    this.drawing = true;
    this.pad().nativeElement.setPointerCapture(event.pointerId);
    const { x, y } = this.pointOf(event);
    this.context.beginPath();
    this.context.moveTo(x, y);
  }

  protected continueStroke(event: PointerEvent): void {
    if (!this.drawing || !this.context) {
      return;
    }
    const { x, y } = this.pointOf(event);
    this.context.lineTo(x, y);
    this.context.stroke();
  }

  protected endStroke(): void {
    if (!this.drawing) {
      return;
    }
    this.drawing = false;
    this.commitCanvas();
  }

  protected clear(): void {
    if (this.isRemoving || this.isUploading) {
      return;
    }

    const current = this.attachments[0];
    if (current?.fileId) {
      this.isRemoving = true;
      this.changeDetector.markForCheck();
      this.uploads
        .delete(current.fileId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: () => this.resetPad(),
          error: () => this.resetPad(),
        });
      return;
    }

    this.resetPad();
  }

  private commitCanvas(): void {
    const canvas = this.pad().nativeElement;
    canvas.toBlob((blob) => {
      if (!blob) {
        return;
      }

      const previous = this.attachments[0];
      if (previous?.fileId) {
        this.uploads
          .delete(previous.fileId)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({ error: () => undefined });
      }

      const file = new File([blob], SIGNATURE_FILE_NAME, { type: SIGNATURE_MIME });
      const url = URL.createObjectURL(blob);
      this.ownedUrls.add(url);
      this.paintedSource = url;

      const formId = this.formId;
      if (formId === null) {
        // Designer preview — local only; nothing is uploaded.
        this.setAttachments([
          { name: SIGNATURE_FILE_NAME, size: blob.size, type: SIGNATURE_MIME, url },
        ]);
        this.formControl.markAsTouched();
        this.changeDetector.markForCheck();
        return;
      }

      this.rejection = null;
      this.uploadProgress = 0;
      this.setAttachments([
        {
          name: SIGNATURE_FILE_NAME,
          size: blob.size,
          type: SIGNATURE_MIME,
          url,
          uploading: true,
          progress: 0,
        },
      ]);
      this.formControl.markAsTouched();
      this.changeDetector.markForCheck();

      this.uploads
        .upload(formId, this.dataName, file, this.uploadContext)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (event) => {
            if (event.kind === 'progress') {
              this.uploadProgress = event.percent;
              this.patchAttachment({ progress: event.percent });
              return;
            }

            this.uploadProgress = 100;
            this.paintedSource = `file:${event.file.fileId}`;
            this.patchAttachment({
              fileId: event.file.fileId,
              path: event.file.path,
              uploading: false,
              progress: 100,
            });
          },
          error: (error: unknown) => {
            this.rejection = this.translate.instant(I18N_KEYS.UploadFailed, {
              reason: error instanceof Error ? error.message : '',
            });
            this.resetPad();
          },
        });
    }, SIGNATURE_MIME);
  }

  private resetPad(): void {
    this.isRemoving = false;
    this.uploadProgress = 0;
    this.rejection = null;
    this.paintedSource = null;
    this.context?.clearRect(0, 0, this.canvasWidth, this.canvasHeight);
    this.releaseOwnedUrls();
    this.setAttachments(null);
    this.formControl.markAsTouched();
    this.changeDetector.markForCheck();
  }

  private patchAttachment(changes: Partial<FormlyAttachment>): void {
    const current = this.attachments[0];
    if (!current) {
      return;
    }
    this.setAttachments([{ ...current, ...changes }]);
    this.changeDetector.markForCheck();
  }

  private setAttachments(value: FormlyAttachment[] | null): void {
    this.formControl.setValue(value);

    if (value?.some((item) => item.uploading)) {
      this.formControl.setErrors({ ...(this.formControl.errors ?? {}), [UPLOADING_ERROR_KEY]: true });
      return;
    }

    this.formControl.updateValueAndValidity();
  }

  /** Map a pointer position onto the canvas' own coordinate space (it is CSS-scaled). */
  private pointOf(event: PointerEvent): { x: number; y: number } {
    const canvas = this.pad().nativeElement;
    const bounds = canvas.getBoundingClientRect();
    return {
      x: ((event.clientX - bounds.left) / bounds.width) * canvas.width,
      y: ((event.clientY - bounds.top) / bounds.height) * canvas.height,
    };
  }

  private restore(): void {
    if (!this.context) {
      return;
    }

    const value: unknown = this.formControl.value;

    if (this.isLegacyDataUrl(value)) {
      this.paintFromSource(value);
      return;
    }

    const items = Array.isArray(value) ? (value as FormlyAttachment[]) : [];
    const item = items[0];
    if (!item) {
      if (this.paintedSource !== null) {
        this.context.clearRect(0, 0, this.canvasWidth, this.canvasHeight);
        this.paintedSource = null;
      }
      return;
    }

    if (item.url) {
      this.paintFromSource(item.url, item.fileId ? `file:${item.fileId}` : item.url);
      return;
    }

    if (item.fileId && this.paintedSource !== `file:${item.fileId}`) {
      const fileId = item.fileId;
      this.uploads
        .downloadObjectUrl(fileId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (url) => {
            this.ownedUrls.add(url);
            this.patchAttachment({ url });
            this.paintFromSource(url, `file:${fileId}`);
          },
          error: () => {
            // Ignore — the pad stays blank if the file cannot be loaded.
          },
        });
    }
  }

  private paintFromSource(source: string, paintedKey: string = source): void {
    if (!this.context || this.paintedSource === paintedKey) {
      return;
    }

    const image = new Image();
    image.onload = () => {
      this.context?.clearRect(0, 0, this.canvasWidth, this.canvasHeight);
      this.context?.drawImage(image, 0, 0, this.canvasWidth, this.canvasHeight);
      this.paintedSource = paintedKey;
      this.changeDetector.markForCheck();
    };
    image.src = source;
  }

  private isLegacyDataUrl(value: unknown): value is string {
    return (
      typeof value === 'string' &&
      value.startsWith(DATA_URL_PREFIX) &&
      value.includes(',') &&
      !value.includes('…')
    );
  }

  private releaseOwnedUrls(): void {
    this.ownedUrls.forEach((url) => URL.revokeObjectURL(url));
    this.ownedUrls.clear();
  }
}

/** A form-state value that is only meaningful as a non-empty string. */
function asFormStateText(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}
