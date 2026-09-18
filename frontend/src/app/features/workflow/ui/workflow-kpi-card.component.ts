import {
  ChangeDetectionStrategy,
  Component,
  input,
} from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';

export type WorkflowKpiTone = 'default' | 'primary' | 'success' | 'warning' | 'danger';

@Component({
  selector: 'app-workflow-kpi-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslateModule],
  template: `
    <div
      class="relative overflow-hidden rounded-2xl border bg-white p-4 transition-colors dark:bg-dark-800"
      [class]="surfaceClass()">
      <div class="pointer-events-none absolute inset-e-0 top-0 size-24 translate-x-1/4 -translate-y-1/4 rounded-full bg-primary/10 blur-2xl"
           [class.hidden]="tone() === 'default'"></div>
      <p class="text-[11px] font-semibold uppercase tracking-widest text-ink-400 dark:text-dark-400">{{ labelKey() | translate }}</p>
      <p class="mt-2 text-2xl font-bold tracking-tight text-ink-900 dark:text-white" [class]="valueClass()">{{ value() }}</p>
      @if (hintKey()) {
        <p class="mt-1 text-xs text-ink-500 dark:text-dark-300">{{ hintKey()! | translate: hintParams() ?? {} }}</p>
      }
    </div>
  `,
})
export class WorkflowKpiCardComponent {
  readonly labelKey = input.required<string>();
  readonly value = input<string | number>('—');
  readonly hintKey = input<string | null>(null);
  readonly hintParams = input<Record<string, string | number> | null>(null);
  readonly tone = input<WorkflowKpiTone>('default');

  protected surfaceClass(): string {
    switch (this.tone()) {
      case 'primary':
        return 'border-primary/30';
      case 'success':
        return 'border-green-200 dark:border-green-800/40';
      case 'warning':
        return 'border-amber-200 dark:border-amber-800/40';
      case 'danger':
        return 'border-red-200 dark:border-red-800/40';
      default:
        return 'border-ink-200 dark:border-dark-700';
    }
  }

  protected valueClass(): string {
    switch (this.tone()) {
      case 'primary':
        return '!text-primary-600 dark:!text-primary-200';
      case 'success':
        return '!text-green-700 dark:!text-green-300';
      case 'warning':
        return '!text-amber-700 dark:!text-amber-300';
      case 'danger':
        return '!text-red-700 dark:!text-red-300';
      default:
        return '';
    }
  }
}
