import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { PrvEmptyStateComponent, PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../ui/workflow-table-shell.component';
import {
  WorkflowNotificationsService,
  type WorkflowNotificationLogView,
} from '../workflow-notifications.service';

@Component({
  selector: 'app-workflow-notifications',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    TranslateModule,
    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-notifications.component.html',
})
export class WorkflowNotificationsComponent implements OnInit {
  private readonly api = inject(WorkflowNotificationsService);

  protected readonly items = signal<WorkflowNotificationLogView[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 20;
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(page = 1): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.page.set(page);
    this.api.list(page, this.pageSize).subscribe({
      next: res => {
        this.items.set(res.items ?? []);
        this.totalCount.set(res.totalCount ?? 0);
        this.isLoading.set(false);
      },
      error: () => {
        this.loadError.set('workflow.notifications.error_load');
        this.isLoading.set(false);
      },
    });
  }

  protected statusTone(status: string): PrvTone {
    switch (status) {
      case 'Delivered':
        return 'success';
      case 'Queued':
        return 'warning';
      case 'Failed':
        return 'danger';
      default:
        return 'neutral';
    }
  }

  protected totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount() / this.pageSize));
  }
}
