import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import {
  formatRemainingSla,
  remainingSlaClass,
  remainingSlaTone,
} from '../workflow-sla.util';

@Component({
  selector: 'app-workflow-sla-clock',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslateModule],
  template: `
    @if (tone() === 'closed') {
      <span class="inline-flex items-center gap-1 text-xs font-medium text-green-700 dark:text-green-400">
        <span aria-hidden="true">✓</span>
        {{ 'workflow.runtime.sla.closed' | translate }}
      </span>
    } @else if (tone() === 'none') {
      <span class="text-xs text-ink-400 dark:text-dark-500">—</span>
    } @else {
      <span class="inline-flex items-center gap-1 font-mono text-xs tabular-nums" [class]="clockClass()">
        <span aria-hidden="true">⏱</span>
        {{ label() }}
      </span>
    }
  `,
})
export class WorkflowSlaClockComponent {
  readonly minutes = input<number | null | undefined>(null);
  readonly status = input<string | null | undefined>(null);

  protected readonly tone = computed(() => remainingSlaTone(this.minutes(), this.status()));
  protected readonly label = computed(() => formatRemainingSla(this.minutes()));
  protected readonly clockClass = computed(() => remainingSlaClass(this.tone()));
}
