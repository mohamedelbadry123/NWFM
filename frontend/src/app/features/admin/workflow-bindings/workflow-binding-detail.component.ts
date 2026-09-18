import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { WorkflowBindingsService, type WorkflowBindingReadinessView } from '../../workflow/workflow-bindings.service';
import type { WorkflowBindingAssignmentMappingDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-binding-assignment-mapping-dto';
import type { WorkflowAssignmentGroupDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-assignment-group-dto';
import type { WorkflowBindingViewModel } from '@core/models/workflow-binding.models';
import type { WorkflowBindingSimulateResult } from '@core/models/workflow-ops.models';
import { ToastService } from '@core/notifications/toast.service';
import { PrvEmptyStateComponent, PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../../workflow/ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-binding-detail',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslateModule,
    RouterLink,
    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-binding-detail.component.html',
})
export class WorkflowBindingDetailComponent implements OnInit {
  private readonly route     = inject(ActivatedRoute);
  private readonly service   = inject(WorkflowBindingsService);
  private readonly toast     = inject(ToastService);
  private readonly translate = inject(TranslateService);
  private readonly fb        = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly bindingId  = signal('');
  protected readonly binding    = signal<WorkflowBindingViewModel | null>(null);
  protected readonly mappings   = signal<WorkflowBindingAssignmentMappingDto[]>([]);
  protected readonly orgGroups  = signal<WorkflowAssignmentGroupDto[]>([]);
  protected readonly isLoading  = signal(true);
  protected readonly loadError  = signal<string | null>(null);
  protected readonly actioning  = signal(false);

  protected readonly showAddMapping    = signal(false);
  protected readonly addMappingSaving  = signal(false);
  protected readonly editMappingTarget = signal<WorkflowBindingAssignmentMappingDto | null>(null);
  protected readonly editMappingSaving = signal(false);
  protected readonly deleteMappingTarget = signal<WorkflowBindingAssignmentMappingDto | null>(null);
  protected readonly deleteMappingSaving = signal(false);
  protected readonly confirmAction = signal<'activate' | 'deactivate' | null>(null);

  protected readonly samplePayloadJson = signal('{\n  "sample": true\n}');
  protected readonly simulating = signal(false);
  protected readonly simulateResult = signal<WorkflowBindingSimulateResult | null>(null);
  protected readonly simulateError = signal<string | null>(null);
  protected readonly readiness = signal<WorkflowBindingReadinessView | null>(null);

  protected readonly addMappingForm = this.fb.group({
    assignmentKey:   [''],
    assignmentGroupId: [''],
  });

  protected readonly editMappingForm = this.fb.group({
    assignmentGroupId: [''],
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('bindingId') ?? '';
    this.bindingId.set(id);
    this.loadBinding(id);
  }

  private loadBinding(id: string): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.service.getByIdForAdmin(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: b => {
          this.binding.set(b);
          this.loadMappingsAndGroups(b);
        },
        error: (err: { error?: { message?: string } }) => {
          this.loadError.set(err?.error?.message ?? 'Failed to load binding.');
          this.isLoading.set(false);
        },
      });
  }

  private loadMappingsAndGroups(b: WorkflowBindingViewModel): void {
    if (!b.workflowDefinitionId || !b.id || !b.organizationId) {
      this.isLoading.set(false);
      return;
    }
    forkJoin({
      mappings: this.service.listMappings(b.workflowDefinitionId, b.id, b.organizationId),
      groups: this.service.listOrgGroups(b.organizationId),
      readiness: this.service.getReadiness(b.workflowDefinitionId, b.id),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ mappings, groups, readiness }) => {
        this.mappings.set(mappings.filter(m => m.isActive));
        this.orgGroups.set(groups);
        this.readiness.set(readiness);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      },
    });
  }

  protected reload(): void {
    const b = this.binding();
    if (!b?.id) return;
    this.loadBinding(b.id);
  }

  protected openConfirm(action: 'activate' | 'deactivate'): void { this.confirmAction.set(action); }
  protected closeConfirm(): void { this.confirmAction.set(null); }

  protected executeConfirm(): void {
    const b = this.binding();
    const action = this.confirmAction();
    if (!b?.workflowDefinitionId || !b.id || !action || this.actioning()) return;
    this.actioning.set(true);
    const call = action === 'activate'
      ? this.service.activate(b.workflowDefinitionId, b.id)
      : this.service.deactivate(b.workflowDefinitionId, b.id);
    call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.actioning.set(false);
        this.confirmAction.set(null);
        this.toast.success(action === 'activate' ? 'Binding activated.' : 'Binding deactivated.');
        this.reload();
      },
      error: (err: { error?: { message?: string; Message?: string; code?: string; Code?: string } }) => {
        this.actioning.set(false);
        this.confirmAction.set(null);
        const code = err?.error?.code ?? err?.error?.Code;
        const translated = code ? this.translate.instant('error.' + code) : '';
        this.toast.error(
          (translated && translated !== 'error.' + code ? translated : null)
            ?? err?.error?.message
            ?? err?.error?.Message
            ?? 'Action failed.'
        );
      },
    });
  }

  protected runSimulate(): void {
    const b = this.binding();
    if (!b?.id || this.simulating()) return;
    this.simulating.set(true);
    this.simulateError.set(null);
    this.simulateResult.set(null);
    this.service.simulate(b.id, {
      organizationId: b.organizationId,
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
        if (code === 'Workflow.Version.NoPublishedVersion') {
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

  protected onSamplePayloadInput(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.samplePayloadJson.set(value);
  }

  protected modeTone(mode: string | null | undefined): PrvTone {
    switch ((mode ?? '').toLowerCase()) {
      case 'active': return 'success';
      case 'shadow': return 'warning';
      case 'paused': return 'info';
      default:       return 'neutral';
    }
  }

  protected modeClass(mode: string | null | undefined): string {
    switch ((mode ?? '').toLowerCase()) {
      case 'active':   return 'bg-green-50 text-green-700 border-green-200 dark:bg-green-900/40 dark:text-green-400 dark:border-green-800/50';
      case 'shadow':   return 'bg-amber-50 text-amber-800 border-amber-200 dark:bg-amber-900/40 dark:text-amber-300 dark:border-amber-800/50';
      case 'paused':   return 'bg-sky-50 text-sky-700 border-sky-200 dark:bg-blue-900/40 dark:text-blue-300 dark:border-blue-800/50';
      default:         return 'bg-ink-100 text-ink-500 border-ink-200 dark:bg-dark-700 dark:text-dark-300 dark:border-dark-600';
    }
  }

  protected openAddMapping(): void {
    this.addMappingForm.reset();
    this.showAddMapping.set(true);
  }
  protected closeAddMapping(): void { this.showAddMapping.set(false); }

  protected submitAddMapping(): void {
    const b = this.binding();
    const v = this.addMappingForm.value;
    if (!b?.workflowDefinitionId || !b.id || !b.organizationId || !v.assignmentKey || !v.assignmentGroupId || this.addMappingSaving()) return;
    this.addMappingSaving.set(true);
    this.service.createMapping(b.workflowDefinitionId, b.id, b.organizationId, {
      assignmentKey: v.assignmentKey,
      assignmentGroupId: v.assignmentGroupId,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.addMappingSaving.set(false);
        this.showAddMapping.set(false);
        this.toast.success('Mapping added.');
        this.reload();
      },
      error: (err: { error?: { message?: string } }) => {
        this.addMappingSaving.set(false);
        this.toast.error(err?.error?.message ?? 'Failed to add mapping.');
      },
    });
  }

  protected openEditMapping(m: WorkflowBindingAssignmentMappingDto): void {
    this.editMappingTarget.set(m);
    this.editMappingForm.patchValue({ assignmentGroupId: m.assignmentGroupId });
  }
  protected closeEditMapping(): void { this.editMappingTarget.set(null); }

  protected submitEditMapping(): void {
    const b = this.binding();
    const m = this.editMappingTarget();
    const v = this.editMappingForm.value;
    if (!b?.workflowDefinitionId || !b.id || !b.organizationId || !m?.id || !v.assignmentGroupId || this.editMappingSaving()) return;
    this.editMappingSaving.set(true);
    this.service.updateMapping(b.workflowDefinitionId, b.id, b.organizationId, m.id, {
      assignmentGroupId: v.assignmentGroupId,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.editMappingSaving.set(false);
        this.editMappingTarget.set(null);
        this.toast.success('Mapping updated.');
        this.reload();
      },
      error: (err: { error?: { message?: string } }) => {
        this.editMappingSaving.set(false);
        this.toast.error(err?.error?.message ?? 'Failed to update mapping.');
      },
    });
  }

  protected openDeleteMapping(m: WorkflowBindingAssignmentMappingDto): void { this.deleteMappingTarget.set(m); }
  protected closeDeleteMapping(): void { this.deleteMappingTarget.set(null); }

  protected submitDeleteMapping(): void {
    const b = this.binding();
    const m = this.deleteMappingTarget();
    if (!b?.workflowDefinitionId || !b.id || !b.organizationId || !m?.id || this.deleteMappingSaving()) return;
    this.deleteMappingSaving.set(true);
    this.service.deleteMapping(b.workflowDefinitionId, b.id, b.organizationId, m.id)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: () => {
          this.deleteMappingSaving.set(false);
          this.deleteMappingTarget.set(null);
          this.toast.success('Mapping removed.');
          this.reload();
        },
        error: (err: { error?: { message?: string } }) => {
          this.deleteMappingSaving.set(false);
          this.toast.error(err?.error?.message ?? 'Failed to remove mapping.');
        },
      });
  }

  protected groupName(groupId: string | null | undefined): string {
    if (!groupId) return '—';
    return this.orgGroups().find(g => g.id === groupId)?.name ?? groupId;
  }

  protected formatDate(iso: string | null | undefined): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
