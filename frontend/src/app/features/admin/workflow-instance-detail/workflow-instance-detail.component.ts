import {
  ChangeDetectionStrategy,
  Component,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowRuntimeService } from '@features/workflow/workflow-runtime.service';
import type { WorkflowProgressDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-progress-dto';
import type { WorkflowHistoryEvent } from '@core/models/workflow-ops.models';
import type { WorkflowIncidentSummaryDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-incident-summary-dto';
import type { WorkflowTimerDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-timer-dto';
import { PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';
import { WorkflowLiveGraphComponent } from '../../workflow/live-graph/workflow-live-graph.component';
import { WorkflowHistoryTimelineComponent } from '../../workflow/ui/workflow-history-timeline.component';
import { activityDisplayName, stepActivities } from '../../workflow/workflow-history.util';
import {
  TenantOption,
  AppContextService,
} from '@core/context/app-context.service';
import { prettyWorkflowLabel, shortId } from '../../workflow/workflow-display.util';

@Component({
  selector: 'app-workflow-instance-detail',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, TranslatePipe, PrvStatusPillComponent, WorkflowPageHeaderComponent, WorkflowLiveGraphComponent, WorkflowHistoryTimelineComponent],
  templateUrl: './workflow-instance-detail.component.html',
})
export class WorkflowInstanceDetailComponent implements OnInit {
  private readonly runtimeService = inject(WorkflowRuntimeService);
  private readonly orgsService = inject(AppContextService);
  private readonly route = inject(ActivatedRoute);
  protected readonly router = inject(Router);

  protected readonly progress = signal<WorkflowProgressDto | null>(null);
  protected readonly timeline = signal<WorkflowHistoryEvent[]>([]);
  protected readonly timers = signal<WorkflowTimerDto[]>([]);
  protected readonly incidents = signal<WorkflowIncidentSummaryDto[]>([]);
  protected readonly orgs = signal<TenantOption[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly isActioning = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly pretty = prettyWorkflowLabel;
  protected readonly shortId = shortId;

  protected instanceId!: string;

  ngOnInit(): void {
    this.instanceId = this.route.snapshot.paramMap.get('id')!;
    this.orgsService.list().subscribe({
      next: orgs => this.orgs.set(orgs),
    });
    this.load();
  }

  protected orgLabel(orgId: string | undefined): string {
    if (!orgId) return '—';
    return this.orgs().find(o => o.id === orgId)?.name ?? shortId(orgId);
  }

  protected activityStatusKey(status: string | undefined): string {
    return `workflow.runtime.status_${status ?? 'Pending'}`;
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    this.runtimeService.adminGetInstanceById(this.instanceId).subscribe({
      next: prog => {
        this.progress.set(prog);
        this.isLoading.set(false);
        this.runtimeService.getTimeline(this.instanceId).subscribe({
          next: events => {
            const ordered = [...(events ?? [])].sort((a, b) =>
              Date.parse(a.occurredAt ?? '') - Date.parse(b.occurredAt ?? '')
            );
            this.timeline.set(ordered);
          },
        });
        this.runtimeService.getTimersForInstance(this.instanceId).subscribe({
          next: items => this.timers.set(items ?? []),
          error: () => this.timers.set([]),
        });
        this.runtimeService.getIncidentsForInstance(this.instanceId).subscribe({
          next: items => this.incidents.set(items ?? []),
          error: () => this.incidents.set([]),
        });
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string } };
        this.loadError.set(e?.error?.detail ?? e?.error?.title ?? 'Failed to load instance.');
        this.isLoading.set(false);
      },
    });
  }

  protected suspend(): void {
    this.runAction(() => this.runtimeService.adminSuspend(this.instanceId));
  }

  protected resume(): void {
    this.runAction(() => this.runtimeService.adminResume(this.instanceId));
  }

  protected retry(): void {
    this.runAction(() => this.runtimeService.adminRetry(this.instanceId));
  }

  private runAction(action: () => import('rxjs').Observable<void>): void {
    this.isActioning.set(true);
    this.actionError.set(null);
    action().subscribe({
      next: () => {
        this.isActioning.set(false);
        this.load();
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string } };
        this.actionError.set(e?.error?.detail ?? e?.error?.title ?? 'Action failed.');
        this.isActioning.set(false);
      },
    });
  }

  protected instanceStatus(): string | undefined {
    return this.progress()?.instance?.status;
  }

  protected statusTone(status: string | undefined): PrvTone {
    switch (status) {
      case 'Running':    return 'info';
      case 'Completed':  return 'success';
      case 'Cancelled':  return 'danger';
      case 'Failed':     return 'danger';
      case 'Suspended':  return 'warning';
      default:           return 'neutral';
    }
  }

  protected statusClass(status: string | undefined): string {
    switch (status) {
      case 'Running':    return 'text-primary bg-primary/10';
      case 'Completed':  return 'text-green-700 bg-green-50 dark:text-green-400 dark:bg-green-900/20';
      case 'Cancelled':  return 'text-red-700 bg-red-50 dark:text-red-400 dark:bg-red-900/20';
      case 'Failed':     return 'text-red-700 bg-red-50 dark:text-red-400 dark:bg-red-900/20';
      case 'Suspended':  return 'text-amber-800 bg-amber-50 dark:text-amber-400 dark:bg-amber-900/20';
      default:           return 'text-ink-500 bg-ink-100 dark:text-dark-300 dark:bg-dark-700/50';
    }
  }

  protected stepList() {
    return stepActivities(this.progress()?.activities);
  }

  protected activityName(activity: { name?: string | null; activityNodeKey?: string | null }): string {
    return activityDisplayName(activity);
  }

  protected exportTimeline(): void {
    const rows = this.timeline();
    if (rows.length === 0) return;
    const escape = (value: string): string => `"${value.replace(/"/g, '""')}"`;
    const header = 'occurredAt,step,eventType,actor,action,comment,attachment';
    const lines = rows.map(e =>
      [
        e.occurredAt ?? '',
        e.activityNameEn ?? e.activityNodeKey ?? '',
        String(e.eventType ?? ''),
        e.actorName ?? e.actorUserId ?? '',
        e.actionTaken ?? '',
        e.comment ?? '',
        e.attachmentName ?? e.attachmentUrl ?? '',
      ].map(v => escape(v)).join(',')
    );
    const csv = [header, ...lines].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `workflow-timeline-${this.instanceId}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }
}
