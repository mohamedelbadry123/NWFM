import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  Output,
  computed,
  forwardRef,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MappingDropTargetDirective } from './mapping-dnd.directives';
import { MappingEditContext, type MappingTarget } from './mapping-edit-context';
import type {
  MappingSchema,
  MissingDataPolicy,
  SourceDescriptor,
  TransformNode,
  TransformOp,
} from './mapping.models';
import {
  MAX_TRANSFORM_DEPTH,
  arityOf,
  availabilityInfo,
  breadcrumbText,
  callNode,
  describeSchema,
  emptyNode,
  itemNode,
  literalNode,
  looksCompatible,
  opLabel,
  propertyName,
  isValidLiteralName,
  isValidPointer,
  schemaPaths,
  skeleton,
  sourceKey,
  sourceNode,
  summarize,
  typeHint,
} from './mapping-config';

interface OpGroup {
  label: [string, string];
  ops: TransformOp[];
}
const OP_GROUPS: OpGroup[] = [
  { label: ['Value', 'قيمة'], ops: ['source', 'literal'] },
  { label: ['Build', 'بناء'], ops: ['object', 'concat'] },
  { label: ['Calculate', 'حساب'], ops: ['add', 'subtract', 'multiply', 'divide'] },
  { label: ['Convert', 'تحويل'], ops: ['text', 'number', 'integer', 'boolean'] },
  { label: ['Fallback', 'بديل'], ops: ['coalesce'] },
  { label: ['Lists', 'قوائم'], ops: ['map', 'filter', 'count', 'sum'] },
  { label: ['Compare', 'مقارنة'], ops: ['equal', 'greater'] },
];
const SLOT_LABELS: Partial<Record<TransformOp, [string, string][]>> = {
  add: [
    ['Left', 'اليسار'],
    ['Right', 'اليمين'],
  ],
  subtract: [
    ['From', 'من'],
    ['Subtract', 'اطرح'],
  ],
  multiply: [
    ['Left', 'اليسار'],
    ['Right', 'اليمين'],
  ],
  divide: [
    ['Divide', 'اقسم'],
    ['By', 'على'],
  ],
  equal: [
    ['Left', 'اليسار'],
    ['Right', 'اليمين'],
  ],
  greater: [
    ['Value', 'القيمة'],
    ['Is greater than', 'أكبر من'],
  ],
  map: [
    ['List', 'القائمة'],
    ['For each item', 'لكل عنصر'],
  ],
  filter: [
    ['List', 'القائمة'],
    ['Keep an item when', 'احتفظ بالعنصر عندما'],
  ],
  text: [['Value to convert', 'القيمة المراد تحويلها']],
  number: [['Value to convert', 'القيمة المراد تحويلها']],
  integer: [['Value to convert', 'القيمة المراد تحويلها']],
  boolean: [['Value to convert', 'القيمة المراد تحويلها']],
  count: [['List', 'القائمة']],
  sum: [['List of numbers', 'قائمة أرقام']],
};

/** A constant is cheap to retype and a plain source is just a pointer, so mapping onto either replaces it. */
export const isReplaceable = (node: TransformNode) => node.op === 'literal' || node.op === 'source';

/**
 * Recursive visual editor for one mapping expression — the full supported set: source, constant, object,
 * join text, arithmetic, conversions, first-available, map/filter/count/sum over lists, and comparisons.
 * It builds documents only; values are always computed by the server (the Test stage previews them).
 *
 * Every slot is a drop target for a dragged source AND offers the equivalent click/keyboard controls
 * (Use as target + the browser's Map button, or the tray select below).
 */
