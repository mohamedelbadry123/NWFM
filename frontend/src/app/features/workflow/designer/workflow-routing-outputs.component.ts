import { Component, Input, Output, EventEmitter, OnChanges, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';

@Component({
  selector: 'app-workflow-routing-outputs',
  standalone: true,
  imports: [FormsModule],
  template: `<details class="outputs">
    <summary>{{ t('Declared routing outputs', 'مخرجات المسارات المعلنة') }}</summary>
    <p>
      {{
        t(
          'Declare completed result fields. For Events and child workflows, match output mapping keys. For an ANY Join, use fields from its winning result. ALL results keep their original source names.',
          'حدد حقول النتائج المكتملة. للأحداث والمسارات الفرعية طابق مفاتيح تعيينات المخرجات. في انتظار أي فرع استخدم حقول النتيجة الفائزة. تحتفظ نتائج كل الفروع بأسماء مصادرها الأصلية.'
        )
      }}
    </p>
    <fieldset [disabled]="readonly">
      @for (output of outputs; track $index; let i = $index) {
        <div class="output">
          <label
            >{{ t('Field key', 'مفتاح الحقل')
            }}<input
              [(ngModel)]="output.key"
              (ngModelChange)="save()"
              pattern="[A-Za-z][A-Za-z0-9_]*"
          /></label>
          <label
            >{{ t('Label', 'العنوان') }}<input [(ngModel)]="output.label" (ngModelChange)="save()"
          /></label>
          <label
            >{{ t('Type', 'النوع')
            }}<select [(ngModel)]="output.type" (ngModelChange)="save()">
              <option value="text">{{ t('Text', 'نص') }}</option>
              <option value="numeric">{{ t('Number', 'رقم') }}</option>
              <option value="yes_no">{{ t('Yes / No', 'نعم / لا') }}</option>
              <option value="date">{{ t('Date', 'تاريخ') }}</option>
              <option value="date_time">{{ t('Date and time', 'التاريخ والوقت') }}</option>
            </select></label
          >
          <label
            ><input type="checkbox" [(ngModel)]="output.sensitive" (ngModelChange)="save()" />
            {{ t('Sensitive value', 'قيمة حساسة') }}</label
          >
          <button type="button" (click)="outputs.splice(i, 1); save()">
            {{ t('Remove output', 'إزالة المخرج') }}
          </button>
        </div>
      }
      <button type="button" [disabled]="outputs.length >= 200" (click)="add()">
        {{ t('Add output field', 'إضافة حقل مخرج') }}
      </button>
    </fieldset>
  </details>`,
  styles: [
    `
      .outputs {
        margin: 16px;
        padding: 14px;
        border: 1px solid var(--ds-border);
        border-radius: 8px;
      }
      summary {
        cursor: pointer;
        font-weight: 600;
      }
      p {
        font-size: 12px;
        line-height: 1.6;
        margin: 10px 0;
      }
      .output {
        border-block-end: 1px solid var(--ds-border);
        padding: 12px 0;
      }
      label {
        display: block;
        font-size: 12px;
        margin: 8px 0;
      }
      input:not([type='checkbox']),
      select {
        display: block;
        width: 100%;
        padding: 7px;
        border: 1px solid var(--ds-border-strong);
        background: var(--ds-surface);
        color: var(--ds-text);
        border-radius: 5px;
      }
      button {
        border: 1px solid var(--ds-border-strong);
        padding: 7px 10px;
        border-radius: 5px;
        margin-top: 8px;
      }
      button:focus-visible,
      input:focus-visible,
      select:focus-visible {
        outline: 3px solid var(--ds-focus);
        outline-offset: 2px;
      }
    `,
  ],
})
export class WorkflowRoutingOutputsComponent implements OnChanges {
  @Input() configuration = '{}';
  @Input() readonly = false;
  @Output() changed = new EventEmitter<string>();
  private readonly locale = inject(LocaleService);
  outputs: { key: string; type: string; label: string; sensitive?: boolean }[] = [];
  t(en: string, ar: string) {
    return this.locale.isRtl() ? ar : en;
  }
  ngOnChanges() {
    try {
      this.outputs = JSON.parse(this.configuration).routingOutputs || [];
    } catch {
      this.outputs = [];
    }
  }
  add() {
    this.outputs.push({ key: '', type: 'text', label: '' });
    this.save();
  }
  save() {
    if (!this.readonly)
      this.changed.emit(
        JSON.stringify({ ...JSON.parse(this.configuration), routingOutputs: this.outputs }),
      );
  }
}
