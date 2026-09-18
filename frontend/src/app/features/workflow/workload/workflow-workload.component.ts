import {
  ChangeDetectionStrategy,
  Component,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import type { WorkflowWorkloadDto, WorkflowWorkloadGroupDto } from '@core/models/workflow-ops.models';
import { WorkflowWorkloadService } from '../workflow-workload.service';
import { PrvEmptyStateComponent } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowKpiCardComponent } from '../ui/workflow-kpi-card.component';
import { WorkflowTableShellComponent } from '../ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-workload',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TranslatePipe,
    RouterLink,
    PrvEmptyStateComponent,
    WorkflowPageHeaderComponent,
    WorkflowKpiCardComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-workload.component.html',
})
export class WorkflowWorkloadComponent implements OnInit {
  private readonly service = inject(WorkflowWorkloadService);
  private readonly router = inject(Router);

  protected helpLink(): string {
    return this.router.url.startsWith('/admin')
      ? '/admin/workflow/help'
      : '/org/workflow/help';
  }

  protected readonly data = signal<WorkflowWorkloadDto | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.service.getWorkload().subscribe({
      next: dto => {
        this.data.set(dto ?? {});
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string; message?: string } };
        this.loadError.set(
          e?.error?.detail ?? e?.error?.title ?? e?.error?.message ?? 'Failed to load workload.'
        );
        this.isLoading.set(false);
      },
    });
  }

  protected groups(): WorkflowWorkloadGroupDto[] {
    return this.data()?.groups ?? [];
  }
}
