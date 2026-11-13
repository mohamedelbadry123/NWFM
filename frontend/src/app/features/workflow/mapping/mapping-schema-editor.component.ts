import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  forwardRef,
  inject,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MappingEditContext } from './mapping-edit-context';
import type { MappingSchema, MappingSchemaType } from './mapping.models';
import {
  MAX_SCHEMA_DEPTH,
  SCHEMA_TYPES,
  changeSchemaType,
  isValidLiteralName,
  newSchema,
  propertyName,
} from './mapping-config';

const TYPE_LABELS: Record<MappingSchemaType, [string, string]> = {
  string: ['Text', 'نص'],
  number: ['Number', 'رقم'],
  integer: ['Whole number', 'رقم صحيح'],
  boolean: ['Yes/No', 'نعم/لا'],
  object: ['Object (group of fields)', 'كائن (مجموعة حقول)'],
  array: ['List', 'قائمة'],
};

/**
 * Visual editor for one schema node: type, required, can-be-empty, object properties, array items and open
 * objects. Used for request parameters, the body, the response, outputs and a receiving event's inputs.
 * A closed object (the default) rejects fields it does not declare; "other fields" opens it explicitly.
 */
@Component({
  selector: 'app-mapping-schema-editor',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, forwardRef(() => MappingSchemaEditorComponent)],
  styleUrl: './mapping.css',
  template: `
    <div class="mx-slot" [attr.data-schema]="idPath">
      <div class="mx-row">
        <label class="mx-field">
          {{ label || ctx.t('Type', 'النوع') }}
          <select
            class="wf-input"
            [ngModel]="schema.type"
            [disabled]="ro()"
            (ngModelChange)="setType($event)"
          >
            @for (type of types(); track type) {
              <option [value]="type">{{ typeLabel(type) }}</option>
            }
          </select>
        </label>
        @if (showRequired) {
          <label class="mx-field">
            <span>{{ ctx.t('Required', 'مطلوب') }}</span>
            <input
              type="checkbox"
              [ngModel]="!!schema.required"
              [disabled]="ro() || forceRequired"
              (ngModelChange)="patch({ required: $event })"
            />
          </label>
        }
        @if (!scalarOnly) {
          <label class="mx-field">
            <span>{{ ctx.t('Can be empty (null)', 'يمكن أن يكون فارغاً (null)') }}</span>
            <input
              type="checkbox"
              [ngModel]="!!schema.nullable"
              [disabled]="ro()"
              (ngModelChange)="patch({ nullable: $event })"
            />
          </label>
        }
        @if (schema.inferred) {
          <span class="mx-chip mx-chip-warn">{{
            ctx.t('Inferred from a sample — review it', 'مستنتج من عينة — راجعه')
          }}</span>
        }
      </div>

      @if (schema.type === 'object') {
        <div class="mx-children">
          @for (entry of entries(); track entry.name) {
            <div class="mx-child">
              <label class="mx-field">
                {{ ctx.t('Field name', 'اسم الحقل') }}
                <input
                  class="wf-input"
                  [ngModel]="entry.name"
                  [disabled]="ro()"
                  (change)="rename(entry.name, $any($event.target).value)"
                />
              </label>
              <app-mapping-schema-editor
                [schema]="entry.schema"
                [label]="entry.name"
                [showRequired]="true"
                [depth]="depth + 1"
                [idPath]="idPath + '.' + entry.name"
                (schemaChange)="setProperty(entry.name, $event)"
              />
              @if (!ro()) {
                <button
                  type="button"
                  class="mx-link mx-danger"
                  (click)="removeProperty(entry.name)"
                >
                  {{ ctx.t('Remove field', 'إزالة الحقل') }}
                </button>
              }
            </div>
          }
          @if (!ro()) {
            <button
              type="button"
              class="wf-btn-secondary"
              [disabled]="depth >= maxDepth"
              (click)="addProperty()"
            >
              {{ ctx.t('Add field', 'إضافة حقل') }}
            </button>
          }
          <label class="mx-field">
            <span>{{ ctx.t('Allow other fields', 'السماح بحقول أخرى') }}</span>
            <input
              type="checkbox"
              [ngModel]="!!schema.additionalProperties"
              [disabled]="ro()"
              (ngModelChange)="setOpen($event)"
            />
          </label>
          @if (schema.additionalProperties) {
            <p class="mx-help">
              {{
                ctx.t(
                  'Open object: fields that are not listed are accepted. Give them a type to read them in a mapping.',
                  'كائن مفتوح: تُقبل الحقول غير المدرجة. حدد لها نوعاً لقراءتها في الربط.'
                )
              }}
            </p>
            @if (schema.additionalPropertySchema) {
              <app-mapping-schema-editor
                [schema]="schema.additionalPropertySchema"
                [label]="ctx.t('Type of other fields', 'نوع الحقول الأخرى')"
                [depth]="depth + 1"
                [idPath]="idPath + '.*'"
                (schemaChange)="patch({ additionalPropertySchema: $event })"
              />
              @if (!ro()) {
                <button
                  type="button"
                  class="mx-link"
                  (click)="patch({ additionalPropertySchema: null })"
                >
                  {{ ctx.t('Leave other fields untyped', 'ترك الحقول الأخرى دون نوع') }}
                </button>
              }
            } @else if (!ro()) {
              <button
                type="button"
                class="mx-link"
                (click)="patch({ additionalPropertySchema: { type: 'string' } })"
              >
                {{ ctx.t('Type the other fields', 'تحديد نوع الحقول الأخرى') }}
              </button>
            }
          }
        </div>
      }
      @if (schema.type === 'array') {
        <div class="mx-children">
          <app-mapping-schema-editor
            [schema]="schema.items ?? { type: 'string' }"
            [label]="ctx.t('Each item is', 'كل عنصر هو')"
            [depth]="depth + 1"
            [idPath]="idPath + '[]'"
            (schemaChange)="patch({ items: $event })"
          />
        </div>
      }
    </div>
  `,
})
export class MappingSchemaEditorComponent {
  protected readonly ctx = inject(MappingEditContext);
  @Input({ required: true }) schema!: MappingSchema;
  @Input() label = '';
  @Input() showRequired = false;
  /** Path/query/header values are non-null scalars: no object, list or null. */
  @Input() scalarOnly = false;
  @Input() forceRequired = false;
  @Input() depth = 0;
  @Input() idPath = 'schema';
  @Output() schemaChange = new EventEmitter<MappingSchema>();
  protected readonly maxDepth = MAX_SCHEMA_DEPTH;