@Component({
  selector: 'app-mapping-transform-editor',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MappingDropTargetDirective,
    forwardRef(() => MappingTransformEditorComponent),
  ],
  styleUrl: './mapping.css',
  template: `
    <div
      class="mx-slot"
      [class.mx-slot-active]="ctx.activeTarget()?.id === target().id"
      [mappingDropTarget]="target()"
      [attr.data-slot]="idPath"
    >
      <div class="mx-slot-head">
        <span class="mx-slot-label" [id]="idPath + '-label'">{{ label }}</span>
        @if (hint(); as h) {
          <span
            class="mx-chip"
            [class.mx-chip-warn]="!compatible()"
            [attr.title]="ctx.t('Type this produces', 'النوع الذي ينتجه')"
            >{{ h }}</span
          >
        }
        <button
          type="button"
          class="mx-link"
          [attr.aria-pressed]="ctx.activeTarget()?.id === target().id"
          [attr.aria-label]="
            ctx.t('Use ' + label + ' as the mapping target', 'استخدم ' + label + ' كهدف للربط')
          "
          (click)="ctx.setActive(ctx.activeTarget()?.id === target().id ? null : target())"
        >
          {{
            ctx.activeTarget()?.id === target().id
              ? ctx.t('Target selected', 'تم تحديد الهدف')
              : ctx.t('Use as target', 'استخدم كهدف')
          }}
        </button>
      </div>

      <label class="mx-field">
        <span class="mx-sr">{{ ctx.t('How this value is produced', 'كيف تنتج هذه القيمة') }}</span>
        <select
          class="wf-input"
          [ngModel]="kindDraft() ?? node.op"
          [disabled]="ro()"
          (ngModelChange)="changeOp($event)"
          [attr.aria-labelledby]="idPath + '-label'"
        >
          @for (group of groups; track group.label[0]) {
            <optgroup [label]="ctx.t(group.label[0], group.label[1])">
              @for (op of group.ops; track op) {
                <option [value]="op" [disabled]="!canUse(op)">{{ opText(op) }}</option>
              }
            </optgroup>
          }
          @if (itemSchema || node.op === 'item') {
            <optgroup [label]="ctx.t('Inside a list', 'داخل قائمة')">
              <option value="item">{{ opText('item') }}</option>
            </optgroup>
          }
        </select>
      </label>

      @switch (kindDraft() ?? node.op) {
        @case ('source') {
          @if (node.op === 'source' && node.source && kindDraft() === null) {
            @let descriptor = current();
            <div class="mx-source" [class.mx-source-broken]="!descriptor">
              @if (descriptor) {
                <strong>{{ descriptor.label }}</strong>
                <span class="mx-crumbs">{{ crumbs(descriptor) }}</span>
                <span class="mx-chip">{{ describe(descriptor.schema) }}</span>
                <span class="mx-chip" [class.mx-chip-warn]="avail(descriptor).waits">{{
                  avail(descriptor).text
                }}</span>
                @if (descriptor.sensitive) {
                  <span class="mx-chip mx-chip-warn">{{ ctx.t('Sensitive', 'حساس') }}</span>
                }
              } @else {
                <strong role="alert">{{ node.source.fieldKey }}</strong>
                <span class="mx-error" role="alert">{{
                  ctx.t(
                    'This source is not in the current catalog (version, form or schema changed, or it is not available to you). It is kept until you correct it.',
                    'هذا المصدر غير موجود في الفهرس الحالي (تغير الإصدار أو النموذج أو المخطط أو لا يتاح لك). سيبقى حتى تصححه.'
                  )
                }}</span>
              }
              @if (descriptor && pathOptions().length > 1) {
                <label class="mx-field">
                  {{ ctx.t('Use this part of the value', 'استخدم هذا الجزء من القيمة') }}
                  <select
                    class="wf-input"
                    [ngModel]="node.source.jsonPointer"
                    [disabled]="ro()"
                    (ngModelChange)="setPointer($event)"
                  >
                    @for (p of pathOptions(); track p.pointer) {
                      <option [value]="p.pointer">{{ p.label }} — {{ describe(p.schema) }}</option>
                    }
                  </select>
                </label>
              }
              @if (descriptor) {
                <label class="mx-field">
                  {{
                    ctx.t(
                      'Exact path inside the value (JSON pointer, optional)',
                      'المسار الدقيق داخل القيمة (مؤشر JSON، اختياري)'
                    )
                  }}
                  <input
                    class="wf-input mx-code"
                    dir="ltr"
                    spellcheck="false"
                    placeholder="/customer/name"
                    [ngModel]="node.source.jsonPointer"
                    [disabled]="ro()"
                    [attr.aria-invalid]="!pointerValid(node.source.jsonPointer)"
                    (change)="setPointer($any($event.target).value)"
                  />
                  <span class="mx-help">{{
                    ctx.t(
                      'Use for map keys or fields the picker cannot list. Escape “/” as ~1 and “~” as ~0.',
                      'للمفاتيح الديناميكية أو الحقول التي لا تعرضها القائمة. اكتب «/» كـ ~1 و«~» كـ ~0.'
                    )
                  }}</span>
                  @if (!pointerValid(node.source.jsonPointer)) {
                    <span class="mx-error" role="alert">{{
                      ctx.t('A pointer is empty or starts with “/”.', 'المؤشر فارغ أو يبدأ بـ «/».')
                    }}</span>
                  }
                </label>
              }
              <label class="mx-field">
                {{ ctx.t('If this value is not available', 'إذا لم تتوفر هذه القيمة') }}
                <select
                  class="wf-input"
                  [ngModel]="node.missing?.mode ?? 'Attention'"
                  [disabled]="ro()"
                  (ngModelChange)="setMissingMode($event)"
                >
                  <option value="Attention">
                    {{
                      ctx.t('Stop and ask for attention (default)', 'توقف واطلب الانتباه (افتراضي)')
                    }}
                  </option>
                  <option value="Wait">
                    {{
                      ctx.t(
                        'Wait for it, then use the timeout outcome',
                        'انتظر ثم استخدم نتيجة المهلة'
                      )
                    }}
                  </option>
                  <option value="Fallback">
                    {{ ctx.t('Use a fallback value', 'استخدم قيمة بديلة') }}
                  </option>
                </select>
              </label>
              @if (node.missing?.mode === 'Wait') {
                <div class="mx-row">
                  <label class="mx-field">
                    {{ ctx.t('Wait up to (seconds)', 'الانتظار حتى (ثوانٍ)') }}
                    <input
                      class="wf-input"
                      type="number"
                      min="1"
                      max="31536000"
                      [ngModel]="node.missing?.deadlineSeconds"
                      [disabled]="ro()"
                      (ngModelChange)="patchMissing({ deadlineSeconds: $event })"
                    />
                  </label>
                  <label class="mx-field">
                    {{ ctx.t('Outcome on timeout', 'النتيجة عند انتهاء المهلة') }}
                    <input
                      class="wf-input"
                      [ngModel]="node.missing?.timeoutOutcome"
                      [disabled]="ro()"
                      (ngModelChange)="patchMissing({ timeoutOutcome: $event })"
                    />
                  </label>
                </div>
              }
              @if (node.missing?.mode === 'Fallback') {
                <div class="mx-row">
                  <label class="mx-field">
                    {{ ctx.t('Fallback type', 'نوع القيمة البديلة') }}
                    <select
                      class="wf-input"
                      [ngModel]="fallbackKind()"
                      [disabled]="ro()"
                      (ngModelChange)="setFallbackKind($event)"
                    >
                      <option value="string">{{ ctx.t('Text', 'نص') }}</option>
                      <option value="number">{{ ctx.t('Number', 'رقم') }}</option>
                      <option value="boolean">{{ ctx.t('Yes/No', 'نعم/لا') }}</option>
                      <option value="null">{{ ctx.t('Empty (null)', 'فارغ (null)') }}</option>
                    </select>
                  </label>
                  @if (fallbackKind() !== 'null') {
                    <label class="mx-field">
                      {{ ctx.t('Fallback value', 'القيمة البديلة') }}
                      @if (fallbackKind() === 'boolean') {
                        <select
                          class="wf-input"
                          [ngModel]="node.missing?.fallback === true ? 'true' : 'false'"
                          [disabled]="ro()"
                          (ngModelChange)="patchMissing({ fallback: $event === 'true' })"
                        >
                          <option value="true">{{ ctx.t('Yes', 'نعم') }}</option>
                          <option value="false">{{ ctx.t('No', 'لا') }}</option>
                        </select>
                      } @else {
                        <input
                          class="wf-input"
                          [type]="fallbackKind() === 'number' ? 'number' : 'text'"
                          [ngModel]="node.missing?.fallback"
                          [disabled]="ro()"
                          (ngModelChange)="
                            patchMissing({
                              fallback:
                                fallbackKind() === 'number'
                                  ? $event === '' || $event === null
                                    ? 0
                                    : +$event
                                  : $event,
                            })
                          "
                        />
                      }
                    </label>
                  }
                </div>
                <p class="mx-help">
                  {{
                    ctx.t(
                      'A fallback only replaces a MISSING value. An explicit empty value is kept as empty.',
                      'تحل القيمة البديلة محل القيمة المفقودة فقط. القيمة الفارغة الصريحة تبقى فارغة.'
                    )
                  }}
                </p>
              }
            </div>
          } @else {
            <div class="mx-pick">
              <p class="mx-help">
                {{
                  ctx.t(
                    'Drop a source here, or choose one from the tray.',
                    'أفلت مصدراً هنا أو اختر واحداً من الصينية.'
                  )
                }}
              </p>
              @if (ctx.liveTray().length) {
                <label class="mx-field">
                  {{ ctx.t('Source from the tray', 'مصدر من الصينية') }}
                  <select
                    class="wf-input"
                    [ngModel]="''"
                    [disabled]="ro()"
                    (ngModelChange)="pickFromTray($event)"
                  >
                    <option value="">{{ ctx.t('Choose…', 'اختر…') }}</option>
                    @for (d of ctx.liveTray(); track key(d)) {
                      <option [value]="key(d)">{{ crumbs(d) }}</option>
                    }
                  </select>
                </label>
              } @else {
                <p class="mx-help">
                  {{
                    ctx.t(
                      'The tray is empty: select sources in the browser first.',
                      'الصينية فارغة: حدد مصادر في المتصفح أولاً.'
                    )
                  }}
                </p>
              }
            </div>
          }
        }
        @case ('literal') {
          <div class="mx-row">
            <label class="mx-field">
              {{ ctx.t('Type', 'النوع') }}
              <select
                class="wf-input"
                [ngModel]="literalKind()"
                [disabled]="ro()"
                (ngModelChange)="setLiteralKind($event)"
              >
                <option value="string">{{ ctx.t('Text', 'نص') }}</option>
                <option value="number">{{ ctx.t('Number', 'رقم') }}</option>
                <option value="integer">{{ ctx.t('Whole number', 'رقم صحيح') }}</option>
                <option value="boolean">{{ ctx.t('Yes/No', 'نعم/لا') }}</option>
                <option value="null">{{ ctx.t('Empty (null)', 'فارغ (null)') }}</option>
                <option value="json">
                  {{ ctx.t('List or object (advanced)', 'قائمة أو كائن (متقدم)') }}
                </option>
              </select>
            </label>
            @switch (literalKind()) {
              @case ('boolean') {
                <label class="mx-field">
                  {{ ctx.t('Value', 'القيمة') }}
                  <select
                    class="wf-input"
                    [ngModel]="node.value === true ? 'true' : 'false'"
                    [disabled]="ro()"
                    (ngModelChange)="setLiteral($event === 'true')"
                  >
                    <option value="true">{{ ctx.t('Yes', 'نعم') }}</option>
                    <option value="false">{{ ctx.t('No', 'لا') }}</option>
                  </select>
                </label>
              }
              @case ('null') {
                <p class="mx-help">
                  {{
                    ctx.t(
                      'An explicit empty value (different from a missing one).',
                      'قيمة فارغة صريحة (تختلف عن المفقودة).'
                    )
                  }}
                </p>
              }
              @case ('json') {
                <label class="mx-field mx-grow">
                  {{ ctx.t('JSON value', 'قيمة JSON') }}
                  <textarea
                    class="wf-input mx-code"
                    rows="3"
                    spellcheck="false"
                    [ngModel]="jsonText()"
                    [disabled]="ro()"
                    (ngModelChange)="setJson($event)"
                  ></textarea>
                </label>
                @if (jsonError()) {
                  <p class="mx-error" role="alert">{{ jsonError() }}</p>
                }
              }
              @default {
                <label class="mx-field mx-grow">
                  {{ ctx.t('Value', 'القيمة') }}
                  <input
                    class="wf-input"
                    [type]="literalKind() === 'string' ? 'text' : 'number'"
                    [step]="literalKind() === 'integer' ? 1 : 'any'"
                    [ngModel]="node.value"
                    [disabled]="ro()"
                    (ngModelChange)="
                      setLiteral(
                        literalKind() === 'string'
                          ? $event
                          : $event === '' || $event === null
                            ? 0
                            : +$event
                      )
                    "
                  />
                </label>
              }
            }
          </div>
        }
        @case ('item') {
          @if (itemPaths().length > 1) {
            <label class="mx-field">
              {{ ctx.t('Field of the current item', 'حقل العنصر الحالي') }}
              <select
                class="wf-input"
                [ngModel]="node.jsonPointer ?? ''"
                [disabled]="ro()"
                (ngModelChange)="setItemPointer($event)"
              >
                @if (!itemPathListed()) {
                  <option [value]="node.jsonPointer ?? ''">
                    {{ ctx.t('Custom path', 'مسار مخصص') }}: {{ node.jsonPointer }}
                  </option>
                }
                @for (p of itemPaths(); track p.pointer) {
                  <option [value]="p.pointer">{{ p.label }} — {{ describe(p.schema) }}</option>
                }
              </select>
            </label>
          }
          <label class="mx-field">
            {{
              itemPaths().length > 1
                ? ctx.t(
                    'Exact path inside the item (JSON pointer, optional)',
                    'المسار الدقيق داخل العنصر (مؤشر JSON، اختياري)'
                  )
                : ctx.t('Field of the current item', 'حقل العنصر الحالي')
            }}
            <input
              class="wf-input mx-code"
              dir="ltr"
              spellcheck="false"
              placeholder="/name"
              data-testid="item-pointer"
              [value]="itemPointerText()"
              [disabled]="ro()"
              [attr.aria-invalid]="itemPointerDraft() !== null"
              (change)="setItemPointerText($any($event.target).value)"
            />
            <span class="mx-help">{{
              ctx.t(
                'Empty means the whole item; otherwise a JSON pointer such as /name, /scores/0 or /a~1b (“/” is ~1, “~” is ~0). Use it for dictionary keys and nested lists the picker cannot list.',
                'الفارغ يعني العنصر كاملاً؛ وإلا مؤشر JSON مثل /name أو /scores/0 أو /a~1b («/» هي ~1 و«~» هي ~0). استخدمه لمفاتيح القواميس والقوائم المتداخلة التي لا تعرضها القائمة.'
              )
            }}</span>
            @if (itemPointerDraft() !== null) {
              <span class="mx-error" role="alert">{{
                ctx.t('A pointer is empty or starts with “/”.', 'المؤشر فارغ أو يبدأ بـ «/».')
              }}</span>
            }
          </label>
        }
        @case ('object') {
          <div class="mx-children">
            @for (entry of props(); track entry.name) {
              <div class="mx-child">
                <label class="mx-field">
                  {{ ctx.t('Field name', 'اسم الحقل') }}
                  <input
                    class="wf-input"
                    [ngModel]="entry.name"
                    [disabled]="ro()"
                    (change)="renameProp(entry.name, $any($event.target).value)"
                    [attr.aria-invalid]="!validName(entry.name)"
                  />
                </label>
                <app-mapping-transform-editor
                  [node]="entry.node"
                  [label]="entry.name"
                  [depth]="depth + 1"
                  [itemSchema]="itemSchema"
                  [idPath]="idPath + '.' + entry.name"
                  [expected]="expectedProperty(entry.name)"
                  (nodeChange)="setProp(entry.name, $event)"
                />
                @if (!ro()) {
                  <button type="button" class="mx-link mx-danger" (click)="removeProp(entry.name)">
                    {{ ctx.t('Remove field', 'إزالة الحقل') }}
                  </button>
                }
              </div>
            }
            @if (!ro()) {
              <div class="mx-row">
                <button
                  type="button"
                  class="wf-btn-secondary"
                  (click)="addProp()"
                  [disabled]="atMaxDepth()"
                >
                  {{ ctx.t('Add field', 'إضافة حقل') }}
                </button>
                @if (ctx.liveTray().length) {
                  <button
                    type="button"
                    class="wf-btn-secondary"
                    (click)="addTrayAsProps()"
                    [disabled]="atMaxDepth()"
                  >
                    {{ ctx.t('Add tray sources as fields', 'أضف مصادر الصينية كحقول') }} ({{
                      ctx.liveTray().length
                    }})
                  </button>
                }
              </div>
            }
          </div>
        }
        @case ('concat') {
          <div class="mx-children">
            @for (arg of args(); track $index) {
              <div class="mx-child">
                <app-mapping-transform-editor
                  [node]="arg"
                  [label]="ctx.t('Piece', 'جزء') + ' ' + ($index + 1)"
                  [depth]="depth + 1"
                  [itemSchema]="itemSchema"
                  [idPath]="idPath + '.' + $index"
                  [expected]="textSchema"
                  (nodeChange)="setArg($index, $event)"
                />
                @if (needsText(arg)) {
                  <p class="mx-help mx-warn">
                    {{
                      ctx.t(
                        'This piece is not text. Join text needs text pieces.',
                        'هذا الجزء ليس نصاً. دمج النص يتطلب أجزاء نصية.'
                      )
                    }}
                    @if (!ro()) {
                      <button type="button" class="mx-link" (click)="wrapArg($index, 'text')">
                        {{ ctx.t('Convert to text', 'تحويل إلى نص') }}
                      </button>
                    }
                  </p>
                }
                @if (!ro() && args().length > 1) {
                  <button type="button" class="mx-link mx-danger" (click)="removeArg($index)">
                    {{ ctx.t('Remove piece', 'إزالة الجزء') }}
                  </button>
                }
              </div>
            }
            @if (!ro()) {
              <div class="mx-row">
                <button
                  type="button"
                  class="wf-btn-secondary"
                  (click)="addArg()"
                  [disabled]="args().length >= 100 || atMaxDepth()"
                >
                  {{ ctx.t('Add piece', 'إضافة جزء') }}
                </button>
                @if (ctx.liveTray().length) {
                  <button
                    type="button"
                    class="wf-btn-secondary"
                    (click)="addTrayAsArgs(true)"
                    [disabled]="atMaxDepth()"
                  >
                    {{ ctx.t('Add tray sources', 'أضف مصادر الصينية') }} ({{
                      ctx.liveTray().length
                    }})
                  </button>
                }
              </div>
            }
          </div>
        }
        @case ('coalesce') {
          <div class="mx-children">
            <p class="mx-help">
              {{
                ctx.t(
                  'Uses the first value that is available and not empty. Invalid conversions still fail.',
                  'يستخدم أول قيمة متوفرة وغير فارغة. التحويلات غير الصالحة تفشل.'
                )
              }}
            </p>
            @for (arg of args(); track $index) {
              <div class="mx-child">
                <app-mapping-transform-editor
                  [node]="arg"
                  [label]="
                    $index === 0
                      ? ctx.t('First choice', 'الخيار الأول')
                      : ctx.t('Then', 'ثم') + ' ' + $index
                  "
                  [depth]="depth + 1"
                  [itemSchema]="itemSchema"
                  [idPath]="idPath + '.' + $index"
                  [expected]="expected"
                  (nodeChange)="setArg($index, $event)"
                />
                @if (!ro() && args().length > 1) {
                  <button type="button" class="mx-link mx-danger" (click)="removeArg($index)">
                    {{ ctx.t('Remove', 'إزالة') }}
                  </button>
                }
              </div>
            }
            @if (!ro()) {
              <button
                type="button"
                class="wf-btn-secondary"
                (click)="addArg()"
                [disabled]="args().length >= 100 || atMaxDepth()"
              >
                {{ ctx.t('Add another choice', 'إضافة خيار آخر') }}
              </button>
            }
          </div>
        }
        @default {
          @if (slotLabels(); as slots) {
            <div class="mx-children">
              @for (arg of args(); track $index) {
                <app-mapping-transform-editor
                  [node]="arg"
                  [label]="ctx.t(slots[$index][0], slots[$index][1])"
                  [depth]="depth + 1"
                  [itemSchema]="childItemSchema($index)"
                  [idPath]="idPath + '.' + $index"
                  [expected]="expectedArg($index)"
                  (nodeChange)="setArg($index, $event)"
                />
              }
              @if (node.op === 'map' || node.op === 'filter') {
                <p class="mx-help">
                  {{
                    ctx.t(
                      'Inside the second box, use "Each item" to read a field of the list item being processed.',
                      'داخل المربع الثاني استخدم "كل عنصر" لقراءة حقل من العنصر الجاري معالجته.'
                    )
                  }}
                </p>
              }
              @if (node.op === 'divide') {
                <p class="mx-help">
                  {{
                    ctx.t(
                      'Dividing by zero stops with an error; it is never replaced by a default.',
                      'القسمة على صفر تتوقف بخطأ ولا تُستبدل بقيمة افتراضية.'
                    )
                  }}
                </p>
              }
            </div>
          }
        }
      }

      @if (problems().length) {
        <ul class="mx-errors" role="alert">
          @for (p of problems(); track p) {
            <li>{{ p }}</li>
          }
        </ul>
      }
      @if (summaryVisible()) {
        <p class="mx-summary" [attr.aria-label]="ctx.t('Summary', 'ملخص')">{{ summary() }}</p>
      }
    </div>
  `,
})
export class MappingTransformEditorComponent {
  protected readonly ctx = inject(MappingEditContext);
  @Input({ required: true }) node!: TransformNode;
  @Input() label = '';
  @Input() depth = 0;
  /** Schema of the list element when this slot is inside Each item / Keep matching. */
  @Input() itemSchema: MappingSchema | null = null;
  @Input() expected: MappingSchema | null = null;
  /** Unique id of this slot within the editor; also names its drop target. */
  @Input() idPath = 'slot';
  @Input() showSummary = false;
  @Output() nodeChange = new EventEmitter<TransformNode>();

