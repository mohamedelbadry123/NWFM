import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowWorkItemsService } from '../workflow-work-items.service';
import { dueRelativeLabel, isOverdue } from '../workflow-sla.util';
import type { WorkItemDto } from '@shared/models/models/Workflow/Application/DTOs/work-item-dto';
import { PrvEmptyStateComponent, PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowKpiCardComponent } from '../ui/workflow-kpi-card.component';
import { WorkflowTableShellComponent } from '../ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-my-tasks',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    TranslatePipe,
    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowKpiCardComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-my-tasks.component.html',
})
export class WorkflowMyTasksComponent implements OnInit {
  private readonly workItemsService = inject(WorkflowWorkItemsService);
  private readonly router = inject(Router);

  protected readonly items = signal<WorkItemDto[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly claimingId = signal<string | null>(null);

  protected readonly isOverdue = isOverdue;
  protected readonly dueRelativeLabel = dueRelativeLabel;

  protected readonly overdueCount  = computed(() => this.items().filter(i => isOverdue(i.dueAt, i.status)).length);
  protected readonly claimedCount  = computed(() => this.items().filter(i => i.status === 'Claimed').length);
  protected readonly pendingCount  = computed(() => this.items().filter(i => i.status !== 'Completed' && i.status !== 'Cancelled').length);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.workItemsService.getMyWorkItems().subscribe({
      next: items => {
        this.items.set(items ?? []);
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string } };
        this.loadError.set(e?.error?.detail ?? e?.error?.title ?? 'Failed to load tasks.');
        this.isLoading.set(false);
      },
    });
  }

  protected openTask(id: string): void {
    this.router.navigate(['/org/workflow/tasks', id]);
  }

  protected statusTone(status: string | undefined): PrvTone {
    switch (status) {
      case 'Claimed':    return 'info';
      case 'Completed':  return 'success';
      case 'Cancelled':  return 'danger';
      default:           return 'warning';
    }
  }

  protected statusBadgeClass(status: string | undefined): string {
    switch (status) {
      case 'Claimed':    return 'bg-primary/10 text-primary border border-primary/30';
      case 'Completed':  return 'bg-green-900/40 text-green-400 border border-green-800/50';
      case 'Cancelled':  return 'bg-red-900/40 text-red-400 border border-red-800/50';
      default:           return 'bg-amber-900/40 text-amber-400 border border-amber-800/50';
    }
  }
}
