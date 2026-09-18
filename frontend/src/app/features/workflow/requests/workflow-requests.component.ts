import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowRuntimeService } from '../workflow-runtime.service';
import { WorkflowAssignmentGroupsService } from '../workflow-assignment-groups.service';
import { formatSlaDuration, remainingSlaTone } from '../workflow-sla.util';
import { workflowApiErrorKey } from '../workflow-api-error';
import type { WorkflowRequestKpiView, WorkflowRequestView } from '@core/models/workflow-ops.models';
import type { WorkflowAssignmentGroupDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-assignment-group-dto';
import { LocaleService } from '@core/i18n/locale.service';
import { PrvEmptyStateComponent, PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowKpiCardComponent } from '../ui/workflow-kpi-card.component';
import { WorkflowSlaClockComponent } from '../ui/workflow-sla-clock.component';
import { WorkflowLiveGraphDialogComponent } from '../ui/workflow-live-graph-dialog.component';
import { WorkflowTableShellComponent } from '../ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-requests',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    ReactiveFormsModule,
    TranslatePipe,
    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowKpiCardComponent,
    WorkflowSlaClockComponent,
    WorkflowLiveGraphDialogComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-requests.component.html',
})
export class WorkflowRequestsComponent implements OnInit {
  private readonly runtimeService = inject(WorkflowRuntimeService);
  private readonly groupsService = inject(WorkflowAssignmentGroupsService);
  private readonly router = inject(Router);
  private readonly locale = inject(LocaleService);
  private readonly fb = inject(FormBuilder);

  protected readonly items = signal<WorkflowRequestView[]>([]);
  protected readonly groups = signal<WorkflowAssignmentGroupDto[]>([]);
  protected readonly kpis = signal<WorkflowRequestKpiView>({});
  protected readonly totalCount = signal(0);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly page = signal(1);
  protected readonly pageSize = 10;
  protected readonly selectedId = signal<string | null>(null);
  protected readonly liveGraphInstanceId = signal<string | null>(null);
  protected readonly highlightBreached = signal(false);
  protected readonly sortBySla = signal(false);
  protected readonly formatSlaDuration = formatSlaDuration;
  protected readonly remainingSlaTone = remainingSlaTone;

