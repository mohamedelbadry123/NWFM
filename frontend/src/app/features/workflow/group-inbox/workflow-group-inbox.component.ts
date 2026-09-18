import {
  ChangeDetectionStrategy,
  Component,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowWorkItemsService } from '../workflow-work-items.service';
import { WorkflowAssignmentGroupsService } from '../workflow-assignment-groups.service';
import { dueRelativeLabel, isOverdue } from '../workflow-sla.util';
import type { WorkItemDto } from '@shared/models/models/Workflow/Application/DTOs/work-item-dto';
import type { WorkflowAssignmentGroupDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-assignment-group-dto';
import { PrvEmptyStateComponent, PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-group-inbox',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    TranslatePipe,
    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-group-inbox.component.html',
})
export class WorkflowGroupInboxComponent implements OnInit {
  private readonly workItemsService = inject(WorkflowWorkItemsService);
  private readonly groupsService = inject(WorkflowAssignmentGroupsService);
  private readonly router = inject(Router);

  protected readonly groups = signal<WorkflowAssignmentGroupDto[]>([]);
  protected readonly selectedGroupId = signal<string | null>(null);
  protected readonly items = signal<WorkItemDto[]>([]);
  protected readonly isLoadingGroups = signal(true);
  protected readonly isLoadingItems = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly claimingId = signal<string | null>(null);

  protected readonly isOverdue = isOverdue;
  protected readonly dueRelativeLabel = dueRelativeLabel;

  ngOnInit(): void {
    this.groupsService.getPaged(1, 100).subscribe({
      next: result => {
        this.groups.set(result.items ?? []);
        this.isLoadingGroups.set(false);
        if (result.items && result.items.length > 0) {
          this.selectGroup(result.items[0].id!);
        }
      },
      error: () => this.isLoadingGroups.set(false),
    });
  }

  protected selectGroup(groupId: string): void {
    this.selectedGroupId.set(groupId);
    this.isLoadingItems.set(true);
    this.loadError.set(null);
    this.workItemsService.getGroupWorkItems(groupId).subscribe({
      next: items => {
        this.items.set(items ?? []);
        this.isLoadingItems.set(false);
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string } };
        this.loadError.set(e?.error?.detail ?? e?.error?.title ?? 'Failed to load group inbox.');
        this.isLoadingItems.set(false);
      },
    });
  }

  protected claim(workItemId: string, event: MouseEvent): void {
    event.stopPropagation();
    this.claimingId.set(workItemId);
    this.workItemsService.claim(workItemId).subscribe({
      next: () => {
        this.claimingId.set(null);
        this.router.navigate(['/org/workflow/tasks', workItemId]);
      },
      error: () => this.claimingId.set(null),
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

  protected statusBadgeClass(status: string | undefined): string {
    switch (status) {
      case 'Claimed':    return 'bg-primary/10 text-primary border border-primary/30';
      case 'Completed':  return 'bg-green-900/40 text-green-400 border border-green-800/50';
      case 'Cancelled':  return 'bg-red-900/40 text-red-400 border border-red-800/50';
      default:           return 'bg-amber-900/40 text-amber-400 border border-amber-800/50';
    }
  }
}
