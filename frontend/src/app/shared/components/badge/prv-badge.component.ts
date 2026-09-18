import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type PrvBadgeColor = 'primary' | 'success' | 'warning' | 'danger' | 'info' | 'neutral';
export type PrvBadgeVariant = 'light' | 'solid';
export type PrvBadgeSize = 'sm' | 'md';

/**
 * prv-badge — pill label for counts, tags, and inline status.
 * Adapted from TailAdmin's badge to NWFM semantic tokens.
 */
@Component({
  selector: 'prv-badge',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span [class]="classes()">
      <ng-content select="[prv-icon-start]" />
      <ng-content />
      <ng-content select="[prv-icon-end]" />
    </span>
  `,
  styles: [':host { display: inline-flex; }'],
})
export class PrvBadgeComponent {
  readonly color = input<PrvBadgeColor>('primary');
  readonly variant = input<PrvBadgeVariant>('light');
  readonly size = input<PrvBadgeSize>('md');

  private static readonly BASE =
    'inline-flex items-center justify-center gap-1 rounded-full font-medium whitespace-nowrap';

  private static readonly SIZES: Record<PrvBadgeSize, string> = {
    sm: 'px-2 py-0.5 text-[11px]',
    md: 'px-2.5 py-0.5 text-xs',
  };

  private static readonly COLORS: Record<PrvBadgeVariant, Record<PrvBadgeColor, string>> = {
    light: {
      primary: 'bg-primary-50 text-primary-700 dark:bg-primary/15 dark:text-primary-200',
      success: 'bg-success-50 text-success-700 dark:bg-success-500/15 dark:text-success-400',
      warning: 'bg-warning-50 text-warning-700 dark:bg-warning-500/15 dark:text-warning-400',
      danger:  'bg-danger-50 text-danger-700 dark:bg-danger-500/15 dark:text-danger-400',
      info:    'bg-info-50 text-info-600 dark:bg-info-500/15 dark:text-info-400',
      neutral: 'bg-ink-100 text-ink-700 dark:bg-white/5 dark:text-ink-100',
    },
    solid: {
      primary: 'bg-primary text-white',
      success: 'bg-success text-white',
      warning: 'bg-warning text-white',
      danger:  'bg-danger text-white',
      info:    'bg-info text-white',
      neutral: 'bg-ink-500 text-white',
    },
  };

  protected readonly classes = computed(() =>
    [
      PrvBadgeComponent.BASE,
      PrvBadgeComponent.SIZES[this.size()],
      PrvBadgeComponent.COLORS[this.variant()][this.color()],
    ].join(' '),
  );
}
