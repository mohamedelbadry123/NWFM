import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  inject,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MappingEditContext } from './mapping-edit-context';
import { MappingSchemaEditorComponent } from './mapping-schema-editor.component';
import { MappingTransformEditorComponent } from './mapping-transform-editor.component';
import type { OutputDefinition } from './mapping.models';
import {
  emptyNode,
  isValidLiteralName,
  newId,
  newSchema,
  propertyName,
  sourceNode,
  typeHint,
} from './mapping-config';

/**
 * Outputs: the named, typed values this event publishes once it completes. Each has a stable ID (so later
 * mappings keep pointing at it through renames and reordering) and may read the event's own response, any source,
 * or an output declared above it. The order is the evaluation order, so move an output below what it uses.
 */
@Component({
  selector: 'app-mapping-outputs-stage',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MappingSchemaEditorComponent, MappingTransformEditorComponent],
  styleUrl: './mapping.css',
  template: `
    @for (output of outputs; track output.id; let i = $index) {
      <section
        class="mx-slot"
        [attr.data-output]="output.id"
        [attr.aria-label]="ctx.t('Output ', 'المخرج ') + output.name"
      >
        <div class="mx-row">
          <label class="mx-field mx-grow">
            {{ ctx.t('Output name', 'اسم المخرج') }}
            <input
              class="wf-input"
              [ngModel]="output.name"
              [disabled]="ctx.readonly()"
              [attr.aria-invalid]="!nameOk(output, i)"
              (change)="rename(i, $any($event.target).value)"
            />
          </label>
          <label class="mx-field">
            <span>{{ ctx.t('Sensitive', 'حساس') }}</span>
            <input
              type="checkbox"
              [ngModel]="!!output.sensitive"
              [disabled]="ctx.readonly()"
              (ngModelChange)="patch(i, { sensitive: $event || undefined })"
            />
          </label>
          @if (!ctx.readonly()) {
            <button
              type="button"
              class="wf-btn-secondary"
              [disabled]="i === 0"
              [attr.aria-label]="
                ctx.t('Move ' + output.name + ' up', 'انقل ' + output.name + ' للأعلى')
              "
              (click)="move(i, -1)"
            >
              ↑
            </button>
            <button
              type="button"
              class="wf-btn-secondary"
              [disabled]="i === outputs.length - 1"
              [attr.aria-label]="
                ctx.t('Move ' + output.name + ' down', 'انقل ' + output.name + ' للأسفل')
              "
              (click)="move(i, 1)"
            >
              ↓
            </button>
            <button type="button" class="mx-link mx-danger" (click)="remove(i)">
              {{ ctx.t('Remove output', 'إزالة المخرج') }}
            </button>
          }
        </div>
        @if (!nameOk(output, i)) {
          <p class="mx-error" role="alert">
            {{
              ctx.t(
                'Names are 1–200 characters and must be unique.',
                'الأسماء من 1 إلى 200 حرف ويجب أن تكون فريدة.'
              )
            }}
          </p>
        }
        <span class="mx-help"
          >{{ ctx.t('Stable ID', 'المعرّف الثابت') }}: <code dir="ltr">{{ output.id }}</code></span
        >
        <details>
          <summary>{{ ctx.t('Type of this output', 'نوع هذا المخرج') }}</summary>
          <app-mapping-schema-editor
            [schema]="output.schema"
            [showRequired]="false"
            [idPath]="'output:' + output.id"
            (schemaChange)="patch(i, { schema: $event })"
          />
          @if (!ctx.readonly() && hintFor(output)) {
            <button type="button" class="mx-link" (click)="matchType(i)">
              {{ ctx.t('Use the type of the mapped value', 'استخدم نوع القيمة المربوطة') }}
            </button>
          }
        </details>
        <app-mapping-transform-editor
          [node]="output.value"
          [label]="output.name"
          [expected]="output.schema"
          [idPath]="'output:' + output.id + ':value'"
          [showSummary]="true"
          (nodeChange)="patch(i, { value: $event })"
        />
      </section>
    } @empty {
      <p class="mx-help">
        {{
          ctx.t(
            'No outputs yet. Add the values later steps, forms and follow-up actions should be able to use.',
            'لا توجد مخرجات بعد. أضف القيم التي يمكن للخطوات والنماذج والإجراءات اللاحقة استخدامها.'
          )
        }}
      </p>
    }
    @if (!ctx.readonly()) {
      <div class="mx-row">
        <button type="button" class="wf-btn-secondary" (click)="add()">
          {{ ctx.t('Add output', 'إضافة مخرج') }}
        </button>
        <button
          type="button"
          class="wf-btn-secondary"
          [disabled]="!ctx.liveTray().length"
          (click)="addFromTray()"
        >
          {{ ctx.t('Add the tray as outputs', 'أضف الصينية كمخرجات') }} ({{
            ctx.liveTray().length
          }})
        </button>
      </div>
    }
    @for (message of serverProblems; track message) {
      <p class="mx-error" role="alert">{{ message }}</p>
    }
  `,
})
export class MappingOutputsStageComponent {
  protected readonly ctx = inject(MappingEditContext);
  @Input({ required: true }) outputs: OutputDefinition[] = [];
  @Input() serverProblems: string[] = [];
  @Output() outputsChange = new EventEmitter<OutputDefinition[]>();

