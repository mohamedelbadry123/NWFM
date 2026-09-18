import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { WorkflowBindingsService } from '../../workflow/workflow-bindings.service';
import { WorkflowModuleCatalogService } from '../../workflow/workflow-module-catalog.service';
import { WorkflowDefinitionsService } from '../../workflow/workflow-definitions.service';
import { WorkflowVersionsService } from '../../workflow/workflow-versions.service';
import type { WorkflowDefinitionDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-definition-dto';
import type { WorkflowVersionDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-version-dto';
import type { ModuleCatalogEntry } from '@shared/models/models/Workflow/Application/Constants/module-catalog-entry';
import type { EntityTypeEntry } from '@shared/models/models/Workflow/Application/Constants/entity-type-entry';
import type { TriggerEventEntry } from '@shared/models/models/Workflow/Application/Constants/trigger-event-entry';
import type { WorkflowVersionPolicy } from '@shared/models/models/Workflow/Domain/Enums/workflow-version-policy';
import type {
  KeyValueRow,
  WorkflowBindingModeExt,
  WorkflowExecutionPolicy,
} from '@core/models/workflow-binding.models';
import type { WorkflowBindingSimulateResult } from '@core/models/workflow-ops.models';
import { AppContextService } from '@core/context/app-context.service';
import { ToastService } from '@core/notifications/toast.service';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';

interface OrgOption { id: string; name: string; }
interface CanvasAssignment { activityName: string; groupName: string; assigned: boolean; }

const TOTAL_STEPS = 11;

const BINDING_MODES: WorkflowBindingModeExt[] = ['Disabled', 'Shadow', 'Active', 'Paused'];

/** Suggested screen keys derived from module catalog (until catalog exposes screens). */
const MODULE_SCREEN_SUGGESTIONS: Record<string, {key:string; labelEn:string}[]> = { Standalone: [{key:'workflow.start',labelEn:'Start workflow'}] };

@Component({
  selector: 'app-workflow-binding-wizard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslateModule, WorkflowPageHeaderComponent],
  templateUrl: './workflow-binding-wizard.component.html',
})
export class WorkflowBindingWizardComponent implements OnInit {
  private readonly bindingsService    = inject(WorkflowBindingsService);
  private readonly catalogService     = inject(WorkflowModuleCatalogService);
  private readonly definitionsService = inject(WorkflowDefinitionsService);
  private readonly versionsService    = inject(WorkflowVersionsService);
  private readonly orgsService        = inject(AppContextService);
  private readonly toast              = inject(ToastService);
  private readonly translate          = inject(TranslateService);
  private readonly router             = inject(Router);
  private readonly fb                 = inject(FormBuilder);
  private readonly destroyRef         = inject(DestroyRef);

  protected readonly totalSteps = TOTAL_STEPS;
  protected readonly step       = signal(1);
  protected readonly loading    = signal(true);
  protected readonly saving     = signal(false);

  protected readonly orgs        = signal<OrgOption[]>([]);
  protected readonly catalog     = signal<ModuleCatalogEntry[]>([]);
  protected readonly definitions = signal<WorkflowDefinitionDto[]>([]);
  protected readonly versions    = signal<WorkflowVersionDto[]>([]);

  protected readonly selectedOrg        = signal<OrgOption | null>(null);
  protected readonly selectedModule     = signal<ModuleCatalogEntry | null>(null);
  protected readonly selectedEntity     = signal<EntityTypeEntry | null>(null);
  protected readonly selectedTrigger    = signal<TriggerEventEntry | null>(null);
  protected readonly selectedDefinition = signal<WorkflowDefinitionDto | null>(null);
  protected readonly selectedVersion    = signal<WorkflowVersionDto | null>(null);

  protected readonly mode            = signal<WorkflowBindingModeExt>('Disabled');
  protected readonly versionPolicy   = signal<WorkflowVersionPolicy>('Latest');
  protected readonly executionPolicy = signal<WorkflowExecutionPolicy>('StartNewInstance');
  protected readonly bindingModes    = BINDING_MODES;
  protected readonly executionPolicies: WorkflowExecutionPolicy[] = [
    'StartNewInstance',
    'SignalExistingInstance',
    'StartIfNoRunningInstance',
    'RestartAfterTerminal',
  ];

  protected readonly canvasAssignments = signal<CanvasAssignment[]>([]);
  protected readonly canvasAssignmentsLoading = signal(false);
  protected readonly inputMappingRows   = signal<KeyValueRow[]>([{ key: '', value: '' }]);
  protected readonly outcomeMappingRows = signal<KeyValueRow[]>([{ key: '', value: '' }]);

  protected readonly createdBindingId = signal<string | null>(null);
  protected readonly samplePayloadJson = signal('{\n  "entityId": "00000000-0000-0000-0000-000000000001"\n}');
  protected readonly simulating = signal(false);
  protected readonly simulateResult = signal<WorkflowBindingSimulateResult | null>(null);
  protected readonly simulateError = signal<string | null>(null);

  protected readonly conditionForm = this.fb.group({
    screenKey:                ['', Validators.maxLength(200)],
    startEventKey:            ['', Validators.maxLength(200)],
    startConditionExpression: ['', Validators.maxLength(2000)],
    conditionJson:            ['', Validators.maxLength(4000)],
    description:              ['', Validators.maxLength(500)],
  });

  protected readonly entityTypes = computed(() => this.selectedModule()?.entityTypes ?? []);
  protected readonly triggerEvents = computed(() => this.selectedEntity()?.triggerEvents ?? []);

  protected readonly screenSuggestions = computed(() => {
    const mod = this.selectedModule();
    if (!mod?.moduleKey) return [] as { key: string; labelEn: string }[];
    const fromCatalog = MODULE_SCREEN_SUGGESTIONS[mod.moduleKey] ?? [];
    const entity = this.selectedEntity()?.entityType;
    const derived = entity
      ? [{ key: `${mod.moduleKey.toLowerCase()}.${entity.toLowerCase()}`, labelEn: `${mod.nameEn ?? mod.moduleKey} · ${entity}` }]
      : [];
    const seen = new Set<string>();
    return [...fromCatalog, ...derived].filter(s => {
      if (seen.has(s.key)) return false;
      seen.add(s.key);
      return true;
    });
  });

  protected readonly publishedVersions = computed(() =>
    this.versions().filter(v => v.status === 'Published'));

  protected readonly stepValid = computed(() => {
    switch (this.step()) {
      case 1:  return !!this.selectedOrg();
      case 2:  return !!this.selectedModule();
      case 3:  return !!this.selectedEntity();
      case 4:  return !!this.selectedTrigger();
      case 5:  return !!this.selectedDefinition();
      case 6:  return true;
      case 7:  return this.versionPolicy() === 'Latest' || !!this.selectedVersion();
      case 8:  return true;
      case 9:  return true;
      case 10: return true;
      case 11: return true;
      default: return false;
    }
  });

  ngOnInit(): void {
    this.loading.set(true);
    forkJoin({
      orgs: this.orgsService.list(),
      catalog: this.catalogService.getCatalog(),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ orgs, catalog }) => {
        this.orgs.set(orgs);
        this.catalog.set(catalog);
        this.loading.set(false);
      },
      error: () => {
        this.toast.error('Failed to load wizard data.');
        this.loading.set(false);
      },
    });
  }

  protected selectOrg(org: OrgOption): void {
    this.selectedOrg.set(org);
    this.selectedDefinition.set(null);
    this.definitions.set([]);
    this.definitionsService.getPaged(1, 200, undefined, org.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: r => this.definitions.set(r.items ?? []),
        error: () => this.definitions.set([]),
      });
  }
  protected selectModule(m: ModuleCatalogEntry): void {
    this.selectedModule.set(m);
    this.selectedEntity.set(null);
    this.selectedTrigger.set(null);
    const suggestions = MODULE_SCREEN_SUGGESTIONS[m.moduleKey ?? ''] ?? [];
    if (suggestions.length === 1 && !this.conditionForm.value.screenKey) {
      this.conditionForm.patchValue({ screenKey: suggestions[0].key });
    }
  }
  protected selectEntity(e: EntityTypeEntry): void {
    this.selectedEntity.set(e);
    this.selectedTrigger.set(null);
  }
  protected selectTrigger(t: TriggerEventEntry): void { this.selectedTrigger.set(t); }
  protected selectDefinition(d: WorkflowDefinitionDto): void {
    this.selectedDefinition.set(d);
    this.selectedVersion.set(null);
    this.versions.set([]);
    if (!d.id) return;
    this.versionsService.getPaged(d.id, 1, 50)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: r => this.versions.set(r.items ?? []), error: () => {} });
  }
  protected selectVersion(v: WorkflowVersionDto): void { this.selectedVersion.set(v); }

  protected setMode(m: WorkflowBindingModeExt): void { this.mode.set(m); }
  protected setVersionPolicy(p: WorkflowVersionPolicy): void {
    this.versionPolicy.set(p);
    if (p === 'Latest') this.selectedVersion.set(null);
  }
  protected setExecutionPolicy(p: WorkflowExecutionPolicy): void {
    this.executionPolicy.set(p);
  }

  protected selectScreenKey(key: string): void {
    this.conditionForm.patchValue({ screenKey: key });
  }

  protected next(): void {
    if (!this.stepValid() || this.step() >= TOTAL_STEPS) return;
    const nextStep = this.step() + 1;
    if (nextStep === 9) this.loadCanvasAssignments();
    this.step.set(nextStep);
  }

  protected back(): void {
    if (this.step() <= 1) return;
    this.step.set(this.step() - 1);
  }

  private loadCanvasAssignments(): void {
    const def = this.selectedDefinition();
    const ver = this.selectedVersion();
    const org = this.selectedOrg();
    if (!def?.id || !org) {
      this.canvasAssignments.set([]);
      return;
    }

    const versionId = ver?.id ?? this.publishedVersions()[0]?.id;
    if (!versionId) {
      this.canvasAssignments.set([]);
      return;
    }

    this.canvasAssignmentsLoading.set(true);
    forkJoin({
      version: this.versionsService.getById(def.id, versionId),
      groups: this.bindingsService.listOrgGroups(org.id),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ version, groups }) => {
        const byId = new Map(groups.map(g => [g.id ?? '', g]));
        const byCode = new Map(groups.map(g => [g.code ?? '', g]));
        const rows: CanvasAssignment[] = [];
        for (const activity of version.activities ?? []) {
          if (activity.activityType !== 'UserTask') continue;
          const rule = activity.assignmentRules?.[0];
          const group = (rule?.referenceId ? byId.get(rule.referenceId) : undefined)
            ?? (rule?.assignmentKey ? byCode.get(rule.assignmentKey) : undefined);
          rows.push({
            activityName: activity.name || activity.nodeKey || '',
            groupName: group?.name || group?.code || '',
            assigned: !!group,
          });
        }
        this.canvasAssignments.set(rows);
        this.canvasAssignmentsLoading.set(false);
      },
      error: () => {
        this.canvasAssignments.set([]);
        this.canvasAssignmentsLoading.set(false);
      },
    });
  }

  protected addInputRow(): void {
    this.inputMappingRows.update(rows => [...rows, { key: '', value: '' }]);
  }
  protected removeInputRow(index: number): void {
    this.inputMappingRows.update(rows => rows.length <= 1 ? rows : rows.filter((_, i) => i !== index));
  }
  protected updateInputRow(index: number, field: 'key' | 'value', value: string): void {
    this.inputMappingRows.update(rows =>
      rows.map((r, i) => i === index ? { ...r, [field]: value } : r)
    );
  }

  protected addOutcomeRow(): void {
    this.outcomeMappingRows.update(rows => [...rows, { key: '', value: '' }]);
  }
  protected removeOutcomeRow(index: number): void {
    this.outcomeMappingRows.update(rows => rows.length <= 1 ? rows : rows.filter((_, i) => i !== index));
  }
  protected updateOutcomeRow(index: number, field: 'key' | 'value', value: string): void {
    this.outcomeMappingRows.update(rows =>
      rows.map((r, i) => i === index ? { ...r, [field]: value } : r)
    );
  }

  private rowsToJson(rows: KeyValueRow[]): string | undefined {
    const obj: Record<string, string> = {};
    for (const r of rows) {
      const k = r.key.trim();
      if (!k) continue;
      obj[k] = r.value;
    }
    return Object.keys(obj).length ? JSON.stringify(obj) : undefined;
  }

  protected submit(): void {
    if (!this.stepValid() || this.saving()) return;
    if (this.createdBindingId()) {
      this.step.set(11);
      return;
    }
    const org    = this.selectedOrg()!;
    const mod    = this.selectedModule()!;
    const entity = this.selectedEntity()!;
    const trig   = this.selectedTrigger()!;
    const def    = this.selectedDefinition()!;
    const ver    = this.selectedVersion();
    const form   = this.conditionForm.value;

    if (!def.id) return;
    this.saving.set(true);
    this.bindingsService.create(def.id, {
      organizationId:            org.id,
      moduleKey:                 mod.moduleKey,
      entityType:                entity.entityType,
      triggerEvent:              trig.eventKey,
      description:               form.description || undefined,
      mode:                      this.mode(),
      versionPolicy:             this.versionPolicy(),
      executionPolicy:           this.executionPolicy(),
      fixedWorkflowVersionId:    ver?.id,
      startEventKey:             form.startEventKey || undefined,
      startConditionExpression:  form.startConditionExpression || undefined,
      screenKey:                 form.screenKey || undefined,
      inputMappingJson:          this.rowsToJson(this.inputMappingRows()),
      outcomeMappingJson:        this.rowsToJson(this.outcomeMappingRows()),
      conditionJson:             form.conditionJson?.trim() || undefined,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: binding => {
        this.saving.set(false);
        this.createdBindingId.set(binding.id ?? null);
        this.toast.success('Binding created successfully.');
        this.step.set(11);
      },
      error: (err: { error?: { message?: string } }) => {
        this.saving.set(false);
        this.toast.error(err?.error?.message ?? 'Failed to create binding.');
      },
    });
  }

  protected runSimulate(): void {
    const bindingId = this.createdBindingId();
    if (!bindingId || this.simulating()) return;
    this.simulating.set(true);
    this.simulateError.set(null);
    this.simulateResult.set(null);
    this.bindingsService.simulate(bindingId, {
      organizationId: this.selectedOrg()?.id,
      samplePayloadJson: this.samplePayloadJson(),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        this.simulating.set(false);
        this.simulateResult.set(result);
      },
      error: (err: unknown) => {
        this.simulating.set(false);
        const e = err as { title?: string; detail?: string; message?: string; error?: { message?: string; detail?: string; code?: string; Code?: string } };
        const code = e?.title ?? e?.error?.code ?? e?.error?.Code;
        const detail = e?.detail ?? e?.message ?? e?.error?.detail ?? e?.error?.message;
        if (code === 'Workflow.Binding.NotFound' || code === 'error.Workflow.Binding.NotFound') {
          this.simulateError.set(this.translate.instant('error.Workflow.Binding.NotFound'));
          return;
        }
        if (code === 'Workflow.Version.NoPublishedVersion' || code === 'error.Workflow.Version.NoPublishedVersion') {
          this.simulateError.set(
            detail
              ?? 'Simulation needs a Published workflow version. Publish the definition version first, then run Simulate again.'
          );
          return;
        }
        if (code === 'Workflow.Version.NotPublished') {
          this.simulateError.set(
            detail
              ?? 'The fixed version is not Published. Publish it, or switch the binding to Latest with a published version.'
          );
          return;
        }
        this.simulateError.set(detail ?? 'Simulation failed.');
      },
    });
  }

  protected finish(): void {
    const id = this.createdBindingId();
    if (id) this.router.navigate(['/admin/workflow/bindings', id]);
    else this.router.navigate(['/admin/workflow/bindings']);
  }

  protected cancel(): void { this.router.navigate(['/admin/workflow/bindings']); }

  protected stepTitle(n: number): string {
    const keys: Record<number, string> = {
      1: 'select_org', 2: 'select_module', 3: 'select_entity',
      4: 'select_trigger', 5: 'select_definition', 6: 'select_mode',
      7: 'select_version', 8: 'start_condition', 9: 'groups_ready', 10: 'review',
      11: 'simulate',
    };
    return keys[n] ?? '';
  }

  protected modeLabelKey(m: WorkflowBindingModeExt): string {
    return 'workflow.bindings.mode.' + m.toLowerCase();
  }

  protected modeHintKey(m: WorkflowBindingModeExt): string {
    return 'workflow.bindings.mode.' + m.toLowerCase() + '_hint';
  }
}
