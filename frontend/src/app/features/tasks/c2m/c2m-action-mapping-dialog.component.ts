import { Component, computed, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { Observable, finalize } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageService } from 'primeng/api';
import { SelectModule } from 'primeng/select';
import { ToggleSwitchModule } from 'primeng/toggleswitch';

import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { ApiResult } from '../../../core/api/api-result';
import { apiErrorMessage } from '../../../core/api/api-error-message';
import { C2mActionMappingsService } from '../../../core/tasks/c2m-action-mappings.service';
import { C2mActionMapping } from '../../../core/tasks/tasks.models';
import { C2M_FA_STATUSES } from '../../../shared/form-schema/form-schema.types';

/**
 * Creates or edits one C2M action mapping. C2M rejects the reason that does not belong to the
 * status, so only the matching reason field is offered: a cancel reason for X, a closure reason for C.
 */
@Component({
  selector: 'app-c2m-action-mapping-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslateContextDirective,
    ButtonModule,
    DialogModule,
    InputTextModule,
    SelectModule,
    ToggleSwitchModule,
  ],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        (onShow)="onShow()"
        [header]="t(isEdit() ? 'c2mMappings.edit' : 'c2mMappings.new')"
        [modal]="true"
        [draggable]="false"
        [style]="{ width: '36rem' }"
        [breakpoints]="{ '640px': '95vw' }"
      >
        <form [formGroup]="form" class="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-2">
            <label for="map-code" class="text-sm font-medium">{{ t('c2mMappings.actionCode') }} <span class="text-red-500">*</span></label>
            <input pInputText id="map-code" formControlName="actionCode" class="w-full font-mono uppercase" dir="ltr" [readonly]="isEdit()" />
          </div>

          <div class="flex flex-col gap-2">
            <label for="map-status" class="text-sm font-medium">{{ t('c2mMappings.faStatus') }} <span class="text-red-500">*</span></label>
            <p-select
              inputId="map-status"
              formControlName="faStatus"
              [options]="statusOptions()"
              optionLabel="label"
              optionValue="value"
              appendTo="body"
              styleClass="w-full"
            />
          </div>

          @if (isCancelled()) {
            <div class="flex flex-col gap-2 sm:col-span-2">
              <label for="map-cancel" class="text-sm font-medium">{{ t('c2mMappings.cancelReason') }}</label>
              <input pInputText id="map-cancel" formControlName="cancelReason" class="w-full font-mono" dir="ltr" />
              <small class="text-surface-500">{{ t('c2mMappings.cancelReasonHint') }}</small>
            </div>
          } @else {
            <div class="flex flex-col gap-2 sm:col-span-2">
              <label for="map-closure" class="text-sm font-medium">{{ t('c2mMappings.closureReason') }}</label>
              <input pInputText id="map-closure" formControlName="closureReason" class="w-full font-mono" dir="ltr" />
            </div>
          }

          <div class="flex flex-col gap-2">
            <label for="map-name-en" class="text-sm font-medium">{{ t('taskTypes.nameEn') }} <span class="text-red-500">*</span></label>
            <input pInputText id="map-name-en" formControlName="nameEn" class="w-full" />
          </div>

          <div class="flex flex-col gap-2">
            <label for="map-name-ar" class="text-sm font-medium">{{ t('taskTypes.nameAr') }} <span class="text-red-500">*</span></label>
            <input pInputText id="map-name-ar" formControlName="nameAr" class="w-full" dir="rtl" />
          </div>

          <div class="flex items-center gap-2 sm:col-span-2">
            <p-toggleswitch inputId="map-active" formControlName="isActive" />
            <label for="map-active" class="text-sm">{{ t('common.active') }}</label>
          </div>
        </form>

        <ng-template pTemplate="footer">
          <p-button [label]="t('common.cancel')" [text]="true" severity="secondary" [disabled]="saving()" (onClick)="visible.set(false)" />
          <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="save()" />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class C2mActionMappingDialogComponent {
  readonly visible = model.required<boolean>();
  readonly mapping = input<C2mActionMapping | null>(null);
  readonly saved = output<void>();

  private readonly api = inject(C2mActionMappingsService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly fb = inject(FormBuilder);

  protected readonly saving = signal(false);
  protected readonly isEdit = computed(() => this.mapping() !== null);
  private readonly faStatus = signal<string>(C2M_FA_STATUSES.Cancelled);
  protected readonly isCancelled = computed(() => this.faStatus() === C2M_FA_STATUSES.Cancelled);

  protected readonly form = this.fb.group({
    actionCode: this.fb.control('', [Validators.required, Validators.maxLength(50)]),
    faStatus: this.fb.control<string>(C2M_FA_STATUSES.Cancelled, Validators.required),
    cancelReason: this.fb.control<string>('', Validators.maxLength(50)),
    closureReason: this.fb.control<string>('', Validators.maxLength(50)),
    nameEn: this.fb.control('', [Validators.required, Validators.maxLength(250)]),
    nameAr: this.fb.control('', [Validators.required, Validators.maxLength(250)]),
    isActive: this.fb.control(true),
  });

  constructor() {
    this.form.controls.faStatus.valueChanges.subscribe((value) => this.faStatus.set(value ?? C2M_FA_STATUSES.Cancelled));
  }

  protected statusOptions(): { label: string; value: string }[] {
    return [
      { value: C2M_FA_STATUSES.Completed, label: this.translate.instant('formBuilder.editor.c2mFaStatusCompleted') },
      { value: C2M_FA_STATUSES.Cancelled, label: this.translate.instant('formBuilder.editor.c2mFaStatusCancelled') },
    ];
  }

  protected onShow(): void {
    const mapping = this.mapping();
    this.form.reset({
      actionCode: mapping?.actionCode ?? '',
      faStatus: mapping?.faStatus ?? C2M_FA_STATUSES.Cancelled,
      cancelReason: mapping?.cancelReason ?? '',
      closureReason: mapping?.closureReason ?? '',
      nameEn: mapping?.nameEn ?? '',
      nameAr: mapping?.nameAr ?? '',
      isActive: mapping?.isActive ?? true,
    });
  }

  protected save(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const cancelled = value.faStatus === C2M_FA_STATUSES.Cancelled;
    const payload = {
      faStatus: value.faStatus!,
      // Only the reason that belongs to the status is sent; C2M refuses the other.
      cancelReason: cancelled ? value.cancelReason?.trim() || null : null,
      closureReason: cancelled ? null : value.closureReason?.trim() || null,
      nameEn: value.nameEn!.trim(),
      nameAr: value.nameAr!.trim(),
      isActive: !!value.isActive,
    };

    const current = this.mapping();
    const request: Observable<ApiResult<unknown>> = current
      ? this.api.update(current.id, payload)
      : this.api.create({ ...payload, actionCode: value.actionCode!.trim().toUpperCase() });

    this.saving.set(true);
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.messages.add({
          severity: 'success',
          summary: this.translate.instant('common.success'),
          detail: this.translate.instant(current ? 'c2mMappings.updated' : 'c2mMappings.created'),
        });
        this.visible.set(false);
        this.saved.emit();
      },
      error: (error: unknown) =>
        this.messages.add({
          severity: 'error',
          summary: this.translate.instant('common.error'),
          detail: apiErrorMessage(error, this.translate),
          life: 8000,
        }),
    });
  }
}
