import { Component, EventEmitter, Input, OnChanges, OnInit, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MultiSelectModule } from 'primeng/multiselect';
import { forkJoin } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { LookupItem, LookupsService } from '@core/lookups/lookups.service';
import { WorkflowOrganizationScope } from './workflow-workspace.service';
import { buildWorkflowScopes, pruneWorkflowSelection, selectionFromScopes, WorkflowScopeLookups, WorkflowScopeSelection } from './workflow-scope-selection';

@Component({ selector: 'app-workflow-scope-editor', standalone: true, imports: [FormsModule, MultiSelectModule], template: `
  <fieldset class="space-y-3" [disabled]="readonly">
    <legend class="font-medium mb-2">{{ t('Organization scope', 'النطاق التنظيمي') }}</legend>
    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      @for (level of levels; track level.key) {
        <div class="flex flex-col gap-2">
          <label [for]="'workflow-scope-' + level.key">{{ t(level.en, level.ar) }}</label>
          <p-multiselect [inputId]="'workflow-scope-' + level.key" [options]="options(level.key)"
            [ngModel]="selection[level.key]" (ngModelChange)="change(level.key, $event)" [ngModelOptions]="standalone"
            optionLabel="label" optionValue="value" display="chip" [filter]="true" [showClear]="true"
            [maxSelectedLabels]="2" [selectedItemsLabel]="t('{0} selected', 'تم تحديد {0}')"
            [placeholder]="t('Select', 'اختر')" [loading]="loading()" [disabled]="readonly || loading() || !!error() || unavailable(level.key)"
            appendTo="body" styleClass="w-full" />
        </div>
      }
    </div>
    <p class="text-xs opacity-70">{{ t('Select one or more clusters. CBU, Branch and Operation Area are optional. Branch and Operation Area both belong directly to CBU.', 'اختر قطاعاً واحداً أو أكثر. وحدة الأعمال والفرع ومنطقة العمليات اختيارية. يتبع الفرع ومنطقة العمليات وحدة الأعمال مباشرة.') }}</p>
    @if (error()) { <p role="alert" class="text-red-600">{{ error() }}</p><button type="button" class="wf-btn-secondary" (click)="load()">{{ t('Retry', 'إعادة المحاولة') }}</button> }
    @if (missingSelection()) { <p role="alert" class="text-red-600">{{ t('Some saved units are no longer available. Review the scope before publishing.', 'بعض الوحدات المحفوظة لم تعد متاحة. راجع النطاق قبل النشر.') }}</p> }
    @if (scopes.length) {
      <ul class="flex flex-wrap gap-2 text-sm" [attr.aria-label]="t('Selected scope', 'النطاق المحدد')">
        @for (scope of scopes; track $index) { <li class="rounded border px-3 py-1">{{ summary(scope) }}</li> }
      </ul>
    }
  </fieldset>
` })
export class WorkflowScopeEditorComponent implements OnInit, OnChanges {
  @Input() scopes: WorkflowOrganizationScope[] = [];
  @Input() readonly = false;
  @Output() scopesChange = new EventEmitter<WorkflowOrganizationScope[]>();
  private readonly api = inject(LookupsService);
  private readonly locale = inject(LocaleService);
  readonly standalone = { standalone: true };
  readonly loading = signal(true);
  readonly error = signal('');
  readonly levels = [
    { key: 'clusters' as const, en: 'Cluster', ar: 'القطاع' }, { key: 'cbus' as const, en: 'CBU', ar: 'وحدة الأعمال' },
    { key: 'branches' as const, en: 'Branch', ar: 'الفرع' }, { key: 'areas' as const, en: 'Operation Area', ar: 'منطقة العمليات' },
  ];
  lookups: WorkflowScopeLookups = { clusters: [], cbus: [], branches: [], areas: [] };
  selection: WorkflowScopeSelection = { clusters: [], cbus: [], branches: [], areas: [] };
  t(en: string, ar: string) { return this.locale.locale() === 'ar' ? ar : en; }
  ngOnInit() { this.load(); }
  ngOnChanges() { this.selection = selectionFromScopes(this.scopes); }
  load() {
    this.loading.set(true); this.error.set('');
    forkJoin({ clusters: this.api.listAll('Cluster', { isActive: true }), cbus: this.api.listAll('Cbu', { isActive: true }),
      branches: this.api.listAll('Branch', { isActive: true }), areas: this.api.listAll('OperationArea', { isActive: true }) })
      .subscribe({ next: rows => { this.lookups = rows; this.loading.set(false); },
        error: () => { this.error.set(this.t('Could not load organization choices.', 'تعذر تحميل الخيارات التنظيمية.')); this.loading.set(false); } });
  }
  unavailable(key: keyof WorkflowScopeSelection) { return key === 'cbus' ? !this.selection.clusters.length : key !== 'clusters' && !this.selection.cbus.length; }
  options(key: keyof WorkflowScopeSelection) {
    const parents = key === 'cbus' ? this.selection.clusters : this.selection.cbus;
    return this.lookups[key].filter(item => key === 'clusters' || parents.includes(item.parentCode || ''))
      .map(item => ({ value: item.code, label: this.name(item) + (item.parentCode ? ' · ' + this.parentName(key, item.parentCode) : '') }));
  }
  change(key: keyof WorkflowScopeSelection, values: string[] | null) {
    if (this.readonly || this.loading()) return;
    this.selection = pruneWorkflowSelection({ ...this.selection, [key]: values || [] }, this.lookups);
    this.scopes = buildWorkflowScopes(this.selection, this.lookups);
    this.scopesChange.emit(this.scopes);
  }
  missingSelection() { return !this.loading() && !this.error() && this.levels.some(l => this.selection[l.key].some(code => !this.lookups[l.key].some(i => i.code === code))); }
  summary(scope: WorkflowOrganizationScope) {
    const key = scope.level === 'Cluster' ? 'clusters' : scope.level === 'Cbu' ? 'cbus' : scope.level === 'Branch' ? 'branches' : 'areas';
    const item = this.lookups[key].find(i => i.code === scope.code);
    const level = this.levels.find(l => l.key === key)!;
    return `${this.t(level.en, level.ar)}: ${item ? this.name(item) : scope.code}`;
  }
  private name(item: LookupItem) { return this.t(item.nameEn, item.nameAr || item.nameEn); }
  private parentName(key: keyof WorkflowScopeSelection, code: string) {
    const item = this.lookups[key === 'cbus' ? 'clusters' : 'cbus'].find(i => i.code === code);
    return item ? this.name(item) : code;
  }
}
