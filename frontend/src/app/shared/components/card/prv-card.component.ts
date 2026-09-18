import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * prv-card — the surface primitive for panels and grouped content.
 * Adapted from TailAdmin's component-card to NWFM tokens (rounded-2xl, ink borders,
 * navy-tinted dark surface).
 *
 *   <prv-card [title]="'consents.title' | translate" [desc]="...">
 *     <button prv-card-actions ...>…</button>   <!-- optional header actions -->
 *     …body…
 *   </prv-card>
 *
 * Omit title/desc for a bare surface (body-only).
 */
@Component({
  selector: 'prv-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [':host { display: block; }'],
  template: `
    <div class="rounded-2xl border border-ink-100 bg-white dark:border-surface-600 dark:bg-surface-800 {{ className() }}">
      @if (title() || desc()) {
        <div class="flex items-start justify-between gap-4 px-5 py-4 sm:px-6">
          <div class="min-w-0">
            <h3 class="text-base font-semibold text-ink-900 dark:text-white/90 truncate">{{ title() }}</h3>
            @if (desc()) {
              <p class="mt-1 text-sm text-ink-500 dark:text-ink-300">{{ desc() }}</p>
            }
          </div>
          <div class="flex-shrink-0"><ng-content select="[prv-card-actions]" /></div>
        </div>
      }
      <div [class]="bodyClass()">
        <ng-content />
      </div>
    </div>
  `,
})
export class PrvCardComponent {
  readonly title = input('');
  readonly desc = input('');
  readonly className = input('');
  /** Set to false to remove the body padding (e.g. when embedding a full-bleed table). */
  readonly bodyPadding = input(true);
  /** Set to false when there is no header, to drop the divider border. */
  readonly divided = input(true);

  protected bodyClass(): string {
    const hasHeader = !!(this.title() || this.desc());
    const border = hasHeader && this.divided() ? 'border-t border-ink-100 dark:border-surface-600' : '';
    const pad = this.bodyPadding() ? 'p-5 sm:p-6' : '';
    return `${border} ${pad}`.trim();
  }
}