  protected readonly filterForm = this.fb.nonNullable.group({
    search: [''],
    status: [''],
    service: [''],
    currentStep: [''],
    originalGroupId: [''],
    fromDate: [''],
    toDate: [''],
    slaStatus: [''],
  });

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize))
  );

  protected readonly selected = computed((): WorkflowRequestView | null => {
    const list = this.items();
    if (list.length === 0) return null;
    return list.find(i => i.id === this.selectedId()) ?? list[0];
  });

  protected selectedInstanceId(): string | null {
    return this.selected()?.workflowInstanceId ?? null;
  }

  protected readonly inProgressPct = computed(() => {
    const total = this.kpis().total ?? 0;
    if (total === 0) return '0';
    return (((this.kpis().inProgress ?? 0) / total) * 100).toFixed(1);
  });

  protected readonly completedPct = computed(() => {
    const total = this.kpis().total ?? 0;
    if (total === 0) return '0';
    return (((this.kpis().completed ?? 0) / total) * 100).toFixed(1);
  });

  protected readonly breachedPct = computed(() => {
    const total = this.kpis().total ?? 0;
    if (total === 0) return '0';
    return (((this.kpis().breached ?? 0) / total) * 100).toFixed(1);
  });

  ngOnInit(): void {
    this.groupsService.getPaged(1, 100).subscribe({
      next: page => this.groups.set(page.items ?? []),
      error: () => this.groups.set([]),
    });
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    const f = this.filterForm.getRawValue();
    this.runtimeService
      .listRequests({
        page: this.page(),
        pageSize: this.pageSize,
        search: f.search || null,
        status: f.status || null,
        service: f.service || null,
        currentStep: f.currentStep || null,
        originalGroupId: f.originalGroupId || null,
        fromUtc: f.fromDate ? `${f.fromDate}T00:00:00.000Z` : null,
        toUtc: f.toDate ? `${f.toDate}T23:59:59.000Z` : null,
        slaStatus: f.slaStatus || null,
        sortBy: this.sortBySla() ? 'remainingSla' : null,
      })
      .subscribe({
        next: result => {
          this.items.set(result.items ?? []);
          this.totalCount.set(result.totalCount ?? 0);
          this.kpis.set(result.kpis ?? {});
          this.isLoading.set(false);
        },
        error: (err: unknown) => {
          this.loadError.set(workflowApiErrorKey(err));
          this.isLoading.set(false);
        },
      });
  }

  protected applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  protected clearFilters(): void {
    this.filterForm.reset({
      search: '', status: '', service: '', currentStep: '',
      originalGroupId: '', fromDate: '', toDate: '', slaStatus: '',
    });
    this.sortBySla.set(false);
    this.highlightBreached.set(false);
    this.page.set(1);
    this.load();
  }

  protected toggleSortBySla(): void {
    this.sortBySla.update(v => !v);
    this.page.set(1);
    this.load();
  }

  protected toggleHighlightBreached(): void {
    this.highlightBreached.update(v => !v);
  }

  protected selectRow(item: WorkflowRequestView): void {
    this.selectedId.set(item.id ?? null);
  }

  protected prevPage(): void {
    if (this.page() <= 1) return;
    this.page.update(p => p - 1);
    this.load();
  }

  protected nextPage(): void {
    if (this.page() >= this.totalPages()) return;
    this.page.update(p => p + 1);
    this.load();
  }

  protected pageStart(): number {
    if (this.totalCount() === 0) return 0;
    return (this.page() - 1) * this.pageSize + 1;
  }

  protected pageEnd(): number {
    return Math.min(this.page() * this.pageSize, this.totalCount());
  }

  protected openProgress(item: WorkflowRequestView | null): void {
    const instanceId = item?.workflowInstanceId;
    if (!instanceId) return;
    this.router.navigate(['/org/workflow/requests', instanceId]);
  }

  protected exportCsv(): void {
    const rows = this.items();
    if (rows.length === 0) return;
    const escape = (value: string): string => `"${value.replace(/"/g, '""')}"`;
    const header = 'requestId,service,requestDate,status,currentStep,groupOwner,slaDuration,remainingSla';
    const lines = rows.map(r =>
      [
        r.requestNumber ?? '',
        this.serviceName(r),
        r.requestDate ?? '',
        r.status ?? '',
        this.currentStep(r),
        r.originalAssignedGroupName ?? '',
        formatSlaDuration(r.currentTaskSlaMinutes),
        String(r.remainingSlaMinutes ?? ''),
      ].map(v => escape(v)).join(',')
    );
    const csv = [header, ...lines].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'workflow-requests.csv';
    a.click();
    URL.revokeObjectURL(url);
  }

  protected serviceName(item: WorkflowRequestView): string {
    return this.locale.isRtl()
      ? (item.serviceNameAr || item.serviceNameEn || '—')
      : (item.serviceNameEn || item.serviceNameAr || '—');
  }

  protected currentStep(item: WorkflowRequestView): string {
    return this.locale.isRtl()
      ? (item.currentActivityNameAr || item.currentActivityNameEn || '—')
      : (item.currentActivityNameEn || item.currentActivityNameAr || '—');
  }

  protected groupLabel(group: WorkflowAssignmentGroupDto): string {
    return this.locale.isRtl()
      ? (group.nameAr || group.name || group.code || '—')
      : (group.name || group.nameAr || group.code || '—');
  }

  protected rowClass(item: WorkflowRequestView): string {
    const selected = this.selected()?.id === item.id ? 'bg-primary/10' : '';
    const breached = this.highlightBreached()
      && remainingSlaTone(item.remainingSlaMinutes, item.status) === 'breached'
      ? 'bg-red-900/20' : '';
    return `${selected} ${breached}`.trim();
  }

  protected displayStatus(status: string | undefined): string {
    switch (status) {
      case 'Running': return 'InProgress';
      case 'Suspended': return 'OnHold';
      default: return status ?? 'New';
    }
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

  protected openLiveGraph(event: Event, instanceId: string | undefined): void {
    event.stopPropagation();
    event.preventDefault();
    if (instanceId) this.liveGraphInstanceId.set(instanceId);
  }

  protected closeLiveGraph(): void {
    this.liveGraphInstanceId.set(null);
  }

  protected cardAccent(item: WorkflowRequestView): string {
    switch (remainingSlaTone(item.remainingSlaMinutes, item.status)) {
      case 'breached': return 'border-s-4 border-s-red-500';
      case 'warning':  return 'border-s-4 border-s-amber-400';
      default:         return 'border-s-4 border-s-primary/50';
    }
  }
}
