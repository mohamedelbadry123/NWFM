import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { WorkflowBindingsService } from '../../workflow/workflow-bindings.service';
import type { WorkflowBindingDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-binding-dto';
import type { WorkflowBindingMode } from '@shared/models/models/Workflow/Domain/Enums/workflow-binding-mode';
import { ToastService } from '@core/notifications/toast.service';
import { PrvEmptyStateComponent, PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../../workflow/ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../../workflow/ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-bindings-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TranslateModule,
    RouterLink,
    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-bindings-list.component.html',
})
export class WorkflowBindingsListComponent implements OnInit {
  private readonly service = inject(WorkflowBindingsService);
  private readonly toast      = inject(ToastService);
  private readonly translate  = inject(TranslateService);

  protected readonly items      = signal<WorkflowBindingDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly isLoading  = signal(true);
  protected readonly loadError  = signal<string | null>(null);
  protected readonly page       = signal(1);
  protected readonly pageSize   = signal(20);

  protected readonly confirmDeactivate = signal<WorkflowBindingDto | null>(null);
  protected readonly confirmActivate   = signal<WorkflowBindingDto | null>(null);
  protected readonly actioning         = signal(false);

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));
  protected readonly rangeStart = computed(() =>
    this.totalCount() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1);
  protected readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize(), this.totalCount()));

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.service.listAll(this.page(), this.pageSize()).subscribe({
      next: r => {
        this.items.set(r.items ?? []);
        this.totalCount.set(r.totalCount ?? 0);
        this.isLoading.set(false);
      },
      error: (err: { error?: { message?: string } }) => {
        this.loadError.set(err?.error?.message ?? 'Failed to load bindings.');
        this.isLoading.set(false);
      },
    });
  }

  protected openDeactivate(b: WorkflowBindingDto): void { this.confirmDeactivate.set(b); }
  protected closeDeactivate(): void { this.confirmDeactivate.set(null); }

  protected executeDeactivate(): void {
    const b = this.confirmDeactivate();
    if (!b?.workflowDefinitionId || !b.id || this.actioning()) return;
    this.actioning.set(true);
    this.service.deactivate(b.workflowDefinitionId, b.id).subscribe({
      next: () => {
        this.actioning.set(false);
        this.confirmDeactivate.set(null);
        this.toast.success('Binding deactivated.');
        this.load();
      },
      error: (err: { error?: { message?: string } }) => {
        this.actioning.set(false);
        this.confirmDeactivate.set(null);
        this.toast.error(err?.error?.message ?? 'Failed to deactivate binding.');
      },
    });
  }

  protected openActivate(b: WorkflowBindingDto): void { this.confirmActivate.set(b); }
  protected closeActivate(): void { this.confirmActivate.set(null); }

  protected executeActivate(): void {
    const b = this.confirmActivate();
    if (!b?.workflowDefinitionId || !b.id || this.actioning()) return;
    this.actioning.set(true);
    this.service.activate(b.workflowDefinitionId, b.id).subscribe({
      next: () => {
        this.actioning.set(false);
        this.confirmActivate.set(null);
        this.toast.success('Binding activated.');
        this.load();
      },
      error: (err: { error?: { message?: string; Message?: string; code?: string; Code?: string } }) => {
        this.actioning.set(false);
        this.confirmActivate.set(null);
        const code = err?.error?.code ?? err?.error?.Code;
        const translated = code ? this.translate.instant('error.' + code) : '';
        this.toast.error(
          (translated && translated !== 'error.' + code ? translated : null)
            ?? err?.error?.message
            ?? err?.error?.Message
            ?? 'Failed to activate binding.'
        );
      },
    });
  }

  protected modeTone(mode: WorkflowBindingMode | 'Paused' | null | undefined): PrvTone {
    switch (mode) {
      case 'Active':  return 'success';
      case 'Shadow':  return 'warning';
      case 'Paused':  return 'info';
      default:        return 'neutral';
    }
  }

  protected modeClass(mode: WorkflowBindingMode | 'Paused' | null | undefined): string {
    switch (mode) {
      case 'Active':  return 'bg-green-50 text-green-700 border-green-200 dark:bg-green-900/40 dark:text-green-400 dark:border-green-800/50';
      case 'Shadow':  return 'bg-amber-50 text-amber-800 border-amber-200 dark:bg-amber-900/40 dark:text-amber-400 dark:border-amber-800/50';
      case 'Paused':  return 'bg-orange-50 text-orange-800 border-orange-200 dark:bg-orange-900/40 dark:text-orange-400 dark:border-orange-800/50';
      default:        return 'bg-ink-100 text-ink-500 border-ink-200 dark:bg-dark-700 dark:text-dark-400 dark:border-dark-600';
    }
  }

  protected prevPage(): void { if (this.page() <= 1) return; this.page.update(p => p - 1); this.load(); }
  protected nextPage(): void { if (this.page() >= this.totalPages()) return; this.page.update(p => p + 1); this.load(); }

  protected formatDate(iso: string | null | undefined): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
