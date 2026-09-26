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
import { FormsService } from '../../../core/form-engine/forms.service';
import { formEngineErrorMessage } from '../../../core/form-engine/form-engine-api-error';
import { FormListItem } from '../../../core/form-engine/form-engine.models';

/** Copies a form's design into a new draft under a new code. Version history is not copied. */
@Component({
  selector: 'app-form-clone-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslateModule, ButtonModule, DialogModule, InputTextModule],
  template: `
    <p-dialog
      [(visible)]="visible"
      [modal]="true"
      [draggable]="false"
      [style]="{ width: '30rem' }"
      [header]="'forms.clone.title' | translate"
      (onShow)="onShow()"
    >
      <form [formGroup]="model" class="flex flex-col gap-4" (ngSubmit)="save()">
        <p class="text-sm opacity-70">{{ 'forms.clone.hint' | translate }}</p>

        <div class="flex flex-col gap-2">
          <label for="clone-code" class="text-sm font-medium">{{ 'forms.code' | translate }}</label>
          <input id="clone-code" pInputText formControlName="newCode" autocomplete="off" />
          @if (model.controls.newCode.touched && model.controls.newCode.invalid) {
            <small class="text-red-500">{{ 'forms.codeInvalid' | translate }}</small>
          }
        </div>

        <div class="flex flex-col gap-2">
          <label for="clone-name-en" class="text-sm font-medium">{{ 'forms.nameEn' | translate }}</label>
          <input id="clone-name-en" pInputText formControlName="newNameEn" />
        </div>

        <div class="flex flex-col gap-2">
          <label for="clone-name-ar" class="text-sm font-medium">{{ 'forms.nameAr' | translate }}</label>
          <input id="clone-name-ar" pInputText formControlName="newNameAr" dir="rtl" />
        </div>
      </form>

      <ng-template pTemplate="footer">
        <p-button [label]="'common.cancel' | translate" [text]="true" [disabled]="saving()" (onClick)="cancel()" />
        <p-button
          [label]="'forms.actions.clone' | translate"
          icon="pi pi-copy"
          [loading]="saving()"
          [disabled]="saving()"
          (onClick)="save()"
        />
      </ng-template>
    </p-dialog>
  `,
})
export class FormCloneDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly formsApi = inject(FormsService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  readonly visible = model.required<boolean>();
  readonly form = input<FormListItem | null>(null);
  readonly cloned = output<void>();

  protected readonly saving = signal(false);

  protected readonly model = this.fb.group({
    newCode: ['', [Validators.required, Validators.maxLength(50), Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9_.-]*$/)]],
    newNameEn: ['', [Validators.required, Validators.maxLength(250)]],
    newNameAr: ['', [Validators.required, Validators.maxLength(250)]],
  });

  protected onShow(): void {
    const form = this.form();
    const suffix = this.translate.instant('forms.clone.copySuffix');

    this.model.reset({
      newCode: form ? `${form.code}-COPY` : '',
      newNameEn: form ? `${form.nameEn} ${suffix}` : '',
      newNameAr: form ? `${form.nameAr} ${suffix}` : '',
    });
  }

  protected save(): void {
    const source = this.form();

    if (!source || this.model.invalid || this.saving()) {
      this.model.markAllAsTouched();
      return;
    }

    const value = this.model.getRawValue();

    this.saving.set(true);
    this.formsApi
      .clone(source.id, {
        newCode: value.newCode!,
        newNameEn: value.newNameEn!,
        newNameAr: value.newNameAr!,
      })
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messages.add({ severity: 'success', summary: this.translate.instant('forms.clone.success') });
          this.visible.set(false);
          this.cloned.emit();
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
