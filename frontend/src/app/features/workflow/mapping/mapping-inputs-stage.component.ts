import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  inject,
} from '@angular/core';
import { MappingEditContext } from './mapping-edit-context';
import { MappingTransformEditorComponent } from './mapping-transform-editor.component';
import type { EndpointDefinition, InputBinding, MappingSchema } from './mapping.models';
import { describeSchema, emptyNode, skeleton } from './mapping-config';

export interface InputDestination {
  key: string;
  location: InputBinding['location'];
  name: string;
  label: string;
  schema: MappingSchema;
}

/** Every request parameter and the body the endpoint declares, each one a destination for a mapping. */
export function inputDestinations(endpoint: EndpointDefinition | null): InputDestination[] {
  if (!endpoint) return [];
  const result: InputDestination[] = endpoint.parameters.map((p) => ({
    key: `${p.location}:${p.name}`,
    location: p.location,
    name: p.name,
    label: `${p.location} “${p.name}”`,
    schema: p.schema,
  }));
  if (endpoint.body)
    result.push({
      key: 'body:$body',
      location: 'body',
      name: '$body',
      label: 'body',
      schema: endpoint.body,
    });
  return result;
}

const sameDestination = (b: InputBinding, d: { location: string; name: string }) =>
  b.location === d.location && b.name === d.name;

/**
 * Saved input bindings whose parameter or body the endpoint no longer declares (renamed, deleted, or replaced by an
 * OpenAPI import). The server rejects them ("Unknown mapping destination"), so they must stay visible until the
 * author moves or removes them on purpose; nothing here ever deletes an authored expression.
 */
export function orphanBindings(
  endpoint: EndpointDefinition | null,
  inputs: InputBinding[],
): InputBinding[] {
  const destinations = inputDestinations(endpoint);
  return inputs.filter((b) => !destinations.some((d) => sameDestination(b, d)));
}

/** Inputs: what the request sends. One typed expression per declared parameter and for the body. */
@Component({
  selector: 'app-mapping-inputs-stage',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MappingTransformEditorComponent],
  styleUrl: './mapping.css',
  template: `
    @if (!destinations().length) {
      <p class="mx-help">
        {{
          ctx.t(
            'Declare parameters or a body in the Endpoint stage first; each becomes an input to map here.',
            'عرّف المعاملات أو المحتوى في مرحلة نقطة النهاية أولاً؛ يصبح كل منها مدخلاً للربط هنا.'
          )
        }}
      </p>
    }
    @for (d of destinations(); track d.key) {
      <section
        class="mx-slot"
        [attr.data-input]="d.key"
        [attr.aria-label]="ctx.t('Input ', 'المدخل ') + d.label"
      >
        <div class="mx-slot-head">
          <strong
            >{{ locationText(d) }} ·
            {{ d.name === '$body' ? ctx.t('request body', 'محتوى الطلب') : d.name }}</strong
          >
          <span class="mx-chip">{{ describe(d.schema) }}</span>
          @if (d.schema.required) {
            <span class="mx-chip mx-chip-warn">{{ ctx.t('Required', 'مطلوب') }}</span>
          }
        </div>
        @if (binding(d); as b) {
          <app-mapping-transform-editor
            [node]="b.value"
            [label]="d.label"
            [expected]="d.schema"
            [idPath]="'input:' + d.key"
            [showSummary]="true"
            (nodeChange)="setValue(d, $event)"
          />
          @if (!ctx.readonly() && !d.schema.required) {
            <button type="button" class="mx-link mx-danger" (click)="unmap(d)">
              {{ ctx.t('Stop sending this input', 'إيقاف إرسال هذا المدخل') }}
            </button>
          }
        } @else {
          <p class="mx-help" [class.mx-error]="d.schema.required">
            {{
              d.schema.required
                ? ctx.t('Required and not mapped yet.', 'مطلوب ولم يتم ربطه بعد.')
                : ctx.t('Not sent.', 'غير مُرسل.')
            }}
          </p>
          @if (!ctx.readonly()) {
            <button type="button" class="wf-btn-secondary" (click)="map(d)">
              {{ ctx.t('Map this input', 'ربط هذا المدخل') }}
            </button>
          }
        }
      </section>
    }
    @if (orphans().length) {
      <section class="mx-slot" role="alert" data-testid="orphan-inputs">
        <h4 class="mx-slot-label">
          {{
            ctx.t('Inputs the endpoint no longer declares', 'مدخلات لم تعد نقطة النهاية تعرّفها')
          }}
        </h4>
        <p class="mx-help">
          {{
            ctx.t(
              'These inputs were mapped for a parameter or body that was renamed, removed or replaced. Nothing was deleted, but the server rejects them. Move each one to a current input, or remove it on purpose.',
              'تم ربط هذه المدخلات لمعامل أو محتوى أُعيدت تسميته أو أُزيل أو استُبدل. لم يُحذف شيء، لكن الخادم يرفضها. انقل كلاً منها إلى مدخل حالي أو أزلها عن قصد.'
            )
          }}
        </p>
        @for (b of orphans(); track orphanKey(b)) {
          <div
            class="mx-slot"
            [attr.data-orphan]="orphanKey(b)"
            [attr.aria-label]="ctx.t('Unmatched input ', 'مدخل غير مطابق ') + orphanLabel(b)"
          >
            <div class="mx-slot-head">
              <strong>{{ orphanLabel(b) }}</strong>
              <span class="mx-chip mx-chip-warn">{{
                ctx.t('Not in the endpoint', 'ليس في نقطة النهاية')
              }}</span>
            </div>
            <app-mapping-transform-editor
              [node]="b.value"
              [label]="orphanLabel(b)"
              [idPath]="'orphan:' + orphanKey(b)"
              [showSummary]="true"
              (nodeChange)="setOrphanValue(b, $event)"
            />
            @if (!ctx.readonly()) {
              <div class="mx-row">
                @if (free().length) {
                  <label class="mx-field">
                    {{ ctx.t('Move to', 'انقل إلى') }}
                    <select
                      class="wf-input"
                      data-testid="orphan-remap"
                      (change)="remap(b, $any($event.target).value)"
                    >
                      <option value="">{{ ctx.t('Choose an input…', 'اختر مدخلاً…') }}</option>
                      @for (d of free(); track d.key) {
                        <option [value]="d.key">{{ d.label }}</option>
                      }
                    </select>
                  </label>
                }
                <button type="button" class="mx-link mx-danger" (click)="removeOrphan(b)">
                  {{ ctx.t('Remove this input', 'إزالة هذا المدخل') }}
                </button>
              </div>
            }
          </div>
        }
      </section>
    }
    @for (message of serverProblems; track message) {
      <p class="mx-error" role="alert">{{ message }}</p>
    }
  `,
})
export class MappingInputsStageComponent {
  protected readonly ctx = inject(MappingEditContext);
  @Input({ required: true }) endpoint: EndpointDefinition | null = null;
  @Input({ required: true }) inputs: InputBinding[] = [];
  @Input() serverProblems: string[] = [];
  @Output() inputsChange = new EventEmitter<InputBinding[]>();

