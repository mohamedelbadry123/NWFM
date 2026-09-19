import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowWorkItemsService, type WorkflowWorkItemView } from '../workflow-work-items.service';
import { dueRelativeLabel, formatRemainingSla, formatSlaDuration, isOverdue } from '../workflow-sla.util';
import { workflowApiErrorKey } from '../workflow-api-error';
import type { WorkflowActivityOutcomeView } from '@core/models/workflow-ops.models';
import { LocaleService } from '@core/i18n/locale.service';
import { PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowSlaClockComponent } from '../ui/workflow-sla-clock.component';
import { WorkflowAssignmentGroupsService } from '../workflow-assignment-groups.service';
import { WorkflowDepartmentsService } from '../workflow-departments.service';
import type { WorkflowAssignmentGroupDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-assignment-group-dto';
import type { WorkflowDepartmentDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-department-dto';
import { isRedirectOutcome } from '../designer/workflow-outcome.util';
import { WorkflowRuntimeService } from '../workflow-runtime.service';
import type { WorkflowProgressDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-progress-dto';
import type { WorkflowHistoryEvent, WorkflowRequestView } from '@core/models/workflow-ops.models';
import { WorkflowHistoryTimelineComponent } from '../ui/workflow-history-timeline.component';
import { WorkflowLiveGraphDialogComponent } from '../ui/workflow-live-graph-dialog.component';
import { WorkflowRelatedRecordComponent } from '../ui/workflow-related-record.component';
import { activityDisplayName, stepActivities } from '../workflow-history.util';

type TaskDetailTab = 'action' | 'details' | 'steps';

@Component({
  selector: 'app-workflow-task-detail',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    ReactiveFormsModule,
    FormsModule,
    TranslatePipe,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowSlaClockComponent,
    WorkflowHistoryTimelineComponent,
    WorkflowLiveGraphDialogComponent,
    WorkflowRelatedRecordComponent,
  ],
  templateUrl: './workflow-task-detail.component.html',
})
export class WorkflowTaskDetailComponent implements OnInit {
  private readonly workItemsService = inject(WorkflowWorkItemsService);
  private readonly runtimeService = inject(WorkflowRuntimeService);
  private readonly groupsService = inject(WorkflowAssignmentGroupsService);
  private readonly departmentsService = inject(WorkflowDepartmentsService);
  private readonly route = inject(ActivatedRoute);
  protected readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  protected readonly locale = inject(LocaleService);

  protected readonly item = signal<WorkflowWorkItemView | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly isSubmitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly selectedOutcome = signal<WorkflowActivityOutcomeView | null>(null);
  protected readonly groups = signal<WorkflowAssignmentGroupDto[]>([]);
  protected readonly departments = signal<WorkflowDepartmentDto[]>([]);
  protected readonly reassignGroupId = signal('');
  protected readonly tab = signal<TaskDetailTab>('action');
  protected readonly progress = signal<WorkflowProgressDto | null>(null);
  protected readonly request = signal<WorkflowRequestView | null>(null);
  protected readonly timeline = signal<WorkflowHistoryEvent[]>([]);
  protected readonly liveGraphOpen = signal(false);
  protected formValues: Record<string, any> = {};
  protected formLabel(field: { labelEn: string; labelAr: string; key: string }) { return (this.locale.isRtl() ? field.labelAr : field.labelEn) || field.labelEn || field.key; }

  protected readonly isOverdue = isOverdue;
  protected readonly dueRelativeLabel = dueRelativeLabel;
  protected readonly formatRemainingSla = formatRemainingSla;
  protected readonly formatSlaDuration = formatSlaDuration;

  protected readonly completeForm = this.fb.group({
    actionTaken: ['', Validators.required],
    comment: [''],
    redirectGroupId: [''],
    redirectDepartmentId: [''],
  });

  protected readonly outcomes = computed(() => this.item()?.availableOutcomes ?? []);
  protected readonly hasDefinedOutcomes = computed(() => this.outcomes().length > 0);
  protected readonly isRedirectSelected = computed(() => {
    const o = this.selectedOutcome();
    return isRedirectOutcome(o?.outcomeKey, o?.resultValue);
  });
  protected readonly canReassign = computed(() => {
    const status = this.item()?.status;
    return status !== 'Completed'
      && status !== 'Cancelled';
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.workItemsService.getById(id).subscribe({
      next: item => {
        this.item.set(item);
        this.formValues = { ...(item.formValues ?? {}) };
        this.isLoading.set(false);
        this.tab.set(item.status === 'Claimed' ? 'details' : 'action');
        this.loadWorkflowContext(item.workflowInstanceId);
      },
      error: () => this.isLoading.set(false),
    });
    this.groupsService.getPaged(1, 100).subscribe({
      next: page => this.groups.set(page.items ?? []),
      error: () => this.groups.set([]),
    });
    this.departmentsService.getPaged(1, 100).subscribe({
      next: page => this.departments.set((page.items ?? []).filter(d => d.isActive !== false)),
      error: () => this.departments.set([]),
    });
  }

  protected claim(): void {
    const id = this.item()?.id;
    if (!id) return;
    this.isSubmitting.set(true);
    this.submitError.set(null);
    this.workItemsService.claim(id).subscribe({
      next: updated => {
        this.item.set(updated);
        this.isSubmitting.set(false);
      },
      error: (err: unknown) => {
        this.submitError.set(workflowApiErrorKey(err));
        this.isSubmitting.set(false);
      },
    });
  }

  protected selectOutcome(outcome: WorkflowActivityOutcomeView): void {
    this.selectedOutcome.set(outcome);
    this.completeForm.patchValue({
      actionTaken: outcome.outcomeKey ?? '',
      redirectGroupId: '',
      redirectDepartmentId: '',
    });
    const commentCtrl = this.completeForm.controls.comment;
    if (outcome.requiresComment) {
      commentCtrl.setValidators([Validators.required]);
    } else {
      commentCtrl.clearValidators();
    }
    commentCtrl.updateValueAndValidity();
  }

  protected isRedirectOutcomeBtn(outcome: WorkflowActivityOutcomeView): boolean {
    return isRedirectOutcome(outcome.outcomeKey, outcome.resultValue);
  }

  protected canSubmitComplete(): boolean {
    if (this.completeForm.invalid || this.isSubmitting()) return false;
    if (!this.isRedirectSelected()) return true;
    const { redirectGroupId, redirectDepartmentId } = this.completeForm.getRawValue();
    return !!redirectGroupId || !!redirectDepartmentId;
  }

  protected onRedirectGroupChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.completeForm.patchValue({ redirectGroupId: value, redirectDepartmentId: value ? '' : this.completeForm.controls.redirectDepartmentId.value });
  }

  protected onRedirectDepartmentChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.completeForm.patchValue({ redirectDepartmentId: value, redirectGroupId: value ? '' : this.completeForm.controls.redirectGroupId.value });
  }

  protected departmentLabel(dept: WorkflowDepartmentDto): string {
    return this.locale.isRtl()
      ? (dept.nameAr || dept.name || dept.code || '—')
      : (dept.name || dept.nameAr || dept.code || '—');
  }

  protected outcomeLabel(outcome: WorkflowActivityOutcomeView): string {
    return this.locale.isRtl()
      ? (outcome.nameAr || outcome.name || outcome.outcomeKey || '—')
      : (outcome.name || outcome.nameAr || outcome.outcomeKey || '—');
  }

  protected complete(): void {
    if (!this.canSubmitComplete()) return;
    const id = this.item()?.id;
    if (!id) return;
    const { actionTaken, comment, redirectGroupId, redirectDepartmentId } = this.completeForm.getRawValue();
    this.isSubmitting.set(true);
    this.submitError.set(null);
    this.workItemsService.complete(id, {
      actionTaken: actionTaken!,
      formValues: Object.fromEntries(Object.entries(this.formValues).map(([key, value]) => {
        const field = this.item()?.formFields?.find(f => f.key === key);
        return [key, field?.type === 'number' && value !== '' && value != null ? Number(value) : value];
      })),
      comment,
      redirectAssignmentGroupId: this.isRedirectSelected() ? (redirectGroupId || null) : null,
      redirectDepartmentId: this.isRedirectSelected() ? (redirectDepartmentId || null) : null,
    }).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        const view = this.isRedirectSelected() ? 'available' : 'claimedByMe';
        this.router.navigate(['/org/workflow/tasks'], { queryParams: { view } });
      },
      error: (err: unknown) => {
        this.submitError.set(workflowApiErrorKey(err));
        this.isSubmitting.set(false);
      },
    });
  }

  protected release(): void {
    const id = this.item()?.id;
    if (!id) return;
    this.isSubmitting.set(true);
    this.workItemsService.release(id).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.router.navigate(['/org/workflow/tasks'], { queryParams: { view: 'available' } });
      },
      error: () => this.isSubmitting.set(false),
    });
  }

  protected reassign(): void {
    const id = this.item()?.id;
    const groupId = this.reassignGroupId();
    if (!id || !groupId) return;
    this.isSubmitting.set(true);
    this.submitError.set(null);
    this.workItemsService.reassign(id, groupId).subscribe({
      next: updated => {
        this.item.set(updated);
        this.isSubmitting.set(false);
        this.reassignGroupId.set('');
      },
      error: (err: unknown) => {
        this.submitError.set(workflowApiErrorKey(err));
        this.isSubmitting.set(false);
      },
    });
  }

  protected onReassignGroupChange(event: Event): void {
    this.reassignGroupId.set((event.target as HTMLSelectElement).value);
  }

  protected groupLabel(group: WorkflowAssignmentGroupDto): string {
    return this.locale.isRtl()
      ? (group.nameAr || group.name || group.code || '—')
      : (group.name || group.nameAr || group.code || '—');
  }

  protected serviceName(item: WorkflowWorkItemView): string {
    return this.locale.isRtl()
      ? (item.serviceNameAr || item.serviceNameEn || '—')
      : (item.serviceNameEn || item.serviceNameAr || '—');
  }

  protected openLiveGraph(): void {
    if (this.item()?.workflowInstanceId) this.liveGraphOpen.set(true);
  }

  protected closeLiveGraph(): void {
    this.liveGraphOpen.set(false);
  }

  protected tabClass(id: TaskDetailTab): string {
    return this.tab() === id
      ? 'rounded-lg bg-white px-4 py-2 text-sm font-semibold text-ink-900 shadow-sm dark:bg-dark-800 dark:text-white'
      : 'rounded-lg px-4 py-2 text-sm font-medium text-ink-500 hover:text-ink-800 dark:text-dark-300';
  }

  protected stepList() {
    return stepActivities(this.progress()?.activities);
  }

  protected activityName(activity: { name?: string | null; activityNodeKey?: string | null }): string {
    return activityDisplayName(activity);
  }

  protected activityStatusClass(status: string | undefined): string {
    switch (status) {
      case 'Completed': return 'bg-green-500';
      case 'Active':    return 'bg-primary animate-pulse';
      case 'Skipped':   return 'bg-ink-400';
      case 'Failed':    return 'bg-red-500';
      default:          return 'bg-ink-300 dark:bg-dark-600';
    }
  }

  private loadWorkflowContext(instanceId: string | undefined): void {
    if (!instanceId) return;
    this.runtimeService.getProgress(instanceId).subscribe({
      next: progress => this.progress.set(progress),
    });
    this.runtimeService.getTimeline(instanceId).subscribe({
      next: events => {
        const ordered = [...(events ?? [])].sort(
          (a, b) => Date.parse(a.occurredAt ?? '') - Date.parse(b.occurredAt ?? ''),
        );
        this.timeline.set(ordered);
      },
    });
    this.runtimeService.getRequestByInstanceId(instanceId).subscribe({
      next: req => this.request.set(req),
      error: () => this.request.set(null),
    });
  }

  protected statusTone(status: string | undefined): PrvTone {
    switch (status) {
      case 'Claimed':    return 'info';
      case 'Completed':  return 'success';
      case 'Cancelled':  return 'danger';
      default:           return 'warning';
    }
  }
}
