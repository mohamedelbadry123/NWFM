import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  ViewChild,
  inject,
} from '@angular/core';
import { DynamicFormRendererComponent } from '@shared/components/dynamic-form/dynamic-form-renderer.component';
import { LocaleService } from '@core/i18n/locale.service';

export interface WorkflowSelectedForm {
  formId: string;
  versionNo: number;
  schemaJson?: string | null;
  visitId?: string;
  stepId?: string;
}

/** Stable renderer inputs preserve partially entered answers while the workspace polls. */
@Component({
  selector: 'app-workflow-selected-form',
  standalone: true,
  imports: [DynamicFormRendererComponent],
  template: `
    @if (definition) {
      <app-dynamic-form-renderer
        [definition]="definition"
        [formId]="selectedForm.formId"
        [versionNo]="selectedForm.versionNo"
        contextType="WorkItem"
        [contextId]="selectedForm.visitId ? workItemId + ':' + selectedForm.visitId : workItemId"
        [answers]="initialAnswers"
        [readOnly]="readonly"
        (valueChanged)="answersChange.emit($event)"
      />
    } @else {
      <p role="alert">
        {{
          locale.isRtl()
            ? 'تعذر تحميل إصدار النموذج المحدد. أعد المحاولة قبل إكمال النشاط.'
            : 'The selected form version could not be loaded. Retry before completing this activity.'
        }}
      </p>
    }
  `,
})
export class WorkflowSelectedFormComponent implements OnChanges {
  @Input({ required: true }) selectedForm!: WorkflowSelectedForm;
  @Input({ required: true }) workItemId = '';
  @Input() answers: Record<string, unknown> = {};
  @Input() readonly = false;
  @Output() answersChange = new EventEmitter<Record<string, unknown>>();
  @ViewChild(DynamicFormRendererComponent) renderer?: DynamicFormRendererComponent;
  readonly locale = inject(LocaleService);
  definition: Record<string, unknown> | null = null;
  initialAnswers: Record<string, unknown> = {};
  private key = '';
  ngOnChanges() {
    const key = JSON.stringify([
      this.workItemId,
      this.selectedForm.visitId,
      this.selectedForm.formId,
      this.selectedForm.versionNo,
      this.selectedForm.schemaJson,
    ]);
    if (this.readonly) this.initialAnswers = this.answers;
    if (key === this.key) return;
    this.key = key;
    this.initialAnswers = this.answers;
    try {
      this.definition = this.selectedForm.schemaJson
        ? JSON.parse(this.selectedForm.schemaJson)
        : null;
    } catch {
      this.definition = null;
    }
  }
  validate() {
    return !!this.definition && !!this.renderer?.validate();
  }
  payload() {
    return this.renderer?.payload() ?? {};
  }
}