  protected destinations() {
    return inputDestinations(this.endpoint);
  }
  protected describe = (schema: MappingSchema) =>
    describeSchema(schema, (en) => this.ctx.t(en, en));
  protected locationText(d: InputDestination) {
    return d.location === 'path'
      ? this.ctx.t('Path', 'المسار')
      : d.location === 'query'
        ? this.ctx.t('Query', 'الاستعلام')
        : d.location === 'header'
          ? this.ctx.t('Header', 'الترويسة')
          : this.ctx.t('Body', 'المحتوى');
  }
  protected orphans() {
    return orphanBindings(this.endpoint, this.inputs);
  }
  /** Current inputs that have no mapping yet, which an unmatched binding can be moved to. */
  protected free() {
    return this.destinations().filter((d) => !this.binding(d));
  }
  protected orphanKey(b: InputBinding) {
    return `${b.location}:${b.name}`;
  }
  protected orphanLabel(b: InputBinding) {
    return b.name === '$body'
      ? this.ctx.t('body', 'المحتوى')
      : `${this.locationText({ location: b.location } as InputDestination)} “${b.name}”`;
  }
  protected setOrphanValue(orphan: InputBinding, value: InputBinding['value']) {
    this.inputsChange.emit(
      this.inputs.map((b) => (this.orphanKey(b) === this.orphanKey(orphan) ? { ...b, value } : b)),
    );
  }
  protected removeOrphan(orphan: InputBinding) {
    this.inputsChange.emit(this.inputs.filter((b) => this.orphanKey(b) !== this.orphanKey(orphan)));
  }
  /** Moves the authored expression, unchanged, to a current input. */
  protected remap(orphan: InputBinding, key: string) {
    const target = this.free().find((d) => d.key === key);
    if (!target) return;
    this.inputsChange.emit(
      this.inputs.map((b) =>
        this.orphanKey(b) === this.orphanKey(orphan)
          ? { ...b, location: target.location, name: target.name }
          : b,
      ),
    );
    this.ctx.announce(
      this.ctx.t(`Moved the input to ${target.label}.`, `تم نقل المدخل إلى ${target.label}.`),
    );
  }
  protected binding(d: InputDestination) {
    return this.inputs.find((b) => b.location === d.location && b.name === d.name);
  }
  protected map(d: InputDestination) {
    const value =
      d.schema.type === 'object' && d.location === 'body' ? skeleton('object') : emptyNode();
    this.inputsChange.emit([...this.inputs, { location: d.location, name: d.name, value }]);
  }
  protected unmap(d: InputDestination) {
    this.inputsChange.emit(
      this.inputs.filter((b) => !(b.location === d.location && b.name === d.name)),
    );
  }
  protected setValue(d: InputDestination, value: InputBinding['value']) {
    this.inputsChange.emit(
      this.inputs.map((b) =>
        b.location === d.location && b.name === d.name ? { ...b, value } : b,
      ),
    );
  }
}
