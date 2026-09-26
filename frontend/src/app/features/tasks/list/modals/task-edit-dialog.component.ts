import { Component, computed, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { finalize } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { MessageModule } from 'primeng/message';
import { MessageService } from 'primeng/api';
import { SelectModule } from 'primeng/select';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../../core/i18n/locale.service';
import { apiErrorMessage } from '../../../../core/api/api-error-message';
import { LookupsService } from '../../../../core/lookups/lookups.service';
import { TasksService } from '../../../../core/tasks/tasks.service';
import { TaskListItem } from '../../../../core/tasks/tasks.models';
import { OrgScopeSelectorComponent } from '../../../../shared/components/org-scope/org-scope-selector.component';
import { EMPTY_ORG_LOCATION, OrgLocation } from '../../../../shared/components/org-scope/org-scope.model';
import { GeoMapComponent } from '../../../../shared/components/geo-map/geo-map.component';
import type { GeoPoint } from '../../../../shared/components/geo-map/google-maps.types';
import { TASK_PRIORITIES, TaskPriority, canReassign } from '../../task-status';

interface SelectOption {
  readonly label: string;
  readonly value: string;
}

/**
 * Corrects a task: its details until it closes, its place only until it is filled. The API replaces
 * every field it is sent, so the form is seeded from the task as it stands and sent back whole.
 */
@Component({
  selector: 'app-task-edit-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslateContextDirective,
    ButtonModule,
    DatePickerModule,
    DialogModule,
    InputTextModule,
    TextareaModule,
    MessageModule,
    ProgressSpinnerModule,
    SelectModule,
    OrgScopeSelectorComponent,
    GeoMapComponent,
  ],
  templateUrl: './task-edit-dialog.component.html',
})
export class TaskEditDialogComponent {
  readonly visible = model.required<boolean>();
  readonly task = input<TaskListItem | null>(null);
  readonly updated = output<void>();

  private readonly tasksApi = inject(TasksService);
  private readonly lookups = inject(LookupsService);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);
  private readonly fb = inject(FormBuilder);

  protected readonly saving = signal(false);
  protected readonly loading = signal(false);
  protected readonly departments = signal<SelectOption[]>([]);

  protected readonly priorityOptions = computed<SelectOption[]>(() =>
    TASK_PRIORITIES.map((value) => ({ label: this.translate.instant(`tasks.priority.${value}`), value })),
  );

  /** A filled task keeps its place: its answers describe that spot. */
  protected readonly canMove = computed(() => {
    const task = this.task();
    return !!task && canReassign(task.status, task.submissionCount);
  });

  protected readonly location = signal<OrgLocation>({ ...EMPTY_ORG_LOCATION });
  protected readonly locationSeed = signal<OrgLocation>({ ...EMPTY_ORG_LOCATION });
  protected readonly locationPoint = signal<GeoPoint | null>(null);

  protected readonly form = this.fb.group({
    title: this.fb.control<string>('', Validators.maxLength(250)),
    externalReference: this.fb.control<string>('', Validators.maxLength(100)),
    faId: this.fb.control<string>('', Validators.maxLength(50)),
    // Text rather than a number input: a ticket id is an identifier, and a spinner or grouping would mangle it.
    wfmTicketId: this.fb.control<string>('', Validators.pattern(/^d{1,15}$/)),
    priority: this.fb.control<string>(TaskPriority.Normal, Validators.required),
    departmentCode: this.fb.control<string | null>(null),
    notes: this.fb.control<string>('', Validators.maxLength(1000)),
    dueDate: this.fb.control<Date | null>(null),
    completionDueDate: this.fb.control<Date | null>(null),
  });

  protected onShow(): void {
    const task = this.task();
    if (!task) {
      return;
    }

    this.form.reset({
      title: task.title ?? '',
      externalReference: task.externalReference ?? '',
      faId: task.faId ?? '',
      wfmTicketId: task.wfmTicketId != null ? String(task.wfmTicketId) : '',
      priority: task.priority,
      departmentCode: task.departmentCode,
      notes: '',
      dueDate: task.dueDate ? new Date(task.dueDate) : null,
      completionDueDate: task.completionDueDate ? new Date(task.completionDueDate) : null,
    });

    const location: OrgLocation = {
      clusterCode: null,
      cbuCode: task.cbuCode,
      branchCode: task.branchCode,
      operationAreaCode: task.operationAreaCode,
    };
    this.location.set(location);
    // The picker works out the cluster from the CBU; a new identity makes it reseed.
    this.locationSeed.set({ ...location });
    this.locationPoint.set({ lat: task.latitude, lng: task.longitude, address: task.address });

    // Notes are not on the grid row; the detail has them.
    this.loading.set(true);
    this.tasksApi
      .get(task.id)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (res) => this.form.controls.notes.setValue(res.value?.notes ?? '') });

    this.lookups.listAll('Department', { isActive: true }).subscribe({
      next: (items) =>
        this.departments.set(
          items.map((d) => ({
            label: `${d.code} — ${this.locale.locale() === 'ar' ? d.nameAr : d.nameEn}`,
            value: d.code,
          })),
        ),
    });
  }

  protected onLocationChange(location: OrgLocation): void {
    this.location.set(location);
  }

  protected onLocationPointChange(point: GeoPoint | null): void {
    this.locationPoint.set(point);
  }

  protected save(): void {
    const task = this.task();
    const point = this.locationPoint();
    if (!task || this.form.invalid || this.saving() || point == null) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const location = this.location();
    this.saving.set(true);

    this.tasksApi
      .update(task.id, {
        title: value.title?.trim() || null,
        externalReference: value.externalReference?.trim() || null,
        faId: value.faId?.trim() || null,
        wfmTicketId: value.wfmTicketId?.trim() ? Number(value.wfmTicketId.trim()) : null,
        priority: value.priority ?? TaskPriority.Normal,
        notes: value.notes?.trim() || null,
        latitude: point.lat,
        longitude: point.lng,
        address: point.address ?? null,
        cbuCode: location.cbuCode,
        branchCode: location.branchCode,
        operationAreaCode: location.operationAreaCode,
        departmentCode: value.departmentCode,
        dueDate: value.dueDate?.toISOString() ?? null,
        completionDueDate: value.completionDueDate?.toISOString() ?? null,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messageService.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('tasks.messages.updated'),
          });
          this.visible.set(false);
          this.updated.emit();
        },
        error: (error: unknown) => {
          this.messageService.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error, this.translate, 'tasks.messages.updateFailed'),
            life: 8000,
          });
        },
      });
  }

  protected cancel(): void {
    this.visible.set(false);
  }
}