  protected nameOk(output: OutputDefinition, index: number) {
    return (
      isValidLiteralName(output.name) &&
      !this.outputs.some((o, i) => i !== index && o.name === output.name)
    );
  }
  protected hintFor(output: OutputDefinition) {
    return typeHint(output.value, this.ctx.schemaFor, null);
  }
  private emit(next: OutputDefinition[]) {
    this.outputsChange.emit(next);
  }
  protected patch(index: number, change: Partial<OutputDefinition>) {
    const next = { ...this.outputs[index], ...change } as OutputDefinition;
    for (const key of Object.keys(change) as (keyof OutputDefinition)[])
      if (change[key] === undefined) delete next[key];
    this.emit(this.outputs.map((o, i) => (i === index ? next : o)));
  }
  protected rename(index: number, name: string) {
    const trimmed = name.trim();
    if (
      isValidLiteralName(trimmed) &&
      !this.outputs.some((o, i) => i !== index && o.name === trimmed)
    )
      this.patch(index, { name: trimmed });
    else
      this.ctx.announce(
        this.ctx.t(
          'That output name is not valid or already used.',
          'اسم المخرج غير صالح أو مستخدم.',
        ),
      );
  }
  protected matchType(index: number) {
    const hint = this.hintFor(this.outputs[index]);
    if (hint) this.patch(index, { schema: { ...hint, required: undefined, inferred: undefined } });
  }
  protected move(index: number, by: number) {
    const next = [...this.outputs];
    const target = index + by;
    if (target < 0 || target >= next.length) return;
    [next[index], next[target]] = [next[target], next[index]];
    this.emit(next);
  }
  protected remove(index: number) {
    this.emit(this.outputs.filter((_, i) => i !== index));
  }
  protected add() {
    this.emit([
      ...this.outputs,
      {
        id: newId(
          'out',
          this.outputs.map((o) => o.id),
        ),
        name: propertyName(
          'output',
          this.outputs.map((o) => o.name),
        ),
        schema: newSchema('string'),
        value: emptyNode(),
      },
    ]);
  }
  protected addFromTray() {
    const next = [...this.outputs];
    for (const d of this.ctx.liveTray()) {
      next.push({
        id: newId(
          'out',
          next.map((o) => o.id),
        ),
        name: propertyName(
          d.label,
          next.map((o) => o.name),
        ),
        schema: { ...d.schema, required: undefined, inferred: undefined },
        value: sourceNode(d.reference),
        ...(d.sensitive ? { sensitive: true } : {}),
      });
    }
    this.emit(next);
    this.ctx.clearTray();
  }
}
