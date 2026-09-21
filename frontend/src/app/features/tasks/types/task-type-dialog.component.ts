import { Component, computed, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { Observable, finalize } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { MessageService } from 'primeng/api';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';

import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../core/i18n/locale.service';
import { ApiResult } from '../../../core/api/api-result';
import { apiErrorMessage } from '../../../core/api/api-error-message';
import { LookupsService } from '../../../core/lookups/lookups.service';
import { TaskTypesService } from '../../../core/tasks/task-types.service';
import { FormOption, TaskType } from '../../../core/tasks/tasks.models';

interface SelectOption {
  readonly label: string;
  readonly value: string;
}

/**
 * Creates or edits a task type. Pointing a type at another form changes what new tasks are filled
 * with; tasks already raised keep the form and version they pinned.
 */
@Component({
  selector: 'app-task-type-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslateContextDirective,
    ButtonModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    MessageModule,
    SelectModule,
    TextareaModule,
  ],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        (onShow)="onShow()"
        [header]="t(isEdit() ? 'taskTypes.edit' : 'taskTypes.new')"
        [modal]="true"
        [draggable]="false"
        [style]="{ width: '40rem' }"
        [breakpoints]="{ '720px': '95vw' }"
      >
        <form [formGroup]="form" class="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-2">
            <label for="type-code" class="text-sm font-medium">{{ t('taskTypes.code') }} <span class="text-red-500">*</span></label>
            <input pInputText id="type-code" formControlName="code" class="w-full font-mono" [readonly]="isEdit()" />
            @if (form.controls.code.touched && form.controls.code.invalid) {
              <small class="text-red-500">{{ t('taskTypes.codeInvalid') }}</small>
            }
          </div>

          <div class="flex flex-col gap-2">
            <label for="type-department" class="text-sm font-medium">{{ t('tasks.fields.department') }}</label>
            <p-select
              inputId="type-department"
              formControlName="departmentCode"
              [options]="departments()"
              optionLabel="label"
              optionValue="value"
              [showClear]="true"
              [filter]="true"
              filterBy="label"
              appendTo="body"
              styleClass="w-full"
            />
          </div>

          <div class="flex flex-col gap-2">
            <label for="type-name-en" class="text-sm font-medium">{{ t('taskTypes.nameEn') }} <span class="text-red-500">*</span></label>
            <input pInputText id="type-name-en" formControlName="nameEn" class="w-full" />
          </div>

          <div class="flex flex-col gap-2">
            <label for="type-name-ar" class="text-sm font-medium">{{ t('taskTypes.nameAr') }} <span class="text-red-500">*</span></label>
            <input pInputText id="type-name-ar" formControlName="nameAr" class="w-full" dir="rtl" />
          </div>

          <div class="flex flex-col gap-2 sm:col-span-2">
            <label for="type-form" class="text-sm font-medium">{{ t('taskTypes.form') }} <span class="text-red-500">*</span></label>
            <p-select
              inputId="type-form"
              formControlName="formDefinitionId"
              [options]="formOptions()"
              optionLabel="label"
              optionValue="value"
              [filter]="true"
              filterBy="label"
              [loading]="loadingForms()"
              [placeholder]="t('taskTypes.formPlaceholder')"
              appendTo="body"
              styleClass="w-full"
            />
            <small class="text-surface-500">{{ t('taskTypes.formHint') }}</small>
            @if (isEdit() && formChanged()) {
              <p-message severity="info" [text]="t('taskTypes.formChangeHint')" styleClass="w-full" />
            }
          </div>

          <div class="flex flex-col gap-2">
            <label for="type-fill-sla" class="text-sm font-medium">{{ t('taskTypes.fillSla') }}</label>
            <p-inputNumber inputId="type-fill-sla" formControlName="fillSlaHours" [min]="1" [max]="8784" [showButtons]="false" suffix=" h" styleClass="w-full" inputStyleClass="w-full" />
          </div>

          <div class="flex flex-col gap-2">
            <label for="type-completion-sla" class="text-sm font-medium">{{ t('taskTypes.completionSla') }}</label>
            <p-inputNumber inputId="type-completion-sla" formControlName="completionSlaHours" [min]="1" [max]="8784" suffix=" h" styleClass="w-full" inputStyleClass="w-full" />
          </div>

          <div class="flex flex-col gap-2">
            <label for="type-desc-en" class="text-sm font-medium">{{ t('taskTypes.descriptionEn') }}</label>
            <textarea pTextarea id="type-desc-en" formControlName="descriptionEn" rows="2" class="w-full"></textarea>
          </div>

          <div class="flex flex-col gap-2">
            <label for="type-desc-ar" class="text-sm font-medium">{{ t('taskTypes.descriptionAr') }}</label>
            <textarea pTextarea id="type-desc-ar" formControlName="descriptionAr" rows="2" class="w-full" dir="rtl"></textarea>
          </div>
        </form>

        <ng-template pTemplate="footer">
          <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" [disabled]="saving()" (onClick)="visible.set(false)" />
          <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" [disabled]="saving()" (onClick)="save()" />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class TaskTypeDialogComponent {
  readonly visible = model.required<boolean>();
  readonly taskType = input<TaskType | null>(null);
  readonly saved = output<void>();

  private readonly api = inject(TaskTypesService);
  private readonly lookups = inject(LookupsService);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);
  private readonly fb = inject(FormBuilder);

  protected readonly saving = signal(false);
  protected readonly loadingForms = signal(false);
  protected readonly forms = signal<FormOption[]>([]);
  protected readonly departments = signal<SelectOption[]>([]);
  protected readonly selectedFormId = signal<string | null>(null);

  protected readonly isEdit = computed(() => this.taskType() !== null);
  protected readonly formChanged = computed(() => !!this.taskType() && this.selectedFormId() !== this.taskType()!.formDefinitionId);

  protected readonly formOptions = computed<SelectOption[]>(() => {
    const options = this.forms().map((form) => ({
      label: `${form.code} — ${this.locale.locale() === 'ar' ? form.nameAr : form.nameEn} (v${form.currentVersionNo})`,
      value: form.id,
    }));

    // The form a type already uses may no longer be offered (deprecated since); keep it selectable.
    const current = this.taskType();
    if (current && !options.some((o) => o.value === current.formDefinitionId)) {
      options.unshift({ label: current.formCode ?? current.formDefinitionId, value: current.formDefinitionId });
    }

    return options;
  });

  protected readonly form = this.fb.group({
    code: this.fb.control('', [Validators.required, Validators.maxLength(50), Validators.pattern(/^[A-Za-z0-9_-]+$/)]),
    nameEn: this.fb.control('', [Validators.required, Validators.maxLength(250)]),
    nameAr: this.fb.control('', [Validators.required, Validators.maxLength(250)]),
    descriptionEn: this.fb.control<string>('', Validators.maxLength(1000)),
    descriptionAr: this.fb.control<string>('', Validators.maxLength(1000)),
    formDefinitionId: this.fb.control<string | null>(null, Validators.required),
    departmentCode: this.fb.control<string | null>(null),
    fillSlaHours: this.fb.control<number | null>(null),
    completionSlaHours: this.fb.control<number | null>(null),
  });

  constructor() {
    this.form.controls.formDefinitionId.valueChanges.subscribe((value) => this.selectedFormId.set(value));
  }

  protected onShow(): void {
    const type = this.taskType();

    this.form.reset({
      code: type?.code ?? '',
      nameEn: type?.nameEn ?? '',
      nameAr: type?.nameAr ?? '',
      descriptionEn: type?.descriptionEn ?? '',
      descriptionAr: type?.descriptionAr ?? '',
      formDefinitionId: type?.formDefinitionId ?? null,
      departmentCode: type?.departmentCode ?? null,
      fillSlaHours: type?.fillSlaHours ?? null,
      completionSlaHours: type?.completionSlaHours ?? null,
    });

    this.loadingForms.set(true);
    this.api
      .formOptions()
      .pipe(finalize(() => this.loadingForms.set(false)))
      .subscribe({ next: (res) => this.forms.set(res.value ?? []), error: () => this.forms.set([]) });

    this.lookups.listAll('Department', { isActive: true }).subscribe({
      next: (items) =>
        this.departments.set(
          items.map((d) => ({ label: `${d.code} — ${this.locale.locale() === 'ar' ? d.nameAr : d.nameEn}`, value: d.code })),
        ),
    });
  }

  protected save(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const payload = {
      nameEn: value.nameEn!.trim(),
      nameAr: value.nameAr!.trim(),
      descriptionEn: value.descriptionEn?.trim() || null,
      descriptionAr: value.descriptionAr?.trim() || null,
      formDefinitionId: value.formDefinitionId!,
      departmentCode: value.departmentCode,
      fillSlaHours: value.fillSlaHours,
      completionSlaHours: value.completionSlaHours,
    };

    const current = this.taskType();
    const request: Observable<ApiResult<TaskType>> = current
      ? this.api.update(current.id, payload)
      : this.api.create({ ...payload, code: value.code!.trim() });

    this.saving.set(true);
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.messageService.add({
          severity: 'success',
          summary: this.translate.instant('common.success'),
          detail: this.translate.instant(current ? 'taskTypes.updated' : 'taskTypes.created'),
        });
        this.visible.set(false);
        this.saved.emit();
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
