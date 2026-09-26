import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TranslateService } from '@ngx-translate/core';
import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import type { ValidationPattern } from '../../../../shared/form-schema/form-schema.types';

interface PatternExample {
  readonly labelKey: string;
  readonly regex: string;
}

const PATTERN_EXAMPLES: readonly PatternExample[] = [
  { labelKey: 'formBuilder.pattern.examples.alphanumeric', regex: '^[A-Za-z0-9]+$' },
  { labelKey: 'formBuilder.pattern.examples.exactly5Letters', regex: '^[A-Za-z]{5}$' },
  { labelKey: 'formBuilder.pattern.examples.exactly3Chars', regex: '^.{3}$' },
  { labelKey: 'formBuilder.pattern.examples.digitsOnly', regex: '^[0-9]+$' },
  { labelKey: 'formBuilder.pattern.examples.usPhone', regex: '^\\(?\\d{3}\\)?[-.\\s]?\\d{3}[-.\\s]?\\d{4}$' },
  { labelKey: 'formBuilder.pattern.examples.price', regex: '^\\d+(\\.\\d{1,2})?$' },
  { labelKey: 'formBuilder.pattern.examples.year', regex: '^(19|20)\\d{2}$' },
  { labelKey: 'formBuilder.pattern.examples.uppercase', regex: '^[A-Z]+$' },
  { labelKey: 'formBuilder.pattern.examples.ip', regex: '^(\\d{1,3}\\.){3}\\d{1,3}$' },
  { labelKey: 'formBuilder.pattern.examples.singleLine', regex: '^[^\\n]*$' },
];

@Component({
  selector: 'app-validation-pattern-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, DialogModule, ButtonModule, InputTextModule, TranslateContextDirective],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [header]="t('formBuilder.pattern.title')"
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [style]="{ width: '46rem' }"
        [draggable]="false"
        [maximizable]="true"
      >
        <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
          <div class="md:col-span-2 space-y-4">
            <div>
              <label class="block text-sm font-semibold mb-1">{{ t('formBuilder.pattern.pattern') }} *</label>
              <input pInputText class="w-full" [(ngModel)]="regex" (ngModelChange)="runTest()" />
            </div>
            <div>
              <label class="block text-sm font-semibold mb-1">{{ t('formBuilder.pattern.description') }} *</label>
              <input pInputText class="w-full" [(ngModel)]="description" />
            </div>

            <div class="rounded-lg bg-[var(--app-surface-alt)] border border-[var(--app-border)] p-3">
              <p class="text-xs text-[var(--p-text-muted-color)] mb-2">{{ t('formBuilder.pattern.tryHint') }}</p>
              <div class="flex items-center gap-2">
                <input pInputText class="flex-1" [(ngModel)]="testValue" (ngModelChange)="runTest()" [placeholder]="t('formBuilder.pattern.tryPlaceholder')" />
                @if (testResult() === true) {
                  <i class="pi pi-check-circle text-[var(--acc-success-fg)] text-lg"></i>
                } @else if (testResult() === false) {
                  <i class="pi pi-times-circle text-[var(--acc-danger-fg)] text-lg"></i>
                }
              </div>
            </div>
          </div>

          <div class="rounded-lg bg-[var(--app-surface-alt)] border border-[var(--app-border)] p-3">
            <h4 class="text-sm font-bold mb-2">{{ t('formBuilder.pattern.examples.title') }}</h4>
            <ul class="space-y-1.5">
              @for (ex of examples; track ex.regex) {
                <li>
                  <button type="button" class="text-sm text-[var(--p-primary-color)] hover:underline text-start" (click)="applyExample(ex)">
                    {{ t(ex.labelKey) }}
                  </button>
                </li>
              }
            </ul>
          </div>
        </div>

        <ng-template pTemplate="footer">
          <p-button [label]="t('formBuilder.actions.cancel')" [text]="true" (onClick)="close()" />
          <p-button [label]="t('formBuilder.actions.save')" (onClick)="save()" [disabled]="!regex.trim() || !description.trim()" />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class ValidationPatternDialogComponent {
  private readonly translate = inject(TranslateService);

  readonly visible = model(false);
  readonly pattern = input<ValidationPattern | null>(null);
  readonly patternChange = output<ValidationPattern | null>();

  protected readonly examples = PATTERN_EXAMPLES;

  protected regex = '';
  protected description = '';
  protected testValue = '';
  protected readonly testResult = signal<boolean | null>(null);

  constructor() {
    effect(() => {
      if (this.visible()) {
        const current = this.pattern();
        this.regex = current?.regex ?? '';
        this.description = current?.description ?? '';
        this.testValue = '';
        this.testResult.set(null);
      }
    });
  }

  protected applyExample(example: PatternExample): void {
    this.regex = example.regex;
    this.description = this.translate.instant(example.labelKey);
    this.runTest();
  }

  protected runTest(): void {
    if (!this.regex.trim() || !this.testValue) {
      this.testResult.set(null);
      return;
    }
    try {
      this.testResult.set(new RegExp(this.regex).test(this.testValue));
    } catch {
      this.testResult.set(null);
    }
  }

  protected save(): void {
    this.patternChange.emit({ regex: this.regex.trim(), description: this.description.trim() });
    this.visible.set(false);
  }

  protected close(): void {
    this.visible.set(false);
  }
}