  protected readonly groups = OP_GROUPS;
  protected readonly textSchema: MappingSchema = { type: 'string' };
  /** "I chose a source kind but have not picked the source yet." */
  protected readonly kindDraft = signal<TransformOp | null>(null);
  private readonly nodeSignal = signal<TransformNode | null>(null);

  private destroyed = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.ctx.releaseTarget(this);
    });
  }

  ro = () => this.ctx.readonly();
  protected readonly target = computed<MappingTarget>(() => ({
    id: this.idPath,
    label: this.label || this.idPath,
    expected: this.expected,
    apply: (node) => {
      this.kindDraft.set(null);
      this.emit(node);
    },
    occupied: () => !isReplaceable(this.node),
    owner: this,
    alive: () => !this.destroyed,
  }));

  protected atMaxDepth() {
    return this.depth >= MAX_TRANSFORM_DEPTH - 1;
  }
  protected canUse(op: TransformOp) {
    if (op === 'source' || op === 'literal') return true;
    return !this.atMaxDepth();
  }
  protected opText(op: TransformOp) {
    return this.ctx.t(opLabel(op), AR[op] ?? opLabel(op));
  }
  protected summaryVisible() {
    return this.showSummary || (this.depth === 0 && !['source', 'literal'].includes(this.node.op));
  }
  protected summary() {
    return summarize(this.node, this.ctx.labelOf);
  }
  protected hint() {
    const schema = typeHint(this.node, this.ctx.schemaFor, this.itemSchema);
    return schema ? describeSchema(schema, (en) => this.ctx.t(en, AR_TYPES[en] ?? en)) : '';
  }
  protected compatible() {
    const actual = typeHint(this.node, this.ctx.schemaFor, this.itemSchema);
    return looksCompatible(this.expected, actual);
  }
  protected problems(): string[] {
    const n = this.node;
    const out: string[] = [];
    const [min, max] = arityOf(n.op);
    const count = n.args?.length ?? 0;
    if (count < min || count > max)
      out.push(
        this.ctx.t(
          `"${opLabel(n.op)}" needs ${min === max ? min : `${min}–${max}`} value(s).`,
          `"${this.opText(n.op)}" يحتاج إلى قيم.`,
        ),
      );
    if (n.op === 'item' && !this.itemSchema && this.depth >= 0 && !this.insideItemHint())
      out.push(
        this.ctx.t(
          '"Each item" only works inside a list step.',
          '"كل عنصر" يعمل داخل خطوة قائمة فقط.',
        ),
      );
    if (n.op === 'object')
      for (const name of Object.keys(n.properties ?? {}))
        if (!this.validName(name))
          out.push(
            this.ctx.t(`"${name}" is not a valid field name.`, `"${name}" ليس اسم حقل صالحاً.`),
          );
    if (n.op === 'literal' && this.literalKind() === 'json' && this.jsonError())
      out.push(this.jsonError());
    return out;
  }
  private insideItemHint() {
    return !!this.itemSchema;
  }
  protected pointerValid(pointer: string | null | undefined) {
    return isValidPointer(pointer ?? '');
  }
  protected validName(name: string) {
    return isValidLiteralName(name);
  }

  // ── source node ────────────────────────────────────────────────────────
  protected current(): SourceDescriptor | undefined {
    return this.node.source ? this.ctx.descriptorFor(this.node.source) : undefined;
  }
  protected crumbs = breadcrumbText;
  protected key = (d: SourceDescriptor) => sourceKey(d.reference);
  protected describe = (schema: MappingSchema) =>
    describeSchema(schema, (en) => this.ctx.t(en, AR_TYPES[en] ?? en));
  protected avail(d: SourceDescriptor) {
    return availabilityInfo(d.availability, (en) => this.ctx.t(en, AR_AVAIL[en] ?? en));
  }
  protected pathOptions() {
    const d = this.current();
    return d ? schemaPaths(d.schema) : [];
  }
  protected pickFromTray(key: string) {
    const d = this.ctx.liveTray().find((x) => sourceKey(x.reference) === key);
    if (!d) return;
    this.kindDraft.set(null);
    this.emit(sourceNode(d.reference));
  }
  protected setPointer(pointer: string) {
    if (this.node.source && isValidPointer(pointer))
      this.emit({ ...this.node, source: { ...this.node.source, jsonPointer: pointer } });
  }
  protected setMissingMode(mode: string) {
    const next: TransformNode = { ...this.node };
    if (mode === 'Attention') delete next.missing;
    else if (mode === 'Wait')
      next.missing = { mode: 'Wait', deadlineSeconds: 3600, timeoutOutcome: 'timeout' };
    else next.missing = { mode: 'Fallback', fallback: this.fallbackDefault() };
    this.emit(next);
  }
  private fallbackDefault(): unknown {
    const schema = this.node.source ? this.ctx.schemaAtSource(this.node.source) : null;
    return schema?.type === 'boolean'
      ? false
      : schema?.type === 'number' || schema?.type === 'integer'
        ? 0
        : '';
  }
  protected patchMissing(patch: Partial<MissingDataPolicy>) {
    this.emit({ ...this.node, missing: { ...(this.node.missing as MissingDataPolicy), ...patch } });
  }
  protected fallbackKind(): 'string' | 'number' | 'boolean' | 'null' {
    const v = this.node.missing?.fallback;
    return v === null
      ? 'null'
      : typeof v === 'number'
        ? 'number'
        : typeof v === 'boolean'
          ? 'boolean'
          : 'string';
  }
  protected setFallbackKind(kind: string) {
    this.patchMissing({
      fallback: kind === 'null' ? null : kind === 'number' ? 0 : kind === 'boolean' ? false : '',
    });
  }

  // ── literal node ──────────────────────────────────────────────────────
  protected literalKind(): 'string' | 'number' | 'integer' | 'boolean' | 'null' | 'json' {
    const v = this.node.value;
    if (v === null) return 'null';
    if (typeof v === 'boolean') return 'boolean';
    if (typeof v === 'number')
      return Number.isInteger(v) && this.node.literalSchema?.type !== 'number'
        ? 'integer'
        : 'number';
    if (v !== null && typeof v === 'object') return 'json';
    return this.jsonDraft() ? 'json' : 'string';
  }
  private readonly jsonDraft = signal(false);
  protected readonly jsonError = signal('');
  protected jsonText() {
    return this.jsonRaw() ?? JSON.stringify(this.node.value ?? null, null, 2);
  }
  private readonly jsonRaw = signal<string | null>(null);
  protected setLiteralKind(kind: string) {
    this.jsonDraft.set(kind === 'json');
    this.jsonError.set('');
    this.jsonRaw.set(null);
    const next =
      kind === 'number' || kind === 'integer'
        ? 0
        : kind === 'boolean'
          ? false
          : kind === 'null'
            ? null
            : kind === 'json'
              ? []
              : '';
    this.emit(
      literalNode(next, kind === 'json' ? { type: 'array', items: { type: 'string' } } : undefined),
    );
  }
  protected setLiteral(value: unknown) {
    this.emit(literalNode(value));
  }
  /** Expert escape hatch: a structured constant. Arrays/objects carry the schema the destination expects. */
  protected setJson(text: string) {
    this.jsonRaw.set(text);
    try {
      const parsed = JSON.parse(text) as unknown;
      this.jsonError.set('');
      const schema =
        this.expected &&
        ((Array.isArray(parsed) && this.expected.type === 'array') ||
          (parsed &&
            typeof parsed === 'object' &&
            !Array.isArray(parsed) &&
            this.expected.type === 'object'))
          ? this.expected
          : Array.isArray(parsed)
            ? ({ type: 'array', items: { type: 'string' } } as MappingSchema)
            : undefined;
      this.emit(literalNode(parsed, schema));
    } catch {
      this.jsonError.set(this.ctx.t('That is not valid JSON.', 'هذا ليس JSON صالحاً.'));
    }
  }

  // ── item node ─────────────────────────────────────────────────────────
  protected itemPaths() {
    return schemaPaths(this.itemSchema);
  }
  protected setItemPointer(pointer: string) {
    if (isValidPointer(pointer)) {
      this.itemPointerDraft.set(null);
      this.emit(itemNode(pointer));
    }
  }
  /** The text being typed that is not a valid pointer yet; kept visible so it is corrected, never silently dropped. */
  protected readonly itemPointerDraft = signal<string | null>(null);
  protected itemPointerText() {
    return this.itemPointerDraft() ?? this.node.jsonPointer ?? '';
  }
  protected setItemPointerText(text: string) {
    // Exact: a dictionary key may legitimately end in a space (`/score `), so the text is never trimmed.
    if (isValidPointer(text)) this.setItemPointer(text);
    else this.itemPointerDraft.set(text);
  }
  /** Whether the saved pointer is one the picker lists (otherwise it is a custom path and stays selectable). */
  protected itemPathListed() {
    const pointer = this.node.jsonPointer ?? '';
    return this.itemPaths().some((p) => p.pointer === pointer);
  }

  // ── op change (non-destructive: the current value moves inside the new step) ──
  protected changeOp(op: TransformOp) {
    if (op === this.node.op && this.kindDraft() === null) return;
    if (op === 'source') {
      const first = this.ctx.liveTray()[0];
      if (first) this.emit(sourceNode(first.reference));
      else this.kindDraft.set('source');
      return;
    }
    this.kindDraft.set(null);
    if (op === 'literal') return this.emit(emptyNode());
    if (op === 'item') return this.emit(itemNode(''));
    if (op === 'object')
      return this.emit(this.node.op === 'object' ? this.node : skeleton('object'));
    const next = skeleton(op);
    const keep =
      this.node.op !== 'literal' || (this.node.value !== '' && this.node.value !== undefined);
    if (
      keep &&
      next.args &&
      next.args.length > 0 &&
      !this.node.args?.length &&
      this.node.op !== 'object'
    )
      next.args = [this.node, ...next.args.slice(1)];
    else if (this.node.args?.length && next.args) {
      // Between multi-argument steps keep as many existing values as still fit.
      const [min, max] = arityOf(op);
      const kept = this.node.args.slice(0, max);
      while (kept.length < min) kept.push(emptyNode());
      next.args = kept;
    }
    this.emit(next);
  }

  // ── args / properties ──────────────────────────────────────────────────
  protected args(): TransformNode[] {
    return this.node.args ?? [];
  }
  protected props(): { name: string; node: TransformNode }[] {
    return Object.entries(this.node.properties ?? {}).map(([name, node]) => ({ name, node }));
  }
  protected slotLabels(): [string, string][] | null {
    return SLOT_LABELS[this.node.op] ?? null;
  }
  protected setArg(index: number, child: TransformNode) {
    const args = [...this.args()];
    args[index] = child;
    this.emit({ ...this.node, args });
  }
  protected addArg() {
    this.emit({ ...this.node, args: [...this.args(), emptyNode()] });
  }
  protected removeArg(index: number) {
    this.emit({ ...this.node, args: this.args().filter((_, i) => i !== index) });
  }
  protected wrapArg(index: number, op: TransformOp) {
    this.setArg(index, callNode(op, this.args()[index]));
  }
  protected addTrayAsArgs(asText: boolean) {
    const pieces = this.ctx.liveTray().map((d) => {
      const node = sourceNode(d.reference);
      return asText && d.schema.type !== 'string' ? callNode('text', node) : node;
    });
    const existing = this.args();
    const trailingEmpty =
      existing.length === 1 && existing[0].op === 'literal' && existing[0].value === '';
    this.emit({ ...this.node, args: [...(trailingEmpty ? [] : existing), ...pieces] });
  }
  protected setProp(name: string, child: TransformNode) {
    this.emit({ ...this.node, properties: { ...this.node.properties, [name]: child } });
  }
  protected addProp() {
    const name = propertyName('field', Object.keys(this.node.properties ?? {}));
    this.setProp(name, emptyNode());
  }
  protected removeProp(name: string) {
    const rest = { ...this.node.properties };
    delete rest[name];
    this.emit({ ...this.node, properties: rest });
  }
  protected renameProp(from: string, to: string) {
    const name = to.trim();
    if (!name || name === from || (this.node.properties && name in this.node.properties)) return;
    this.emit({
      ...this.node,
      properties: Object.fromEntries(
        Object.entries(this.node.properties ?? {}).map(([k, v]) => [k === from ? name : k, v]),
      ),
    });
  }
  protected addTrayAsProps() {
    const properties = { ...this.node.properties };
    for (const d of this.ctx.liveTray())
      properties[propertyName(d.label, Object.keys(properties))] = sourceNode(d.reference);
    this.emit({ ...this.node, properties });
  }
  protected needsText(arg: TransformNode) {
    const hint = typeHint(arg, this.ctx.schemaFor, this.itemSchema);
    return !!hint && hint.type !== 'string' && arg.op !== 'text';
  }

  // ── typing hints for children ───────────────────────────────────────────
  protected expectedProperty(name: string): MappingSchema | null {
    return this.expected?.type === 'object'
      ? (this.expected.properties?.[name] ?? this.expected.additionalPropertySchema ?? null)
      : null;
  }
  protected expectedArg(index: number): MappingSchema | null {
    const op = this.node.op;
    if (
      ['add', 'subtract', 'multiply', 'divide', 'sum', 'greater'].includes(op) &&
      (op !== 'sum' || index === 0)
    )
      return { type: 'number' };
    if (op === 'filter' && index === 1) return { type: 'boolean' };
    if (op === 'map' && index === 0) return { type: 'array', items: { type: 'string' } };
    if (op === 'map' && index === 1)
      return this.expected?.type === 'array' ? (this.expected.items ?? null) : null;
    if (op === 'filter' && index === 0) return this.expected;
    return null;
  }
  /** Inside the second slot of map/filter the item is an element of the list in the first slot. */
  protected childItemSchema(index: number): MappingSchema | null {
    if (!['map', 'filter'].includes(this.node.op)) return this.itemSchema;
    if (index === 0) return this.itemSchema;
    const list = this.node.args?.[0];
    const hint = list ? typeHint(list, this.ctx.schemaFor, this.itemSchema) : null;
    return hint?.type === 'array' ? (hint.items ?? null) : null;
  }

  protected emit(node: TransformNode) {
    this.nodeSignal.set(node);
    this.nodeChange.emit(node);
  }
}