  ro = () => this.ctx.readonly();
  protected types() {
    return this.scalarOnly
      ? SCHEMA_TYPES.filter((t) => t !== 'object' && t !== 'array')
      : this.depth >= this.maxDepth
        ? SCHEMA_TYPES.filter((t) => t !== 'object' && t !== 'array')
        : SCHEMA_TYPES;
  }
  protected typeLabel(type: MappingSchemaType) {
    return this.ctx.t(TYPE_LABELS[type][0], TYPE_LABELS[type][1]);
  }
  protected entries() {
    return Object.entries(this.schema.properties ?? {}).map(([name, schema]) => ({ name, schema }));
  }
  protected patch(change: Partial<MappingSchema>) {
    const next = { ...this.schema, ...change } as MappingSchema;
    for (const key of Object.keys(change) as (keyof MappingSchema)[])
      if (change[key] === false || change[key] === null) delete next[key];
    this.schemaChange.emit(next);
  }
  protected setType(type: MappingSchemaType) {
    this.schemaChange.emit(changeSchemaType(this.schema, type));
  }
  protected setProperty(name: string, schema: MappingSchema) {
    this.patch({ properties: { ...this.schema.properties, [name]: schema } });
  }
  protected addProperty() {
    const name = propertyName('field', Object.keys(this.schema.properties ?? {}));
    this.patch({ properties: { ...this.schema.properties, [name]: newSchema('string') } });
  }
  protected removeProperty(name: string) {
    const rest = { ...this.schema.properties };
    delete rest[name];
    this.patch({ properties: rest });
  }
  protected rename(from: string, to: string) {
    const name = to.trim();
    if (
      !isValidLiteralName(name) ||
      name === from ||
      (this.schema.properties && name in this.schema.properties)
    )
      return;
    this.patch({
      properties: Object.fromEntries(
        Object.entries(this.schema.properties ?? {}).map(([k, v]) => [k === from ? name : k, v]),
      ),
    });
  }
  protected setOpen(open: boolean) {
    const next = { ...this.schema, additionalProperties: open } as MappingSchema;
    if (!open) {
      delete next.additionalProperties;
      delete next.additionalPropertySchema;
    }
    this.schemaChange.emit(next);
  }
}
