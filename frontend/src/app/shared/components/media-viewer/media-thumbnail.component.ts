import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { MediaObjectUrlService } from '../../../core/form-engine/media-object-url.service';
import { MEDIA_VIEW_KINDS, MediaItem, mediaIconOf, mediaKindOfItem } from './media-kind';

/** Tile footprint. `sm` sits in a form field's file row, `md` in the survey files gallery. */
export type MediaThumbnailSize = 'sm' | 'md';

const SIZE_CLASSES: Readonly<Record<MediaThumbnailSize, string>> = {
  sm: 'w-16 h-16',
  md: 'w-full aspect-square',
};

const ICON_SIZE_CLASSES: Readonly<Record<MediaThumbnailSize, string>> = {
  sm: 'text-lg',
  md: 'text-3xl',
};

/**
 * A clickable preview tile for one file.
 *
 * Images and videos render their actual content; audio and everything else get a typed icon, since
 * a sound has nothing to show. Whatever the kind, the tile is a button — the point is to open the
 * full viewer, so even an icon tile is actionable.
 *
 * The blob URL comes from `MediaObjectUrlService` and is deliberately *not* revoked on destroy:
 * the cache owns it, and the viewer this tile opens is about to ask for the very same file.
 */
@Component({
  selector: 'app-media-thumbnail',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ProgressSpinnerModule, TranslateContextDirective],
  template: `
    <ng-container *translateContext="let t">
      <button
        type="button"
        class="relative flex items-center justify-center overflow-hidden rounded-lg border border-[var(--app-border)] bg-surface-100 dark:bg-surface-800 cursor-pointer p-0 transition hover:border-primary-400 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-500"
        [class]="sizeClass()"
        [attr.aria-label]="t('media.preview') + ': ' + item().name"
        [title]="item().name"
        (click)="open.emit()"
      >
        @if (loading()) {
          <p-progressSpinner styleClass="h-6 w-6" [attr.aria-label]="t('common.loading')" />
        } @else if (previewUrl(); as url) {
          @switch (kind()) {
            @case (imageKind) {
              <img [src]="url" [alt]="item().name" class="h-full w-full object-cover" />
            }
            @case (videoKind) {
              <video [src]="url" class="h-full w-full object-cover" preload="metadata" muted></video>
              <span
                class="absolute inset-0 flex items-center justify-center bg-black/30 text-white"
                [class]="iconSizeClass()"
              >
                <i class="pi pi-play-circle" aria-hidden="true"></i>
              </span>
            }
            @default {
              <i [class]="icon() + ' ' + iconSizeClass()" class="text-surface-500" aria-hidden="true"></i>
            }
          }
        } @else {
          <i [class]="icon() + ' ' + iconSizeClass()" class="text-surface-500" aria-hidden="true"></i>
        }
      </button>
    </ng-container>
  `,
})
export class MediaThumbnailComponent {
  readonly item = input.required<MediaItem>();
  readonly size = input<MediaThumbnailSize>('md');

  /** The tile was activated; the host opens the viewer on this item. */
  readonly open = output<void>();

  private readonly media = inject(MediaObjectUrlService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly imageKind = MEDIA_VIEW_KINDS.Image;
  protected readonly videoKind = MEDIA_VIEW_KINDS.Video;

  protected readonly loading = signal(false);

  /** Resolved lazily for stored files; a locally owned `url` is used as-is. */
  private readonly fetchedUrl = signal<string | null>(null);

  protected readonly kind = computed(() => mediaKindOfItem(this.item()));
  protected readonly icon = computed(() => mediaIconOf(this.kind()));
  protected readonly sizeClass = computed(() => SIZE_CLASSES[this.size()]);
  protected readonly iconSizeClass = computed(() => ICON_SIZE_CLASSES[this.size()]);
  protected readonly previewUrl = computed(() => this.item().url ?? this.fetchedUrl());

  constructor() {
    effect(() => {
      const item = this.item();
      const kind = this.kind();

      // Audio and unknown types render an icon, so their bytes are only worth fetching once the
      // user actually opens the viewer.
      const isPreviewable = kind === MEDIA_VIEW_KINDS.Image || kind === MEDIA_VIEW_KINDS.Video;
      if (item.url || !item.fileId || !isPreviewable) {
        return;
      }

      this.fetchedUrl.set(null);
      this.loading.set(true);
      this.media
        .objectUrl(item.fileId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (url) => {
            this.fetchedUrl.set(url);
            this.loading.set(false);
          },
          error: () => {
            // Falls back to the typed icon — a missing thumbnail must not block the download.
            this.loading.set(false);
          },
        });
    });
  }
}
