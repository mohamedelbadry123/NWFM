import { Component, DestroyRef, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageService } from 'primeng/api';
import { SelectModule } from 'primeng/select';
import { LocaleService } from '../../../core/i18n/locale.service';
import { LookupItem, LookupsService } from '../../../core/lookups/lookups.service';
import { FormsService } from '../../../core/form-engine/forms.service';
import { formEngineErrorMessage } from '../../../core/form-engine/form-engine-api-error';
import { FORM_CATEGORIES, FormListItem } from '../../../core/form-engine/form-engine.models';

/**
 * Create or rename a form. Only its main info — the fields themselves are designed in the builder,
 * and the code is fixed once created because published versions are known by it.
 */
@Component({
  selector: 'app-form-details-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, TranslateModule,
    ButtonModule, DialogModule, InputTextModule, SelectModule,
  ],
  templateUrl: './form-details-dialog.component.html',
})
export class FormDetailsDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly formsApi = inject(FormsService);
  private readonly lookups = inject(LookupsService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  readonly visible = model.required<boolean>();
  readonly form = input<FormListItem | null>(null);
  readonly saved = output<void>();

  protected readonly saving = signal(false);
  protected readonly departments = signal<LookupItem[]>([]);
  /** Copied into a mutable array: PrimeNG's `options` input does not accept a readonly one. */
  protected readonly categories: string[] = [...FORM_CATEGORIES];

  protected readonly model = this.fb.group({
    code: ['', [Validators.required, Validators.maxLength(50), Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9_.-]*$/)]],
    nameEn: ['', [Validators.required, Validators.maxLength(250)]],
    nameAr: ['', [Validators.required, Validators.maxLength(250)]],
    category: ['GENERAL', [Validators.required]],
    departmentCode: [null as string | null],
  });

  protected get isEdit(): boolean {
    return this.form() !== null;
  }

  protected onShow(): void {
    const form = this.form();

    this.model.reset({
      code: form?.code ?? '',
      nameEn: form?.nameEn ?? '',
      nameAr: form?.nameAr ?? '',
      category: form?.category ?? 'GENERAL',
      departmentCode: form?.departmentCode ?? null,
    });

    // The code identifies published versions, so it is set once and then read-only.
    if (this.isEdit) {
      this.model.controls.code.disable();
    } else {
      this.model.controls.code.enable();
    }

    if (this.departments().length === 0) {
      this.lookups
        .listAll('Department')
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({ next: (items) => this.departments.set(items) });
    }
  }

  protected departmentLabel(item: LookupItem): string {
    return this.locale.locale() === 'ar' ? item.nameAr : item.nameEn;
  }

  protected save(): void {
    if (this.model.invalid || this.saving()) {
      this.model.markAllAsTouched();
      return;
    }

    const value = this.model.getRawValue();
    const existing = this.form();

    const request$ = existing
      ? this.formsApi.update(existing.id, {
          nameEn: value.nameEn!,
          nameAr: value.nameAr!,
          category: value.category!,
          departmentCode: value.departmentCode ?? null,
        })
      : this.formsApi.create({
          code: value.code!,
          nameEn: value.nameEn!,
          nameAr: value.nameAr!,
          category: value.category!,
          departmentCode: value.departmentCode ?? null,
        });

    this.saving.set(true);
    request$
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant(existing ? 'forms.updateSuccess' : 'forms.createSuccess'),
          });
          this.visible.set(false);
          this.saved.emit();
        },
        error: (error) => this.messages.add({
          severity: 'error',
          summary: this.translate.instant('common.error'),
          detail: formEngineErrorMessage(error, this.translate),
          life: 8000,
        }),
      });
  }

  protected cancel(): void {
    this.visible.set(false);
  }
}
