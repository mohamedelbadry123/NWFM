import { ChangeDetectionStrategy, Component, HostListener, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowLiveGraphComponent } from '../live-graph/workflow-live-graph.component';

@Component({
  selector: 'app-workflow-live-graph-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, WorkflowLiveGraphComponent],
  template: `
    <div
      class="fixed inset-0 z-50 flex items-center justify-center bg-ink-950/55 p-4 backdrop-blur-[2px]"
      role="presentation"
      (click)="closed.emit()">
      <div
        class="wf-modal flex max-h-[92vh] w-full max-w-6xl flex-col overflow-hidden"
        role="dialog"
        aria-modal="true"
        [attr.aria-label]="'workflow.runtime.progress.live_graph' | translate"
        (click)="$event.stopPropagation()">
        <div class="flex items-start justify-between gap-3 border-b border-ink-100 px-5 py-4 dark:border-dark-700">
          <div class="min-w-0">
            <h2 class="text-lg font-semibold text-ink-900 dark:text-white">
              {{ 'workflow.runtime.progress.live_graph' | translate }}
            </h2>
            <p class="mt-1 text-sm text-ink-500 dark:text-dark-300">
              {{ 'workflow.runtime.progress.live_graph_hint' | translate }}
            </p>
          </div>
          <button type="button" class="wf-btn-secondary !px-3 !py-1.5 text-xs" (click)="closed.emit()">
            {{ 'workflow.runtime.case.close' | translate }}
          </button>
        </div>
        <div class="min-h-0 flex-1 overflow-auto p-4">
          <app-workflow-live-graph [instanceId]="instanceId()" [showHeading]="false" />
        </div>
      </div>
    </div>
  `,
})
export class WorkflowLiveGraphDialogComponent {
  readonly instanceId = input.required<string>();
  readonly closed = output<void>();

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closed.emit();
  }
}
