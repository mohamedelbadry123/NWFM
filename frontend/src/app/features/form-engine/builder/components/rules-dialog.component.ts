import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { InputTextModule } from 'primeng/inputtext';
import { CheckboxModule } from 'primeng/checkbox';
import { TranslateService } from '@ngx-translate/core';
import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import {
  RULE_MATCH,
  RULE_MODES,
  RULE_OPERATORS,
  type RuleCondition,
  type RuleGroup,
  type RuleMode,
  type RuleOperator,
} from '../../../../shared/form-schema/form-schema.types';

export interface RuleFieldOption {
  readonly data_name: string;
  readonly label: string;
}

const VALUELESS_OPERATORS: readonly RuleOperator[] = [
  RULE_OPERATORS.IsEmpty,
  RULE_OPERATORS.IsNotEmpty,
];

@Component({
  selector: 'app-rules-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    FormsModule,
    DialogModule,
    ButtonModule,
    SelectModule,
    InputTextModule,
    CheckboxModule,
    TranslateContextDirective,
  ],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [header]="mode() === requirementMode ? t('formBuilder.rules.requirementTitle') : t('formBuilder.rules.visibilityTitle')"
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [style]="{ width: '44rem', minHeight: '36rem' }"
        [contentStyle]="{ overflow: 'visible', minHeight: '22rem' }"
        [draggable]="false"
        [maximizable]="true"
      >
        <p class="text-sm text-[var(--p-text-muted-color)] mb-4">
          {{ mode() === requirementMode ? t('formBuilder.rules.requirementHint') : t('formBuilder.rules.visibilityHint') }}
        </p>

        <div class="flex items-center gap-2 mb-4">
          <span class="text-sm">{{ t('formBuilder.rules.match') }}</span>
          <p-select
            [options]="matchOptions"
            optionLabel="label"
            optionValue="value"
            [(ngModel)]="group().match"
            styleClass="w-32"
          />
          <span class="text-sm">{{ t('formBuilder.rules.ofConditions') }}</span>
        </div>

        <div class="space-y-2">
          @for (condition of group().conditions; track $index) {
            <div class="flex items-center gap-2">
              <p-select
                [options]="fields()"
                optionLabel="label"
                optionValue="data_name"
                [(ngModel)]="condition.field"
                [placeholder]="t('formBuilder.rules.selectField')"
                styleClass="flex-1"
              />
              <p-select
                [options]="operatorOptions"
                optionLabel="label"
                optionValue="value"
                [(ngModel)]="condition.operator"
                [placeholder]="t('formBuilder.rules.operator')"
                styleClass="w-44"
              />
              @if (needsValue(condition.operator)) {
                <input pInputText class="flex-1" [(ngModel)]="condition.value" [placeholder]="t('formBuilder.rules.value')" />
              }
              <p-button icon="pi pi-times" [text]="true" severity="danger" size="small" (onClick)="removeCondition($index)" />
            </div>
          }
        </div>

        <p-button class="mt-3 inline-block" [label]="t('formBuilder.rules.addCondition')" icon="pi pi-plus" size="small" [outlined]="true" (onClick)="addCondition()" />

        @if (mode() !== requirementMode) {
          <div class="mt-5 pt-4 border-t border-[var(--app-border)] flex items-start gap-2">
            <p-checkbox [(ngModel)]="group().preserve_data" [binary]="true" inputId="preserveData" />
            <label for="preserveData" class="text-sm">
              <span class="font-semibold block">{{ t('formBuilder.rules.preserveData') }}</span>
              <span class="text-[var(--p-text-muted-color)]">{{ t('formBuilder.rules.preserveDataHint') }}</span>
            </label>
          </div>
        }

        <ng-template pTemplate="footer">
          <p-button [label]="t('formBuilder.actions.cancel')" [text]="true" (onClick)="close()" />
          <p-button [label]="t('formBuilder.actions.save')" (onClick)="save()" />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class RulesDialogComponent {
  readonly visible = model(false);
  readonly mode = input<RuleMode>(RULE_MODES.Visibility);
  readonly rules = input<RuleGroup | null>(null);
  readonly fields = input<RuleFieldOption[]>([]);
  readonly rulesChange = output<RuleGroup>();

  protected readonly requirementMode = RULE_MODES.Requirement;

  private readonly translate = inject(TranslateService);

  /** Working copy edited in the dialog; committed on save. */
  protected readonly group = signal<RuleGroup>({ match: RULE_MATCH.All, conditions: [], preserve_data: false });

  protected readonly matchOptions = [
    { label: this.translate.instant('formBuilder.match.all'), value: RULE_MATCH.All },
    { label: this.translate.instant('formBuilder.match.any'), value: RULE_MATCH.Any },
  ];

  protected readonly operatorOptions = Object.values(RULE_OPERATORS).map((value) => ({
    label: this.translate.instant('formBuilder.operators.' + value),
    value,
  }));

  constructor() {
    effect(() => {
      if (this.visible()) {
        const current = this.rules();
        this.group.set(
          current
            ? structuredClone(current)
            : { match: RULE_MATCH.All, conditions: [], preserve_data: false },
        );
      }
    });
  }

  protected needsValue(operator: RuleOperator): boolean {
    return !VALUELESS_OPERATORS.includes(operator);
  }

  protected addCondition(): void {
    const next: RuleCondition = { field: '', operator: RULE_OPERATORS.Equal, value: '' };
    this.group.update((g) => ({ ...g, conditions: [...g.conditions, next] }));
  }

  protected removeCondition(index: number): void {
    this.group.update((g) => ({ ...g, conditions: g.conditions.filter((_, i) => i !== index) }));
  }

  protected save(): void {
    this.rulesChange.emit(structuredClone(this.group()));
    this.visible.set(false);
  }

  protected close(): void {
    this.visible.set(false);
  }
}
