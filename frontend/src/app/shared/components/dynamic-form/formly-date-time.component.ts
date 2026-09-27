import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FieldType, FieldTypeConfig } from '@ngx-formly/core';
import { TranslateService } from '@ngx-translate/core';
import { FormsModule } from '@angular/forms';
import { DatePickerModule } from 'primeng/datepicker';
import { TimePickerComponent, completeTime, isTime } from './time-picker.component';
import { DATE_TIME_PATTERN, DATE_TIME_SEPARATOR } from './formly-preview.types';

const I18N_PREFIX = 'formly.dateTime';

const I18N_KEYS = {
  Date: `${I18N_PREFIX}.date`,
  Time: `${I18N_PREFIX}.time`,
} as const;

/** PrimeNG date mask (`yy` = 4-digit year). PrimeNG defaults to the US `mm/dd/yy`. */
const DATE_FORMAT = 'dd/mm/yy';

/**
 * Formly type `date_time` — a calendar date and a clock time answered together, stored as the
 * local `YYYY-MM-DD HH:mm` in {@link DATE_TIME_PATTERN}.
 *
 * Deliberately two controls rather than one `p-datepicker` in `[showTime]` mode: that mode puts
 * the clock behind the calendar overlay, so setting a time costs a click into the panel and
 * cannot be typed. Splitting them lets the time half reuse {@link TimePickerComponent}, the
 * same control the `time` element type and both ends of a `calendar_with_hours` shift use.
 *
 * The value is only assembled once *both* halves are answered — a date on its own is not a
 * point in time, so it is submitted as empty and `required` treats the field as unanswered.
 */
@Component({
  selector: 'formly-field-date-time',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePickerModule, TimePickerComponent],
  template: `
    <!-- min-w-0 on both cells: a grid item otherwise refuses to shrink below an input's
         intrinsic ~20-character width, which forces the pair onto two lines. -->
    <div class="grid grid-cols-[1fr_minmax(0,11rem)] items-start gap-2">
      <div class="min-w-0">
        <p-datepicker
          [ngModel]="date()"
          [ngModelOptions]="{ standalone: true }"
          [dateFormat]="dateFormat"
          [minDate]="minDate"
          [maxDate]="maxDate"
          [showIcon]="true"
          [showClear]="!props.required"
          [disabled]="!!props.disabled"
          [placeholder]="dateLabel"
          (ngModelChange)="onDateChange($event)"
          appendTo="body"
          styleClass="w-full"
          inputStyleClass="w-full"
        />
      </div>

      <app-time-picker
        [value]="time()"
        [disabled]="!!props.disabled"
        [ariaLabel]="timeLabel"
        (valueChange)="onTimeChange($event)"
      />
    </div>
  `,
})
export class FormlyDateTimeComponent extends FieldType<FieldTypeConfig> implements OnInit {
  private readonly translate = inject(TranslateService);

  protected readonly dateFormat = DATE_FORMAT;

  /** The two halves are held apart so either can be answered first, in either order. */
  protected readonly date = signal<Date | null>(null);
  protected readonly time = signal<string>('');

  ngOnInit(): void {
    const [date, time] = split(this.formControl.value);
    this.date.set(date);
    this.time.set(time);
  }

  /**
   * The calendar half's bounds, when the field carries a date rule. Only the day is constrained
   * here — a rule measured to the minute (a slot "later than now" on today's date) still needs the
   * validator, because a day the calendar has to keep offering may hold both legal and illegal
   * times.
   */
  protected get minDate(): Date | undefined {
    return asDate(this.props['minDate']);
  }

  protected get maxDate(): Date | undefined {
    return asDate(this.props['maxDate']);
  }

  protected get dateLabel(): string {
    return this.translate.instant(I18N_KEYS.Date);
  }

  protected get timeLabel(): string {
    return this.translate.instant(I18N_KEYS.Time);
  }

  protected onDateChange(value: Date | null): void {
    this.date.set(value instanceof Date && !Number.isNaN(value.getTime()) ? value : null);
    this.commit();
  }

  protected onTimeChange(value: string): void {
    this.time.set(value);
    this.commit();
  }

  /**
   * Half an answer is no answer: a date with no time is not a point in time, so nothing is
   * written until both are set. `required` then reports the field as unanswered, rather than
   * a midnight nobody chose being stored.
   */
  private commit(): void {
    this.formControl.setValue(join(this.date(), this.time()));
    this.formControl.markAsTouched();
    this.formControl.markAsDirty();
  }
}

/**
 * Split a stored value into the two controls. Accepts what the field writes, and the
 * `2026-08-13T14:30:00` an ADO `DATETIME2` reads back as, so a repeat fill opens on what was
 * submitted.
 */
function split(value: unknown): [Date | null, string] {
  if (typeof value !== 'string' || value.trim().length === 0) {
    return [null, ''];
  }

  const [datePart, timePart = ''] = value.trim().split(/[ T]/);
  const [year, month, day] = datePart.split('-').map(Number);

  if (!Number.isFinite(year) || !Number.isFinite(month) || !Number.isFinite(day)) {
    return [null, ''];
  }

  // Built from the parts rather than `new Date(string)`, which reads a bare date as UTC and
  // lands on the previous day anywhere east of Greenwich.
  return [new Date(year, month - 1, day), completeTime(timePart)];
}

/** The two halves as the stored value, or '' while either is still missing. */
function join(date: Date | null, time: string): string {
  return date && isTime(time) ? `${toLocalDate(date)}${DATE_TIME_SEPARATOR}${time}` : '';
}

/** `YYYY-MM-DD` in the user's own timezone — a calendar day, never an instant. */
function toLocalDate(value: Date): string {
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

function asDate(value: unknown): Date | undefined {
  return value instanceof Date && !Number.isNaN(value.getTime()) ? value : undefined;
}

/**
 * Bring a stored value to the one shape this field writes — the `2026-08-13T14:30:00` an ADO
 * `DATETIME2` reads back as becomes `2026-08-13 14:30`. Anything that is not a full date *and*
 * time becomes '', matching an unanswered field.
 */
export function normalizeDateTime(text: string): string {
  if (DATE_TIME_PATTERN.test(text)) {
    return text;
  }

  const [date, time] = split(text);
  return join(date, time);
}
