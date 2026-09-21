import { Component, computed, inject, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { finalize } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { MessageService } from 'primeng/api';
import { SelectModule } from 'primeng/select';

import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../../core/i18n/locale.service';
import { apiErrorMessage } from '../../../../core/api/api-error-message';
import { LookupsService } from '../../../../core/lookups/lookups.service';
import { TasksService } from '../../../../core/tasks/tasks.service';
import { TaskTypesService } from '../../../../core/tasks/task-types.service';
import { TaskType } from '../../../../core/tasks/tasks.models';
import { OrgScopeSelectorComponent } from '../../../../shared/components/org-scope/org-scope-selector.component';
import { EMPTY_ORG_LOCATION, OrgLocation } from '../../../../shared/components/org-scope/org-scope.model';
import { GeoMapComponent } from '../../../../shared/components/geo-map/geo-map.component';
import type { GeoPoint } from '../../../../shared/components/geo-map/google-maps.types';
import { TASK_PRIORITIES, TaskPriority } from '../../task-status';

interface SelectOption {
  readonly label: string;
  readonly value: string;
}

/**
 * Raises a task by hand. Only active types whose form has a published version are offered — the
 * API refuses anything else, because a task pins a published version of its type's form.
 */
@Component({
  selector: 'app-task-create-dialog',
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
    SelectModule,
    OrgScopeSelectorComponent,
    GeoMapComponent,
  ],
  templateUrl: './task-create-dialog.component.html',
})
export class TaskCreateDialogComponent {
  readonly visible = model.required<boolean>();
  readonly created = output<void>();

  private readonly tasksApi = inject(TasksService);
  private readonly taskTypesApi = inject(TaskTypesService);
  private readonly lookups = inject(LookupsService);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);
  private readonly fb = inject(FormBuilder);

  protected readonly saving = signal(false);
  protected readonly loadingOptions = signal(false);
  protected readonly types = signal<TaskType[]>([]);
  protected readonly departments = signal<SelectOption[]>([]);

  protected readonly typeOptions = computed<SelectOption[]>(() =>
    this.types()
      // A type whose form has nothing published cannot raise a task; offering it only to be refused helps nobody.
      .filter((type) => type.formCurrentVersionNo !== null)
      .map((type) => ({ label: `${type.code} — ${this.name(type)}`, value: type.id })),
  );

  protected readonly priorityOptions = computed<SelectOption[]>(() =>
    TASK_PRIORITIES.map((value) => ({ label: this.translate.instant(`tasks.priority.${value}`), value })),
  );

  protected readonly location = signal<OrgLocation>({ ...EMPTY_ORG_LOCATION });
  protected readonly locationSeed = signal<OrgLocation>({ ...EMPTY_ORG_LOCATION });
  protected readonly locationPoint = signal<GeoPoint | null>(null);
  protected readonly locationTouched = signal(false);

  protected readonly form = this.fb.group({
    taskTypeId: this.fb.control<string | null>(null, Validators.required),
    taskNumber: this.fb.control<string>('', Validators.maxLength(60)),
    title: this.fb.control<string>('', Validators.maxLength(250)),
    externalReference: this.fb.control<string>('', Validators.maxLength(100)),
    priority: this.fb.control<string>(TaskPriority.Normal, Validators.required),
    departmentCode: this.fb.control<string | null>(null),
    notes: this.fb.control<string>('', Validators.maxLength(1000)),
    dueDate: this.fb.control<Date | null>(null),
    completionDueDate: this.fb.control<Date | null>(null),
  });

  /** The chosen type, for its form and SLA hint. */
  protected readonly selectedType = signal<TaskType | null>(null);

  protected onShow(): void {
    this.form.reset({ priority: TaskPriority.Normal });
    this.selectedType.set(null);
    this.location.set({ ...EMPTY_ORG_LOCATION });
    // A fresh identity is what tells the picker to clear its cascade for the new task.
    this.locationSeed.set({ ...EMPTY_ORG_LOCATION });
    this.locationPoint.set(null);
    this.locationTouched.set(false);
    this.loadOptions();
  }

  protected onTypeChange(typeId: string | null): void {
    const type = this.types().find((t) => t.id === typeId) ?? null;
    this.selectedType.set(type);

    // The type names the department its work belongs to; start there, and let the operator change it.
    if (type?.departmentCode && !this.form.controls.departmentCode.value) {
      this.form.controls.departmentCode.setValue(type.departmentCode);
    }
  }

  protected onLocationChange(location: OrgLocation): void {
    this.location.set(location);
  }

  protected onLocationPointChange(point: GeoPoint | null): void {
    this.locationPoint.set(point);
    this.locationTouched.set(true);
  }

  protected name(type: { nameEn: string | null; nameAr: string | null }): string {
    return (this.locale.locale() === 'ar' ? type.nameAr : type.nameEn) ?? '';
  }

  protected save(): void {
    const point = this.locationPoint();
    if (this.form.invalid || this.saving() || point == null) {
      this.form.markAllAsTouched();
      this.locationTouched.set(true);
      return;
    }

    const value = this.form.getRawValue();
    const location = this.location();
    this.saving.set(true);

    this.tasksApi
      .create({
        taskTypeId: value.taskTypeId!,
        taskNumber: value.taskNumber?.trim() || null,
        title: value.title?.trim() || null,
        externalReference: value.externalReference?.trim() || null,
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
            detail: this.translate.instant('tasks.messages.created'),
          });
          this.visible.set(false);
          this.created.emit();
        },
        error: (error: unknown) => {
          this.messageService.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error, this.translate, 'tasks.messages.createFailed'),
            life: 8000,
          });
        },
      });
  }

  protected cancel(): void {
    this.visible.set(false);
  }

  private loadOptions(): void {
    this.loadingOptions.set(true);
    let pending = 2;
    const done = () => {
      pending -= 1;
      if (pending === 0) {
        this.loadingOptions.set(false);
      }
    };

    this.taskTypesApi.active().subscribe({
      next: (res) => this.types.set(res.value ?? []),
      error: () => this.types.set([]),
      complete: done,
    });

    this.lookups.listAll('Department', { isActive: true }).subscribe({
      next: (items) =>
        this.departments.set(
          items.map((d) => ({ label: `${d.code} — ${this.name(d)}`, value: d.code })),
        ),
      error: () => this.departments.set([]),
      complete: done,
    });
  }
}
