import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type PrvButtonVariant = 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
export type PrvButtonSize = 'sm' | 'md' | 'lg';

/**
 * prv-button — the design-system button. Adapted from TailAdmin's button to the
 * NWFM brand tokens (Clarity Blue primary), Angular 19 signals, RTL-safe.
 *
 * Icons are projected so callers can use HugeIcons:
 *   <prv-button><hugeicons-icon prv-icon-start [icon]="Add01Icon" [size]="18" />Add</prv-button>
 */
@Component({
  selector: 'prv-button',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.prv-btn-block]': 'block()' },
  template: `
    <button
      [type]="type()"
      [disabled]="disabled() || loading()"
      [class]="classes()"
    >
      @if (loading()) {
        <span class="prv-btn-spinner" aria-hidden="true"></span>
      }
      <ng-content select="[prv-icon-start]" />
      <ng-content />
      <ng-content select="[prv-icon-end]" />
    </button>
  `,
  styles: [`
    :host { display: inline-flex; }
    :host(.prv-btn-block) { display: flex; width: 100%; }
    :host(.prv-btn-block) button { width: 100%; }
    .prv-btn-spinner {
      width: 1em; height: 1em; border-radius: 999px;
      border: 2px solid currentColor; border-top-color: transparent;
      animation: prv-btn-spin 0.6s linear infinite; flex-shrink: 0;
    }
    @keyframes prv-btn-spin { to { transform: rotate(360deg); } }
  `],
})
export class PrvButtonComponent {
  readonly variant = input<PrvButtonVariant>('primary');
  readonly size = input<PrvButtonSize>('md');
  readonly type = input<'button' | 'submit' | 'reset'>('button');
  readonly disabled = input(false);
  readonly loading = input(false);
  readonly block = input(false);

  private static readonly BASE =
    'inline-flex items-center justify-center gap-2 rounded-lg font-medium transition ' +
    'focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-primary/20 ' +
    'disabled:cursor-not-allowed disabled:opacity-60';

  private static readonly SIZES: Record<PrvButtonSize, string> = {
    sm: 'h-9 px-3.5 text-[13px]',
    md: 'h-11 px-5 text-sm',
    lg: 'h-12 px-6 text-[15px]',
  };

  private static readonly VARIANTS: Record<PrvButtonVariant, string> = {
    primary: 'bg-primary text-white shadow-brand-sm hover:bg-primary-600 active:bg-primary-700',
    secondary:
      'bg-primary-50 text-primary-700 hover:bg-primary-100 ' +
      'dark:bg-primary/15 dark:text-primary-200 dark:hover:bg-primary/25',
    outline:
      'bg-white text-ink-700 ring-1 ring-inset ring-ink-200 hover:bg-ink-50 ' +
      'dark:bg-transparent dark:text-ink-100 dark:ring-surface-600 dark:hover:bg-white/5',
    ghost:
      'bg-transparent text-ink-700 hover:bg-ink-50 ' +
      'dark:text-ink-100 dark:hover:bg-white/5',
    danger: 'bg-danger text-white hover:bg-danger-600 active:bg-danger-700',
  };

  protected readonly classes = computed(() =>
    [
      PrvButtonComponent.BASE,
      PrvButtonComponent.SIZES[this.size()],
      PrvButtonComponent.VARIANTS[this.variant()],
    ].join(' '),
  );
}
