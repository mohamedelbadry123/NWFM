import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { LocaleService } from '@core/i18n/locale.service';
import type { WorkflowHistoryEvent } from '@core/models/workflow-ops.models';
import { historyActorName, historyStepName, isHumanHistoryEvent } from '../workflow-history.util';

@Component({
  selector: 'app-workflow-history-timeline',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, TranslateModule],
  template: `
    @if (visible().length === 0) {
      <p class="text-sm text-ink-500 dark:text-dark-300">{{ emptyKey() | translate }}</p>
    } @else {
      <ol class="relative space-y-5 border-s-2 border-ink-100 ps-6 dark:border-dark-700">
        @for (event of visible(); track event.id) {
          <li class="relative">
            <span class="absolute -start-[31px] mt-1.5 size-3.5 rounded-full border-2 border-white bg-primary shadow-sm dark:border-dark-800"></span>
            <div class="rounded-2xl border border-ink-100 bg-ink-50/70 p-4 dark:border-dark-700 dark:bg-dark-900/40">
              <div class="flex flex-wrap items-start justify-between gap-2">
                <div class="min-w-0">
                  <p class="text-sm font-semibold text-ink-900 dark:text-white">
                    {{ stepName(event) || ('workflow.runtime.progress.history_untitled_step' | translate) }}
                  </p>
                  <p class="mt-0.5 text-xs font-medium text-primary-700 dark:text-primary-200">
                    {{ eventLabel(event) | translate }}
                    @if (event.actionTaken) {
                      <span class="text-ink-400">·</span>
                      <span>{{ event.actionTaken }}</span>
                    }
                  </p>
                </div>
                <time class="shrink-0 text-xs text-ink-500 dark:text-dark-300" [attr.datetime]="event.occurredAt">
                  {{ event.occurredAt | date:'dd MMM yyyy, HH:mm' }}
                </time>
              </div>

              <dl class="mt-3 grid gap-2 text-sm sm:grid-cols-2">
                <div>
                  <dt class="text-[11px] uppercase tracking-wide text-ink-400">{{ 'workflow.runtime.progress.history_by' | translate }}</dt>
                  <dd class="mt-0.5 text-ink-800 dark:text-dark-100">{{ actorName(event) || ('workflow.runtime.progress.history_system' | translate) }}</dd>
                </div>
                @if (event.comment) {
                  <div class="sm:col-span-2">
                    <dt class="text-[11px] uppercase tracking-wide text-ink-400">{{ 'workflow.runtime.progress.history_comment' | translate }}</dt>
                    <dd class="mt-0.5 whitespace-pre-wrap text-ink-800 dark:text-dark-100">{{ event.comment }}</dd>
                  </div>
                }
                @if (event.attachmentName || event.attachmentUrl) {
                  <div class="sm:col-span-2">
                    <dt class="text-[11px] uppercase tracking-wide text-ink-400">{{ 'workflow.runtime.progress.history_attachment' | translate }}</dt>
                    <dd class="mt-0.5">
                      @if (event.attachmentUrl) {
                        <a class="text-primary-700 underline-offset-2 hover:underline dark:text-primary-200"
                           [href]="event.attachmentUrl" target="_blank" rel="noopener noreferrer">
                          {{ event.attachmentName || event.attachmentUrl }}
                        </a>
                      } @else {
                        <span class="text-ink-800 dark:text-dark-100">{{ event.attachmentName }}</span>
                      }
                    </dd>
                  </div>
                }
              </dl>
            </div>
          </li>
        }
      </ol>
    }
  `,
})
export class WorkflowHistoryTimelineComponent {
  private readonly locale = inject(LocaleService);

  readonly events = input<WorkflowHistoryEvent[]>([]);
  readonly emptyKey = input('workflow.runtime.progress.timeline_empty');

  protected readonly visible = computed(() => {
    const human = this.events().filter(isHumanHistoryEvent);
    return human.length > 0 ? human : this.events();
  });

  protected stepName(event: WorkflowHistoryEvent): string {
    return historyStepName(event, this.locale.isRtl());
  }

  protected actorName(event: WorkflowHistoryEvent): string | null {
    return historyActorName(event, this.locale.isRtl());
  }

  protected eventLabel(event: WorkflowHistoryEvent): string {
    return `workflow.runtime.progress.history_event_${event.eventType ?? 'unknown'}`;
  }
}
