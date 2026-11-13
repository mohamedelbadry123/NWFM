import { Component, EventEmitter, Input, Output, OnChanges, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import {
  RoutingContext,
  RoutingSimulation,
  RoutingTrace,
  WorkflowRoutingService,
} from './workflow-routing.service';
import { SimulationRow, simulationRows } from './workflow-routing-trace';
import { WorkflowRoutingRulesComponent } from './workflow-routing-rules.component';

@Component({
  selector: 'app-workflow-routing-simulation',
  standalone: true,
  imports: [FormsModule, NgTemplateOutlet, WorkflowRoutingRulesComponent],
  template: ` <div class="simulation" [attr.aria-busy]="busy()">
    <section>
      <h3>{{ t('1. Inputs · sample data', '١. المدخلات · بيانات تجريبية') }}</h3>
      <p>
        {{
          t(
            'No tasks, messages or external calls are created.',
            'لا يتم إنشاء مهام أو رسائل أو اتصالات خارجية.'
          )
        }}
      </p>
      <ng-template #sample let-field>
        @switch (field.type) {
          @case ('numeric') {
            <label
              >{{ field.label }}
              <input
                type="number"
                [attr.data-sample]="field.key"
                [ngModel]="samples[field.key]"
                (ngModelChange)="setNumber(field.key, $event)"
            /></label>
          }
          @case ('yes_no') {
            <label
              >{{ field.label }}
              <select
                [attr.data-sample]="field.key"
                [ngModel]="samples[field.key]"
                (ngModelChange)="setValue(field.key, $event)"
              >
                <option [ngValue]="undefined">{{ t('Empty', 'فارغ') }}</option>
                <option [ngValue]="true">{{ t('Yes', 'نعم') }}</option>
                <option [ngValue]="false">{{ t('No', 'لا') }}</option>
              </select>
            </label>
          }
          @case ('multiple_choice') {
            @if (field.choices?.length) {
              <fieldset [attr.data-sample]="field.key">
                <legend>{{ field.label }}</legend>
                @for (choice of field.choices; track choice.value) {
                  <label class="choice"
                    ><input
                      type="checkbox"
                      [checked]="isChosen(field.key, choice.value)"
                      (change)="toggleChoice(field.key, choice.value, $any($event.target).checked)"
                    />
                    {{ locale.isRtl() ? choice.labelAr || choice.labelEn : choice.labelEn }}</label
                  >
                }
              </fieldset>
            } @else {
              <label
                >{{ field.label }}
                <input
                  [attr.data-sample]="field.key"
                  [attr.placeholder]="t('Values separated by commas', 'قيم مفصولة بفواصل')"
                  [ngModel]="listText(field.key)"
                  (ngModelChange)="setList(field.key, $event)"
              /></label>
            }
          }
          @default {
            <label
              >{{ field.label }}
              <input
                [type]="inputType(field.type)"
                [attr.data-sample]="field.key"
                [(ngModel)]="samples[field.key]"
            /></label>
          }
        }
      </ng-template>
      @if (!result()) {
        @for (field of inputs(); track field.key) {
          <ng-container
            [ngTemplateOutlet]="sample"
            [ngTemplateOutletContext]="{ $implicit: field }"
          />
        }
        <button type="button" [disabled]="busy()" (click)="start()">
          {{ t('2. Run simulation', '٢. تشغيل المحاكاة') }}
        </button>
      } @else {
        <label
          >{{ t('Complete a waiting element', 'إكمال عنصر منتظر') }}
          <select [disabled]="busy()" [(ngModel)]="selected" (ngModelChange)="samples = {}">
            <option value="">{{ t('Select Step or Event', 'اختر خطوة أو حدث') }}</option>
            @for (item of pending(); track key(item)) {
              <option [value]="key(item)">{{ name(item.nodeKey) }} · {{ item.branch }}</option>
            }
          </select>
        </label>
        @if (current(); as item) {
          @for (field of fields(item.nodeKey); track field.key) {
            <ng-container
              [ngTemplateOutlet]="sample"
              [ngTemplateOutletContext]="{ $implicit: field }"
            />
          }
          <label>{{ t('Outcome', 'النتيجة') }}<input [(ngModel)]="outcome" /></label>
          <button type="button" [disabled]="busy() || !outcome.trim()" (click)="complete()">
            {{ t('Submit mock result', 'إرسال نتيجة تجريبية') }}
          </button>
        }
        <div class="clock">
          <label
            >{{ t('Advance seconds', 'تقديم الوقت بالثواني')
            }}<input type="number" min="1" max="31536000" [(ngModel)]="seconds" /></label
          ><button type="button" [disabled]="busy() || seconds < 1" (click)="advance()">
            {{ t('Advance clock', 'تقديم الوقت') }}
          </button>
        </div>
        <button type="button" [disabled]="busy()" (click)="reset()">
          {{ t('Reset sample run', 'إعادة المحاكاة') }}
        </button>
      }
      @if (error()) {
        <p role="alert">{{ error() }}</p>
      }
    </section>
    <section aria-live="polite">
      <h2>{{ t('3. Results', '٣. النتائج') }}</h2>
      @if (!result()) {
        <p>
          {{ t('Run a sample to see what happens and why.', 'شغّل مثالاً لمعرفة ما يحدث ولماذا.') }}
        </p>
      }
      <h3>{{ t('Why is this waiting?', 'لماذا ينتظر؟') }}</h3>
      @for (item of result()?.waiting || []; track $index) {
        <button type="button" class="trace" (click)="focusNode.emit(item.nodeKey)">
          <strong>{{ name(item.nodeKey) }} · {{ item.status }}</strong
          ><span>{{ item.explanation }}</span>
        </button>
      }
      @for (item of result()?.diagnostics || []; track $index) {
        <button type="button" class="trace issue" (click)="focusNode.emit(item.nodeKey || '')">
          {{ item.message }}
        </button>
      }
      <h3>{{ t('Execution trace', 'سجل التنفيذ') }}</h3>
      @for (row of rows(); track $index) {
        @if (row.kind === 'rules') {
          <div class="trace rules-block" data-trace-rules>
            <button type="button" class="rules-title" (click)="focusNode.emit(row.nodeKey)">
              <strong
                >{{ row.number }}. {{ name(row.nodeKey) }} ·
                {{ t('Routes tried', 'المسارات المجرَّبة') }}</strong
              >
            </button>
            <p class="first-match">
              {{
                t(
                  'Routes are tried from top to bottom and the first match is used. Routes after it are not tried.',
                  'تُجرَّب المسارات من الأعلى إلى الأسفل ويُستخدم أول مسار مطابق. لا تُجرَّب المسارات التي بعده.'
                )
              }}
            </p>
            <app-workflow-routing-rules
              [rules]="row.rules"
              [destination]="destinationOf(row.nodeKey)"
            />
          </div>
        } @else {
          <button type="button" class="trace" (click)="focusNode.emit(row.item.nodeKey)">
            <strong>{{ row.number }}. {{ name(row.item.nodeKey) }} · {{ row.item.status }}</strong
            ><span>{{ row.item.explanation }}</span>
          </button>
        }
      }
    </section>
  </div>`,
  styles: [
    `
      .simulation {
        display: grid;
        grid-template-columns: minmax(230px, 320px) 1fr;
        gap: 24px;
        padding: 18px;
        color: var(--ds-text);
      }
      h3 {
        font-weight: 650;
        margin-bottom: 10px;
      }
      p {
        font-size: 12px;
        color: var(--ds-text-3);
        margin-bottom: 10px;
      }
      label {
        display: block;
        font-size: 12px;
        margin: 10px 0;
      }
      input,
      select {
        display: block;
        width: 100%;
        padding: 7px;
        background: var(--ds-surface);
        border: 1px solid var(--ds-border-strong);
        border-radius: 5px;
        color: var(--ds-text);
      }
      button {
        border: 1px solid var(--ds-border-strong);
        border-radius: 5px;
        background: var(--ds-surface);
        color: var(--ds-text);
        padding: 7px 10px;
      }
      button:disabled {
        opacity: 0.5;
      }
      button:focus-visible,
      input:focus-visible,
      select:focus-visible {
        outline: 3px solid var(--ds-focus);
        outline-offset: 2px;
      }
      .trace {
        display: block;
        text-align: start;
        width: 100%;
        margin: 8px 0;
        font-size: 12px;
      }
      .trace span {
        display: block;
        margin-top: 4px;
      }
      .issue {
        color: var(--ds-danger);
      }
      .rules-block {
        border: 1px solid var(--ds-border);
        border-radius: 5px;
        padding: 8px;
      }
      .rules-title {
        display: block;
        width: 100%;
        text-align: start;
        border: 0;
        padding: 0;
      }
      .first-match {
        margin: 4px 0;
      }
      fieldset {
        border: 0;
        margin: 10px 0;
        padding: 0;
        font-size: 12px;
      }
      .choice {
        display: flex;
        gap: 6px;
        align-items: center;
      }
      .choice input {
        width: auto;
      }
      .clock {
        display: flex;
        gap: 10px;
        align-items: center;
      }
      @media (max-width: 700px) {
        .simulation {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
})
export class WorkflowRoutingSimulationComponent implements OnChanges {
  @Input({ required: true }) definitionId = '';
  @Input({ required: true }) versionId = '';
  @Input() context: RoutingContext | null = null;
  @Output() focusNode = new EventEmitter<string>();
  private readonly api = inject(WorkflowRoutingService);
  protected readonly locale = inject(LocaleService);
  readonly result = signal<RoutingSimulation | null>(null);
  readonly error = signal('');
  readonly busy = signal(false);
  samples: Record<string, unknown> = {};
  selected = '';
  outcome = 'success';
  seconds = 60;
  private signals: Parameters<WorkflowRoutingService['simulate']>[2] = [];
  private initial: Record<string, unknown> = {};
  t(en: string, ar: string) {
    return this.locale.isRtl() ? ar : en;
  }
  private seen = '';
  private runToken = 0;
  /**
   * A sample run belongs to one saved workflow. It is dropped when that workflow really changes (another version, or a
   * new revision or graph), never because the designer re-fetched an identical context: a refresh that lands while
   * the author is running or completing a sample must not erase the result they are reading.
   */
  ngOnChanges() {
    const identity = JSON.stringify([this.definitionId, this.versionId, this.context]);
    if (identity === this.seen) return;
    this.seen = identity;
    this.reset();
  }
  reset() {
    this.runToken++;
    this.busy.set(false);
    this.result.set(null);
    this.rows.set([]);
    this.destinations.clear();
    this.signals = [];
    this.initial = {};
    this.samples = {};
    this.selected = '';
    this.error.set('');
  }
  name(key: string) {
    return this.context?.nodes.find((n) => n.key === key)?.name || key;
  }
  /** The trace with each Condition's route lines folded into one ordered block. */
  readonly rows = signal<SimulationRow[]>([]);
  private readonly destinations = new Map<string, (key: string) => string>();
  /** Names where a route of the given Condition leads, from its connection key (one stable function per Condition). */
  destinationOf(nodeKey: string): (key: string) => string {
    let lookup = this.destinations.get(nodeKey);
    if (!lookup) {
      lookup = (key: string) => {
        const connection = this.context?.connections.find(
          (c) => c.from === nodeKey && c.key === key,
        );
        return connection ? this.name(connection.to) : '';
      };
      this.destinations.set(nodeKey, lookup);
    }
    return lookup;
  }
  inputs() {
    return this.context?.sources.filter((s) => s.nodeKey === '$input') || [];
  }
  fields(node: string) {
    return this.context?.sources.filter((s) => s.nodeKey === node && s.field !== '$outcome') || [];
  }
  setValue(key: string, value: unknown) {
    this.samples[key] = value;
  }
  /** A number control sends a real number (or nothing), never the text the user typed. */
  setNumber(key: string, value: unknown) {
    this.samples[key] = typeof value === 'number' && Number.isFinite(value) ? value : undefined;
  }
  private list(key: string): string[] {
    const value = this.samples[key];
    return Array.isArray(value) ? (value as string[]) : [];
  }
  isChosen(key: string, value: string) {
    return this.list(key).includes(value);
  }
  toggleChoice(key: string, value: string, chosen: boolean) {
    const next = this.list(key).filter((v) => v !== value);
    if (chosen) next.push(value);
    this.samples[key] = next.length ? next : undefined;
  }
  listText(key: string) {
    return this.list(key).join(', ');
  }
  setList(key: string, text: string) {
    const items = text
      .split(',')
      .map((item) => item.trim())
      .filter(Boolean);
    this.samples[key] = items.length ? items : undefined;
  }
  inputType(type: string) {
    return type === 'numeric'
      ? 'number'
      : type === 'date'
        ? 'date'
        : type === 'date_time'
          ? 'datetime-local'
          : 'text';
  }
  pending() {
    return (
      this.result()?.waiting.filter((w) =>
        this.context?.nodes.some(
          (n) => n.key === w.nodeKey && !['Start', 'End', 'JoinGateway'].includes(n.type),
        ),
      ) || []
    );
  }
  key(item: RoutingTrace) {
    return item.nodeKey + '|' + item.branch;
  }
  current() {
    return this.pending().find((w) => this.key(w) === this.selected);
  }
  async start() {
    this.initial = Object.fromEntries(
      this.inputs()
        .filter((f) => this.samples[f.key] !== undefined)
        .map((f) => [f.field, this.samples[f.key]]),
    );
    await this.run();
  }
  async complete() {
    const item = this.current();
    if (!item) return;
    const values = Object.fromEntries(
      this.fields(item.nodeKey)
        .filter((f) => this.samples[f.key] !== undefined)
        .map((f) => [f.field, this.samples[f.key]]),
    );
    this.signals.push({
      nodeKey: item.nodeKey,
      branch: item.branch,
      outcome: this.outcome,
      values,
    });
    // Only the run that is still current may clear the form: after the workflow changed, `selected` and
    // `samples` belong to the new context, whether this late answer succeeded or failed.
    if (!(await this.run())) return;
    this.selected = '';
    this.samples = {};
  }
  async advance() {
    this.signals.push({ nodeKey: '$clock', outcome: '', advanceSeconds: this.seconds });
    await this.run();
  }
  /** Resolves true only when the workflow was unchanged while this ran (its answer or failure was applied). */
  private async run(): Promise<boolean> {
    const token = ++this.runToken;
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await firstValueFrom(
        this.api.simulate(this.definitionId, this.versionId, this.signals, this.initial),
      );
      // The workflow changed while this ran: the answer describes a workflow that is no longer shown.
      if (token !== this.runToken) return false;
      this.result.set(result);
      this.rows.set(simulationRows(result.trace));
    } catch {
      if (token !== this.runToken) return false;
      this.error.set(
        this.t(
          'Simulation failed. Check your connection and retry.',
          'فشلت المحاكاة. تحقق من الاتصال ثم أعد المحاولة.',
        ),
      );
    } finally {
      if (token === this.runToken) this.busy.set(false);
    }
    return true;
  }
}
