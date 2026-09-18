import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { HugeiconsIconComponent } from '@hugeicons/angular';
import {
  Alert02Icon,
  CancelCircleIcon,
  CheckmarkCircle02Icon,
  InformationCircleIcon,
} from '@hugeicons/core-free-icons';

export type PrvAlertVariant = 'success' | 'error' | 'warning' | 'info';

/**
 * prv-alert — inline contextual message. Adapted from TailAdmin's alert; icons come from
 * the HugeIcons free set. Pass already-translated `title`/`message` strings.
 */
@Component({
  selector: 'prv-alert',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HugeiconsIconComponent],
  styles: [':host { display: block; }'],
  template: `
    <div [class]="'flex gap-3 rounded-xl border p-4 ' + container()">
      <hugeicons-icon [icon]="icon()" [size]="22" [class]="'flex-shrink-0 mt-0.5 ' + iconColor()" />
      <div class="min-w-0">
        @if (title()) {
          <h4 class="text-sm font-semibold text-ink-900 dark:text-white/90">{{ title() }}</h4>
        }
        @if (message()) {
          <p class="text-sm text-ink-600 dark:text-ink-300" [class.mt-0.5]="!!title()">{{ message() }}</p>
        }
        <ng-content />
      </div>
    </div>
  `,
})
export class PrvAlertComponent {
  readonly variant = input<PrvAlertVariant>('info');
  readonly title = input('');
  readonly message = input('');

  private static readonly CONTAINERS: Record<PrvAlertVariant, string> = {
    success: 'border-success-100 bg-success-50 dark:border-success-500/30 dark:bg-success-500/10',
    error:   'border-danger-100 bg-danger-50 dark:border-danger-500/30 dark:bg-danger-500/10',
    warning: 'border-warning-100 bg-warning-50 dark:border-warning-500/30 dark:bg-warning-500/10',
    info:    'border-info-100 bg-info-50 dark:border-info-500/30 dark:bg-info-500/10',
  };

  private static readonly ICON_COLORS: Record<PrvAlertVariant, string> = {
    success: 'text-success-500',
    error:   'text-danger-500',
    warning: 'text-warning-500',
    info:    'text-info-500',
  };

  private static readonly ICONS = {
    success: CheckmarkCircle02Icon,
    error: CancelCircleIcon,
    warning: Alert02Icon,
    info: InformationCircleIcon,
  };

  protected readonly container = computed(() => PrvAlertComponent.CONTAINERS[this.variant()]);
  protected readonly iconColor = computed(() => PrvAlertComponent.ICON_COLORS[this.variant()]);
  protected readonly icon = computed(() => PrvAlertComponent.ICONS[this.variant()]);
}
