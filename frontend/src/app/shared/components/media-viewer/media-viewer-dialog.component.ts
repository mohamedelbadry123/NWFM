import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  HostListener,
  computed,
  effect,
  inject,
  input,
  model,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { finalize } from 'rxjs';

import { MediaObjectUrlService } from '../../../core/form-engine/media-object-url.service';
import { LocaleService } from '../../../core/i18n/locale.service';
import {
  MEDIA_VIEW_KINDS,
  MediaItem,
  formatFileSize,
  mediaIconOf,
  mediaKindOfItem,
} from './media-kind';

const ZOOM_STEP = 0.25;
const MIN_ZOOM = 0.25;
const MAX_ZOOM = 4;
const QUARTER_TURN_DEGREES = 90;
const FULL_TURN_DEGREES = 360;

const KEYS = {
  ArrowLeft: 'ArrowLeft',
  ArrowRight: 'ArrowRight',
} as const;

/**
 * Full-size viewer for a set of survey files: photos, videos and voice notes.
 *
 * The set is what makes this worth a dialog rather than an inline `<img>` — the user steps through
 * everything attached to a survey or a question without closing and reopening anything. Images add
 * zoom and rotate, which is what a field photo of a meter reading usually needs.
 *
 * Bytes are pulled through `MediaObjectUrlService`, so a file already shown as a thumbnail opens
 * instantly and is not fetched twice.
 */
