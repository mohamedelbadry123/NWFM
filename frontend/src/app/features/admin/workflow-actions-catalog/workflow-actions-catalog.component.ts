import {
  ChangeDetectionStrategy,
  Component,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowActionsCatalogService } from './workflow-actions-catalog.service';
import type { WorkflowActionCatalogEntryDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-action-catalog-entry-dto';
import { PrvEmptyStateComponent } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../../workflow/ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-actions-catalog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TranslatePipe,
    PrvEmptyStateComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-actions-catalog.component.html',
})
export class WorkflowActionsCatalogComponent implements OnInit {
  private readonly service = inject(WorkflowActionsCatalogService);

  protected readonly items = signal<WorkflowActionCatalogEntryDto[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  ngOnInit(): void {
    this.service.list().subscribe({
      next: items => {
        this.items.set(items ?? []);
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string } };
        this.loadError.set(e?.error?.detail ?? e?.error?.title ?? 'Failed to load actions catalog.');
        this.isLoading.set(false);
      },
    });
  }
}
