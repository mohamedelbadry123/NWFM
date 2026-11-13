import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MappingEditContext } from './mapping-edit-context';
import { MappingSchemaEditorComponent } from './mapping-schema-editor.component';
import { WorkflowMappingService } from './workflow-mapping.service';
import type {
  EndpointDefinition,
  EndpointParameter,
  MappingSchema,
  ParameterLocation,
} from './mapping.models';
import { HTTP_METHODS, emptyEndpoint, newSchema } from './mapping-config';

/** Path placeholders like `/orders/{orderId}` → `orderId`. */
export const pathPlaceholders = (path: string) =>
  [...path.matchAll(/\{([^{}/]+)\}/g)].map((m) => m[1]);

/** Problems visible without asking the server; the server's own checks (Check endpoint) remain authoritative. */
export function endpointProblems(endpoint: EndpointDefinition | null): string[] {
  if (!endpoint) return [];
  const problems: string[] = [];
  if (
    !endpoint.path.startsWith('/') ||
    endpoint.path.startsWith('//') ||
    /^[a-z][a-z0-9+.-]*:/i.test(endpoint.path)
  )
    problems.push('The path must be relative to the connection and start with a single "/".');
  const names = new Set<string>();
  for (const p of endpoint.parameters) {
    if (!p.name.trim()) problems.push('Every parameter needs a name.');
    const key = `${p.location}:${p.location === 'header' ? p.name.toLowerCase() : p.name}`;
    if (names.has(key))
      problems.push(`"${p.name}" is declared twice as a ${p.location} parameter.`);
    names.add(key);
  }
  for (const name of pathPlaceholders(endpoint.path))
    if (!endpoint.parameters.some((p) => p.location === 'path' && p.name === name))
      problems.push(`The path uses {${name}} but no path parameter "${name}" is declared.`);
  for (const p of endpoint.parameters)
    if (p.location === 'path' && !pathPlaceholders(endpoint.path).includes(p.name))
      problems.push(`Path parameter "${p.name}" does not appear in the path.`);
  if (endpoint.body && ['GET', 'HEAD'].includes(endpoint.method))
    problems.push(`${endpoint.method} requests cannot send a body.`);
  return problems;
}

/**
 * Endpoint: connection-relative method and path, typed path/query/header parameters, request body, expected
 * response. Structure comes from an OpenAPI import (the server parses it) or is authored here by hand. A response
 * suggested from a sample is labelled inferred, and Send Test never claims to prove the request contract.
 */
