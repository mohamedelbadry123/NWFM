import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { HugeiconsIconComponent } from '@hugeicons/angular';
import type { IconSvgObject } from '@hugeicons/angular';
import { PrvBadgeComponent, type PrvBadgeColor } from '../badge/prv-badge.component';

export type PrvMetricTone = 'primary' | 'teal' | 'accent' | 'success' | 'warning' | 'danger';

/**
 * prv-metric-card — KPI/stat tile with an icon, label, value and optional delta badge.
 * Adapted from TailAdmin's ecommerce-metrics to NWFM tokens + HugeIcons.
 *
 *   <prv-metric-card [icon]="UserIcon" label="Active consents" value="3,782"
 *                    delta="11%" deltaTone="success" tone="primary" />
 */
@Component({
  selector: 'prv-metric-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HugeiconsIconComponent, PrvBadgeComponent],
  template: `
    <div class="rounded-2xl border border-ink-100 bg-white p-5 md:p-6 dark:border-surface-600 dark:bg-surface-800">
      <div [class]="'flex items-center justify-center w-12 h-12 rounded-xl ' + toneClass()">
        <hugeicons-icon [icon]="icon()" [size]="24" />
      </div>
      <div class="flex items-end justify-between mt-5">
        <div class="min-w-0">
          <span class="text-sm text-ink-500 dark:text-ink-300">{{ label() }}</span>
          <h4 class="mt-1.5 text-2xl font-bold text-ink-900 dark:text-white/90 tnum">{{ value() }}</h4>
        </div>
        @if (delta()) {
          <prv-badge [color]="deltaTone()" size="sm">{{ delta() }}</prv-badge>
        }
      </div>
    </div>
  `,
  styles: [':host { display: block; } .tnum { font-variant-numeric: tabular-nums; }'],
})
export class PrvMetricCardComponent {
  readonly icon = input.required<IconSvgObject>();
  readonly label = input('');
  readonly value = input<string | number>('');
  readonly delta = input('');
  readonly deltaTone = input<PrvBadgeColor>('success');
  readonly tone = input<PrvMetricTone>('primary');

  private static readonly TONES: Record<PrvMetricTone, string> = {
    primary: 'bg-primary-50 text-primary-600 dark:bg-primary/15 dark:text-primary-300',
    teal:    'bg-teal-50 text-teal-600 dark:bg-teal-500/15 dark:text-teal-300',
    accent:  'bg-accent-50 text-accent-600 dark:bg-accent-500/15 dark:text-accent-300',
    success: 'bg-success-50 text-success-600 dark:bg-success-500/15 dark:text-success-400',
    warning: 'bg-warning-50 text-warning-600 dark:bg-warning-500/15 dark:text-warning-400',
    danger:  'bg-danger-50 text-danger-600 dark:bg-danger-500/15 dark:text-danger-400',
  };

  protected toneClass(): string {
    return PrvMetricCardComponent.TONES[this.tone()];
  }
}
