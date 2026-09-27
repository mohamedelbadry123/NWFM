import { Component, computed, effect, inject, input, model, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { catchError, finalize, forkJoin, of } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectButtonModule } from 'primeng/selectbutton';
import { TagModule } from 'primeng/tag';

import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../../core/i18n/locale.service';
import { TasksService } from '../../../../core/tasks/tasks.service';
import { TaskDetail, TaskFill, TaskForm } from '../../../../core/tasks/tasks.models';
import { DynamicFormRendererComponent } from '../../../../shared/components/dynamic-form/dynamic-form-renderer.component';
import { taskStatusSeverity } from '../../task-status';

/**
 * The filled form, shown exactly as it was filled and with nothing editable.
 *
 * Separate from the fill dialog rather than a flag on it, because the two are open at different
 * times: filling closes once the task is approved or expired, while reading stays open for good —
 * and is most wanted then, or when a reviewer wants to see what a returned fill actually said.
 *
 * The details dialog lists the same answers as label/value pairs. This shows the form instead: the
 * grouping, the order and the conditional sections are part of what the crew saw, and an answer
 * read outside them can be read wrongly. The version drawn is the one the task pins; a task with
 * several forms shows one at a time.
 */
@Component({
  selector: 'app-task-preview-dialog',
  standalone: true,
  imports: [
    DatePipe,
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
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [header]="t('tasks.preview.title')"
        [modal]="true"
        [draggable]="true"
        [maximizable]="true"
        [style]="{ width: '68rem' }"
        [contentStyle]="{ overflow: 'auto' }"
        [breakpoints]="{ '960px': '95vw' }"
      >
        @if (loading()) {
          <div class="flex justify-center py-10">
            <p-progressSpinner styleClass="w-10 h-10" strokeWidth="4" />
          </div>
        } @else if (loadFailed()) {
          <p-message severity="error" [text]="t('tasks.preview.loadFailed')" styleClass="w-full" />
        } @else if (detail(); as d) {
          <div class="mb-3 flex flex-wrap items-center gap-2 text-sm">
            <span class="font-mono font-medium">{{ d.task.taskNumber }}</span>
            <p-tag [value]="t('tasks.status.' + d.task.status)" [severity]="statusSeverity(d.task.status)" />
            @if (formName()) {
              <span class="text-[var(--p-text-muted-color)]">
                {{ formName() }}
                <span class="app-badge app-badge--code ms-1">v{{ selectedForm()?.versionNo }}</span>
              </span>
            }
          </div>

          @if (formTabs().length > 1) {
            <p-selectbutton
              class="mb-3 block"
              [options]="formTabs()"
              optionLabel="label"
              optionValue="value"
              [allowEmpty]="false"
              [ngModel]="selectedFormId()"
              (ngModelChange)="pickForm($event)"
            />
          }

          @if (fill(); as shown) {
            <p-message
              severity="info"
              [text]="
                t('tasks.preview.filledBy', {
                  by: shown.submittedByName || shown.submittedBy || '—',
                  date: (shown.submittedDate | date: 'yyyy-MM-dd HH:mm') || '',
                })
              "
              styleClass="w-full mb-3"
            />
          } @else {
            <p-message severity="warn" [text]="t('tasks.preview.noFill')" styleClass="w-full mb-3" />
          }

          <p class="m-0 mb-4 text-sm text-[var(--p-text-muted-color)]">
            <i class="pi pi-eye me-1.5 text-[var(--p-primary-color)]"></i>
            {{ t('tasks.preview.subtitle') }}
          </p>

          <!-- Read-only, and with no form or context ids: those are what turn a media field into an
               uploader, and nothing here may write. -->
          @for (key of [selectedFormId()]; track key) {
            <app-dynamic-form-renderer
              [definition]="definition()"
              [answers]="fill()?.answers ?? null"
              [readOnly]="true"
              [emptyMessage]="t('tasks.fill.empty')"
            />
          }
        }

        <ng-template pTemplate="footer">
          <p-button [label]="t('common.close')" severity="secondary" [text]="true" (onClick)="visible.set(false)" />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class TaskPreviewDialogComponent {
  readonly visible = model.required<boolean>();
  readonly taskId = input<string | null>(null);

  /** The fill to show; the newest of the form shown when unset. */
  readonly submissionId = input<string | null>(null);

  /** The form to show; the one the fill answered, else the first with a fill, else the first. */
  readonly formId = input<string | null>(null);

  private readonly tasksApi = inject(TasksService);
  private readonly locale = inject(LocaleService);

  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly detail = signal<TaskDetail | null>(null);
  private readonly fills = signal<TaskFill[]>([]);

  /** The form shown; picked by the reader when the task has several. */
  protected readonly selectedFormId = signal<string | null>(null);

  /** Once the reader switches form, the fill asked for no longer applies. */
  private readonly followSubmission = signal(true);

  protected readonly statusSeverity = taskStatusSeverity;

  private readonly forms = computed<TaskForm[]>(() =>
    [...(this.detail()?.task.forms ?? [])].sort((a, b) => a.sortOrder - b.sortOrder));

  protected readonly selectedForm = computed<TaskForm | null>(() =>
    this.forms().find((f) => f.formDefinitionId === this.selectedFormId()) ?? null);

  protected readonly formTabs = computed(() =>
    this.forms().map((f, i) => ({ value: f.formDefinitionId, label: `${i + 1}. ${this.nameOf(f)}` })));

  /** A form never filled gets a message rather than an empty form pretending to be an answer. */
  protected readonly fill = computed<TaskFill | null>(() => {
    const fills = this.fills().filter((f) => f.formDefinitionId === this.selectedFormId());
    const wanted = this.followSubmission() ? this.submissionId() : null;
    return (wanted ? fills.find((f) => f.submissionId === wanted) : null) ?? fills[0] ?? null;
  });

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

  protected readonly formName = computed(() => {
    const form = this.selectedForm();
    return form ? this.nameOf(form) : '';
  });

  protected pickForm(formId: string | null): void {
    if (formId) {
      this.followSubmission.set(false);
      this.selectedFormId.set(formId);
    }
  }

  private nameOf(form: TaskForm): string {
    const name = this.locale.locale() === 'ar' ? form.nameAr || form.nameEn : form.nameEn || form.nameAr;
    return name || form.code || '';
  }

  constructor() {
    effect(() => {
      const id = this.taskId();
      if (this.visible() && id) {
        this.load(id);
      }
    });
  }

  private load(id: string): void {
    this.detail.set(null);
    this.fills.set([]);
    this.selectedFormId.set(null);
    this.followSubmission.set(true);
    this.loadFailed.set(false);
    this.loading.set(true);

    forkJoin({
      detail: this.tasksApi.get(id),
      fills: this.tasksApi.fills(id).pipe(catchError(() => of(null))),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ detail, fills }) => {
          this.detail.set(detail.value ?? null);
          const all = fills?.value ?? [];
          this.fills.set(all);

          const forms = this.forms();
          const asked = this.submissionId();
          const start = all.find((f) => f.submissionId === asked)?.formDefinitionId
            ?? forms.find((f) => f.formDefinitionId === this.formId())?.formDefinitionId
            ?? all[0]?.formDefinitionId
            ?? forms[0]?.formDefinitionId
            ?? null;
          this.selectedFormId.set(start);
        },
        error: () => this.loadFailed.set(true),
      });
  }
}
