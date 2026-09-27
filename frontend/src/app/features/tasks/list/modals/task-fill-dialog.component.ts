import { Component, computed, effect, inject, input, model, output, signal, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { catchError, finalize, forkJoin, of } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { MessageService } from 'primeng/api';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectButtonModule } from 'primeng/selectbutton';
import { TagModule } from 'primeng/tag';

import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../../core/i18n/locale.service';
import { apiErrorMessage } from '../../../../core/api/api-error-message';
import { TasksService } from '../../../../core/tasks/tasks.service';
import { TaskDetail, TaskFill, TaskForm } from '../../../../core/tasks/tasks.models';
import { DynamicFormRendererComponent } from '../../../../shared/components/dynamic-form/dynamic-form-renderer.component';
import { TaskStatus, taskStatusSeverity } from '../../task-status';

/** What a task's fills are filed under in the form engine. */
export const TASK_FORM_CONTEXT = 'Task';

interface FormTab {
  readonly value: string;
  readonly label: string;
  readonly filled: boolean;
}

/**
 * Fills the task's forms — each at the version pinned to the task, not the form's latest. With
 * several forms a switcher shows which are filled; after one is sent the next unfilled one opens,
 * and the dialog closes once every form is. Each form opens on its previous fill, so a second pass
 * edits the answers instead of retyping them. Media fields upload as they are picked, filed under
 * this task.
 */
@Component({
  selector: 'app-task-fill-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslateContextDirective,
    ButtonModule,
    DialogModule,
    MessageModule,
    ProgressSpinnerModule,
    SelectButtonModule,
    TagModule,
    DynamicFormRendererComponent,
  ],
  templateUrl: './task-fill-dialog.component.html',
})
export class TaskFillDialogComponent {
  readonly visible = model.required<boolean>();
  readonly taskId = input<string | null>(null);
  /** The form to open on; the first unfilled one when not given. */
  readonly formId = input<string | null>(null);
  readonly filled = output<void>();

  protected readonly renderer = viewChild(DynamicFormRendererComponent);

