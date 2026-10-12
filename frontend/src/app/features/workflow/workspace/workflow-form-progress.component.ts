import { Component, Input, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { LocaleService } from '@core/i18n/locale.service';
import { ActivityFormStep, ActivityFormVisit } from './workflow-form-routing.types';
import { WorkflowSelectedFormComponent } from './workflow-selected-form.component';

@Component({
  selector: 'app-workflow-form-progress',
  standalone: true,
  imports: [DatePipe, WorkflowSelectedFormComponent],
  template: ` @if (steps.length) {
    <div class="space-y-3 my-3">
      <ol class="flex flex-wrap gap-2" [attr.aria-label]="t('Activity forms', 'نماذج النشاط')">
        @for (s of steps; track s.id; let i = $index) {
          <li
            class="border rounded px-2 py-1 text-xs"
            [class.border-primary]="activeStepId === s.id"
            [attr.aria-current]="!completed && activeStepId === s.id ? 'step' : null"
          >
            {{ i + 1 }}. {{ s.name || s.formId }}
            @if (!completed && activeStepId === s.id) {
              <strong> · {{ t('Current', 'الحالي') }}</strong>
            }
          </li>
        }
      </ol>
      @if (history.length) {
        <details>
          <summary class="cursor-pointer text-sm">
            {{ t('Form submission history', 'سجل إرسال النماذج') }} ({{ history.length }})
          </summary>
          @for (visit of history; track visit.visitId) {
            <details class="border rounded-lg p-3 my-2">
              <summary class="cursor-pointer text-xs">
                {{ name(visit.stepId) }} · v{{ visit.versionNo }} ·
                {{ visit.submittedAt | date: 'short' }} ·
                {{ visit.ruleId ? ruleName(visit) : t('Otherwise', 'المسار الافتراضي') }}
              </summary>
              <app-workflow-selected-form
                [selectedForm]="visit"
                [workItemId]="workItemId"
                [answers]="visit.answers"
                [readonly]="true"
              />
            </details>
          }
        </details>
      }
    </div>
  }`,
})
export class WorkflowFormProgressComponent {
  @Input() steps: ActivityFormStep[] = [];
  @Input() history: ActivityFormVisit[] = [];
  @Input() activeStepId?: string;
  @Input() workItemId = '';
  @Input() completed = false;
  private locale = inject(LocaleService);
  t(en: string, ar: string) {
    return this.locale.isRtl() ? ar : en;
  }
  name(id: string) {
    return this.steps.find((s) => s.id === id)?.name || id;
  }
  ruleName(visit: ActivityFormVisit) {
    return (
      this.steps.find((s) => s.id === visit.stepId)?.rules.find((r) => r.id === visit.ruleId)
        ?.name || visit.ruleId
    );
  }
}
