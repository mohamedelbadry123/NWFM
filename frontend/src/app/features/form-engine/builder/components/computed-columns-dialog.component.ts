import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TooltipModule } from 'primeng/tooltip';
import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { checkExpression } from '../../../../shared/form-schema/form-computed-expression';
import {
  COMPUTED_KEY_PATTERN,
  COMPUTED_MAX_COLUMNS,
  COMPUTED_MAX_RULES,
  COMPUTED_OUTPUT_TYPES,
  RULE_MATCH,
  RULE_MODES,
  type ComputedColumn,
  type ComputedRule,
  type RuleGroup,
} from '../../../../shared/form-schema/form-schema.types';
import { RulesDialogComponent, type RuleFieldOption } from './rules-dialog.component';

/** One problem with the working columns, and the column it is on. */
interface ColumnProblem {
  readonly column: number;
  readonly message: string;
}

/**
 * Designs a form's computed columns — values worked out of each fill and shown beside the task in
 * the task grid. Each column tries its rules in order ("when these conditions hold, the value is
 * this expression") and falls back to its default. Edits a working copy; nothing reaches the form
 * until Apply, and nothing reaches the server until the form is saved.
 */
@Component({
  selector: 'app-computed-columns-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    CheckboxModule,
    DialogModule,
    InputTextModule,
    MessageModule,
    SelectModule,
    TooltipModule,
    TranslateContextDirective,
    RulesDialogComponent,
  ],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [header]="t('formBuilder.computed.title')"
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [maximizable]="true"
        [draggable]="false"
        [style]="{ width: '64rem' }"
        [breakpoints]="{ '960px': '96vw' }"
      >
        @let problems = validate();
        <p class="mb-4 text-sm text-[var(--p-text-muted-color)]">{{ t('formBuilder.computed.hint') }}</p>

        <div class="grid grid-cols-1 gap-4 md:grid-cols-[16rem_1fr]">
          <!-- The columns -->
          <div class="flex flex-col gap-2">
            @for (column of columns(); track $index; let i = $index; let first = $first; let last = $last) {
              <div
                class="flex items-center gap-1 rounded-lg border px-2 py-1.5 text-sm cursor-pointer"
                [class.border-[var(--p-primary-color)]]="selected() === i"
                [class.border-[var(--app-border)]]="selected() !== i"
                (click)="selected.set(i)"
              >
                <div class="min-w-0 flex-1">
                  <div class="truncate font-medium">{{ column.label_en || column.key || t('formBuilder.computed.untitled') }}</div>
                  <div class="truncate font-mono text-xs text-[var(--p-text-muted-color)]">{{ column.key }}</div>
                </div>
                @if (hasProblem(problems, i)) {
                  <i class="pi pi-exclamation-circle text-red-500"></i>
                }
                <p-button icon="pi pi-arrow-up" size="small" [text]="true" [rounded]="true" severity="secondary" [disabled]="first" (onClick)="moveColumn(i, -1)" />
                <p-button icon="pi pi-arrow-down" size="small" [text]="true" [rounded]="true" severity="secondary" [disabled]="last" (onClick)="moveColumn(i, 1)" />
                <p-button icon="pi pi-trash" size="small" [text]="true" [rounded]="true" severity="danger" (onClick)="removeColumn(i)" />
              </div>
            } @empty {
              <p class="text-sm text-[var(--p-text-muted-color)]">{{ t('formBuilder.computed.empty') }}</p>
            }
            <p-button
              [label]="t('formBuilder.computed.addColumn')"
              icon="pi pi-plus"
              size="small"
              [outlined]="true"
              [disabled]="columns().length >= maxColumns"
              (onClick)="addColumn()"
            />
          </div>

          <!-- The selected column -->
          @if (columns()[selected()]; as column) {
            <div class="flex flex-col gap-4">
              <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div class="flex flex-col gap-1">
                  <label class="text-xs font-semibold">{{ t('formBuilder.computed.key') }}</label>
                  <input pInputText class="font-mono" [(ngModel)]="column.key" [placeholder]="'severity'" />
                  <small class="text-[var(--p-text-muted-color)]">{{ t('formBuilder.computed.keyHint') }}</small>
                </div>
                <div class="flex flex-col gap-1">
                  <label class="text-xs font-semibold">{{ t('formBuilder.computed.outputType') }}</label>
                  <p-select [options]="outputTypes" optionLabel="label" optionValue="value" [(ngModel)]="column.output_type" appendTo="body" />
                </div>
                <div class="flex flex-col gap-1">
                  <label class="text-xs font-semibold">{{ t('formBuilder.computed.labelEn') }}</label>
                  <input pInputText [(ngModel)]="column.label_en" />
                </div>
                <div class="flex flex-col gap-1">
                  <label class="text-xs font-semibold">{{ t('formBuilder.computed.labelAr') }}</label>
                  <input pInputText dir="rtl" [(ngModel)]="column.label_ar" />
                </div>
                <div class="flex items-center gap-2 sm:col-span-2">
                  <p-checkbox [(ngModel)]="column.show_in_task_grid" [binary]="true" inputId="computed-show" />
                  <label for="computed-show" class="text-sm">{{ t('formBuilder.computed.showInGrid') }}</label>
                </div>
              </div>

              <div class="flex flex-col gap-2">
                <h3 class="text-xs font-semibold uppercase tracking-wide text-[var(--p-text-muted-color)]">{{ t('formBuilder.computed.rules') }}</h3>
                @for (rule of column.rules; track $index; let r = $index; let firstRule = $first; let lastRule = $last) {
                  <div class="flex flex-col gap-1 rounded-lg border border-[var(--app-border)] p-2">
                    <div class="flex flex-wrap items-center gap-2">
                      <span class="text-sm font-semibold">{{ t(firstRule ? 'formBuilder.computed.if' : 'formBuilder.computed.elseIf') }}</span>
                      <p-button
                        [label]="conditionSummary(rule.when)"
                        icon="pi pi-filter"
                        size="small"
                        [outlined]="true"
                        [severity]="rule.when.conditions.length === 0 ? 'danger' : 'secondary'"
                        (onClick)="editConditions(r)"
                      />
                      <span class="text-sm font-semibold">{{ t('formBuilder.computed.then') }}</span>
                      <input pInputText class="min-w-[12rem] flex-1 font-mono" [(ngModel)]="rule.then" [placeholder]="t('formBuilder.computed.expressionPlaceholder')" />
                      <p-button icon="pi pi-arrow-up" size="small" [text]="true" [rounded]="true" severity="secondary" [disabled]="firstRule" (onClick)="moveRule(r, -1)" />
                      <p-button icon="pi pi-arrow-down" size="small" [text]="true" [rounded]="true" severity="secondary" [disabled]="lastRule" (onClick)="moveRule(r, 1)" />
                      <p-button icon="pi pi-times" size="small" [text]="true" [rounded]="true" severity="danger" (onClick)="removeRule(r)" />
                    </div>
                    @if (expressionError(rule.then); as error) {
                      <small class="text-red-500">{{ error }}</small>
                    }
                  </div>
                }
                <p-button
                  class="self-start"
                  [label]="t('formBuilder.computed.addRule')"
                  icon="pi pi-plus"
                  size="small"
                  [outlined]="true"
                  [disabled]="column.rules.length >= maxRules"
                  (onClick)="addRule()"
                />
              </div>

              <div class="flex flex-col gap-1">
                <label class="text-xs font-semibold">{{ t(column.rules.length > 0 ? 'formBuilder.computed.otherwise' : 'formBuilder.computed.value') }}</label>
                <input pInputText class="font-mono" [(ngModel)]="column.default" [placeholder]="t('formBuilder.computed.expressionPlaceholder')" />
                @if (column.default.trim() && expressionError(column.default); as error) {
                  <small class="text-red-500">{{ error }}</small>
                }
              </div>

              <details class="rounded-lg bg-[var(--p-content-hover-background)] p-3 text-sm">
                <summary class="cursor-pointer font-semibold">{{ t('formBuilder.computed.helpTitle') }}</summary>
                <ul class="mt-2 list-disc space-y-1 ps-5 text-[var(--p-text-muted-color)]">
                  <li>{{ t('formBuilder.computed.helpText') }}</li>
                  <li>{{ t('formBuilder.computed.helpFields') }}</li>
                  <li>{{ t('formBuilder.computed.helpMath') }}</li>
                  <li>{{ t('formBuilder.computed.helpFunctions') }}</li>
                </ul>
                <div class="mt-2 flex flex-wrap gap-1">
                  @for (field of fields(); track field.data_name) {
                    <code class="rounded bg-[var(--app-surface)] px-1.5 py-0.5 text-xs" [pTooltip]="field.label">{{ field.data_name }}</code>
                  }
                </div>
              </details>
            </div>
          }
        </div>

        @if (problems.length > 0) {
          <div class="mt-4 flex flex-col gap-1">
            @for (problem of problems; track $index) {
              <p-message severity="warn" size="small" [text]="problem.message" styleClass="w-full" />
            }
          </div>
        }

        <ng-template pTemplate="footer">
          <p-button [label]="t('formBuilder.actions.cancel')" [text]="true" (onClick)="visible.set(false)" />
          <p-button [label]="t('formBuilder.computed.apply')" icon="pi pi-check" [disabled]="problems.length > 0" (onClick)="apply()" />
        </ng-template>
      </p-dialog>

      <app-rules-dialog
        [(visible)]="rulesVisible"
        [mode]="conditionMode"
        [rules]="editingWhen()"
        [fields]="fields()"
        (rulesChange)="onConditionsChange($event)"
      />
    </ng-container>
  `,
})
export class ComputedColumnsDialogComponent {
  readonly visible = model(false);
  /** The form's current columns; copied when the dialog opens. */
  readonly value = input<ComputedColumn[]>([]);
  /** The form's answerable fields, for conditions and expressions. */
  readonly fields = input<RuleFieldOption[]>([]);
  readonly applied = output<ComputedColumn[]>();

  private readonly translate = inject(TranslateService);

  protected readonly maxColumns = COMPUTED_MAX_COLUMNS;
  protected readonly maxRules = COMPUTED_MAX_RULES;
  protected readonly conditionMode = RULE_MODES.Condition;

  protected readonly columns = signal<ComputedColumn[]>([]);
  protected readonly selected = signal(0);

  protected readonly rulesVisible = signal(false);
  private readonly editingRule = signal<number | null>(null);
  protected readonly editingWhen = signal<RuleGroup | null>(null);

  protected readonly outputTypes = [
    { label: this.translate.instant('formBuilder.computed.outputTypes.text'), value: COMPUTED_OUTPUT_TYPES.Text },
    { label: this.translate.instant('formBuilder.computed.outputTypes.number'), value: COMPUTED_OUTPUT_TYPES.Number },
  ];

  constructor() {
    effect(() => {
      if (this.visible()) {
        this.columns.set(structuredClone(this.value()));
        this.selected.set(0);
      }
    });
  }

  protected addColumn(): void {
    const next: ComputedColumn = {
      key: `column_${this.columns().length + 1}`,
      label_en: '',
      label_ar: '',
      output_type: COMPUTED_OUTPUT_TYPES.Text,
      show_in_task_grid: true,
      rules: [],
      default: '',
    };
    this.columns.update((columns) => [...columns, next]);
    this.selected.set(this.columns().length - 1);
  }

  protected removeColumn(index: number): void {
    this.columns.update((columns) => columns.filter((_, i) => i !== index));
    this.selected.set(Math.max(0, Math.min(this.selected(), this.columns().length - 1)));
  }

  protected moveColumn(index: number, by: -1 | 1): void {
    this.columns.update((columns) => swap(columns, index, index + by));
    this.selected.set(index + by);
  }

  protected addRule(): void {
    const column = this.columns()[this.selected()];
    if (!column) {
      return;
    }
    const rule: ComputedRule = { when: { match: RULE_MATCH.All, conditions: [], preserve_data: false }, then: '' };
    column.rules = [...column.rules, rule];
    this.columns.update((columns) => [...columns]);
    this.editConditions(column.rules.length - 1);
  }

  protected removeRule(index: number): void {
    const column = this.columns()[this.selected()];
    column.rules = column.rules.filter((_, i) => i !== index);
    this.columns.update((columns) => [...columns]);
  }

  protected moveRule(index: number, by: -1 | 1): void {
    const column = this.columns()[this.selected()];
    column.rules = swap(column.rules, index, index + by);
    this.columns.update((columns) => [...columns]);
  }

  protected editConditions(ruleIndex: number): void {
    const rule = this.columns()[this.selected()]?.rules[ruleIndex];
    if (!rule) {
      return;
    }
    this.editingRule.set(ruleIndex);
    this.editingWhen.set(rule.when);
    this.rulesVisible.set(true);
  }

  protected onConditionsChange(group: RuleGroup): void {
    const index = this.editingRule();
    const column = this.columns()[this.selected()];
    if (index === null || !column?.rules[index]) {
      return;
    }
    column.rules[index] = { ...column.rules[index], when: group };
    this.columns.update((columns) => [...columns]);
  }

  protected conditionSummary(group: RuleGroup): string {
    const count = group.conditions.filter((c) => c.field).length;
    return count === 0
      ? this.translate.instant('formBuilder.computed.setConditions')
      : this.translate.instant('formBuilder.computed.conditionCount', { count });
  }

  protected expressionError(text: string): string | null {
    const check = checkExpression(text);
    if (check.error) {
      return `${check.error} (${check.position! + 1})`;
    }

    const known = new Set(this.fields().map((f) => f.data_name.toLowerCase()));
    const unknown = check.fields.filter((f) => !known.has(f.toLowerCase()));
    return unknown.length > 0
      ? this.translate.instant('formBuilder.computed.unknownFields', { fields: unknown.join(', ') })
      : null;
  }

  protected hasProblem(problems: ColumnProblem[], index: number): boolean {
    return problems.some((p) => p.column === index);
  }

  /** Every problem the server would refuse, so Apply is only offered for columns it will take. */
  protected validate(): ColumnProblem[] {
    const problems: ColumnProblem[] = [];
    const seen = new Set<string>();
    const known = new Set(this.fields().map((f) => f.data_name.toLowerCase()));

    this.columns().forEach((column, i) => {
      const name = column.key || this.translate.instant('formBuilder.computed.untitled');
      const add = (key: string, params: Record<string, unknown> = {}) =>
        problems.push({ column: i, message: `${name}: ${this.translate.instant(key, params)}` });

      if (!COMPUTED_KEY_PATTERN.test(column.key.trim())) {
        add('formBuilder.computed.problems.key');
      } else if (seen.has(column.key.trim())) {
        add('formBuilder.computed.problems.duplicate');
      } else {
        seen.add(column.key.trim());
      }

      if (!column.label_en.trim() || !column.label_ar.trim()) {
        add('formBuilder.computed.problems.labels');
      }

      if (column.rules.length === 0 && !column.default.trim()) {
        add('formBuilder.computed.problems.noValue');
      }

      column.rules.forEach((rule, r) => {
        const conditions = rule.when.conditions.filter((c) => c.field);
        if (conditions.length === 0) {
          add('formBuilder.computed.problems.noConditions', { rule: r + 1 });
        }
        const missing = conditions.filter((c) => !known.has(c.field.toLowerCase())).map((c) => c.field);
        if (missing.length > 0) {
          add('formBuilder.computed.unknownFields', { fields: missing.join(', ') });
        }
        if (this.expressionError(rule.then)) {
          add('formBuilder.computed.problems.expression', { rule: r + 1 });
        }
      });

      if (column.default.trim() && this.expressionError(column.default)) {
        add('formBuilder.computed.problems.defaultExpression');
      }
    });

    return problems;
  }

  protected apply(): void {
    if (this.validate().length > 0) {
      return;
    }
    this.applied.emit(structuredClone(this.columns()));
    this.visible.set(false);
  }
}

function swap<T>(items: T[], from: number, to: number): T[] {
  if (to < 0 || to >= items.length) {
    return items;
  }
  const next = [...items];
  [next[from], next[to]] = [next[to], next[from]];
  return next;
}
