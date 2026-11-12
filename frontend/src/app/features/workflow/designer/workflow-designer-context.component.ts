import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  OnChanges,
  Output,
  inject,
  signal,
  DestroyRef,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { forkJoin, catchError, of } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { LookupsService, LookupItem, LookupType } from '@core/lookups/lookups.service';
import { WorkflowDefinitionsService } from '../workflow-definitions.service';
import { effectiveScopeRows } from '../workspace/workflow-scope-rows';
import { WorkflowScopeSummaryComponent } from '../workspace/workflow-scope-summary.component';
import {
  WorkspaceSettings,
  ReferenceItem,
  WorkflowWorkspaceService,
  hasRequiredLocation,
  hasRequiredClassification,
} from '../workspace/workflow-workspace.service';

@Component({
  selector: 'app-workflow-designer-context',
  standalone: true,
  imports: [WorkflowScopeSummaryComponent],
  templateUrl: './workflow-designer-context.component.html',
  styleUrl: './workflow-designer-context.component.css',
})
export class WorkflowDesignerContextComponent implements OnInit, OnChanges {
  @Input({ required: true }) settings!: WorkspaceSettings;
  @Input() name = '';
  @Input() versionNumber = 1;
  @Input() status = 'Draft';
  @Input() selectedName = '';
  @Input() selectedType = '';
  @Input() workCount = 0;
  @Input() hierarchical = false;
  @Input() embedded = false;
  @Input() parentName = '';
  @Output() libraryRequested = new EventEmitter<void>();
  @Output() parentRequested = new EventEmitter<void>();
  @Output() settingsRequested = new EventEmitter<void>();
  @Output() addRequested = new EventEmitter<'MainActivity' | 'UserTask'>();
  readonly locale = inject(LocaleService);
  private readonly api = inject(WorkflowWorkspaceService);
  private readonly lookups = inject(LookupsService);
  private readonly definitions = inject(WorkflowDefinitionsService);
  private readonly destroyRef = inject(DestroyRef);
  readonly units = signal<Record<string, LookupItem[]>>({});
  readonly activities = signal<ReferenceItem[]>([]);
  readonly tasks = signal<ReferenceItem[]>([]);
  readonly resolvedParent = signal('');
  readonly labelsFailed = signal(false);
  readonly loading = signal(true);
  private parentId?: string;
  t(en: string, ar: string) {
    return this.locale.locale() === 'ar' ? ar : en;
  }
  ngOnInit() {
    this.loadLabels();
  }
  ngOnChanges() {
    const id = this.settings?.parentDefinitionId;
    if (id === this.parentId) return;
    this.parentId = id;
    this.resolvedParent.set('');
    if (id && !this.parentName)
      this.definitions
        .getById(id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (definition) => {
            if (this.parentId === id)
              this.resolvedParent.set(
                this.t(definition.name || '', definition.nameAr || definition.name || ''),
              );
          },
          error: () => {},
        });
  }
  loadLabels() {
    this.loading.set(true);
    this.labelsFailed.set(false);
    const level = (type: LookupType) => this.lookups.listAll(type);
    forkJoin({
      Cluster: level('Cluster'),
      Cbu: level('Cbu'),
      Branch: level('Branch'),
      OperationArea: level('OperationArea'),
      activities: this.api.references('field-activity-types'),
      tasks: this.api.references('task-types'),
    })
      .pipe(
        catchError(() => {
          this.labelsFailed.set(true);
          return of(null);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((rows) => {
        this.loading.set(false);
        if (rows) {
          this.units.set({
            Cluster: rows.Cluster,
            Cbu: rows.Cbu,
            Branch: rows.Branch,
            OperationArea: rows.OperationArea,
          });
          this.activities.set(rows.activities);
          this.tasks.set(rows.tasks);
        }
      });
  }
  child() {
    return this.settings.kind === 'Child';
  }
  configured() {
    return hasRequiredLocation(this.settings) && hasRequiredClassification(this.settings);
  }
  classification() {
    const item = (this.child() ? this.tasks() : this.activities()).find(
      (row) =>
        row.id === (this.child() ? this.settings.taskTypeId : this.settings.fieldActivityTypeId),
    );
    return item
      ? this.t(item.nameEn, item.nameAr || item.nameEn)
      : this.settings.fieldActivityCode ||
          this.settings.taskTypeId ||
          this.settings.fieldActivityTypeId ||
          this.t('Not selected', 'لم يتم الاختيار');
  }
  unit(level: string, code?: string) {
    if (!code) return '';
    const item = this.units()[level]?.find((row) => row.code === code);
    return item ? this.t(item.nameEn, item.nameAr || item.nameEn) : code;
  }
  scopeLabels() {
    // Saved entries are shown as authored while they still describe the saved scopes; otherwise one line per scope.
    if (this.settings.organizationScopes)
      return effectiveScopeRows(this.settings.organizationScopes, this.settings.scopeRows).map(
        (row) => {
          const parts = [this.unit('Cluster', row.clusterCode)];
          if (row.cbuCode) parts.push(this.unit('Cbu', row.cbuCode));
          if (row.branchCode)
            parts.push(this.t('Branch', 'الفرع') + ': ' + this.unit('Branch', row.branchCode));
          if (row.operationAreaCode)
            parts.push(
              this.t('Operation area', 'منطقة العمليات') +
                ': ' +
                this.unit('OperationArea', row.operationAreaCode),
            );
          return parts.filter(Boolean).join(' / ');
        },
      );
    return [
      this.unit('Cluster', this.settings.clusterCode),
      this.unit('Cbu', this.settings.cbuCode),
      this.unit('Branch', this.settings.branchCode),
      this.unit('OperationArea', this.settings.operationAreaCode),
    ].filter(Boolean).length
      ? [
          [
            this.unit('Cluster', this.settings.clusterCode),
            this.unit('Cbu', this.settings.cbuCode),
            this.unit('Branch', this.settings.branchCode),
            this.unit('OperationArea', this.settings.operationAreaCode),
          ]
            .filter(Boolean)
            .join(' / '),
        ]
      : [];
  }
  selectionType() {
    const names: Record<string, [string, string]> = {
      MainActivity: ['Task Type', 'نوع المهمة'],
      UserTask: ['Step', 'خطوة'],
      Start: ['Start', 'البداية'],
      End: ['End', 'النهاية'],
      ServiceTask: ['Event', 'حدث'],
    };
    const name = names[this.selectedType];
    return name ? this.t(...name) : this.t('Canvas element', 'عنصر الرسم');
  }
}
