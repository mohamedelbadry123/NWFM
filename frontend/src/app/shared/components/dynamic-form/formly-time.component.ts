import { ChangeDetectionStrategy, Component, OnInit } from '@angular/core';
import { FieldType, FieldTypeConfig } from '@ngx-formly/core';
import { TimePickerComponent, completeTime } from './time-picker.component';

/**
 * Formly type `time` — a clock time on the 24-hour clock, stored as `HH:mm`.
 *
 * Shares {@link TimePickerComponent} with both ends of a `calendar_with_hours` shift, so a
 * time is typed and picked the same way wherever it appears. It replaced a `p-datepicker` in
 * `[timeOnly]` mode, which could only be clicked: the picker re-parsed the whole field on
 * every keystroke and nulled its model while the text was still partial, feeding that null
 * back into the box and erasing what was being typed.
 *
 * The control holds the raw text, which is what keeps a half-typed `08:` from being
 * overwritten by a re-render. `toPreviewPayload` emits it as-is — it is already the `HH:mm`
 * the `TIME` column takes — and text that never became a time is submitted as the empty
 * string, exactly as an untouched field is.
 */
@Component({
  selector: 'formly-field-time',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TimePickerComponent],
  template: `
    <app-time-picker
      [value]="text"
      [disabled]="!!props.disabled"
      [ariaLabel]="props.label ?? ''"
      (valueChange)="onValueChange($event)"
    />
  `,
})
export class FormlyTimeComponent extends FieldType<FieldTypeConfig> implements OnInit {
  /**
   * Tidied **once**, not on every read: a value seeded from a stored answer may be `08:30:00`,
   * but running the same tidy while the box is being typed into would turn a half-typed `08:`
   * straight back into `08:00`.
   */
  ngOnInit(): void {
    const seeded = normalize(this.formControl.value);
    if (seeded !== this.formControl.value) {
      this.formControl.setValue(seeded, { emitEvent: false });
    }
  }

  protected get text(): string {
    const value: unknown = this.formControl.value;
    return typeof value === 'string' ? value : '';
  }

  protected onValueChange(value: string): void {
    this.formControl.setValue(value);
    this.formControl.markAsTouched();
    this.formControl.markAsDirty();
  }
}

/** `08:30:00` and a legacy `Date` default both become the `HH:mm` the box shows. */
function normalize(value: unknown): string {
  if (value instanceof Date && !Number.isNaN(value.getTime())) {
    return completeTime(`${value.getHours()}${String(value.getMinutes()).padStart(2, '0')}`);
  }

  return typeof value === 'string' ? completeTime(value) : '';
}
