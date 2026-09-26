import { Component, computed, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { finalize } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageService } from 'primeng/api';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';

import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { apiErrorMessage } from '../../../../core/api/api-error-message';
import { TasksService } from '../../../../core/tasks/tasks.service';
import { TaskListItem } from '../../../../core/tasks/tasks.models';
import { TASK_RETURN_REASONS } from '../../task-status';

interface SelectOption {
  readonly label: string;
  readonly value: string;
}

/**
 * Sends a fill back for rework: a reason code (so returns can be counted by cause), what needs
 * doing, and optionally another crew to do it. Only crews covering the task are offered for that.
 */
@Component({
  selector: 'app-task-return-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslateContextDirective, ButtonModule, DialogModule, SelectModule, TextareaModule],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        (onShow)="onShow()"
        [header]="t('tasks.return.title')"
        [modal]="true"
        [draggable]="false"
        [style]="{ width: '500px' }"
        [breakpoints]="{ '620px': '95vw' }"
      >
        @if (task(); as current) {
          <p class="mb-4 text-sm text-surface-500">{{ t('tasks.return.subtitle', { number: current.taskNumber }) }}</p>
        }

        <form [formGroup]="form" class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label for="return-reason-code" class="text-sm font-medium">
              {{ t('tasks.fields.returnReason') }} <span class="text-red-500">*</span>
            </label>
            <p-select
              inputId="return-reason-code"
              formControlName="reasonCode"
              [options]="reasonOptions()"
              optionLabel="label"
              optionValue="value"
              [placeholder]="t('tasks.return.reasonPlaceholder')"
              appendTo="body"
              styleClass="w-full"
            />
            @if (form.controls.reasonCode.touched && form.controls.reasonCode.invalid) {
              <small class="text-red-500">{{ t('tasks.validation.reasonRequired') }}</small>
            }
          </div>

          <div class="flex flex-col gap-2">
            <label for="return-reason" class="text-sm font-medium">
              {{ t('tasks.return.whatToDo') }} <span class="text-red-500">*</span>
            </label>
            <textarea pTextarea id="return-reason" formControlName="reason" rows="3" maxlength="1000" class="w-full"></textarea>
            @if (form.controls.reason.touched && form.controls.reason.invalid) {
              <small class="text-red-500">{{ t('tasks.validation.returnNoteRequired') }}</small>
            }
          </div>

          <div class="flex flex-col gap-2">
            <label for="return-team" class="text-sm font-medium">{{ t('tasks.return.reassignTo') }}</label>
            <p-select
              inputId="return-team"
              formControlName="reassignToTeamId"
              [options]="teams()"
              optionLabel="label"
              optionValue="value"
              [showClear]="true"
              [filter]="true"
              filterBy="label"
              [loading]="loadingTeams()"
              [placeholder]="t('tasks.return.sameTeam')"
              appendTo="body"
              styleClass="w-full"
            />
          </div>
        </form>

        <ng-template pTemplate="footer">
          <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" [disabled]="saving()" (onClick)="visible.set(false)" />
          <p-button [label]="t('tasks.actions.return')" icon="pi pi-replay" severity="danger" [loading]="saving()" [disabled]="saving()" (onClick)="submit()" />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class TaskReturnDialogComponent {
  readonly visible = model.required<boolean>();
  readonly task = input<TaskListItem | null>(null);
  readonly completed = output<void>();

  private readonly tasksApi = inject(TasksService);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly fb = inject(FormBuilder);

  protected readonly saving = signal(false);
  protected readonly loadingTeams = signal(false);
  protected readonly teams = signal<SelectOption[]>([]);

  protected readonly reasonOptions = computed<SelectOption[]>(() =>
    TASK_RETURN_REASONS.map((value) => ({ label: this.translate.instant(`tasks.returnReason.${value}`), value })),
  );

  protected readonly form = this.fb.group({
    reasonCode: this.fb.control<string | null>(null, Validators.required),
    reason: this.fb.control<string>('', [Validators.required, Validators.maxLength(1000)]),
    reassignToTeamId: this.fb.control<string | null>(null),
  });

  protected onShow(): void {
    this.form.reset({ reasonCode: null, reason: '', reassignToTeamId: null });

    const task = this.task();
    this.teams.set([]);
    if (!task) {
      return;
    }

    this.loadingTeams.set(true);
    this.tasksApi
      .eligibleTeams(task.id)
      .pipe(finalize(() => this.loadingTeams.set(false)))
      .subscribe({
        // The crew that holds it now is the default; offering it again as "another team" is noise.
        next: (res) =>
          this.teams.set(
            (res.value ?? [])
              .filter((team) => team.teamId !== task.assignedTeamId)
              .map((team) => ({ label: team.name, value: team.teamId })),
          ),
        error: () => this.teams.set([]),
      });
  }

  protected submit(): void {
    const task = this.task();
    if (!task || this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.saving.set(true);

    this.tasksApi
      .returnTask(task.id, {
        reasonCode: value.reasonCode!,
        reason: value.reason!.trim(),
        reassignToTeamId: value.reassignToTeamId,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messageService.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('tasks.messages.returned'),
          });
          this.visible.set(false);
          this.completed.emit();
        },
        error: (error: unknown) => {
          this.messageService.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error, this.translate),
            life: 8000,
          });
        },
      });
  }
}