@Component({
  selector: 'app-mapping-endpoint-stage',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MappingSchemaEditorComponent],
  styleUrl: './mapping.css',
  template: `
    <fieldset [disabled]="ctx.readonly()" class="space-y-3">
      <div class="mx-row">
        <label class="mx-field">
          {{ ctx.t('Method', 'الطريقة') }}
          <select
            class="wf-input"
            [ngModel]="value.method"
            (ngModelChange)="patch({ method: $event })"
          >
            @for (m of methods; track m) {
              <option>{{ m }}</option>
            }
          </select>
        </label>
        <label class="mx-field mx-grow">
          {{ ctx.t('Path on the connection host', 'المسار على خادم الاتصال') }}
          <input
            class="wf-input"
            [ngModel]="value.path"
            (ngModelChange)="patch({ path: $event })"
            placeholder="/orders/{orderId}"
            [attr.aria-invalid]="pathInvalid()"
          />
        </label>
        <button type="button" class="wf-btn-secondary" (click)="detectPathParameters()">
          {{ ctx.t('Add path parameters from the path', 'أضف معاملات المسار من المسار') }}
        </button>
      </div>
      <p class="mx-help">
        {{
          ctx.t(
            'The connection supplies the host and credentials. The path must be relative.',
            'يوفر الاتصال الخادم وبيانات الاعتماد. يجب أن يكون المسار نسبياً.'
          )
        }}
      </p>

      <details>
        <summary>
          {{
            ctx.t('Import structure from an OpenAPI document', 'استيراد الهيكل من مستند OpenAPI')
          }}
        </summary>
        <div class="mx-slot">
          <p class="mx-help">
            {{
              ctx.t(
                'OpenAPI 3.0 JSON only. Pick the operation to import; this replaces the parameters, body and response below.',
                'OpenAPI 3.0 بصيغة JSON فقط. اختر العملية المراد استيرادها؛ يستبدل هذا المعاملات والمحتوى والاستجابة أدناه.'
              )
            }}
          </p>
          <label class="mx-field">
            {{ ctx.t('Document file (.json)', 'ملف المستند (.json)') }}
            <input type="file" accept=".json,application/json" (change)="readFile($event)" />
          </label>
          <label class="mx-field">
            {{ ctx.t('…or paste the document', '…أو الصق المستند') }}
            <textarea
              class="wf-input mx-code"
              rows="5"
              spellcheck="false"
              [ngModel]="document()"
              (ngModelChange)="document.set($event)"
            ></textarea>
          </label>
          <div class="mx-row">
            <label class="mx-field">
              {{ ctx.t('Operation path', 'مسار العملية') }}
              <input
                class="wf-input"
                [ngModel]="importPath()"
                (ngModelChange)="importPath.set($event)"
                placeholder="/orders/{orderId}"
              />
            </label>
            <label class="mx-field">
              {{ ctx.t('Operation method', 'طريقة العملية') }}
              <select
                class="wf-input"
                [ngModel]="importMethod()"
                (ngModelChange)="importMethod.set($event)"
              >
                @for (m of methods; track m) {
                  <option>{{ m }}</option>
                }
              </select>
            </label>
            <button
              type="button"
              class="wf-btn-primary"
              [disabled]="busy() || !document().trim() || !importPath().trim()"
              (click)="importOpenApi()"
            >
              {{ ctx.t('Import operation', 'استيراد العملية') }}
            </button>
          </div>
        </div>
      </details>

      <h4 class="mx-slot-label">{{ ctx.t('Request parameters', 'معاملات الطلب') }}</h4>
      @for (p of value.parameters; track $index) {
        <div class="mx-slot" [attr.data-parameter]="p.location + ':' + p.name">
          <div class="mx-row">
            <label class="mx-field">
              {{ ctx.t('Sent in', 'يُرسل في') }}
              <select
                class="wf-input"
                [ngModel]="p.location"
                (ngModelChange)="patchParameter($index, { location: $event })"
              >
                <option value="path">{{ ctx.t('Path', 'المسار') }}</option>
                <option value="query">{{ ctx.t('Query', 'الاستعلام') }}</option>
                <option value="header">{{ ctx.t('Header', 'الترويسة') }}</option>
              </select>
            </label>
            <label class="mx-field mx-grow">
              {{ ctx.t('Name', 'الاسم') }}
              <input
                class="wf-input"
                [ngModel]="p.name"
                (ngModelChange)="patchParameter($index, { name: $event })"
              />
            </label>
            <button type="button" class="mx-link mx-danger" (click)="removeParameter($index)">
              {{ ctx.t('Remove', 'إزالة') }}
            </button>
          </div>
          <app-mapping-schema-editor
            [schema]="p.schema"
            [showRequired]="true"
            [forceRequired]="p.location === 'path'"
            [scalarOnly]="true"
            [idPath]="'param.' + $index"
            (schemaChange)="setParameterSchema($index, $event)"
          />
        </div>
      }
      <div class="mx-row">
        <button type="button" class="wf-btn-secondary" (click)="addParameter('query')">
          {{ ctx.t('Add query parameter', 'إضافة معامل استعلام') }}
        </button>
        <button type="button" class="wf-btn-secondary" (click)="addParameter('header')">
          {{ ctx.t('Add header', 'إضافة ترويسة') }}
        </button>
        <button type="button" class="wf-btn-secondary" (click)="addParameter('path')">
          {{ ctx.t('Add path parameter', 'إضافة معامل مسار') }}
        </button>
      </div>
      <p class="mx-help">
        {{
          ctx.t(
            'Path, query and header values are single non-empty values (text, number, whole number or yes/no). Authorization headers belong to the connection.',
            'قيم المسار والاستعلام والترويسة قيم مفردة غير فارغة. ترويسات التفويض تتبع الاتصال.'
          )
        }}
      </p>

      <label class="mx-field">
        <span>{{ ctx.t('This request sends a JSON body', 'يرسل هذا الطلب محتوى JSON') }}</span>
        <input
          type="checkbox"
          [ngModel]="!!value.body"
          (ngModelChange)="toggleBody($event)"
          [disabled]="['GET', 'HEAD'].includes(value.method)"
        />
      </label>
      @if (value.body) {
        <app-mapping-schema-editor
          [schema]="value.body"
          [label]="ctx.t('Body type', 'نوع المحتوى')"
          [idPath]="'body'"
          (schemaChange)="patch({ body: $event })"
        />
      }

      <h4 class="mx-slot-label">
        {{ ctx.t('Expected response body', 'محتوى الاستجابة المتوقع') }}
      </h4>
      <label class="mx-field">
        <span>{{
          ctx.t(
            'Describe the response so its values can be used by outputs',
            'صِف الاستجابة ليمكن استخدام قيمها في المخرجات'
          )
        }}</span>
        <input
          type="checkbox"
          [ngModel]="!!value.response"
          (ngModelChange)="toggleResponse($event)"
        />
      </label>
      @if (value.response) {
        <app-mapping-schema-editor
          [schema]="value.response"
          [label]="ctx.t('Response type', 'نوع الاستجابة')"
          [idPath]="'response'"
          (schemaChange)="patch({ response: $event })"
        />
      }
      <details>
        <summary>
          {{ ctx.t('Suggest the response from a sample', 'اقتراح الاستجابة من عينة') }}
        </summary>
        <div class="mx-slot">
          <p class="mx-help">
            {{
              ctx.t(
                'A suggestion from one example. It is marked as inferred, cannot tell which fields are required, and says nothing about the request.',
                'اقتراح من مثال واحد. يُعلَّم كمستنتج ولا يحدد الحقول المطلوبة ولا يخبر شيئاً عن الطلب.'
              )
            }}
          </p>
          <label class="mx-field">
            {{ ctx.t('Sample response body (JSON)', 'نموذج محتوى الاستجابة (JSON)') }}
            <textarea
              class="wf-input mx-code"
              rows="5"
              spellcheck="false"
              [ngModel]="sample()"
              (ngModelChange)="sample.set($event)"
            ></textarea>
          </label>
          <button
            type="button"
            class="wf-btn-secondary"
            [disabled]="busy() || !sample().trim()"
            (click)="infer()"
          >
            {{ ctx.t('Suggest response structure', 'اقتراح هيكل الاستجابة') }}
          </button>
        </div>
      </details>
    </fieldset>

    @if (localProblems().length || error()) {
      <ul class="mx-errors" role="alert" data-testid="endpoint-problems">
        @for (p of localProblems(); track p) {
          <li>{{ p }}</li>
        }
        @if (error()) {
          <li>{{ error() }}</li>
        }
      </ul>
    }
    <div class="mx-row">
      <button type="button" class="wf-btn-secondary" [disabled]="busy()" (click)="check()">
        {{ ctx.t('Check endpoint with the server', 'فحص نقطة النهاية مع الخادم') }}
      </button>
      @if (checked()) {
        <span class="mx-chip" role="status">{{
          ctx.t('The server accepted this endpoint.', 'قبل الخادم نقطة النهاية هذه.')
        }}</span>
      }
    </div>
  `,
})
export class MappingEndpointStageComponent {
  protected readonly ctx = inject(MappingEditContext);
  private readonly api = inject(WorkflowMappingService);
  @Input({ required: true }) value: EndpointDefinition = emptyEndpoint();
  /** Problems the server reported for this stage (from validate), shown inline. */
  @Input() serverProblems: string[] = [];
  @Output() valueChange = new EventEmitter<EndpointDefinition>();
  protected readonly methods = HTTP_METHODS;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly checked = signal(false);
  protected readonly document = signal('');
  protected readonly importPath = signal('');
  protected readonly importMethod = signal('GET');
  protected readonly sample = signal('');

