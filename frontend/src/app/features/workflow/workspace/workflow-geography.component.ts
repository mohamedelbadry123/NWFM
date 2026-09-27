import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';
import { ReferenceItem, WorkflowWorkspaceService, WorkspaceSettings } from './workflow-workspace.service';

@Component({ selector: 'app-workflow-geography', standalone: true, imports: [FormsModule], template: `
  <fieldset [disabled]="readonly" class="grid gap-3 sm:grid-cols-3">
    <label>{{ t('Workflow type','نوع سير العمل') }}
      <select class="wf-input" [ngModel]="settings.kind" [ngModelOptions]="standalone" (ngModelChange)="kind($event)">
        <option value="Main">{{ t('Main workflow','سير عمل رئيسي') }}</option><option value="Child">{{ t('Child workflow','سير عمل فرعي') }}</option>
      </select>
    </label>
    @if (settings.kind === 'Main') {
      <label>{{ t('Cluster','القطاع') }}<select class="wf-input" [ngModel]="settings.clusterCode || ''" [ngModelOptions]="standalone" (ngModelChange)="chooseCluster($event)"><option value="">{{ t('Select cluster','اختر القطاع') }}</option>@for (item of clusters(); track item.code) {<option [value]="item.code">{{ label(item) }}</option>}</select></label>
      <label>{{ t('Region','المنطقة') }}<select class="wf-input" [disabled]="!settings.clusterCode" [ngModel]="settings.regionCode || ''" [ngModelOptions]="standalone" (ngModelChange)="chooseRegion($event)"><option value="">{{ t('Select region','اختر المنطقة') }}</option>@for (item of regions(); track item.code) {<option [value]="item.code">{{ label(item) }}</option>}</select></label>
      <label>{{ t('City','المدينة') }}<select class="wf-input" [disabled]="!settings.regionCode" [ngModel]="settings.cityCode || ''" [ngModelOptions]="standalone" (ngModelChange)="emit({cityCode:$event})"><option value="">{{ t('Select city','اختر المدينة') }}</option>@for (item of cities(); track item.code) {<option [value]="item.code">{{ label(item) }}</option>}</select></label>
    } @else { <p class="text-sm sm:col-span-2">{{ t('Geography is inherited from the main workflow when this child starts.','يتم توريث القطاع والمنطقة والمدينة من سير العمل الرئيسي عند بدء التنفيذ.') }}</p> }
  </fieldset>
  @if (error()) { <p class="text-red-600" role="alert">{{ error() }}</p> }
` })
export class WorkflowGeographyComponent implements OnChanges {
  @Input() settings: WorkspaceSettings = { kind: 'Main' };
  @Input() readonly = false;
  @Output() settingsChange = new EventEmitter<WorkspaceSettings>();
  private readonly api = inject(WorkflowWorkspaceService); private readonly locale = inject(LocaleService);
  readonly clusters = signal<ReferenceItem[]>([]); readonly regions = signal<ReferenceItem[]>([]); readonly cities = signal<ReferenceItem[]>([]); readonly error = signal('');
  readonly standalone = { standalone: true };
  t(en: string, ar: string) { return this.locale.locale() === 'ar' ? ar : en; }
  label(item: ReferenceItem) { return this.locale.locale() === 'ar' ? item.nameAr : item.nameEn; }
  ngOnChanges() { this.load('clusters', this.clusters); if (this.settings.clusterCode) this.load('regions', this.regions, this.settings.clusterCode); if (this.settings.regionCode) this.load('cities', this.cities, this.settings.regionCode); }
  private load(kind: string, target: typeof this.clusters, parent?: string) { this.api.references(kind, parent).subscribe({ next: items => {if(kind==='regions' && parent!==this.settings.clusterCode || kind==='cities' && parent!==this.settings.regionCode)return;target.set(items);this.error.set('');}, error: () => this.error.set(this.t('Could not load lookups. Please reopen this page.','تعذر تحميل القوائم. أعد فتح الصفحة.')) }); }
  emit(patch: Partial<WorkspaceSettings>) { this.settings = { ...this.settings, ...patch }; this.settingsChange.emit(this.settings); }
  kind(kind: 'Main' | 'Child') { this.settings = { kind }; this.regions.set([]); this.cities.set([]); this.settingsChange.emit(this.settings); }
  chooseCluster(clusterCode: string) { this.emit({ clusterCode, regionCode: '', cityCode: '' }); this.regions.set([]); this.cities.set([]); if (clusterCode) this.load('regions', this.regions, clusterCode); }
  chooseRegion(regionCode: string) { this.emit({ regionCode, cityCode: '' }); this.cities.set([]); if (regionCode) this.load('cities', this.cities, regionCode); }
}
