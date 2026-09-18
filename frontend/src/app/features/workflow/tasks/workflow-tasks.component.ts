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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { WorkflowWorkItemsService, type WorkflowWorkItemView } from '../workflow-work-items.service';
import { WorkflowWorkloadService } from '../workflow-workload.service';
import { formatSlaDuration, isOverdue, remainingSlaTone } from '../workflow-sla.util';
import { workflowApiErrorKey } from '../workflow-api-error';
import { LocaleService } from '@core/i18n/locale.service';
import { PrvEmptyStateComponent, PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowKpiCardComponent } from '../ui/workflow-kpi-card.component';
import { WorkflowSlaClockComponent } from '../ui/workflow-sla-clock.component';
import { WorkflowLiveGraphDialogComponent } from '../ui/workflow-live-graph-dialog.component';
import { WorkflowTableShellComponent } from '../ui/workflow-table-shell.component';

export type WorkflowTasksView = 'available' | 'claimedByMe' | 'overdue';

@Component({
  selector: 'app-workflow-tasks',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowKpiCardComponent,
    WorkflowSlaClockComponent,
    WorkflowLiveGraphDialogComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-tasks.component.html',
})
export class WorkflowTasksComponent implements OnInit {
  private readonly workItemsService = inject(WorkflowWorkItemsService);
  private readonly workloadService = inject(WorkflowWorkloadService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly locale = inject(LocaleService);
  private readonly fb = inject(FormBuilder);

  protected readonly view = signal<WorkflowTasksView>('available');
  protected readonly available = signal<WorkflowWorkItemView[]>([]);
  protected readonly claimed = signal<WorkflowWorkItemView[]>([]);
  protected readonly overdue = signal<WorkflowWorkItemView[]>([]);
  protected readonly completedToday = signal(0);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly claimingId = signal<string | null>(null);
  protected readonly claimError = signal<string | null>(null);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly liveGraphInstanceId = signal<string | null>(null);
  protected readonly page = signal(1);
  protected readonly pageSize = 10;

  protected readonly formatSlaDuration = formatSlaDuration;

  protected readonly filterForm = this.fb.nonNullable.group({
    search: [''],
    status: [''],
    slaStatus: [''],
    fromDate: [''],
    toDate: [''],
  });

  protected readonly appliedFilters = signal({
    search: '',
    status: '',
    slaStatus: '',
    fromDate: '',
    toDate: '',
  });

  protected readonly sourceItems = computed(() => {
    switch (this.view()) {
      case 'claimedByMe': return this.claimed();
      case 'overdue': return this.overdue();
      default: return this.available();
    }
  });

  protected readonly filteredItems = computed(() => {
    const { search, status, slaStatus, fromDate, toDate } = this.appliedFilters();
    const term = search.trim().toLowerCase();
    const fromMs = fromDate ? Date.parse(`${fromDate}T00:00:00`) : null;
    const toMs = toDate ? Date.parse(`${toDate}T23:59:59`) : null;
    return this.sourceItems().filter(item => {
      if (status && item.status !== status) return false;
      if (slaStatus) {
        const tone = remainingSlaTone(item.remainingSlaMinutes, item.status);
        if (slaStatus !== tone) return false;
      }
      if (fromMs != null && !Number.isNaN(fromMs)) {
        const requestMs = item.requestDate ? Date.parse(item.requestDate) : NaN;
        if (Number.isNaN(requestMs) || requestMs < fromMs) return false;
      }
      if (toMs != null && !Number.isNaN(toMs)) {
        const requestMs = item.requestDate ? Date.parse(item.requestDate) : NaN;
        if (Number.isNaN(requestMs) || requestMs > toMs) return false;
      }
      if (!term) return true;
      const hay = [
        item.requestNumber, item.serviceNameEn, item.serviceNameAr, item.assignmentGroupName,
      ].join(' ').toLowerCase();
      return hay.includes(term);
    });
  });

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.filteredItems().length / this.pageSize))
  );

  protected readonly pagedItems = computed(() => {
    const start = (this.page() - 1) * this.pageSize;
    return this.filteredItems().slice(start, start + this.pageSize);
  });

  protected readonly selected = computed((): WorkflowWorkItemView | null => {
    const page = this.pagedItems();
    if (page.length === 0) return null;
    return page.find(i => i.id === this.selectedId()) ?? page[0];
  });

  ngOnInit(): void {
    this.route.queryParamMap.subscribe(params => {
      const raw = params.get('view');
      const next: WorkflowTasksView =
        raw === 'claimedByMe' || raw === 'overdue' ? raw : 'available';
      this.view.set(next);
      this.page.set(1);
      this.load();
    });
  }

  protected viewTabClass(view: WorkflowTasksView): string {
    return this.view() === view
      ? 'rounded-lg bg-white px-3 py-1.5 text-xs font-semibold text-ink-900 shadow-sm dark:bg-dark-700 dark:text-white'
      : 'rounded-lg px-3 py-1.5 text-xs font-medium text-ink-500 hover:text-ink-800 dark:text-dark-300';
  }

  protected setView(view: WorkflowTasksView): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { view },
      queryParamsHandling: 'merge',
    });
  }

  protected applyFilters(): void {
    this.appliedFilters.set(this.filterForm.getRawValue());
    this.page.set(1);
  }

  protected resetFilters(): void {
    this.filterForm.reset({ search: '', status: '', slaStatus: '', fromDate: '', toDate: '' });
    this.appliedFilters.set(this.filterForm.getRawValue());
    this.page.set(1);
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.claimError.set(null);
    forkJoin({
      available: this.workItemsService.getAvailable(),
      claimed: this.workItemsService.getMyWorkItems(),
      overdue: this.workItemsService.getOverdue(),
      workload: this.workloadService.getWorkload(),
    }).subscribe({
      next: result => {
        this.available.set(result.available ?? []);
        this.claimed.set(result.claimed ?? []);
        this.overdue.set(result.overdue ?? []);
        this.completedToday.set(result.workload.totals?.completedToday ?? 0);
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        this.loadError.set(workflowApiErrorKey(err));
        this.isLoading.set(false);
      },
    });
  }

  protected selectRow(item: WorkflowWorkItemView): void {
    this.selectedId.set(item.id ?? null);
  }

  protected openTask(id: string | undefined): void {
    if (!id) return;
    this.router.navigate(['/org/workflow/tasks', id]);
  }

  protected claim(workItemId: string | undefined, event?: Event): void {
    event?.stopPropagation();
    if (!workItemId) return;
    this.claimingId.set(workItemId);
    this.claimError.set(null);
    this.workItemsService.claim(workItemId).subscribe({
      next: () => {
        this.claimingId.set(null);
        this.router.navigate(['/org/workflow/tasks', workItemId]);
      },
      error: (err: unknown) => {
        this.claimingId.set(null);
        this.claimError.set(workflowApiErrorKey(err));
        this.load();
      },
    });
  }

  protected releaseSelected(): void {
    const id = this.selected()?.id;
    if (!id) return;
    this.workItemsService.release(id).subscribe({
      next: () => this.load(),
      error: (err: unknown) => this.claimError.set(workflowApiErrorKey(err)),
    });
  }

  protected serviceName(item: WorkflowWorkItemView): string {
    return this.locale.isRtl()
      ? (item.serviceNameAr || item.serviceNameEn || '—')
      : (item.serviceNameEn || item.serviceNameAr || '—');
  }

  protected currentStep(item: WorkflowWorkItemView): string {
    return this.locale.isRtl()
      ? (item.currentStepNameAr || item.currentStepNameEn || item.assignmentGroupName || '—')
      : (item.currentStepNameEn || item.currentStepNameAr || item.assignmentGroupName || '—');
  }

  protected displayStatus(item: WorkflowWorkItemView): string {
    if (isOverdue(item.dueAt, item.status) && item.status !== 'Completed') return 'Overdue';
    if (item.status === 'Claimed') return 'ClaimedByMe';
    if (item.status === 'Pending') return 'Available';
    return item.status ?? 'Pending';
  }

  protected statusTone(item: WorkflowWorkItemView): PrvTone {
    const status = this.displayStatus(item);
    switch (status) {
      case 'ClaimedByMe': return 'info';
      case 'Completed':   return 'success';
      case 'Overdue':     return 'danger';
      case 'Cancelled':   return 'danger';
      default:            return 'warning';
    }
  }

  protected selectedWorkItemId(): string | undefined {
    return this.selected()?.id;
  }

  protected selectedStatus(): string | undefined {
    return this.selected()?.status;
  }

  protected rowSelectedClass(item: WorkflowWorkItemView): string {
    return this.selected()?.id === item.id ? 'bg-primary/10' : '';
  }

  protected pageStart(): number {
    if (this.filteredItems().length === 0) return 0;
    return (this.page() - 1) * this.pageSize + 1;
  }

  protected pageEnd(): number {
    return Math.min(this.page() * this.pageSize, this.filteredItems().length);
  }

  protected prevPage(): void {
    if (this.page() <= 1) return;
    this.page.update(p => p - 1);
  }

  protected nextPage(): void {
    if (this.page() >= this.totalPages()) return;
    this.page.update(p => p + 1);
  }

  protected openLiveGraph(event: Event, instanceId: string | undefined): void {
    event.stopPropagation();
    event.preventDefault();
    if (instanceId) this.liveGraphInstanceId.set(instanceId);
  }

  protected closeLiveGraph(): void {
    this.liveGraphInstanceId.set(null);
  }

  protected cardAccent(item: WorkflowWorkItemView): string {
    switch (remainingSlaTone(item.remainingSlaMinutes, item.status)) {
      case 'breached': return 'border-s-4 border-s-red-500';
      case 'warning':  return 'border-s-4 border-s-amber-400';
      default:         return 'border-s-4 border-s-primary/50';
    }
  }
}
