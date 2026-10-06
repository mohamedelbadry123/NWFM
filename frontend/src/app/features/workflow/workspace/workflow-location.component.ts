import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';
import { OrgScopeSelectorComponent } from '@shared/components/org-scope/org-scope-selector.component';
import { EMPTY_ORG_LOCATION, OrgLocation } from '@shared/components/org-scope/org-scope.model';
import { WorkspaceSettings, WorkflowOrganizationScope, hasLegacyLocationConflict, workspaceLocation } from './workflow-workspace.service';
import { WorkflowScopeEditorComponent } from './workflow-scope-editor.component';
import { WorkflowTypeSelectorComponent } from './workflow-type-selector.component';

/**
 * The workflow kind and, for a main workflow, its place in the shared org hierarchy — picked with the
 * same cascading selector as every other module, so the levels, lookups and parent rules are one set.
 */
@Component({ selector: 'app-workflow-location', standalone: true, imports: [FormsModule, OrgScopeSelectorComponent, WorkflowScopeEditorComponent, WorkflowTypeSelectorComponent], template: `
  <fieldset [disabled]="readonly" class="grid gap-3">
    <label class="sm:max-w-xs">{{ t('Workflow type','نوع سير العمل') }}
      <select class="wf-input" [ngModel]="settings.kind" [ngModelOptions]="standalone" (ngModelChange)="kind($event)">
        <option value="Main">{{ t('Main workflow','سير عمل رئيسي') }}</option><option value="Child">{{ t('Child workflow','سير عمل فرعي') }}</option>
      </select>
    </label>
    @if (settings.schemaVersion === 2) {
      <app-workflow-scope-editor [scopes]="settings.organizationScopes || []" [readonly]="readonly" (scopesChange)="chooseScopes($event)" />
      <app-workflow-type-selector [settings]="settings" [readonly]="readonly" (settingsChange)="updateType($event)" />
    } @else if (settings.kind === 'Main') {
      <div class="grid gap-2">
        <span class="font-medium">{{ t('Organization location','الموقع التنظيمي') }}</span>
        @if (conflict()) {
          <p class="text-red-600" role="alert">{{ t('This workflow was saved with two different CBUs or branches. Re-select its organization location before publishing.','تم حفظ سير العمل هذا بوحدتي أعمال أو فرعين مختلفين. أعد اختيار الموقع التنظيمي قبل النشر.') }}</p>
        }
        <app-org-scope-selector mode="single" layout="row" [disabled]="readonly" [initialLocation]="seed()" (locationChange)="chooseLocation($event)" />
        <p class="text-xs opacity-70">{{ t('Select at least a cluster and a CBU. Branch and operation area are optional; both sit directly under the CBU.','اختر القطاع ووحدة الأعمال على الأقل. الفرع ومنطقة العمليات اختياريان، وكلاهما يتبع وحدة الأعمال مباشرة.') }}</p>
      </div>
    } @else { <p class="text-sm">{{ t('The organization location is inherited from the main workflow when this child starts.','يتم توريث الموقع التنظيمي من سير العمل الرئيسي عند بدء التنفيذ.') }}</p> }
    @if (settings.schemaVersion !== 2 && !readonly) {
      <button type="button" class="wf-btn-secondary justify-self-start" (click)="upgrade()">{{ t('Configure organization scope and workflow type', 'إعداد النطاق التنظيمي ونوع النشاط أو المهمة') }}</button>
      <p class="text-xs opacity-70">{{ t('Review the scope when upgrading this older workflow. Branch and Operation Area selections will be separate eligible units.', 'راجع النطاق عند تحديث سير العمل القديم. تصبح الفروع ومناطق العمليات المحددة وحدات مؤهلة مستقلة.') }}</p>
    }
  </fieldset>
` })
export class WorkflowLocationComponent implements OnChanges {
  @Input() settings: WorkspaceSettings = { kind: 'Main' };
  @Input() readonly = false;
  @Output() settingsChange = new EventEmitter<WorkspaceSettings>();
  private readonly locale = inject(LocaleService);
  /** What the selector starts from. Re-set only for settings arriving from outside, never for our own echo. */
  readonly seed = signal<OrgLocation>({ ...EMPTY_ORG_LOCATION });
  readonly conflict = signal(false);
  readonly standalone = { standalone: true };
  private emitted?: WorkspaceSettings;
  t(en: string, ar: string) { return this.locale.locale() === 'ar' ? ar : en; }
  ngOnChanges() {
    this.conflict.set(hasLegacyLocationConflict(this.settings));
    if (this.settings !== this.emitted) this.seed.set(workspaceLocation(this.settings));
  }
  kind(kind: 'Main' | 'Child') {
    if (this.readonly) return;
    if (this.settings.schemaVersion !== 2) this.upgrade();
    const { fieldActivityTypeId: _field, departmentCode: _department, fieldActivityCode: _code, taskTypeId: _task, ...rest } = this.settings;
    this.emit({ ...rest, kind });
  }
  upgrade() {
    if (this.readonly) return;
    const place = workspaceLocation(this.settings);
    const scopes: WorkflowOrganizationScope[] = [];
    if (place.clusterCode) {
      const base = { clusterCode: place.clusterCode, ...(place.cbuCode ? { cbuCode: place.cbuCode } : {}) };
      if (place.branchCode) scopes.push({ ...base, level: 'Branch', code: place.branchCode });
      if (place.operationAreaCode) scopes.push({ ...base, level: 'OperationArea', code: place.operationAreaCode });
      if (!scopes.length) scopes.push({ ...base, level: place.cbuCode ? 'Cbu' : 'Cluster', code: place.cbuCode || place.clusterCode });
    }
    this.chooseScopes(scopes);
  }
  chooseScopes(organizationScopes: WorkflowOrganizationScope[]) {
    if (this.readonly) return;
    const { clusterCode: _cluster, cbuCode: _cbu, branchCode: _branch, operationAreaCode: _area, regionCode: _region, cityCode: _city, ...rest } = this.settings;
    this.emit({ ...rest, schemaVersion: 2, organizationScopes });
  }
  updateType(settings: WorkspaceSettings) { if (!this.readonly) this.emit(settings); }
  chooseLocation(location: OrgLocation) {
    // An explicit choice replaces whatever the saved settings held, legacy keys included.
    const { regionCode: _region, cityCode: _city, clusterCode: _cluster, cbuCode: _cbu, branchCode: _branch, operationAreaCode: _area, ...rest } = this.settings;
    const place: Partial<WorkspaceSettings> = {};
    if (location.clusterCode) place.clusterCode = location.clusterCode;
    if (location.cbuCode) place.cbuCode = location.cbuCode;
    if (location.branchCode) place.branchCode = location.branchCode;
    if (location.operationAreaCode) place.operationAreaCode = location.operationAreaCode;
    this.emit({ ...rest, ...place });
  }
  private emit(settings: WorkspaceSettings) {
    this.settings = this.emitted = settings;
    this.conflict.set(hasLegacyLocationConflict(settings));
    this.settingsChange.emit(settings);
  }
}
