import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FieldType, FieldTypeConfig } from '@ngx-formly/core';
import { ReactiveFormsModule } from '@angular/forms';
import { DatePickerModule } from 'primeng/datepicker';

/** PrimeNG date mask (`yy` = 4-digit year). PrimeNG defaults to the US `mm/dd/yy`. */
const DATE_FORMAT = 'dd/mm/yy';

/**
 * Formly type `date` — a calendar day, held as a local `Date`.
 *
 * Replaces the `datepicker` type shipped with `@ngx-formly/primeng`, whose template binds neither
 * `minDate` nor `maxDate`. A field carrying a date rule needs both: the validator is what refuses a
 * bad answer, but greying the disallowed days out of the calendar is what stops the crew choosing
 * one in the first place — and on a phone, where the calendar is the only way in, that is the
 * difference between a rule that guides and a rule that scolds.
 *
 * The bounds are computed per render by `FormSchemaFormlyMapper.applyDateRule`, so a rule stated
 * relative to "today" moves with the day rather than freezing at design time.
 */
@Component({
  selector: 'formly-field-date',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, DatePickerModule],
  template: `
    <p-datepicker
      [formControl]="formControl"
      [dateFormat]="dateFormat"
      [minDate]="minDate"
      [maxDate]="maxDate"
      [showIcon]="true"
      [showClear]="!props.required"
      [disabled]="!!props.disabled"
      [placeholder]="props.placeholder ?? ''"
      appendTo="body"
      styleClass="w-full"
      inputStyleClass="w-full"
    />
  `,
})
export class FormlyDateComponent extends FieldType<FieldTypeConfig> {
  protected get dateFormat(): string {
    return (this.props['dateFormat'] as string | undefined) ?? DATE_FORMAT;
  }

  protected get minDate(): Date | undefined {
    return asDate(this.props['minDate']);
  }

  protected get maxDate(): Date | undefined {
    return asDate(this.props['maxDate']);
  }
}

function asDate(value: unknown): Date | undefined {
  return value instanceof Date && !Number.isNaN(value.getTime()) ? value : undefined;
}
