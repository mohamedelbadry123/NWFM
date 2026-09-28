import { Component, computed, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
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
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { TooltipModule } from 'primeng/tooltip';

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

/** A max the server enforces too (`TaskType.MaxForms`). */
const MAX_FORMS = 10;

/**
 * Creates or edits a task type. A type lists the forms its tasks are filled with, in the order a
 * crew meets them; changing them changes what new tasks are filled with, while tasks already raised
 * keep the forms and versions they pinned.
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
    ToggleSwitchModule,
    TooltipModule,
    FormsModule,
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
            <label for="type-form" class="text-sm font-medium">{{ t('taskTypes.forms') }} <span class="text-red-500">*</span></label>

            @if (selectedForms().length > 0) {
              <ol class="flex flex-col gap-1.5">
                @for (entry of selectedForms(); track entry.value; let i = $index; let first = $first; let last = $last) {
                  <li class="flex items-center gap-2 rounded-lg border border-[var(--app-border)] px-2 py-1.5">
                    <span class="app-badge app-badge--code">{{ i + 1 }}</span>
                    <span class="min-w-0 flex-1 truncate text-sm">{{ entry.label }}</span>
                    @if (closesC2m()) {
                      <p-button
                        [icon]="closingFormId() === entry.value ? 'pi pi-flag-fill' : 'pi pi-flag'"
                        [severity]="closingFormId() === entry.value ? 'warn' : 'secondary'"
                        size="small"
                        [text]="true"
                        [rounded]="true"
                        [ariaLabel]="t('taskTypes.closingForm')"
                        [pTooltip]="t('taskTypes.closingForm')"
                        (onClick)="closingFormId.set(entry.value)"
                      />
                    }
                    <p-button icon="pi pi-arrow-up" severity="secondary" size="small" [text]="true" [rounded]="true" [disabled]="first" [ariaLabel]="t('common.moveUp')" (onClick)="moveForm(i, -1)" />
                    <p-button icon="pi pi-arrow-down" severity="secondary" size="small" [text]="true" [rounded]="true" [disabled]="last" [ariaLabel]="t('common.moveDown')" (onClick)="moveForm(i, 1)" />
                    <p-button icon="pi pi-times" severity="danger" size="small" [text]="true" [rounded]="true" [ariaLabel]="t('common.remove')" (onClick)="removeForm(entry.value)" />
                  </li>
                }
              </ol>
            }

            @if (selectedForms().length < maxForms) {
              <p-select
                inputId="type-form"
                [options]="addableForms()"
                optionLabel="label"
                optionValue="value"
                [filter]="true"
                filterBy="label"
                [loading]="loadingForms()"
                [placeholder]="t('taskTypes.addForm')"
                [ngModel]="null"
                [ngModelOptions]="{ standalone: true }"
                (onChange)="addForm($event.value)"
                appendTo="body"
                styleClass="w-full"
              />
            }

            @if (formsTouched() && selectedForms().length === 0) {
              <small class="text-red-500">{{ t('taskTypes.formsRequired') }}</small>
            }
            <small class="text-surface-500">{{ t('taskTypes.formsHint') }}</small>
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

          <div class="flex items-start gap-3 rounded-lg border border-[var(--app-border)] p-3 sm:col-span-2">
            <p-toggleswitch inputId="type-closes-c2m" formControlName="closesC2mActivity" />
            <div class="flex flex-col gap-1">
              <label for="type-closes-c2m" class="text-sm font-medium">{{ t('taskTypes.closesC2mActivity') }}</label>
              <small class="text-surface-500">{{ t('taskTypes.closesC2mActivityHint') }}</small>
            </div>
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
  protected readonly maxForms = MAX_FORMS;

  /** The type's forms, in order. Labels are kept with them so a form no longer offered (deprecated since) still reads. */
  protected readonly selectedForms = signal<SelectOption[]>([]);
  protected readonly closingFormId = signal<string | null>(null);
  protected readonly closesC2m = signal(false);
  protected readonly formsTouched = signal(false);

  protected readonly isEdit = computed(() => this.taskType() !== null);
  protected readonly formChanged = computed(() => {
    const type = this.taskType();
    if (!type) {
      return false;
    }

    const before = [...type.forms].sort((a, b) => a.sortOrder - b.sortOrder).map((f) => f.formDefinitionId);
    const after = this.selectedForms().map((f) => f.value);
    return before.length !== after.length || before.some((id, i) => id !== after[i]);
  });

  private readonly formOptions = computed<SelectOption[]>(() =>
    this.forms().map((form) => ({
      label: `${form.code} — ${this.locale.locale() === 'ar' ? form.nameAr : form.nameEn} (v${form.currentVersionNo})`,
      value: form.id,
    })));

  /** Published forms not on the type yet. */
  protected readonly addableForms = computed<SelectOption[]>(() => {
    const chosen = new Set(this.selectedForms().map((f) => f.value));
    return this.formOptions().filter((o) => !chosen.has(o.value));
  });

  protected readonly form = this.fb.group({
    code: this.fb.control('', [Validators.required, Validators.maxLength(50), Validators.pattern(/^[A-Za-z0-9_-]+$/)]),
    nameEn: this.fb.control('', [Validators.required, Validators.maxLength(250)]),
    nameAr: this.fb.control('', [Validators.required, Validators.maxLength(250)]),
    descriptionEn: this.fb.control<string>('', Validators.maxLength(1000)),
    descriptionAr: this.fb.control<string>('', Validators.maxLength(1000)),
    departmentCode: this.fb.control<string | null>(null),
    fillSlaHours: this.fb.control<number | null>(null),
    completionSlaHours: this.fb.control<number | null>(null),
    closesC2mActivity: this.fb.control(false),
  });

  constructor() {
    this.form.controls.closesC2mActivity.valueChanges.subscribe((value) => this.closesC2m.set(!!value));
  }

  protected addForm(formId: string | null): void {
    const option = this.formOptions().find((o) => o.value === formId);
    if (!option || this.selectedForms().some((f) => f.value === formId) || this.selectedForms().length >= MAX_FORMS) {
      return;
    }

    this.selectedForms.update((forms) => [...forms, option]);
    this.formsTouched.set(true);
  }

  protected removeForm(formId: string): void {
    this.selectedForms.update((forms) => forms.filter((f) => f.value !== formId));
    if (this.closingFormId() === formId) {
      this.closingFormId.set(null);
    }
    this.formsTouched.set(true);
  }

  protected moveForm(index: number, by: -1 | 1): void {
    this.selectedForms.update((forms) => {
      const next = [...forms];
      const target = index + by;
      if (target < 0 || target >= next.length) {
        return forms;
      }
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }

  protected onShow(): void {
    const type = this.taskType();

    this.form.reset({
      code: type?.code ?? '',
      nameEn: type?.nameEn ?? '',
      nameAr: type?.nameAr ?? '',
      descriptionEn: type?.descriptionEn ?? '',
      descriptionAr: type?.descriptionAr ?? '',
      departmentCode: type?.departmentCode ?? null,
      fillSlaHours: type?.fillSlaHours ?? null,
      completionSlaHours: type?.completionSlaHours ?? null,
      closesC2mActivity: type?.closesC2mActivity ?? false,
    });

    const typeForms = [...(type?.forms ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);
    this.selectedForms.set(typeForms.map((f) => ({
      label: f.code
        ? `${f.code} — ${(this.locale.locale() === 'ar' ? f.nameAr : f.nameEn) ?? ''}${f.currentVersionNo ? ` (v${f.currentVersionNo})` : ''}`
        : f.formDefinitionId,
      value: f.formDefinitionId,
    })));
    this.closingFormId.set(typeForms.find((f) => f.isC2mClosingForm)?.formDefinitionId ?? null);
    this.closesC2m.set(type?.closesC2mActivity ?? false);
    this.formsTouched.set(false);

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
    if (this.form.invalid || this.selectedForms().length === 0 || this.saving()) {
      this.form.markAllAsTouched();
      this.formsTouched.set(true);
      return;
    }

    const value = this.form.getRawValue();
    const formIds = this.selectedForms().map((f) => f.value);
    const closing = this.closingFormId();
    const payload = {
      nameEn: value.nameEn!.trim(),
      nameAr: value.nameAr!.trim(),
      descriptionEn: value.descriptionEn?.trim() || null,
      descriptionAr: value.descriptionAr?.trim() || null,
      formDefinitionIds: formIds,
      c2mClosingFormId: closing && formIds.includes(closing) ? closing : null,
      departmentCode: value.departmentCode,
      fillSlaHours: value.fillSlaHours,
      completionSlaHours: value.completionSlaHours,
      closesC2mActivity: !!value.closesC2mActivity,
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