const AR: Partial<Record<TransformOp, string>> = {
  literal: 'قيمة ثابتة',
  source: 'قيمة من مصدر',
  item: 'كل عنصر',
  object: 'بناء كائن',
  concat: 'دمج نص',
  add: 'جمع',
  subtract: 'طرح',
  multiply: 'ضرب',
  divide: 'قسمة',
  text: 'تحويل إلى نص',
  number: 'تحويل إلى رقم',
  integer: 'تحويل إلى رقم صحيح',
  boolean: 'تحويل إلى نعم/لا',
  coalesce: 'أول قيمة متوفرة',
  map: 'تحويل كل عنصر',
  filter: 'إبقاء العناصر المطابقة',
  count: 'عدّ العناصر',
  sum: 'مجموع العناصر',
  equal: 'يساوي',
  greater: 'أكبر من',
};
const AR_TYPES: Record<string, string> = {
  text: 'نص',
  number: 'رقم',
  'whole number': 'رقم صحيح',
  'yes/no': 'نعم/لا',
  object: 'كائن',
  'list of': 'قائمة من',
  'or empty': 'أو فارغ',
  'unknown type': 'نوع غير معروف',
  'other fields': 'حقول أخرى',
};
const AR_AVAIL: Record<string, string> = {
  'Available when the event starts': 'متاح عند بدء الحدث',
  'Available once the form is submitted (may need to wait)':
    'متاح بعد إرسال النموذج (قد يتطلب انتظاراً)',
  'Available once that event completes (may need to wait)':
    'متاح بعد اكتمال ذلك الحدث (قد يتطلب انتظاراً)',
  'Needs a declared type before it can be mapped': 'يحتاج إلى نوع معرّف قبل الربط',
};
