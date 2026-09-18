import {
  ChangeDetectionStrategy,
  Component,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { ToastService } from '@core/notifications/toast.service';
import type { WorkflowIntegrationMessageDto } from '@core/models/workflow-ops.models';
import { WorkflowDeadLettersService } from './workflow-dead-letters.service';
import { PrvEmptyStateComponent } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../../workflow/ui/workflow-table-shell.component';

type DeadLetterTab = 'inbox' | 'outbox';

@Component({
  selector: 'app-workflow-dead-letters',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    TranslatePipe,
    RouterLink,
    PrvEmptyStateComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-dead-letters.component.html',
})
export class WorkflowDeadLettersComponent implements OnInit {
  private readonly service = inject(WorkflowDeadLettersService);
  private readonly toast = inject(ToastService);

  protected readonly activeTab = signal<DeadLetterTab>('inbox');
  protected readonly items = signal<WorkflowIntegrationMessageDto[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly replayingId = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected setTab(tab: DeadLetterTab): void {
    if (this.activeTab() === tab) return;
    this.activeTab.set(tab);
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    const req = this.activeTab() === 'inbox'
      ? this.service.listInboxDeadLetters()
      : this.service.listFailedOutbox();

    req.subscribe({
      next: page => {
        this.items.set(page?.items ?? []);
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        const e = err as { error?: { detail?: string; title?: string; message?: string } };
        this.loadError.set(
          e?.error?.detail ?? e?.error?.title ?? e?.error?.message ?? 'Failed to load messages.'
        );
        this.isLoading.set(false);
      },
    });
  }

  protected replay(row: WorkflowIntegrationMessageDto): void {
    const id = row.id;
    if (!id || this.replayingId()) return;
    this.replayingId.set(id);
    const req = this.activeTab() === 'inbox'
      ? this.service.replayInbox(id)
      : this.service.replayOutbox(id);

    req.subscribe({
      next: () => {
        this.replayingId.set(null);
        this.toast.success('Message queued for replay.');
        this.load();
      },
      error: (err: unknown) => {
        this.replayingId.set(null);
        const e = err as { error?: { message?: string; detail?: string } };
        this.toast.error(e?.error?.detail ?? e?.error?.message ?? 'Replay failed.');
      },
    });
  }

  protected entityLabel(row: WorkflowIntegrationMessageDto): string {
    const type = row.businessEntityType ?? '—';
    const id = row.businessEntityId;
    return id ? `${type} · ${id}` : type;
  }
}
