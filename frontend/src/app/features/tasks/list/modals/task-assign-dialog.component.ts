import { Component, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { finalize } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { MessageService } from 'primeng/api';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { TooltipModule } from 'primeng/tooltip';

import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { apiErrorMessage } from '../../../../core/api/api-error-message';
import { TasksService } from '../../../../core/tasks/tasks.service';
import { TaskListItem } from '../../../../core/tasks/tasks.models';

interface TeamOption {
  readonly label: string;
  readonly value: string;
  readonly activeTaskCount: number;
}

/**
 * Hands a task to a crew. Only crews whose territory covers the task are offered — the same rule the
 * assign endpoint enforces — each with the open tasks it already holds, so load can be spread.
 */
@Component({
  selector: 'app-task-assign-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslateContextDirective,
    ButtonModule,
    DatePickerModule,
    DialogModule,
    MessageModule,
    SelectModule,
    TagModule,
    TextareaModule,
    TooltipModule,
  ],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        (onShow)="onShow()"
        [header]="t('tasks.assign.title')"
        [modal]="true"
        [draggable]="true"
        [resizable]="false"
        [style]="{ width: '480px' }"
        [breakpoints]="{ '640px': '95vw' }"
      >
        @if (task(); as current) {
          <p class="mb-2 text-sm text-surface-500">{{ t('tasks.assign.subtitle', { number: current.taskNumber }) }}</p>
          @if (current.assignedTeamId) {
            <!-- Reassigning takes the task off the current crew; say so before they commit. -->
            <p-message
              severity="warn"
              [text]="t('tasks.assign.reassignHint', { team: current.assignedTeamName || '' })"
              styleClass="w-full mb-4"
            />
          }
        }

        <form [formGroup]="form" class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label for="assign-team" class="text-sm font-medium">
              {{ t('tasks.fields.team') }} <span class="text-red-500">*</span>
            </label>
            <p-select
              inputId="assign-team"
              formControlName="teamId"
              [options]="teams()"
              optionLabel="label"
              optionValue="value"
              [filter]="true"
              filterBy="label"
              [loading]="loadingTeams()"
              [placeholder]="t('tasks.assign.teamPlaceholder')"
              appendTo="body"
              styleClass="w-full"
            >
              <ng-template pTemplate="selectedItem" let-selected>
                <div class="flex items-center gap-2">
                  <span>{{ selected.label }}</span>
                  <p-tag [value]="selected.activeTaskCount" [severity]="selected.activeTaskCount > 0 ? 'info' : 'secondary'" />
                </div>
              </ng-template>
              <ng-template pTemplate="item" let-team>
                <div class="flex w-full items-center justify-between gap-3">
                  <span>{{ team.label }}</span>
                  <p-tag
                    [value]="team.activeTaskCount"
                    [severity]="team.activeTaskCount > 0 ? 'info' : 'secondary'"
                    [pTooltip]="t('tasks.assign.activeTasks', { count: team.activeTaskCount })"
                    tooltipPosition="left"
                  />
                </div>
              </ng-template>
            </p-select>
            @if (form.controls.teamId.touched && form.controls.teamId.invalid) {
              <small class="text-red-500">{{ t('tasks.validation.teamRequired') }}</small>
            }
            <!-- Only crews covering this task are listed; an empty list is a coverage gap. -->
            @if (!loadingTeams() && teams().length === 0) {
              <p-message severity="warn" [text]="t('tasks.assign.noEligibleTeams')" styleClass="w-full" />
            }
          </div>

          <div class="flex flex-col gap-2">
            <label for="assign-due" class="text-sm font-medium">{{ t('tasks.fields.fillDueDate') }}</label>
            <p-datepicker inputId="assign-due" formControlName="dueDate" dateFormat="yy-mm-dd" [showTime]="true" [showIcon]="true" [showClear]="true" appendTo="body" styleClass="w-full" />
          </div>

          <div class="flex flex-col gap-2">
            <label for="assign-completion" class="text-sm font-medium">{{ t('tasks.fields.completionDueDate') }}</label>
            <p-datepicker inputId="assign-completion" formControlName="completionDueDate" dateFormat="yy-mm-dd" [showTime]="true" [showIcon]="true" [showClear]="true" appendTo="body" styleClass="w-full" />
            <small class="text-surface-500">{{ t('tasks.assign.slaHint') }}</small>
          </div>

          <div class="flex flex-col gap-2">
            <label for="assign-note" class="text-sm font-medium">{{ t('tasks.fields.note') }}</label>
            <textarea pTextarea id="assign-note" formControlName="note" rows="3" class="w-full" [placeholder]="t('tasks.assign.notePlaceholder')"></textarea>
          </div>
        </form>

        <ng-template pTemplate="footer">
          <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" [disabled]="saving()" (onClick)="cancel()" />
          <p-button [label]="t('tasks.actions.assign')" icon="pi pi-send" [loading]="saving()" [disabled]="saving()" (onClick)="assign()" />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class TaskAssignDialogComponent {
  readonly visible = model.required<boolean>();
  readonly task = input<TaskListItem | null>(null);
  readonly assigned = output<void>();

  private readonly tasksApi = inject(TasksService);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly fb = inject(FormBuilder);

  protected readonly saving = signal(false);
  protected readonly loadingTeams = signal(false);
  protected readonly teams = signal<TeamOption[]>([]);

  protected readonly form = this.fb.group({
    teamId: this.fb.control<string | null>(null, Validators.required),
    dueDate: this.fb.control<Date | null>(null),
    completionDueDate: this.fb.control<Date | null>(null),
    note: this.fb.control<string>('', Validators.maxLength(1000)),
  });

  protected onShow(): void {
    const current = this.task();
    this.form.reset({
      teamId: null,
      // Deadlines left blank are set from the task type's SLA by the server, counted from now.
      dueDate: current?.dueDate ? new Date(current.dueDate) : null,
      completionDueDate: current?.completionDueDate ? new Date(current.completionDueDate) : null,
      note: '',
    });
    this.loadTeams(current?.id);
  }

  protected assign(): void {
    const taskId = this.task()?.id;
    if (this.form.invalid || this.saving() || !taskId) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.saving.set(true);

    this.tasksApi
      .assign(taskId, {
        teamId: value.teamId!,
        dueDate: value.dueDate?.toISOString() ?? null,
        completionDueDate: value.completionDueDate?.toISOString() ?? null,
        note: value.note?.trim() || null,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messageService.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('tasks.messages.assigned'),
          });
          this.visible.set(false);
          this.assigned.emit();
        },
        error: (error: unknown) => {
          this.messageService.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error, this.translate, 'tasks.messages.assignFailed'),
            life: 8000,
          });
        },
      });
  }

  protected cancel(): void {
    this.visible.set(false);
  }

  private loadTeams(taskId?: string): void {
    if (!taskId) {
      this.teams.set([]);
      return;
    }

    this.loadingTeams.set(true);
    this.tasksApi
      .eligibleTeams(taskId)
      .pipe(finalize(() => this.loadingTeams.set(false)))
      .subscribe({
        next: (res) =>
          this.teams.set(
            (res.value ?? []).map((team) => ({
              label: team.name,
              value: team.teamId,
              activeTaskCount: team.activeTaskCount,
            })),
          ),
        error: () => this.teams.set([]),
      });
  }
}
