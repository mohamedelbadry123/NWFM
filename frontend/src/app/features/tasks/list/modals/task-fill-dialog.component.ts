import { Component, computed, effect, inject, input, model, output, signal, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateService } from '@ngx-translate/core';
import { catchError, finalize, forkJoin, of } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { MessageService } from 'primeng/api';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TagModule } from 'primeng/tag';

import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../../core/i18n/locale.service';
import { apiErrorMessage } from '../../../../core/api/api-error-message';
import { TasksService } from '../../../../core/tasks/tasks.service';
import { TaskDetail, TaskFill } from '../../../../core/tasks/tasks.models';
import { DynamicFormRendererComponent } from '../../../../shared/components/dynamic-form/dynamic-form-renderer.component';
import { taskStatusSeverity } from '../../task-status';

/** What a task's fills are filed under in the form engine. */
export const TASK_FORM_CONTEXT = 'Task';

/**
 * Fills the task's form — the version pinned to the task, not the form's latest. The form opens on
 * the previous fill, so a second pass edits the answers instead of retyping them. Media fields upload
 * as they are picked, filed under this task.
 */
@Component({
  selector: 'app-task-fill-dialog',
  standalone: true,
  imports: [
    CommonModule,
    TranslateContextDirective,
    ButtonModule,
    DialogModule,
    MessageModule,
    ProgressSpinnerModule,
    TagModule,
    DynamicFormRendererComponent,
  ],
  templateUrl: './task-fill-dialog.component.html',
})
export class TaskFillDialogComponent {
  readonly visible = model.required<boolean>();
  readonly taskId = input<string | null>(null);
  readonly filled = output<void>();

  private readonly renderer = viewChild(DynamicFormRendererComponent);

  private readonly tasksApi = inject(TasksService);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);

  protected readonly contextType = TASK_FORM_CONTEXT;
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly detail = signal<TaskDetail | null>(null);

  /** The pinned form, parsed once per load and handed to the renderer. */
  protected readonly definition = signal<Record<string, unknown> | null>(null);

  /** The previous fill, so a second pass edits the answers instead of retyping them. */
  protected readonly previousFill = signal<TaskFill | null>(null);

  protected readonly seedAnswers = computed<Record<string, unknown> | null>(() => this.previousFill()?.answers ?? null);

  protected readonly statusSeverity = taskStatusSeverity;

  protected readonly formName = computed(() => {
    const detail = this.detail();
    return (this.locale.locale() === 'ar' ? detail?.formNameAr : detail?.formNameEn) ?? detail?.formCode ?? '';
  });

  /**
   * The key this fill is sent under. Made once per opening and kept across retries: if the answers
   * were stored but the reply was lost, sending again under the same key is answered with the stored
   * fill rather than recording a second one.
   */
  private clientSubmissionId = '';

  constructor() {
    effect(() => {
      const id = this.taskId();
      if (this.visible() && id) {
        this.load(id);
      }
    });
  }

  /**
   * The form and the answers that go in it arrive together — building the form first and seeding it
   * afterwards would rebuild it under the respondent a moment after it appeared. Previous fills that
   * fail to load are not fatal: the form still opens, just blank.
   */
  private load(id: string): void {
    this.detail.set(null);
    this.definition.set(null);
    this.previousFill.set(null);
    this.loadFailed.set(false);
    this.loading.set(true);
    this.clientSubmissionId = crypto.randomUUID();

    forkJoin({
      detail: this.tasksApi.get(id),
      fills: this.tasksApi.fills(id).pipe(catchError(() => of(null))),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ detail, fills }) => {
          const task = detail.value ?? null;
          this.detail.set(task);
          this.definition.set(this.parseDefinition(task?.schemaJson));
          this.previousFill.set(fills?.value?.[0] ?? null);
        },
        error: () => this.loadFailed.set(true),
      });
  }

  /** An unparseable form is a load failure, not an empty form — say so rather than show nothing. */
  private parseDefinition(schemaJson?: string | null): Record<string, unknown> | null {
    if (!schemaJson) {
      this.loadFailed.set(true);
      return null;
    }

    try {
      return JSON.parse(schemaJson) as Record<string, unknown>;
    } catch {
      this.loadFailed.set(true);
      return null;
    }
  }

  protected submit(): void {
    const detail = this.detail();
    const renderer = this.renderer();
    if (this.saving() || !renderer || !detail) {
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
        clientSubmissionId: this.clientSubmissionId,
        clientFilledAt: new Date().toISOString(),
        answers: renderer.payload(),
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messageService.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('tasks.messages.filled'),
          });
          this.visible.set(false);
          this.filled.emit();
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
