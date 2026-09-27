import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { TextareaModule } from 'primeng/textarea';
import { SelectModule } from 'primeng/select';
import { MultiSelectModule } from 'primeng/multiselect';
import { DatePickerModule } from 'primeng/datepicker';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LocaleService } from '../../../../core/i18n/locale.service';
import {
  TimePickerComponent,
  completeTime,
  isTime,
} from '../../../../shared/components/dynamic-form/time-picker.component';
import { DATE_TIME_SEPARATOR } from '../../../../shared/components/dynamic-form/formly-preview.types';
import { YES_NO_VALUES } from '../../../../shared/form-schema/form-schema-formly.mapper';
import { parseLocalDate, toLocalDate } from '../../../../shared/form-schema/form-schema-payload';
import {
  DEFAULT_VALUE_MODES,
  ELEMENT_TYPES,
  NUMERIC_FORMATS,
  isClockDefaultType,
  isMediaType,
  localizedDisplay,
  type Choice,
  type DefaultValueMode,
  type ElementType,
  type NumericFormat,
} from '../../../../shared/form-schema/form-schema.types';

/** PrimeNG date mask (`yy` = 4-digit year). PrimeNG defaults to the US `mm/dd/yy`. */
const DATE_FORMAT = 'dd/mm/yy';

const I18N_PREFIX = 'formBuilder.editor';

const I18N_KEYS = {
  Yes: 'formBuilder.preview.yes',
  No: 'formBuilder.preview.no',
  ModeNone: `${I18N_PREFIX}.defaultModes.none`,
  ModeNow: `${I18N_PREFIX}.defaultModes.now`,
  ModeNowDate: `${I18N_PREFIX}.defaultModes.nowDate`,
  ModeFixed: `${I18N_PREFIX}.defaultModes.fixed`,
} as const;

/**
 * What the author picked in the mode select. {@link DEFAULT_VALUE_MODES} has no "none" of its own —
 * an empty default is simply `fixed` with nothing stored — but the select needs the three states
 * apart so choosing "a date I pick" does not read back as "none" until a date is actually picked.
 */
const EDITOR_MODES = {
  None: 'none',
  Now: 'now',
  Fixed: 'fixed',
} as const;

type EditorMode = (typeof EDITOR_MODES)[keyof typeof EDITOR_MODES];

/**
 * Between the parts of a default that packs more than one value into its string: the two halves of
 * a `"lat,lng"` geolocation pin, and the selected options of a multiple-choice field.
 */
const VALUE_SEPARATOR = ',';

/**
 * The Default Value control for one element, matched to that element's type.
 *
 * Every default is stored as a single string — that is the shape the definition JSON carries and
 * the shape `FormSchemaFormlyMapper.defaultValue()` parses back — but typing one by hand meant an
 * author only found out at preview time whether it parsed. Each type gets the control it deserves
 * here, and the string is assembled from it.
 *
 * A `date` / `date_time` / `time` default may also be {@link DEFAULT_VALUE_MODES.Now}: resolved from
 * the clock when the survey is filled, so it cannot go stale the way a fixed date does.
 *
 * Seeded from {@link value} whenever {@link seedToken} changes — the editing dialog passes the
 * element key, so opening a different field re-reads it while ordinary edits flow outward only.
 */
