import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';

export type PrvStatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral' | 'primary';
export type PrvTone = PrvStatusTone;

/**
 * prv-status-pill — a status label with a leading dot.
 * Label comes from content projection, or from `label` / `labelKey` (workflow screens).
 */
@Component({
  selector: 'prv-status-pill',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslateModule],
  template: `
    <span [class]="classes()">
      @if (dot()) {
        <span [class]="dotClasses()" aria-hidden="true"></span>
      }
      @if (labelKey()) {
        {{ labelKey()! | translate }}
      } @else if (label()) {
        {{ label() }}
      } @else {
        <ng-content />
      }
    </span>
  `,
  styles: [':host { display: inline-flex; }'],
})
export class PrvStatusPillComponent {
  readonly tone = input<PrvStatusTone>('neutral');
  readonly label = input('');
  readonly labelKey = input<string | null>(null);
  readonly dot = input(true);

  private static readonly BASE =
    'inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium';

  private static readonly TONES: Record<PrvStatusTone, string> = {
    success: 'bg-success-50 text-success-700 dark:bg-success-500/15 dark:text-success-400',
    warning: 'bg-warning-50 text-warning-700 dark:bg-warning-500/15 dark:text-warning-400',
    danger:  'bg-danger-50 text-danger-700 dark:bg-danger-500/15 dark:text-danger-400',
    info:    'bg-info-50 text-info-600 dark:bg-info-500/15 dark:text-info-400',
    primary: 'bg-primary-50 text-primary-700 dark:bg-primary/15 dark:text-primary-200',
    neutral: 'bg-ink-100 text-ink-600 dark:bg-white/5 dark:text-ink-100',
  };

  private static readonly DOTS: Record<PrvStatusTone, string> = {
    success: 'bg-success-500',
    warning: 'bg-warning-500',
    danger:  'bg-danger-500',
    info:    'bg-info-500',
    primary: 'bg-primary-500',
    neutral: 'bg-ink-400',
  };

  protected readonly classes = computed(() =>
    `${PrvStatusPillComponent.BASE} ${PrvStatusPillComponent.TONES[this.tone()]}`,
  );

  protected readonly dotClasses = computed(() =>
    `w-1.5 h-1.5 rounded-full flex-shrink-0 ${PrvStatusPillComponent.DOTS[this.tone()]}`,
  );
}
