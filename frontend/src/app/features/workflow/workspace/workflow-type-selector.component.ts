import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin, of } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { ReferenceItem, WorkspaceSettings, WorkflowWorkspaceService } from './workflow-workspace.service';

@Component({ selector: 'app-workflow-type-selector', standalone: true, imports: [FormsModule], template: `
  <div class="space-y-2 sm:max-w-lg">
    <label for="workflow-classification">{{ settings.kind === 'Main' ? t('Activity Type', 'نوع النشاط') : t('Task Types', 'أنواع المهام') }} <span aria-hidden="true">*</span></label>
    <select id="workflow-classification" class="wf-input" [ngModel]="selectedId()" [ngModelOptions]="standalone"
      (ngModelChange)="choose($event)" [disabled]="readonly || loading() || !!error()" required>
      <option value="">{{ t('Select a type', 'اختر النوع') }}</option>
      @if (selectedId() && !available()) { <option [value]="selectedId()" disabled>{{ t('Saved type is unavailable', 'النوع المحفوظ غير متاح') }}</option> }
      @for (item of items(); track item.id) { <option [value]="item.id">{{ label(item) }}</option> }
    </select>
    @if (error()) { <p role="alert" class="text-red-600">{{ error() }}</p><button type="button" class="wf-btn-secondary" (click)="load()">{{ t('Retry', 'إعادة المحاولة') }}</button> }
    @if (!loading() && !error() && !items().length) { <p role="status">{{ t('No active types are available. Add a type in Lookups.', 'لا توجد أنواع نشطة. أضف نوعاً في البيانات المرجعية.') }}</p> }
    @if (!loading() && selectedId() && !available()) { <p role="alert" class="text-red-600">{{ t('Select an active type before publishing.', 'اختر نوعاً نشطاً قبل النشر.') }}</p> }
  </div>
` })
export class WorkflowTypeSelectorComponent implements OnChanges {
  @Input() settings: WorkspaceSettings = { kind: 'Main', schemaVersion: 2 };
  @Input() readonly = false;
  @Output() settingsChange = new EventEmitter<WorkspaceSettings>();
  private readonly api = inject(WorkflowWorkspaceService);
  private readonly locale = inject(LocaleService);
  readonly standalone = { standalone: true };
  readonly items = signal<ReferenceItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  private departments: ReferenceItem[] = [];
  private loadedKind?: string;
  private request = 0;
  t(en: string, ar: string) { return this.locale.locale() === 'ar' ? ar : en; }
  ngOnChanges() { if (this.loadedKind !== this.settings.kind) this.load(); }
  load() {
    this.loadedKind = this.settings.kind;
    const request = ++this.request;
    this.items.set([]); this.loading.set(true); this.error.set('');
    forkJoin({ items: this.api.references(this.settings.kind === 'Main' ? 'field-activity-types' : 'task-types'),
      departments: this.settings.kind === 'Main' ? this.api.references('departments') : of([] as ReferenceItem[]) })
      .subscribe({ next: result => { if (request !== this.request) return; this.items.set(result.items); this.departments = result.departments; this.loading.set(false); },
        error: () => { if (request !== this.request) return; this.loading.set(false); this.error.set(this.t('Could not load types.', 'تعذر تحميل الأنواع.')); } });
  }
  selectedId() { return (this.settings.kind === 'Main' ? this.settings.fieldActivityTypeId : this.settings.taskTypeId) || ''; }
  available() { return this.items().some(i => i.id === this.selectedId()); }
  label(item: ReferenceItem) {
    const department = this.departments.find(d => d.code === item.parentCode);
    return this.t(item.nameEn, item.nameAr || item.nameEn) + (department ? ' · ' + this.t(department.nameEn, department.nameAr || department.nameEn) : '');
  }
  choose(id: string) {
    if (this.readonly) return;
    const item = this.items().find(i => i.id === id);
    const { fieldActivityTypeId: _field, fieldActivityCode: _code, departmentCode: _department, taskTypeId: _task, ...settings } = this.settings;
    this.settingsChange.emit({ ...settings, ...(this.settings.kind === 'Main'
      ? { fieldActivityTypeId: item?.id, fieldActivityCode: item?.code, departmentCode: item?.parentCode }
      : { taskTypeId: item?.id }) });
  }
}