@Component({
  selector: 'app-default-value-editor',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    InputTextModule,
    InputNumberModule,
    TextareaModule,
    SelectModule,
    MultiSelectModule,
    DatePickerModule,
    TimePickerComponent,
    TranslateModule,
  ],
  templateUrl: './default-value-editor.component.html',
})
export class DefaultValueEditorComponent {
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);
  private readonly activeLang = this.locale.locale;

  /** The stored default — the exact string written to `default_value` in the definition. */
  readonly value = model<string | null>(null);
  readonly mode = model<DefaultValueMode>(DEFAULT_VALUE_MODES.Fixed);

  readonly type = input.required<ElementType>();
  /** The element's own options, so a choice default is picked rather than typed. */
  readonly choices = input<readonly Choice[]>([]);
  readonly format = input<NumericFormat | null>(null);
  /** Changing this re-seeds the internal state from {@link value} — pass the element key. */
  readonly seedToken = input<string | null>(null);

  protected readonly dateFormat = DATE_FORMAT;
  protected readonly editorModes = EDITOR_MODES;
  protected readonly elementTypes = ELEMENT_TYPES;

  /** Which of the three states the mode select is showing. */
  protected readonly editorMode = signal<EditorMode>(EDITOR_MODES.None);

  /** The two halves of a `date_time`, held apart so either can be answered first. */
  protected readonly dateHalf = signal<Date | null>(null);

  /**
   * The raw text of a time box — the `time` type's own control as well as the `date_time` pair's
   * second half.
   *
   * Held locally rather than derived from {@link value}, because a half-typed `08:` is not a time
   * and would be stored as `null`; feeding that back into the box would erase the keystroke that
   * produced it. {@link value} is written only once the text is a real time.
   */
  protected readonly timeHalf = signal<string>('');

  /**
   * A numeric default while it is being typed. Local for the same reason as {@link timeHalf}: a
   * lone `-` is not a number, and rebinding null mid-entry would take the minus sign away.
   */
  protected readonly numberHalf = signal<number | null>(null);

  /** The two halves of a `"lat,lng"` geolocation default. */
  protected readonly latHalf = signal<number | null>(null);
  protected readonly lngHalf = signal<number | null>(null);

  protected readonly isClockType = computed(() => isClockDefaultType(this.type()));

  /** Media, sections and the weekly shift grid have no meaningful single-value default. */
  protected readonly isSupported = computed(() => {
    const type = this.type();
    return (
      type !== ELEMENT_TYPES.Section &&
      type !== ELEMENT_TYPES.CalendarWithHours &&
      !isMediaType(type)
    );
  });

  /** Hidden while the default is "none" or the fill-time clock — there is nothing to pick. */
  protected readonly showControl = computed(
    () => !this.isClockType() || this.editorMode() === EDITOR_MODES.Fixed,
  );

  protected readonly modeOptions = computed(() => [
    { label: this.translate.instant(I18N_KEYS.ModeNone), value: EDITOR_MODES.None },
    {
      label: this.translate.instant(
        this.type() === ELEMENT_TYPES.Date ? I18N_KEYS.ModeNowDate : I18N_KEYS.ModeNow,
      ),
      value: EDITOR_MODES.Now,
    },
    { label: this.translate.instant(I18N_KEYS.ModeFixed), value: EDITOR_MODES.Fixed },
  ]);

  protected readonly yesNoOptions = computed(() => [
    { label: this.translate.instant(I18N_KEYS.Yes), value: YES_NO_VALUES.Yes },
    { label: this.translate.instant(I18N_KEYS.No), value: YES_NO_VALUES.No },
  ]);

  protected readonly choiceOptions = computed(() =>
    this.choices().map((choice) => ({
      label: localizedDisplay(choice.label_en, choice.label_ar, this.activeLang(), choice.value),
      value: choice.value,
    })),
  );

  /** Integers get no fraction digits, matching the `integer` numeric format's own validator. */
  protected readonly maxFractionDigits = computed(() =>
    this.format() === NUMERIC_FORMATS.Integer ? 0 : undefined,
  );

  // ---- values bound straight to a single control, derived from `value` ----

  protected readonly dateValue = computed(() => {
    const raw = this.value();
    return raw ? (parseLocalDate(raw) ?? null) : null;
  });

  protected readonly multiValue = computed(() =>
    (this.value() ?? '')
      .split(VALUE_SEPARATOR)
      .map((item) => item.trim())
      .filter((item) => item.length > 0),
  );

  constructor() {
    effect(() => {
      // Tracked so a different field re-seeds; everything else is read untracked so the component's
      // own writes never feed back into it.
      this.seedToken();
      untracked(() => this.seed());
    });
  }

  protected onModeChange(mode: EditorMode): void {
    this.editorMode.set(mode);
    this.mode.set(mode === EDITOR_MODES.Now ? DEFAULT_VALUE_MODES.Now : DEFAULT_VALUE_MODES.Fixed);

    if (mode !== EDITOR_MODES.Fixed) {
      this.value.set(null);
      this.dateHalf.set(null);
      this.timeHalf.set('');
    }
  }

  /** `null` for anything the type cannot store, so an emptied control clears the default. */
  protected onTextChange(text: string | null): void {
    this.value.set(text && text.length > 0 ? text : null);
  }

  protected onDateChange(date: Date | null): void {
    this.value.set(isRealDate(date) ? toLocalDate(date) : null);
  }

  protected onTimeChange(time: string): void {
    this.timeHalf.set(time);
    this.value.set(isTime(time) ? time : null);
  }

  protected onNumberChange(value: number | null): void {
    this.numberHalf.set(value ?? null);
    // Compared against null rather than falsy: 0 is a legitimate default.
    this.value.set(value === null || value === undefined ? null : String(value));
  }

  protected onChoiceChange(value: string | null): void {
    this.value.set(value || null);
  }

  /** Stored comma-joined — the shape the mapper splits back into an array. */
  protected onMultiChoiceChange(values: string[] | null): void {
    const selected = values ?? [];
    this.value.set(selected.length > 0 ? selected.join(VALUE_SEPARATOR) : null);
  }

  protected onDateTimeDateChange(date: Date | null): void {
    this.dateHalf.set(isRealDate(date) ? date : null);
    this.commitDateTime();
  }

  protected onDateTimeTimeChange(time: string): void {
    this.timeHalf.set(time);
    this.commitDateTime();
  }

  protected onLatChange(lat: number | null): void {
    this.latHalf.set(lat);
    this.commitCoordinate();
  }

  protected onLngChange(lng: number | null): void {
    this.lngHalf.set(lng);
    this.commitCoordinate();
  }

  /** Half a timestamp is not a point in time, so nothing is stored until both halves are set. */
  private commitDateTime(): void {
    const date = this.dateHalf();
    const time = this.timeHalf();
    this.value.set(date && isTime(time) ? `${toLocalDate(date)}${DATE_TIME_SEPARATOR}${time}` : null);
  }

  /** A pin needs both halves too — one coordinate places nothing on the map. */
  private commitCoordinate(): void {
    const lat = this.latHalf();
    const lng = this.lngHalf();
    this.value.set(
      lat !== null && lng !== null ? `${lat}${VALUE_SEPARATOR}${lng}` : null,
    );
  }

  private seed(): void {
    const raw = this.value();

    this.editorMode.set(
      this.mode() === DEFAULT_VALUE_MODES.Now
        ? EDITOR_MODES.Now
        : raw
          ? EDITOR_MODES.Fixed
          : EDITOR_MODES.None,
    );

    if (this.type() === ELEMENT_TYPES.DateTime) {
      const [datePart, timePart = ''] = (raw ?? '').split(DATE_TIME_SEPARATOR);
      this.dateHalf.set(parseLocalDate(datePart) ?? null);
      this.timeHalf.set(completeTime(timePart));
      return;
    }

    if (this.type() === ELEMENT_TYPES.Time) {
      this.timeHalf.set(completeTime(raw ?? ''));
      return;
    }

    if (this.type() === ELEMENT_TYPES.Numeric) {
      const parsed = Number(raw);
      this.numberHalf.set(raw !== null && raw !== '' && !Number.isNaN(parsed) ? parsed : null);
      return;
    }

    if (this.type() === ELEMENT_TYPES.Geolocation) {
      const [lat, lng] = (raw ?? '').split(VALUE_SEPARATOR).map((part) => Number(part.trim()));
      this.latHalf.set(Number.isFinite(lat) ? lat : null);
      this.lngHalf.set(Number.isFinite(lng) ? lng : null);
    }
  }
}

function isRealDate(value: unknown): value is Date {
  return value instanceof Date && !Number.isNaN(value.getTime());
}
