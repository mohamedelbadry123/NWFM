import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FieldType, FieldTypeConfig } from '@ngx-formly/core';
import { TranslateService } from '@ngx-translate/core';
import { FormsModule } from '@angular/forms';
import { CheckboxModule } from 'primeng/checkbox';
import { TimePickerComponent, completeTime, isTime } from './time-picker.component';
import {
  WEEKDAY_CODES,
  CALENDAR_WITH_HOURS_I18N_PREFIX,
  isCalendarWithHoursValue,
  type WeekdayCode,
  type CalendarWithHoursValue,
} from './formly-preview.types';

const I18N_KEYS = {
  From: `${CALENDAR_WITH_HOURS_I18N_PREFIX}.from`,
  To: `${CALENDAR_WITH_HOURS_I18N_PREFIX}.to`,
  Days: `${CALENDAR_WITH_HOURS_I18N_PREFIX}.days`,
} as const;

/** Which end of a shift a cell edits. */
const TIME_ENDS = { From: 'from', To: 'to' } as const;
type TimeEnd = (typeof TIME_ENDS)[keyof typeof TIME_ENDS];

/**
 * One editable row. `from` / `to` hold exactly what is in the box — including a half-typed
 * `08:` — so typing is never fought by a re-render. {@link toValue} is what decides whether
 * the text is a time yet.
 */
interface CalendarDayRow {
  readonly day: WeekdayCode;
  enabled: boolean;
  from: string;
  to: string;
}

/**
 * Formly type `calendar_with_hours` — the weekly shift grid the NWC paper survey calls
 * <c>ساعات العمل</c>: one <c>من … إلى …</c> pair per day, Saturday through Friday.
 *
 * Both ends of a shift are {@link TimePickerComponent}, the same control the standalone
 * `time` element type uses, so a clock time is typed and picked the same way everywhere.
 *
 * The control value is a {@link CalendarWithHoursValue} keyed by day code, holding only
 * the days that are switched on. Clearing every day writes `null` rather than an
 * empty object, so Angular's own `required` validator recognises the field as
 * unanswered without a custom rule.
 *
 * A shift may end before it starts — that is an overnight crew, not a mistake —
 * so only a half-filled row is rejected (see `calendarWithHoursRangeValidator`).
 */
@Component({
  selector: 'formly-field-calendar-with-hours',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, CheckboxModule, TimePickerComponent],
  template: `
    <div class="flex flex-col gap-1.5">
      @for (row of rows(); track row.day) {
        <!--
          A fixed three-column grid, not flex-wrap: the two ends of a shift read as a pair and
          have to stay on one line with their day. Every cell carries min-w-0 because a grid
          item defaults to its content's minimum — for an input that is its intrinsic
          ~20-character width, which is what pushed the second time onto a row of its own.
        -->
        <div
          class="grid grid-cols-[minmax(5rem,8rem)_1fr_1fr] items-center gap-2 rounded-lg border border-[var(--p-content-border-color)] bg-[var(--p-content-background)] px-2.5 py-2 transition-colors"
          [class.opacity-60]="!row.enabled"
        >
          <!--
            standalone ngModel: the renderer wraps everything in a reactive formGroup, and an
            ngModel that tries to register against it throws. The checkbox is the row's own
            state — the field's value is written in patch().
          -->
          <div class="flex min-w-0 items-center gap-2">
            <p-checkbox
              [binary]="true"
              [ngModel]="row.enabled"
              [ngModelOptions]="{ standalone: true }"
              [disabled]="!!props.disabled"
              [inputId]="inputId(row.day)"
              (ngModelChange)="onEnabledChange(row, $event)"
            />
            <label class="text-sm font-medium truncate cursor-pointer" [attr.for]="inputId(row.day)">
              {{ dayLabel(row.day) }}
            </label>
          </div>

          @for (end of timeEnds; track end) {
            <app-time-picker
              [value]="end === 'from' ? row.from : row.to"
              [disabled]="!!props.disabled || !row.enabled"
              [ariaLabel]="endLabel(end) + ' — ' + dayLabel(row.day)"
              (valueChange)="onTimeChange(row, end, $event)"
            />
          }
        </div>
      }
    </div>
  `,
})
export class FormlyCalendarWithHoursComponent extends FieldType<FieldTypeConfig> implements OnInit {
  private readonly translate = inject(TranslateService);

  protected readonly rows = signal<CalendarDayRow[]>([]);
  protected readonly timeEnds: readonly TimeEnd[] = [TIME_ENDS.From, TIME_ENDS.To];

  ngOnInit(): void {
    this.rows.set(toRows(this.formControl.value));
  }

  protected endLabel(end: TimeEnd): string {
    return this.translate.instant(end === TIME_ENDS.From ? I18N_KEYS.From : I18N_KEYS.To);
  }

  protected dayLabel(day: WeekdayCode): string {
    return this.translate.instant(`${I18N_KEYS.Days}.${day}`);
  }

  /** Pairs the checkbox with its label, and keeps the id unique when a form has two such fields. */
  protected inputId(day: WeekdayCode): string {
    return `${this.field.key as string}_${day}`;
  }

  protected onEnabledChange(row: CalendarDayRow, enabled: boolean): void {
    // Times are kept while the day is off, so an accidental untick is one click to undo.
    this.patch({ ...row, enabled });
  }

  protected onTimeChange(row: CalendarDayRow, end: TimeEnd, value: string): void {
    this.patch({ ...row, [end]: value });
  }

  private patch(updated: CalendarDayRow): void {
    const rows = this.rows().map((row) => (row.day === updated.day ? updated : row));
    this.rows.set(rows);
    this.formControl.setValue(toValue(rows));
    this.formControl.markAsTouched();
    this.formControl.markAsDirty();
  }
}

/** Seed the grid from an existing answer — a repeat fill opens on what was submitted. */
function toRows(value: unknown): CalendarDayRow[] {
  const stored: CalendarWithHoursValue = isCalendarWithHoursValue(value) ? value : {};

  return WEEKDAY_CODES.map((day) => {
    const range = stored[day];
    return {
      day,
      enabled: !!range,
      from: completeTime(range?.from ?? ''),
      to: completeTime(range?.to ?? ''),
    };
  });
}

/**
 * Collapse the grid into the control value: every ticked day, whether or not both
 * ends are filled. Text that is not a time yet is carried as an empty end on purpose —
 * that is what `calendarWithHoursRangeValidator` reads to block the submit, and
 * `toPreviewPayload` drops it again before anything is stored.
 *
 * `null` — not `{}` — when no day is ticked, so `required` sees an empty field.
 */
function toValue(rows: readonly CalendarDayRow[]): CalendarWithHoursValue | null {
  const value: Record<string, { from: string; to: string }> = {};

  for (const row of rows) {
    if (row.enabled) {
      value[row.day] = {
        from: isTime(row.from) ? row.from : '',
        to: isTime(row.to) ? row.to : '',
      };
    }
  }

  return Object.keys(value).length > 0 ? (value as CalendarWithHoursValue) : null;
}
