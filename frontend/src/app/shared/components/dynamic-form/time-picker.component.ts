import { ChangeDetectionStrategy, Component, ViewChild, computed, inject, input, model } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { InputTextModule } from 'primeng/inputtext';
import { Popover, PopoverModule } from 'primeng/popover';
import { CALENDAR_WITH_HOURS_TIME_PATTERN } from './formly-preview.types';

const I18N_PREFIX = 'formly.timePicker';

const I18N_KEYS = {
  Hour: `${I18N_PREFIX}.hour`,
  Minute: `${I18N_PREFIX}.minute`,
  Clear: `${I18N_PREFIX}.clear`,
  Placeholder: `${I18N_PREFIX}.placeholder`,
} as const;

const HOURS_PER_DAY = 24;
const MINUTES_PER_HOUR = 60;

/** Minutes offered in the panel. A shift starts on the five; odd values are typed instead. */
const MINUTE_STEP = 5;

/** Longest a typed time can be — `HH:mm`. */
const TIME_LENGTH = 5;
const HOUR_DIGITS = 2;

const HOUR_OPTIONS: readonly string[] = Array.from({ length: HOURS_PER_DAY }, (_, hour) =>
  pad(hour),
);

const MINUTE_OPTIONS: readonly string[] = Array.from(
  { length: MINUTES_PER_HOUR / MINUTE_STEP },
  (_, index) => pad(index * MINUTE_STEP),
);

type ColumnKind = 'hour' | 'minute';

/**
 * A clock time on the 24-hour clock, as `HH:mm` — typed, or picked from a themed panel.
 *
 * Neither stock control could do both halves of the job, which is why this exists:
 *
 * - `p-datepicker` re-parses the whole field on every keystroke and nulls its model while the
 *   text is still partial, which fed a null back into the box and erased what was being typed.
 * - `<input type="time">` types fine, but its popup is drawn by the browser — unstyleable, and
 *   it follows the OS locale, so half the crew saw a 12-hour box with a clipped AM/PM.
 *
 * {@link value} is the raw text, including a half-typed `08:`, so a keystroke is never
 * overwritten by a re-render. Ask {@link isTime} whether it is a time yet — the callers that
 * store it decide what to do with text that is not.
 *
 * Shared by the `time` element type and by each end of a `calendar_with_hours` shift.
 */
@Component({
  selector: 'app-time-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InputTextModule, PopoverModule],
  template: `
    <div class="relative min-w-0">
      <!--
        [value] bound to the caller's own text: whatever is typed is what is stored and
        re-rendered, so a half-typed "08:" survives the next change detection pass.
      -->
      <input
        pInputText
        type="text"
        inputmode="numeric"
        autocomplete="off"
        class="w-full min-w-0 pe-9 tabular-nums"
        [attr.maxlength]="timeLength"
        [placeholder]="placeholder"
        [value]="value()"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel()"
        (input)="onInput($event)"
        (blur)="onBlur($event)"
      />
      <button
        type="button"
        class="picker-trigger absolute inset-y-0 end-0 flex w-9 items-center justify-center rounded-e-md text-[var(--p-text-muted-color)]"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel()"
        (click)="picker.toggle($event)"
      >
        <i class="pi pi-clock text-sm"></i>
      </button>

      <p-popover #picker>
        <div class="flex gap-3">
          @for (column of columns; track column.kind) {
            <div class="flex flex-col gap-1.5">
              <span
                class="px-1 text-[11px] font-semibold uppercase tracking-wide text-[var(--p-text-muted-color)]"
              >
                {{ column.kind === 'hour' ? hourLabel : minuteLabel }}
              </span>
              <div class="time-column" role="listbox">
                @for (option of column.options; track option) {
                  <button
                    type="button"
                    role="option"
                    class="time-option"
                    [class.selected]="isSelected(column.kind, option)"
                    [attr.aria-selected]="isSelected(column.kind, option)"
                    (click)="pick(column.kind, option)"
                  >
                    {{ option }}
                  </button>
                }
              </div>
            </div>
          }
        </div>

        <div class="mt-3 flex justify-end border-t border-[var(--p-content-border-color)] pt-2">
          <button
            type="button"
            class="text-xs font-medium text-[var(--p-primary-color)]"
            (click)="clear()"
          >
            {{ clearLabel }}
          </button>
        </div>
      </p-popover>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
        min-width: 0;
      }

      /* Sits inside the input's padding well, so it reads as part of the field. */
      .picker-trigger:not(:disabled):hover {
        color: var(--p-primary-color);
      }

      .picker-trigger:disabled {
        cursor: default;
        opacity: 0.5;
      }

      .time-column {
        display: flex;
        flex-direction: column;
        gap: 0.125rem;
        max-height: 11rem;
        overflow-y: auto;
        padding-inline-end: 0.25rem;
        scrollbar-width: thin;
      }

      .time-option {
        border-radius: var(--p-border-radius-sm, 0.25rem);
        padding: 0.25rem 0.75rem;
        font-size: 0.8125rem;
        font-variant-numeric: tabular-nums;
        color: var(--p-text-color);
        text-align: center;
        transition: background-color 0.15s;
      }

      .time-option:hover {
        background: var(--p-content-hover-background, rgba(127, 127, 127, 0.12));
      }

      .time-option.selected {
        background: var(--p-primary-color);
        color: var(--p-primary-contrast-color, #fff);
        font-weight: 600;
      }
    `,
  ],
})
export class TimePickerComponent {
  private readonly translate = inject(TranslateService);

