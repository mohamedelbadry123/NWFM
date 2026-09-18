import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowIncidentsService } from './workflow-incidents.service';
import type { WorkflowIncidentDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-incident-dto';
import type { WorkflowIncidentStatus } from '@shared/models/models/Workflow/Domain/Enums/workflow-incident-status';
import type { WorkflowIncidentSeverity } from '@shared/models/models/Workflow/Domain/Enums/workflow-incident-severity';
import { PrvEmptyStateComponent, PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../../workflow/ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-incidents',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    TranslatePipe,
    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-incidents.component.html',
})
export class WorkflowIncidentsComponent implements OnInit {
  private readonly service = inject(WorkflowIncidentsService);
  private readonly router = inject(Router);

  protected readonly items = signal<WorkflowIncidentDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly page = signal(1);
  protected readonly pageSize = 20;
  protected severityFilter = '';
  protected statusFilter = '';

  protected readonly severityOptions: WorkflowIncidentSeverity[] = ['Low', 'Medium', 'High', 'Critical'];
  protected readonly statusOptions: WorkflowIncidentStatus[] = ['Open', 'InProgress', 'Resolved', 'Ignored'];

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize))
  );

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.service.list({
      page: this.page(),
      pageSize: this.pageSize,
      status: (this.statusFilter || undefined) as WorkflowIncidentStatus | undefined,
    }).subscribe({
      next: result => {
        let items = result.items ?? [];
        if (this.severityFilter) {
          items = items.filter(i => i.severity === this.severityFilter);
        }
        this.items.set(items);
        this.totalCount.set(result.totalCount ?? items.length);
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string } };
        this.loadError.set(e?.error?.detail ?? e?.error?.title ?? 'Failed to load incidents.');
        this.isLoading.set(false);
      },
    });
  }

  protected applyFilters(): void {
    this.page.set(1);
    this.load();
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

  protected openDetail(id: string | undefined): void {
    if (!id) return;
    this.router.navigate(['/admin/workflow/incidents', id]);
  }

  protected severityTone(severity: string | undefined): PrvTone {
    switch (severity) {
      case 'Critical': return 'danger';
      case 'High':     return 'warning';
      case 'Medium':   return 'info';
      default:         return 'neutral';
    }
  }

  protected severityClass(severity: string | undefined): string {
    switch (severity) {
      case 'Critical': return 'text-red-700 bg-red-50 dark:text-red-400 dark:bg-red-900/20';
      case 'High':     return 'text-orange-800 bg-orange-50 dark:text-orange-400 dark:bg-orange-900/20';
      case 'Medium':   return 'text-amber-800 bg-amber-50 dark:text-amber-400 dark:bg-amber-900/20';
      default:         return 'text-ink-500 bg-ink-100 dark:text-dark-300 dark:bg-dark-700/50';
    }
  }
}
