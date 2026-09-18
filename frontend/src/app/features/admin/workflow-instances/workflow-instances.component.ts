import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowRuntimeService } from '@features/workflow/workflow-runtime.service';
import {
  TenantOption,
  AppContextService,
} from '@core/context/app-context.service';
import type { WorkflowInstanceDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-instance-dto';
import { PrvEmptyStateComponent, PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../../workflow/ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-instances',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    FormsModule,
    TranslatePipe,
    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-instances.component.html',
})
export class WorkflowInstancesComponent implements OnInit {
  private readonly runtimeService = inject(WorkflowRuntimeService);
  private readonly orgsService = inject(AppContextService);
  private readonly router = inject(Router);

  protected readonly items = signal<WorkflowInstanceDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly page = signal(1);
  protected readonly pageSize = 20;
  protected readonly orgs = signal<TenantOption[]>([]);
  protected readonly orgsLoading = signal(true);
  protected selectedOrgId = '';

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize))
  );

  ngOnInit(): void {
    this.orgsService.list().subscribe({
      next: orgs => {
        this.orgs.set(orgs);
        this.orgsLoading.set(false);
      },
      error: () => this.orgsLoading.set(false),
    });
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.runtimeService.adminGetInstances(
      this.page(),
      this.pageSize,
      this.selectedOrgId || undefined
    ).subscribe({
      next: result => {
        this.items.set(result.items ?? []);
        this.totalCount.set(result.totalCount ?? 0);
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string } };
        this.loadError.set(e?.error?.detail ?? e?.error?.title ?? 'Failed to load instances.');
        this.isLoading.set(false);
      },
    });
  }

  protected applyFilter(): void {
    this.page.set(1);
    this.load();
  }

  protected orgLabel(orgId: string | undefined): string {
    if (!orgId) return '—';
    return this.orgs().find(o => o.id === orgId)?.name ?? `${orgId.slice(0, 8)}…`;
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

  protected openDetail(id: string): void {
    this.router.navigate(['/admin/workflow/instances', id]);
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
}