  @ViewChild('picker') private readonly picker!: Popover;

  /** The raw text in the box — `HH:mm` once complete, possibly partial while it is typed. */
  readonly value = model<string>('');
  readonly disabled = input(false);
  readonly ariaLabel = input('');

  protected readonly timeLength = TIME_LENGTH;
  protected readonly columns = [
    { kind: 'hour' as const, options: HOUR_OPTIONS },
    { kind: 'minute' as const, options: MINUTE_OPTIONS },
  ];

  /** `HH` / `mm` of the current value, so the panel can highlight where it already is. */
  private readonly parts = computed(() => {
    const [hour, minute] = this.value().split(':');
    return { hour, minute };
  });

  protected get placeholder(): string {
    return this.translate.instant(I18N_KEYS.Placeholder);
  }

  protected get hourLabel(): string {
    return this.translate.instant(I18N_KEYS.Hour);
  }

  protected get minuteLabel(): string {
    return this.translate.instant(I18N_KEYS.Minute);
  }

  protected get clearLabel(): string {
    return this.translate.instant(I18N_KEYS.Clear);
  }

  /** Digits only, with the colon inserted for the user: `0830` becomes `08:30` as it is typed. */
  protected onInput(event: Event): void {
    const digits = (event.target as HTMLInputElement).value.replace(/\D/g, '').slice(0, 4);
    this.value.set(mask(digits));
  }

  /** Finish a shorthand on the way out — `8` becomes `08:00`, `830` becomes `08:30`. */
  protected onBlur(event: Event): void {
    const completed = completeTime((event.target as HTMLInputElement).value);
    if (completed !== this.value()) {
      this.value.set(completed);
    }
  }

  protected isSelected(kind: ColumnKind, option: string): boolean {
    const parts = this.parts();
    return kind === 'hour' ? parts.hour === option : parts.minute === option;
  }

  /**
   * Set one half of the time, keeping the other. Picking an hour on an empty box lands on
   * the hour rather than making the crew choose `:00` as well.
   */
  protected pick(kind: ColumnKind, option: string): void {
    const [hour = '', minute = ''] = this.value().split(':');
    this.value.set(kind === 'hour' ? `${option}:${minute || pad(0)}` : `${hour || pad(0)}:${option}`);
  }

  protected clear(): void {
    this.value.set('');
    this.picker.hide();
  }
}

/** True once the text is a real time — `24:00` and `08:75` are not. */
export function isTime(text: string): boolean {
  if (!CALENDAR_WITH_HOURS_TIME_PATTERN.test(text)) {
    return false;
  }

  const [hour, minute] = text.split(':').map(Number);
  return hour < HOURS_PER_DAY && minute < MINUTES_PER_HOUR;
}

/** `0830` -> `08:30`, `08` -> `08`. Drives the colon while typing. */
function mask(digits: string): string {
  return digits.length <= HOUR_DIGITS
    ? digits
    : `${digits.slice(0, HOUR_DIGITS)}:${digits.slice(HOUR_DIGITS)}`;
}

/**
 * Turn shorthand or a stored value into a full `HH:mm`, or '' when it cannot be one. Also the
 * way an `HH:mm:ss` from the database or a mobile client is trimmed to what the box shows.
 *
 * Out-of-range values are dropped rather than clamped — `99:99` is a typo, and silently
 * rewriting it to `23:59` would hide that from whoever typed it.
 */
export function completeTime(text: string): string {
  const digits = text.replace(/\D/g, '').slice(0, 4);
  if (digits.length === 0) {
    return '';
  }

  // 1-2 digits are an hour on its own; 3 are H:mm; 4 are HH:mm.
  const hour = digits.length <= HOUR_DIGITS ? digits : digits.slice(0, digits.length - 2);
  const minute = digits.length <= HOUR_DIGITS ? '0' : digits.slice(-2);
  const candidate = `${pad(Number(hour))}:${pad(Number(minute))}`;

  return isTime(candidate) ? candidate : '';
}

function pad(value: number): string {
  return String(value).padStart(HOUR_DIGITS, '0');
}
