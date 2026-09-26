import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { FieldType, FieldTypeConfig, FormlyModule } from '@ngx-formly/core';
import { TranslateService } from '@ngx-translate/core';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';

const I18N_KEYS = {
  Formats: 'formly.barcode.formats',
  WebHint: 'formly.barcode.webHint',
} as const;

/**
 * Formly type `barcode` — the decoded text of a scanned code.
 *
 * The camera work belongs to the native mobile app, which reads the element's `barcode_formats`
 * straight out of the definition JSON and hands the decoded string back as this field's value.
 * The web has no scanner, so the control is a plain text input the back office types into. Both
 * clients therefore produce the same shape — a short string — which is what lets them share one
 * submissions column.
 *
 * A web scanner would drop in here without touching anything else: it writes to the same
 * `formControl`.
 */
@Component({
  selector: 'formly-field-barcode',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, FormlyModule, InputTextModule, IconFieldModule, InputIconModule],
  template: `
    <p-iconfield iconPosition="left" class="w-full">
      <p-inputicon styleClass="pi pi-qrcode" />
      <input
        pInputText
        type="text"
        class="w-full"
        [formControl]="formControl"
        [formlyAttributes]="field"
        [attr.maxlength]="props.maxLength"
      />
    </p-iconfield>

    @if (formatsLabel) {
      <p class="text-[11px] text-[var(--p-text-muted-color)] mt-1">{{ formatsLabel }}</p>
    }
    <p class="text-[11px] text-[var(--p-text-muted-color)] mt-0.5">{{ webHint }}</p>
  `,
})
export class FormlyBarcodeComponent extends FieldType<FieldTypeConfig> {
  private readonly translate = inject(TranslateService);

  protected get webHint(): string {
    return this.translate.instant(I18N_KEYS.WebHint);
  }

  /**
   * "Accepts: QR Code, Code 128, …" — empty when the field accepts any symbology, since naming
   * none of them reads as a restriction rather than the absence of one.
   */
  protected get formatsLabel(): string {
    const formats = this.props['formatsLabel'] as string | undefined;
    return formats ? this.translate.instant(I18N_KEYS.Formats, { formats }) : '';
  }
}