  protected localProblems() {
    return [...new Set([...endpointProblems(this.value), ...this.serverProblems])];
  }
  protected pathInvalid() {
    return endpointProblems(this.value).some((p) => p.startsWith('The path'));
  }
  private emit(next: EndpointDefinition) {
    this.checked.set(false);
    this.valueChange.emit(next);
  }
  protected patch(change: Partial<EndpointDefinition>) {
    const next = { ...this.value, ...change };
    if (change.method && ['GET', 'HEAD'].includes(change.method)) next.body = null;
    this.emit(next);
  }
  protected addParameter(location: ParameterLocation) {
    const base = location === 'path' ? 'id' : location === 'query' ? 'filter' : 'X-Custom';
    let name = base;
    for (
      let n = 2;
      this.value.parameters.some((p) => p.location === location && p.name === name);
      n++
    )
      name = `${base}${n}`;
    const parameter: EndpointParameter = {
      location,
      name,
      schema: { ...newSchema('string'), required: location === 'path' },
    };
    this.patch({ parameters: [...this.value.parameters, parameter] });
  }
  protected patchParameter(index: number, change: Partial<EndpointParameter>) {
    this.patch({
      parameters: this.value.parameters.map((p, i) => {
        if (i !== index) return p;
        const next = { ...p, ...change };
        if (next.location === 'path') next.schema = { ...next.schema, required: true };
        return next;
      }),
    });
  }
  protected setParameterSchema(index: number, schema: MappingSchema) {
    this.patchParameter(index, { schema });
  }
  protected removeParameter(index: number) {
    this.patch({ parameters: this.value.parameters.filter((_, i) => i !== index) });
  }
  protected detectPathParameters() {
    const missing = pathPlaceholders(this.value.path).filter(
      (n) => !this.value.parameters.some((p) => p.location === 'path' && p.name === n),
    );
    if (!missing.length) return;
    this.patch({
      parameters: [
        ...this.value.parameters,
        ...missing.map((name) => ({
          location: 'path' as const,
          name,
          schema: { ...newSchema('string'), required: true },
        })),
      ],
    });
  }
  protected toggleBody(on: boolean) {
    this.patch({ body: on ? newSchema('object') : null });
  }
  protected toggleResponse(on: boolean) {
    this.patch({ response: on ? newSchema('object') : null });
  }