  private readonly tasksApi = inject(TasksService);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);

  protected readonly contextType = TASK_FORM_CONTEXT;
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly detail = signal<TaskDetail | null>(null);

  /** Every fill of every form, newest first within each form. */
  private readonly fills = signal<TaskFill[]>([]);

  /** The form being filled now. */
  protected readonly selectedFormId = signal<string | null>(null);

  protected readonly forms = computed<TaskForm[]>(() =>
    [...(this.detail()?.task.forms ?? [])].sort((a, b) => a.sortOrder - b.sortOrder));

  protected readonly selectedForm = computed<TaskForm | null>(() =>
    this.forms().find((f) => f.formDefinitionId === this.selectedFormId()) ?? null);

  protected readonly formTabs = computed<FormTab[]>(() =>
    this.forms().map((f, i) => ({
      value: f.formDefinitionId,
      label: `${i + 1}. ${this.formLabel(f)}`,
      filled: f.submissionCount > 0,
    })));

  /** The selected form, parsed once per selection and handed to the renderer. */
  protected readonly definition = computed<Record<string, unknown> | null>(() => {
    const json = this.selectedForm()?.schemaJson;
    if (!json) {
      return null;
    }

    try {
      return JSON.parse(json) as Record<string, unknown>;
    } catch {
      return null;
    }
  });

  /** An unparseable or missing form is a failure, not an empty form — say so rather than show nothing. */
  protected readonly definitionFailed = computed(() => this.selectedForm() !== null && this.definition() === null);

  /** The selected form's previous fill, so a second pass edits the answers instead of retyping them. */
  protected readonly previousFill = computed<TaskFill | null>(() =>
    this.fills().find((f) => f.formDefinitionId === this.selectedFormId()) ?? null);

  protected readonly seedAnswers = computed<Record<string, unknown> | null>(() => this.previousFill()?.answers ?? null);

  protected readonly statusSeverity = taskStatusSeverity;

  protected readonly formName = computed(() => {
    const form = this.selectedForm();
    return form ? this.formLabel(form) : '';
  });

  /**
   * The key each form's fill is sent under. Made once per form per opening and kept across retries:
   * if the answers were stored but the reply was lost, sending again under the same key is answered
   * with the stored fill rather than recording a second one.
   */
  private readonly clientSubmissionIds = new Map<string, string>();

  constructor() {
    effect(() => {
      const id = this.taskId();
      if (this.visible() && id) {
        this.load(id, this.formId());
      }
    });
  }

  protected selectForm(formId: string | null): void {
    if (formId && formId !== this.selectedFormId()) {
      this.selectedFormId.set(formId);
    }
  }

  private formLabel(form: TaskForm): string {
    return (this.locale.locale() === 'ar' ? form.nameAr : form.nameEn) ?? form.code ?? '';
  }

  /**
   * The forms and the answers that go in them arrive together — building a form first and seeding
   * it afterwards would rebuild it under the respondent a moment after it appeared. Previous fills
   * that fail to load are not fatal: the forms still open, just blank.
   */
  private load(id: string, openOn: string | null): void {
    this.detail.set(null);
    this.fills.set([]);
    this.selectedFormId.set(null);
    this.loadFailed.set(false);
    this.loading.set(true);
    this.clientSubmissionIds.clear();

    forkJoin({
      detail: this.tasksApi.get(id),
      fills: this.tasksApi.fills(id).pipe(catchError(() => of(null))),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ detail, fills }) => {
          const task = detail.value ?? null;
          this.detail.set(task);
          this.fills.set(fills?.value ?? []);

          const forms = this.forms();
          if (forms.length === 0) {
            this.loadFailed.set(true);
            return;
          }

          const start = forms.find((f) => f.formDefinitionId === openOn)
            ?? forms.find((f) => f.isRequired && f.submissionCount === 0)
            ?? forms[0];
          this.selectedFormId.set(start.formDefinitionId);
        },
        error: () => this.loadFailed.set(true),
      });
  }

  private submissionKey(formId: string): string {
    let key = this.clientSubmissionIds.get(formId);
    if (!key) {
      key = crypto.randomUUID();
      this.clientSubmissionIds.set(formId, key);
    }
    return key;
  }

  protected submit(): void {
    const detail = this.detail();
    const form = this.selectedForm();
    const renderer = this.renderer();
    if (this.saving() || !renderer || !detail || !form) {
      return;
    }

    if (!renderer.validate()) {
      this.messageService.add({
        severity: 'warn',
        summary: this.translate.instant('common.error'),
        detail: this.translate.instant('tasks.fill.invalid'),
      });
      return;
    }

    this.saving.set(true);
    this.tasksApi
      .fill(detail.task.id, {
        formDefinitionId: form.formDefinitionId,
        clientSubmissionId: this.submissionKey(form.formDefinitionId),
        clientFilledAt: new Date().toISOString(),
        answers: renderer.payload(),
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (res) => {
          const result = res.value;
          this.filled.emit();

          // Forms still waiting: say how far along the task is and open the next one.
          if (result && result.status !== TaskStatus.Submitted && result.filledFormCount < result.requiredFormCount) {
            this.messageService.add({
              severity: 'success',
              summary: this.translate.instant('common.success'),
              detail: this.translate.instant('tasks.fill.formSaved', {
                filled: result.filledFormCount,
                total: result.requiredFormCount,
              }),
            });

            const next = this.forms().find((f) =>
              f.formDefinitionId !== form.formDefinitionId && f.isRequired && f.submissionCount === 0);
            this.load(detail.task.id, next?.formDefinitionId ?? null);
            return;
          }

          this.messageService.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('tasks.messages.filled'),
          });
          this.visible.set(false);
        },
        error: (error: unknown) => {
          this.messageService.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error, this.translate, 'tasks.messages.fillFailed'),
            life: 8000,
          });
        },
      });
  }

  protected cancel(): void {
    this.visible.set(false);
  }
}