@Component({
  selector: 'app-media-viewer-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DialogModule, ButtonModule, ProgressSpinnerModule, TranslateContextDirective],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [dismissableMask]="true"
        appendTo="body"
        [draggable]="false"
        [maximizable]="true"
        [style]="{ width: '90vw', maxWidth: '1100px', height: '85vh' }"
        [contentStyle]="{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', overflow: 'hidden' }"
        [header]="current()?.name ?? t('media.preview')"
      >
        @if (current(); as item) {
          <div class="flex h-full min-h-0 flex-col gap-3">
            <!-- Stage -->
            <div
              class="relative flex min-h-0 flex-1 items-center justify-center overflow-hidden rounded-lg bg-surface-100 dark:bg-surface-900"
            >
              @if (hasPrevious()) {
                <p-button
                  [icon]="previousIcon()"
                  [rounded]="true"
                  severity="secondary"
                  styleClass="absolute z-10 start-2 top-1/2 -translate-y-1/2"
                  [ariaLabel]="t('media.previous')"
                  (onClick)="previous()"
                />
              }
              @if (hasNext()) {
                <p-button
                  [icon]="nextIcon()"
                  [rounded]="true"
                  severity="secondary"
                  styleClass="absolute z-10 end-2 top-1/2 -translate-y-1/2"
                  [ariaLabel]="t('media.next')"
                  (onClick)="next()"
                />
              }

              @if (loading()) {
                <p-progressSpinner styleClass="h-10 w-10" [attr.aria-label]="t('common.loading')" />
              } @else if (failed()) {
                <div class="flex flex-col items-center gap-2 text-surface-500">
                  <i class="pi pi-exclamation-triangle text-3xl" aria-hidden="true"></i>
                  <span class="text-sm">{{ t('media.loadFailed') }}</span>
                </div>
              } @else if (sourceUrl(); as url) {
                @switch (kind()) {
                  @case (imageKind) {
                    <img
                      [src]="url"
                      [alt]="item.name"
                      class="max-h-full max-w-full object-contain transition-transform duration-150"
                      [style.transform]="imageTransform()"
                    />
                  }
                  @case (videoKind) {
                    <video [src]="url" class="max-h-full max-w-full" controls autoplay></video>
                  }
                  @case (audioKind) {
                    <div class="flex w-full max-w-md flex-col items-center gap-4 p-6">
                      <i class="pi pi-volume-up text-4xl text-surface-500" aria-hidden="true"></i>
                      <span class="text-sm font-medium">{{ item.name }}</span>
                      <audio [src]="url" class="w-full" controls autoplay></audio>
                    </div>
                  }
                  @default {
                    <div class="flex flex-col items-center gap-2 text-surface-500">
                      <i [class]="icon()" class="text-3xl" aria-hidden="true"></i>
                      <span class="text-sm">{{ t('media.noPreview') }}</span>
                    </div>
                  }
                }
              }
            </div>

            <!-- Toolbar -->
            <div class="flex flex-wrap items-center justify-between gap-2">
              <div class="flex items-center gap-2 text-xs text-[var(--p-text-muted-color)]">
                @if (items().length > 1) {
                  <span>{{ t('media.counter', { current: index() + 1, total: items().length }) }}</span>
                  <span aria-hidden="true">·</span>
                }
                @if (item.sizeBytes) {
                  <span>{{ sizeLabel(item.sizeBytes) }}</span>
                }
              </div>

              <div class="flex items-center gap-1">
                @if (kind() === imageKind) {
                  <p-button
                    icon="pi pi-search-minus"
                    [text]="true"
                    size="small"
                    severity="secondary"
                    [disabled]="zoom() <= minZoom"
                    [ariaLabel]="t('media.zoomOut')"
                    (onClick)="zoomOut()"
                  />
                  <p-button
                    icon="pi pi-search-plus"
                    [text]="true"
                    size="small"
                    severity="secondary"
                    [disabled]="zoom() >= maxZoom"
                    [ariaLabel]="t('media.zoomIn')"
                    (onClick)="zoomIn()"
                  />
                  <p-button
                    icon="pi pi-replay"
                    [text]="true"
                    size="small"
                    severity="secondary"
                    [ariaLabel]="t('media.rotate')"
                    (onClick)="rotate()"
                  />
                  <p-button
                    icon="pi pi-refresh"
                    [text]="true"
                    size="small"
                    severity="secondary"
                    [disabled]="!isTransformed()"
                    [ariaLabel]="t('media.resetView')"
                    (onClick)="resetView()"
                  />
                }
                <p-button
                  icon="pi pi-download"
                  size="small"
                  [outlined]="true"
                  [label]="t('media.download')"
                  [loading]="downloading()"
                  [disabled]="downloading()"
                  (onClick)="download()"
                />
              </div>
            </div>
          </div>
        }
      </p-dialog>
    </ng-container>
  `,
})
export class MediaViewerDialogComponent {
  readonly visible = model.required<boolean>();
  readonly items = input.required<readonly MediaItem[]>();
  /** Which item to show when the dialog opens. */
  readonly startIndex = input<number>(0);

  private readonly media = inject(MediaObjectUrlService);
  private readonly locale = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly imageKind = MEDIA_VIEW_KINDS.Image;
  protected readonly videoKind = MEDIA_VIEW_KINDS.Video;
  protected readonly audioKind = MEDIA_VIEW_KINDS.Audio;
  protected readonly minZoom = MIN_ZOOM;
  protected readonly maxZoom = MAX_ZOOM;

  protected readonly index = signal(0);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly downloading = signal(false);
  protected readonly zoom = signal(1);
  protected readonly rotation = signal(0);

  private readonly fetchedUrl = signal<string | null>(null);

  protected readonly current = computed<MediaItem | null>(() => this.items()[this.index()] ?? null);
  protected readonly kind = computed(() => {
    const item = this.current();
    return item ? mediaKindOfItem(item) : MEDIA_VIEW_KINDS.Other;
  });
  protected readonly icon = computed(() => mediaIconOf(this.kind()));
  protected readonly sourceUrl = computed(() => this.current()?.url ?? this.fetchedUrl());
  protected readonly hasPrevious = computed(() => this.index() > 0);
  protected readonly hasNext = computed(() => this.index() < this.items().length - 1);
  protected readonly isTransformed = computed(() => this.zoom() !== 1 || this.rotation() !== 0);
  protected readonly imageTransform = computed(
    () => `scale(${this.zoom()}) rotate(${this.rotation()}deg)`,
  );

  // In RTL the chevrons swap: "previous" is the item to the right of the current one on screen.
  protected readonly previousIcon = computed(() =>
    this.locale.isRtl() ? 'pi pi-chevron-right' : 'pi pi-chevron-left',
  );
  protected readonly nextIcon = computed(() =>
    this.locale.isRtl() ? 'pi pi-chevron-left' : 'pi pi-chevron-right',
  );

  constructor() {
    // Opening always starts where the caller pointed, never where the last session left off.
    effect(() => {
      if (this.visible()) {
        this.goTo(this.startIndex());
      }
    });

    // Resolve the bytes for whatever is on screen now.
    effect(() => {
      const item = this.current();
      if (!this.visible() || !item || item.url || !item.fileId) {
        return;
      }

      this.loading.set(true);
      this.failed.set(false);
      this.media
        .objectUrl(item.fileId)
        .pipe(
          takeUntilDestroyed(this.destroyRef),
          finalize(() => this.loading.set(false)),
        )
        .subscribe({
          next: (url) => this.fetchedUrl.set(url),
          error: () => this.failed.set(true),
        });
    });
  }

  /**
   * Arrow keys walk the set. They follow the reading direction, so in Arabic the left arrow moves
   * to the next item — matching where the chevrons point.
   */
  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    if (!this.visible() || this.items().length < 2) {
      return;
    }

    const forward = this.locale.isRtl() ? KEYS.ArrowLeft : KEYS.ArrowRight;
    const backward = this.locale.isRtl() ? KEYS.ArrowRight : KEYS.ArrowLeft;

    if (event.key === forward) {
      event.preventDefault();
      this.next();
    } else if (event.key === backward) {
      event.preventDefault();
      this.previous();
    }
  }

  protected previous(): void {
    this.goTo(this.index() - 1);
  }

  protected next(): void {
    this.goTo(this.index() + 1);
  }

  protected zoomIn(): void {
    this.zoom.update((value) => Math.min(MAX_ZOOM, value + ZOOM_STEP));
  }

  protected zoomOut(): void {
    this.zoom.update((value) => Math.max(MIN_ZOOM, value - ZOOM_STEP));
  }

  protected rotate(): void {
    this.rotation.update((value) => (value + QUARTER_TURN_DEGREES) % FULL_TURN_DEGREES);
  }

  protected resetView(): void {
    this.zoom.set(1);
    this.rotation.set(0);
  }

  protected sizeLabel(bytes: number): string {
    return formatFileSize(bytes);
  }

  protected download(): void {
    const item = this.current();
    if (!item || this.downloading()) {
      return;
    }

    // A file that was never uploaded only exists as a local blob URL — save that directly.
    if (!item.fileId) {
      if (item.url) {
        saveUrl(item.url, item.name);
      }
      return;
    }

    this.downloading.set(true);
    this.media
      .download(item.fileId, item.name)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.downloading.set(false)),
      )
      .subscribe({
        error: () => this.failed.set(true),
      });
  }

  /** Moves to an item and drops any per-item view state so the next image opens unzoomed. */
  private goTo(index: number): void {
    const clamped = Math.min(Math.max(index, 0), Math.max(this.items().length - 1, 0));
    this.index.set(clamped);
    this.fetchedUrl.set(null);
    this.failed.set(false);
    this.resetView();
  }
}

function saveUrl(url: string, fileName: string): void {
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
}