  protected async readFile(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    if (file.size > 512 * 1024) {
      this.error.set(
        this.ctx.t('The document is larger than 512 KB.', 'المستند أكبر من 512 كيلوبايت.'),
      );
      return;
    }
    this.document.set(await file.text());
  }

  protected importOpenApi() {
    const root = this.ctx.root();
    if (!root) return;
    this.busy.set(true);
    this.error.set('');
    this.api
      .importOpenApi(root.definitionId, root.versionId, {
        document: this.document(),
        path: this.importPath().trim(),
        method: this.importMethod(),
      })
      .subscribe({
        next: (endpoint) => {
          this.busy.set(false);
          this.emit(endpoint);
          this.ctx.announce(this.ctx.t('OpenAPI operation imported.', 'تم استيراد عملية OpenAPI.'));
        },
        error: (e) => this.fail(e),
      });
  }
  protected infer() {
    const root = this.ctx.root();
    if (!root) return;
    let parsed: unknown;
    try {
      parsed = JSON.parse(this.sample());
    } catch {
      this.error.set(this.ctx.t('The sample is not valid JSON.', 'العينة ليست JSON صالحاً.'));
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.api.inferResponse(root.definitionId, root.versionId, parsed).subscribe({
      next: (schema) => {
        this.busy.set(false);
        this.patch({ response: schema });
      },
      error: (e) => this.fail(e),
    });
  }
  protected check() {
    const root = this.ctx.root();
    if (!root) return;
    this.busy.set(true);
    this.error.set('');
    this.api.validateSchema(root.definitionId, root.versionId, this.value).subscribe({
      next: () => {
        this.busy.set(false);
        this.checked.set(true);
      },
      error: (e) => this.fail(e),
    });
  }
  private fail(e: unknown) {
    this.busy.set(false);
    this.error.set(
      (e as { error?: { message?: string } })?.error?.message ??
        this.ctx.t('The server could not process the request.', 'تعذر على الخادم معالجة الطلب.'),
    );
  }
}
