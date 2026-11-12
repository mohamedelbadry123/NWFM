import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';
import {
  WorkflowWorkspaceService,
  ReferenceItem,
  WorkspaceWorkflow,
  WorkspaceSettings,
} from './workflow-workspace.service';
import { matchingChild } from './workflow-child-match';

interface BusinessEvent {
  id: string;
  name: string;
  trigger: string;
  kind: 'Http' | 'Soap' | 'Sms' | 'Email';
  required: boolean;
  configuration: Record<string, unknown>;
}
interface BusinessConfiguration {
  [key: string]: unknown;
  taskTypeId?: string;
  departmentCode?: string;
  fieldActivityCode?: string;
  definitionKey?: string;
  versionId?: string;
  rejectTargetNodeKey?: string;
  events?: BusinessEvent[];
}
interface DataMapping {
  target: string;
  source: string;
}

@Component({
  selector: 'app-workflow-business-activity',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <fieldset [disabled]="readonly && !hierarchical()" class="space-y-4 border rounded-lg p-3 my-3">
      <legend class="px-1 font-medium">{{ t('Business activity', 'النشاط') }}</legend>
      <label class="block text-sm"
        >{{ t('Task Type', 'نوع المهمة')
        }}<select
          id="wf-activity-task-type"
          class="wf-input"
          [ngModel]="config.taskTypeId || ''"
          [ngModelOptions]="standalone"
          [disabled]="readonly || (hierarchical() && !main)"
          (ngModelChange)="taskType($event)"
        >
          <option value="">{{ t('Select Task Type', 'اختر نوع المهمة') }}</option>
          @if (config.taskTypeId && !taskTypeAvailable()) {
            <option [value]="config.taskTypeId" disabled>
              {{ t('Saved Task Type is unavailable', 'نوع المهمة المحفوظ غير متاح') }}
            </option>
          }
          @for (item of taskTypes(); track item.id) {
            <option [value]="item.id">{{ label(item) }}</option>
          }
        </select></label
      >
      @if (typeError()) {
        <p role="alert">{{ t('Could not load Task Types.', 'تعذر تحميل أنواع المهام.') }}</p>
        <button type="button" (click)="loadTaskTypes()">{{ t('Retry', 'إعادة المحاولة') }}</button>
      }
      @if (error()) {
        <p role="alert">{{ error() }}</p>
      }
      @if (main && hierarchical()) {
        <p class="text-sm">
          {{
            t(
              'Build the steps for this Task Type inside its child workflow.',
              'صمم خطوات نوع المهمة داخل سير العمل الفرعي.'
            )
          }}
        </p>
        <button
          type="button"
          class="wf-btn-primary"
          [disabled]="!config.taskTypeId || childBusy"
          (click)="openChild.emit()"
        >
          {{
            childBusy
              ? t('Opening…', 'جارٍ الفتح…')
              : config.versionId
                ? t('Open child workflow', 'فتح سير العمل الفرعي')
                : t('Create child workflow', 'إنشاء سير عمل فرعي')
          }}
        </button>
      } @else if (main) {
        <label class="block text-sm"
          >{{ t('Child workflow', 'سير العمل الفرعي')
          }}<select
            id="wf-activity-child"
            class="wf-input"
            [ngModel]="config.versionId || ''"
            [ngModelOptions]="standalone"
            (ngModelChange)="child($event)"
          >
            <option value="">{{ t('Select published child', 'اختر سير عمل فرعي منشور') }}</option>
            @if (config.versionId && !selectedChildMatches()) {
              <option [value]="config.versionId" disabled>
                {{ t('Saved child requires review', 'سير العمل الفرعي المحفوظ يحتاج مراجعة') }}
              </option>
            }
            @for (item of eligibleChildren(); track item.versionId) {
              <option [value]="item.versionId">
                {{ localeName(item) }} · v{{ item.versionNumber }}
              </option>
            }
          </select></label
        >
        @if (childrenLoading()) {
          <p role="status">{{ t('Loading child workflows…', 'جارٍ تحميل سير العمل الفرعي…') }}</p>
        } @else if (childrenError()) {
          <p role="alert">
            {{ t('Could not load child workflows.', 'تعذر تحميل سير العمل الفرعي.') }}
          </p>
          <button type="button" (click)="loadChildren()">{{ t('Retry', 'إعادة المحاولة') }}</button>
        } @else if (config.versionId && !selectedChildMatches()) {
          <p role="alert">
            {{
              t(
                'The selected child no longer matches this Task Type and organization scope. Select a matching child before publishing.',
                'سير العمل الفرعي المحدد لا يطابق نوع المهمة والنطاق التنظيمي. اختر سير عمل مطابقاً قبل النشر.'
              )
            }}
          </p>
        } @else if (!eligibleChildren().length) {
          <p role="status">
            {{
              t(
                'No published child workflow matches this Task Type and organization scope.',
                'لا يوجد سير عمل فرعي منشور يطابق نوع المهمة والنطاق التنظيمي.'
              )
            }}
          </p>
        }
        <p class="text-xs opacity-70">
          {{
            t(
              'The linked child is a separate workflow. Its completion enables this activity’s approval. The SLA includes child time.',
              'سير العمل الفرعي المرتبط مستقل. بعد اكتماله يتاح اعتماد النشاط الرئيسي. تشمل مدة الخدمة وقت التنفيذ الفرعي.'
            )
          }}
        </p>
        @if (selectedChild(); as child) {
          <a
            class="text-sm underline"
            target="_blank"
            rel="noopener"
            [routerLink]="[
              '/admin/workflow/definitions',
              child.id,
              'versions',
              child.versionId,
              'designer',
            ]"
            >{{
              t('Open child workflow in a new tab', 'فتح سير العمل الفرعي في علامة تبويب جديدة')
            }}</a
          >
        }
      }
      @if (hierarchical()) {
        <details class="border-t pt-3">
          <summary class="cursor-pointer font-medium">
            {{ t('Workflow data', 'بيانات سير العمل') }}
          </summary>
          <p class="text-xs opacity-70 my-2">
            {{
              main
                ? t(
                    'Pass parent variables into the child and copy child results back for the next event.',
                    'مرر متغيرات المسار الرئيسي إلى الفرعي وانسخ النتائج للحدث التالي.'
                  )
                : t(
                    'Save form answers as workflow variables. Source paths start with output., for example output.meter_serial.',
                    'احفظ إجابات النموذج كمتغيرات. يبدأ المسار بـ output. مثل output.meter_serial.'
                  )
            }}
          </p>
          @for (group of mappingGroups(); track group.key) {
            <fieldset [disabled]="readonly" class="space-y-2 my-3">
              <legend class="text-sm font-medium">{{ group.label }}</legend>
              @for (row of dataMappings[group.key]; track $index) {
                <div class="grid grid-cols-[1fr_1fr_auto] gap-2">
                  <input
                    class="wf-input"
                    [attr.aria-label]="t('Destination variable', 'المتغير الهدف')"
                    [(ngModel)]="row.target"
                    [ngModelOptions]="standalone"
                  />
                  <input
                    class="wf-input"
                    [attr.aria-label]="t('Source path', 'مسار المصدر')"
                    [(ngModel)]="row.source"
                    [ngModelOptions]="standalone"
                  />
                  <button
                    type="button"
                    class="wf-btn-secondary"
                    (click)="dataMappings[group.key].splice($index, 1)"
                  >
                    {{ t('Remove', 'حذف') }}
                  </button>
                </div>
              }
              <button
                type="button"
                class="wf-btn-secondary"
                (click)="dataMappings[group.key].push({ target: '', source: '' })"
              >
                {{ t('Add mapping', 'إضافة ربط') }}
              </button>
            </fieldset>
          }
          @if (mappingError()) {
            <p role="alert" class="text-sm text-red-600">{{ mappingError() }}</p>
          }
          <button
            type="button"
            class="wf-btn-secondary"
            [disabled]="readonly"
            (click)="saveMappings()"
          >
            {{ t('Save data mappings', 'حفظ ربط البيانات') }}
          </button>
        </details>
      }
    </fieldset>
  `,
})
export class WorkflowBusinessActivityComponent implements OnChanges {
  @Input() parentSettings: WorkspaceSettings | null = null;
  @Input() configuration = '{}';
  @Input() main = false;
  @Input() readonly = false;
  @Input() childBusy = false;
  @Output() openChild = new EventEmitter<void>();
  @Input() nodes: { nodeKey: string; name: string; type: string }[] = [];
  @Input() variables: { variableKey: string }[] = [];
  @Output() configurationChange = new EventEmitter<string>();
  private readonly api = inject(WorkflowWorkspaceService);
  private readonly locale = inject(LocaleService);
  readonly children = signal<WorkspaceWorkflow[]>([]);
  readonly error = signal('');
  readonly mappingError = signal('');
  dataMappings: Record<'input' | 'output', DataMapping[]> = { input: [], output: [] };
  readonly standalone = { standalone: true };
  readonly taskTypes = signal<ReferenceItem[]>([]);
  readonly typeError = signal(false);
  readonly childrenLoading = signal(false);
  readonly childrenError = signal(false);
  private childRequest = 0;
  private loadedChild: string | undefined;
  private childrenLoaded = false;
  config: BusinessConfiguration = {};
  private loaded = false;
  t(en: string, ar: string) {
    return this.locale.locale() === 'ar' ? ar : en;
  }
  label(item: ReferenceItem) {
    return this.locale.locale() === 'ar' ? item.nameAr || item.nameEn : item.nameEn;
  }
  localeName(item: WorkspaceWorkflow) {
    return this.t(item.name, item.nameAr || item.name);
  }
  hierarchical(): boolean {
    return [3, 4].includes(this.parentSettings?.designerVersion ?? 0);
  }
  taskTypeAvailable() {
    return this.taskTypes().some((t) => t.id === this.config.taskTypeId);
  }
  eligibleChildren() {
    return this.parentSettings?.kind !== 'Main' || this.readonly
      ? this.children()
      : this.children().filter((child) =>
          matchingChild(this.parentSettings, child.workspaceJson, this.config.taskTypeId),
        );
  }
  selectedChildMatches() {
    return this.eligibleChildren().some((c) => c.versionId === this.config.versionId);
  }
  taskType(id: string) {
    if (this.readonly) return;
    if (this.hierarchical() && !this.main) return;
    if (id === this.config.taskTypeId) return;
    this.config.taskTypeId = id;
    if (this.hierarchical() && this.main) {
      delete this.config.definitionKey;
      delete this.config.versionId;
    }
    if (Array.isArray(this.config['formSteps']) && this.config['formSteps'].length)
      this.config['formStepsReviewRequired'] = true;
    delete this.config['formDefinitionId'];
    delete this.config['formVersionNo'];
    this.apply();
  }
  loadTaskTypes() {
    this.typeError.set(false);
    this.api
      .references('task-types')
      .subscribe({ next: (r) => this.taskTypes.set(r), error: () => this.typeError.set(true) });
  }
  loadChildren() {
    const request = ++this.childRequest;
    this.childrenLoading.set(true);
    this.childrenError.set(false);
    this.loadedChild = this.config.versionId;
    this.childrenLoaded = true;
    this.api.children(this.config.versionId).subscribe({
      next: (r) => {
        if (request !== this.childRequest) return;
        this.children.set(r);
        this.childrenLoading.set(false);
      },
      error: () => {
        if (request !== this.childRequest) return;
        this.childrenLoading.set(false);
        this.childrenError.set(true);
      },
    });
  }
  selectedChild() {
    return this.children().find((c) => c.versionId === this.config.versionId);
  }
  businessNodes() {
    return this.nodes.filter((n) => n.type === 'UserTask' || n.type === 'MainActivity');
  }
  ngOnChanges() {
    try {
      this.config = JSON.parse(this.configuration || '{}');
    } catch {
      this.config = {};
    }
    this.mappingError.set('');
    this.dataMappings = { input: [], output: [] };
    try {
      const output = this.main
        ? this.config['outputMappings']
        : JSON.parse(String(this.config['outputMappingJson'] || '{}'));
      const rows = (value: unknown): DataMapping[] => {
        if (value === undefined || value === null) return [];
        if (
          typeof value !== 'object' ||
          Array.isArray(value) ||
          Object.values(value).some((source) => typeof source !== 'string')
        )
          throw new Error('Invalid data mappings');
        return Object.entries(value as Record<string, string>).map(([target, source]) => ({
          target,
          source,
        }));
      };
      this.dataMappings = { input: rows(this.config['inputMappings']), output: rows(output) };
    } catch {
      this.mappingError.set(
        this.t('The saved data mappings need review.', 'راجع ربط البيانات المحفوظ.'),
      );
    }
    if (!this.loaded) {
      this.loaded = true;
      this.loadTaskTypes();
    }
    if (
      this.main &&
      !this.hierarchical() &&
      (!this.childrenLoaded || this.loadedChild !== this.config.versionId)
    )
      this.loadChildren();
  }
  child(versionId: string) {
    if (this.readonly) return;
    const child = this.eligibleChildren().find((c) => c.versionId === versionId);
    if (versionId && !child) return;
    this.config.definitionKey = child?.definitionKey;
    this.config.versionId = child?.versionId;
    this.config['waitForCompletion'] = true;
    this.apply();
  }
  apply() {
    if (!this.readonly) this.configurationChange.emit(JSON.stringify(this.config));
  }
  mappingGroups(): { key: 'input' | 'output'; label: string }[] {
    return this.main
      ? [
          { key: 'input', label: this.t('Child inputs', 'مدخلات المسار الفرعي') },
          { key: 'output', label: this.t('Child outputs', 'مخرجات المسار الفرعي') },
        ]
      : [{ key: 'output', label: this.t('Saved form answers', 'إجابات النموذج المحفوظة') }];
  }
  saveMappings() {
    if (this.readonly) return;
    const values: Record<string, Record<string, string>> = {};
    for (const group of this.mappingGroups()) {
      const rows = this.dataMappings[group.key];
      if (
        rows.some(
          (row) => !/^[A-Za-z_][A-Za-z0-9_]*$/.test(row.target.trim()) || !row.source.trim(),
        ) ||
        new Set(rows.map((row) => row.target.trim())).size !== rows.length
      ) {
        this.mappingError.set(
          this.t(
            'Use unique variable names and provide a source path for every mapping.',
            'استخدم أسماء متغيرات فريدة وحدد مسار كل مصدر.',
          ),
        );
        return;
      }
      values[group.key] = Object.fromEntries(
        rows.map((row) => [row.target.trim(), row.source.trim()]),
      );
    }
    if (this.main) {
      this.config['inputMappings'] = values['input'];
      this.config['outputMappings'] = values['output'];
    } else this.config['outputMappingJson'] = JSON.stringify(values['output']);
    this.mappingError.set('');
    this.apply();
  }
}
