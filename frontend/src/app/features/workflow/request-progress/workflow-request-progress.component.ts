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
import { WorkflowRuntimeService } from '../workflow-runtime.service';
import { formatRemainingSla, formatSlaDuration } from '../workflow-sla.util';
import type { WorkflowProgressDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-progress-dto';
import type { WorkflowHistoryEvent } from '@core/models/workflow-ops.models';
import type { WorkflowIncidentSummaryDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-incident-summary-dto';
import type { WorkflowRequestView } from '@core/models/workflow-ops.models';
import { LocaleService } from '@core/i18n/locale.service';
import { PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowSlaClockComponent } from '../ui/workflow-sla-clock.component';
import { WorkflowHistoryTimelineComponent } from '../ui/workflow-history-timeline.component';
import { WorkflowLiveGraphDialogComponent } from '../ui/workflow-live-graph-dialog.component';
import { WorkflowRelatedRecordComponent } from '../ui/workflow-related-record.component';
import { activityDisplayName, stepActivities } from '../workflow-history.util';

type RequestProgressTab = 'overview' | 'details' | 'steps';

@Component({
  selector: 'app-workflow-request-progress',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    TranslatePipe,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowSlaClockComponent,
    WorkflowHistoryTimelineComponent,
    WorkflowLiveGraphDialogComponent,
    WorkflowRelatedRecordComponent,
  ],
  templateUrl: './workflow-request-progress.component.html',
})
export class WorkflowRequestProgressComponent implements OnInit {
  private readonly runtimeService = inject(WorkflowRuntimeService);
  private readonly route = inject(ActivatedRoute);
  protected readonly router = inject(Router);
  private readonly locale = inject(LocaleService);

  protected readonly progress = signal<WorkflowProgressDto | null>(null);
  protected readonly request = signal<WorkflowRequestView | null>(null);
  protected readonly timeline = signal<WorkflowHistoryEvent[]>([]);
  protected readonly incidents = signal<WorkflowIncidentSummaryDto[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly isCancelling = signal(false);
  protected readonly tab = signal<RequestProgressTab>('details');
  protected readonly liveGraphOpen = signal(false);
  protected readonly formatRemainingSla = formatRemainingSla;
  protected readonly formatSlaDuration = formatSlaDuration;

  protected instanceId!: string;

  ngOnInit(): void {
    this.instanceId = this.route.snapshot.paramMap.get('instanceId')!;
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.runtimeService.getProgress(this.instanceId).subscribe({
      next: progress => {
        this.progress.set(progress);
        this.isLoading.set(false);
        this.loadTimeline();
        this.loadIncidents();
        this.loadRequest();
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string } };
        this.loadError.set(e?.error?.detail ?? e?.error?.title ?? 'Failed to load progress.');
        this.isLoading.set(false);
      },
    });
  }

  private loadTimeline(): void {
    this.runtimeService.getTimeline(this.instanceId).subscribe({
      next: events => {
        const ordered = [...(events ?? [])].sort((a, b) =>
          Date.parse(a.occurredAt ?? '') - Date.parse(b.occurredAt ?? '')
        );
        this.timeline.set(ordered);
      },
    });
  }

  private loadIncidents(): void {
    this.runtimeService.getIncidentsForInstance(this.instanceId).subscribe({
      next: items => this.incidents.set(items ?? []),
      error: () => this.incidents.set([]),
    });
  }

  private loadRequest(): void {
    this.runtimeService.getRequestByInstanceId(this.instanceId).subscribe({
      next: req => this.request.set(req),
      error: () => this.request.set(null),
    });
  }

  protected serviceName(): string {
    const req = this.request();
    if (!req) return '—';
    return this.locale.isRtl()
      ? (req.serviceNameAr || req.serviceNameEn || '—')
      : (req.serviceNameEn || req.serviceNameAr || '—');
  }

  protected currentStep(): string {
    const req = this.request();
    if (!req) return this.prettyNodeKey(this.progress()?.instance?.currentActivityNodeKey);
    return this.locale.isRtl()
      ? (req.currentActivityNameAr || req.currentActivityNameEn || '—')
      : (req.currentActivityNameEn || req.currentActivityNameAr || '—');
  }

  protected canCancel(): boolean {
    const status = this.instanceStatus();
    return status !== 'Completed' && status !== 'Cancelled' && status === 'Running';
  }

  protected cancel(): void {
    if (!this.canCancel()) return;
    this.isCancelling.set(true);
    this.runtimeService.cancel(this.instanceId).subscribe({
      next: () => {
        this.isCancelling.set(false);
        this.load();
      },
      error: () => this.isCancelling.set(false),
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

  protected statusBadgeClass(status: string | undefined): string {
    switch (status) {
      case 'Running':    return 'bg-primary/10 text-primary border border-primary/30';
      case 'Completed':  return 'bg-green-900/40 text-green-400 border border-green-800/50';
      case 'Cancelled':  return 'bg-red-900/40 text-red-400 border border-red-800/50';
      case 'Failed':     return 'bg-red-900/40 text-red-400 border border-red-800/50';
      case 'Suspended':  return 'bg-amber-900/40 text-amber-400 border border-amber-800/50';
      default:           return 'bg-dark-700 text-dark-400 border border-dark-600';
    }
  }

  protected activityStatusClass(status: string | undefined): string {
    switch (status) {
      case 'Completed':  return 'bg-green-500';
      case 'Active':     return 'bg-primary animate-pulse';
      case 'Skipped':    return 'bg-dark-400';
      case 'Failed':     return 'bg-red-500';
      default:           return 'bg-dark-600';
    }
  }

  protected stepList() {
    return stepActivities(this.progress()?.activities);
  }

  protected activityName(activity: { name?: string | null; activityNodeKey?: string | null }): string {
    return activityDisplayName(activity);
  }

  protected prettyNodeKey(key: string | null | undefined): string {
    if (!key) return '—';
    return key
      .replace(/([A-Z])/g, ' $1')
      .replace(/[_-]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
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

  protected openLiveGraph(): void {
    this.liveGraphOpen.set(true);
  }

  protected closeLiveGraph(): void {
    this.liveGraphOpen.set(false);
  }

  protected tabClass(id: RequestProgressTab): string {
    return this.tab() === id
      ? 'rounded-lg bg-white px-4 py-2 text-sm font-semibold text-ink-900 shadow-sm dark:bg-dark-800 dark:text-white'
      : 'rounded-lg px-4 py-2 text-sm font-medium text-ink-500 hover:text-ink-800 dark:text-dark-300';
  }
}
