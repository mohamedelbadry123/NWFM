import { Component, computed, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { Observable, finalize } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageService } from 'primeng/api';
import { TextareaModule } from 'primeng/textarea';

import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { ApiResult } from '../../../../core/api/api-result';
import { apiErrorMessage } from '../../../../core/api/api-error-message';
import { TasksService } from '../../../../core/tasks/tasks.service';
import { TaskListItem } from '../../../../core/tasks/tasks.models';

/** The transitions that take nothing but an optional note. */
export type TaskNoteAction = 'complete' | 'expire';

/** Approves or expires a task, with an optional note for the timeline. */
@Component({
  selector: 'app-task-action-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateContextDirective, ButtonModule, DialogModule, TextareaModule],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        (onShow)="note = ''"
        [header]="t('tasks.action.' + action() + '.title')"
        [modal]="true"
        [draggable]="false"
        [style]="{ width: '440px' }"
        [breakpoints]="{ '560px': '95vw' }"
      >
        @if (task(); as current) {
          <p class="mb-4 text-sm">{{ t('tasks.action.' + action() + '.confirm', { number: current.taskNumber }) }}</p>
        }

        <div class="flex flex-col gap-2">
          <label for="action-note" class="text-sm font-medium">{{ t('tasks.fields.note') }}</label>
          <textarea pTextarea id="action-note" [(ngModel)]="note" rows="3" maxlength="1000" class="w-full"></textarea>
        </div>

        <ng-template pTemplate="footer">
          <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" [disabled]="saving()" (onClick)="visible.set(false)" />
          <p-button
            [label]="t('tasks.actions.' + action())"
            [icon]="isExpire() ? 'pi pi-ban' : 'pi pi-check-circle'"
            [severity]="isExpire() ? 'danger' : 'success'"
            [loading]="saving()"
            [disabled]="saving()"
            (onClick)="submit()"
          />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class TaskActionDialogComponent {
  readonly visible = model.required<boolean>();
  readonly task = input<TaskListItem | null>(null);
  readonly action = input<TaskNoteAction>('complete');
  readonly completed = output<void>();

  private readonly tasksApi = inject(TasksService);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);

  protected note = '';
  protected readonly saving = signal(false);
  protected readonly isExpire = computed(() => this.action() === 'expire');

  protected submit(): void {
    const task = this.task();
    if (!task || this.saving()) {
      return;
    }

    const note = this.note.trim() || null;
    const request: Observable<ApiResult<unknown>> = this.isExpire()
      ? this.tasksApi.expire(task.id, note)
      : this.tasksApi.complete(task.id, note);

    this.saving.set(true);
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.messageService.add({
          severity: 'success',
          summary: this.translate.instant('common.success'),
          detail: this.translate.instant(`tasks.action.${this.action()}.success`),
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
