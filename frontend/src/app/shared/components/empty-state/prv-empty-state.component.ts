import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { HugeiconsIconComponent } from '@hugeicons/angular';
import { InboxIcon } from '@hugeicons/core-free-icons';
import type { IconSvgObject } from '@hugeicons/angular';

/**
 * prv-empty-state — shown when a list/table has no data.
 * Accepts already-translated `title`/`message` (main) or i18n `titleKey`/`subtitleKey` (workflow).
 */
@Component({
  selector: 'prv-empty-state',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HugeiconsIconComponent, TranslateModule, RouterLink],
  styles: [':host { display: block; }'],
  template: `
    <div class="flex flex-col items-center justify-center text-center px-6 py-14">
      <span class="flex items-center justify-center w-14 h-14 rounded-2xl bg-primary-50 text-primary-500 dark:bg-primary/15 dark:text-primary-300">
        <ng-content select="[icon]" />
        @if (!hasIconProjection()) {
          <hugeicons-icon [icon]="icon()" [size]="26" />
        }
      </span>
      <h3 class="mt-4 text-base font-semibold text-ink-900 dark:text-white/90">
        @if (titleKey()) {
          {{ titleKey()! | translate }}
        } @else {
          {{ title() }}
        }
      </h3>
      @if (subtitleKey() || message()) {
        <p class="mt-1.5 max-w-sm text-sm text-ink-500 dark:text-ink-300">
          @if (subtitleKey()) {
            {{ subtitleKey()! | translate }}
          } @else {
            {{ message() }}
          }
        </p>
      }
      @if (ctaKey() && ctaLink()) {
        <a
          [routerLink]="ctaLink()!"
          class="mt-5 inline-flex items-center gap-2 rounded-xl bg-primary px-4 py-2.5 text-sm font-medium text-white shadow-lg shadow-primary/20 hover:bg-primary-400 transition-colors">
          {{ ctaKey()! | translate }}
        </a>
      }
      <div class="mt-5 empty:hidden">
        <ng-content select="[actions]" />
        <ng-content />
      </div>
    </div>
  `,
})
export class PrvEmptyStateComponent {
  readonly icon = input<IconSvgObject>(InboxIcon);
  readonly title = input('');
  readonly message = input('');
  readonly titleKey = input<string | null>(null);
  readonly subtitleKey = input<string | null>(null);
  readonly ctaKey = input<string | null>(null);
  readonly ctaLink = input<string | null>(null);
  readonly hasIconProjection = input(false);
}
