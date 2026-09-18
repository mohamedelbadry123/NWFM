import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { WorkflowDefinitionsService } from '../workflow-definitions.service';
import { WorkflowVersionsService } from '../workflow-versions.service';
import { AppContextService } from '@core/context/app-context.service';
import type { TenantOption } from '@core/context/app-context.service';
import type { WorkflowDefinitionDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-definition-dto';
import { ToastService } from '@core/notifications/toast.service';
import { PrvEmptyStateComponent, PrvStatusPillComponent } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowKpiCardComponent } from '../ui/workflow-kpi-card.component';
import { WorkflowTableShellComponent } from '../ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-definitions',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslateModule,
    RouterLink,
    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowKpiCardComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-definitions.component.html',
})
export class WorkflowDefinitionsComponent implements OnInit {
  private readonly service    = inject(WorkflowDefinitionsService);
  private readonly versions   = inject(WorkflowVersionsService);
  private readonly toast      = inject(ToastService);
  private readonly fb         = inject(FormBuilder);
  private readonly router     = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly orgsService = inject(AppContextService);
  protected readonly openingDesignerId = signal<string | null>(null);
  protected readonly orgs = signal<TenantOption[]>([]);
  protected readonly selectedOrgId = signal('');
  protected readonly orgsLoading = signal(false);
  protected readonly needsOrg = computed(() => false);

  // ── Seeded example constants ──────────────────────────────────────────────

  protected copyGuid(id: string): void {
    navigator.clipboard.writeText(id);
  }

  protected readonly items      = signal<WorkflowDefinitionDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly isLoading  = signal(true);
  protected readonly loadError  = signal<string | null>(null);
  protected readonly page       = signal(1);
  protected readonly pageSize   = signal(20);

  protected readonly showCreateModal = signal(false);
  protected readonly saving          = signal(false);

  protected readonly confirmTarget   = signal<WorkflowDefinitionDto | null>(null);
  protected readonly toggling        = signal(false);

  protected readonly createForm = this.fb.group({
    organizationId:[''],
    definitionKey: ['', [Validators.required, Validators.maxLength(100),
      Validators.pattern(/^[A-Za-z0-9\-_]+$/)]],
    name:          ['', [Validators.required, Validators.maxLength(200)]],
    nameAr:        ['', Validators.maxLength(200)],
    description:   ['', Validators.maxLength(1000)],
    descriptionAr: ['', Validators.maxLength(1000)],
  });

  protected readonly searchCtrl = this.fb.control('');

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));
  protected readonly rangeStart = computed(() =>
    this.totalCount() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1);
  protected readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize(), this.totalCount()));

  protected readonly activeCount   = computed(() => this.items().filter(d => d.isActive).length);
  protected readonly inactiveCount = computed(() => this.items().filter(d => !d.isActive).length);
  protected readonly totalVersions = computed(() => this.items().reduce((sum, d) => sum + (d.versionCount ?? 0), 0));

  ngOnInit(): void {
    this.load();
    this.searchCtrl.valueChanges.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(() => { this.page.set(1); this.load(); });
  }

  protected onOrgFilterChange(orgId: string): void {
    this.selectedOrgId.set(orgId);
    this.page.set(1);
    this.load();
  }

  protected load(): void {
    if (this.needsOrg()) {
      this.items.set([]);
      this.totalCount.set(0);
      this.isLoading.set(false);
      this.loadError.set(null);
      return;
    }
    this.isLoading.set(true);
    this.loadError.set(null);
    const search = this.searchCtrl.value?.trim() || undefined;
    const organizationId = undefined;
    this.service.getPaged(this.page(), this.pageSize(), search, organizationId).subscribe({
      next: r => { this.items.set(r.items ?? []); this.totalCount.set(r.totalCount ?? 0); this.isLoading.set(false); },
      error: (err: { error?: { message?: string } }) => {
        this.loadError.set(err?.error?.message ?? 'Failed to load.');
        this.isLoading.set(false);
      },
    });
  }

  protected openCreate(): void {
    if (this.needsOrg()) return;
    this.createForm.reset();
    if (this.selectedOrgId()) {
      this.createForm.patchValue({ organizationId: this.selectedOrgId() });
    }
    this.showCreateModal.set(true);
  }

  protected closeCreate(): void { this.showCreateModal.set(false); }

  protected submitCreate(): void {
    if (this.createForm.invalid || this.saving()) return;
    this.saving.set(true);
    const v = this.createForm.value;
    this.service.create({
      organizationId: undefined,
      definitionKey: v.definitionKey!,
      name: v.name!,
      nameAr: v.nameAr || undefined,
      description: v.description || undefined,
      descriptionAr: v.descriptionAr || undefined,
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.showCreateModal.set(false);
        this.toast.success('Workflow definition created.');
        this.load();
      },
      error: (err: { error?: { message?: string } }) => {
        this.saving.set(false);
        this.toast.error(err?.error?.message ?? 'Failed to create definition.');
      },
    });
  }

  protected confirmToggle(d: WorkflowDefinitionDto): void { this.confirmTarget.set(d); }
  protected cancelToggle(): void { this.confirmTarget.set(null); }

  protected executeToggle(): void {
    const d = this.confirmTarget();
    if (!d || this.toggling()) return;
    this.toggling.set(true);
    if (!d.id) return;
    const call = d.isActive ? this.service.deactivate(d.id) : this.service.activate(d.id);
    call.subscribe({
      next: () => {
        this.toggling.set(false);
        this.confirmTarget.set(null);
        this.toast.success(`Definition ${d.isActive ? 'deactivated' : 'activated'}.`);
        this.load();
      },
      error: (err: { error?: { message?: string } }) => {
        this.toggling.set(false);
        this.confirmTarget.set(null);
        this.toast.error(err?.error?.message ?? 'Failed to update definition.');
      },
    });
  }

  protected prevPage(): void { if (this.page() <= 1) return; this.page.update(p => p - 1); this.load(); }
  protected nextPage(): void { if (this.page() >= this.totalPages()) return; this.page.update(p => p + 1); this.load(); }
  protected goToPage(p: number): void { if (p === this.page()) return; this.page.set(p); this.load(); }

  protected openLatestDraftDesigner(d: WorkflowDefinitionDto): void {
    if (!d.id || this.openingDesignerId()) return;
    this.openingDesignerId.set(d.id);
    this.versions.getPaged(d.id, 1, 100).subscribe({
      next: r => {
        this.openingDesignerId.set(null);
        const items = r.items ?? [];
        const drafts = items
          .filter(v => v.status === 'Draft')
          .sort((a, b) => (b.versionNumber ?? 0) - (a.versionNumber ?? 0));
        const latestDraft = drafts[0];
        if (latestDraft?.id) {
          void this.router.navigate([
            '/admin/workflow/definitions', d.id, 'versions', latestDraft.id, 'designer',
          ]);
          return;
        }
        void this.router.navigate(['/admin/workflow/definitions', d.id, 'versions']);
        this.toast.warning('No draft version found. Opening versions list.');
      },
      error: (err: { error?: { message?: string } }) => {
        this.openingDesignerId.set(null);
        this.toast.error(err?.error?.message ?? 'Failed to open designer.');
      },
    });
  }

  protected formatDate(iso: string | undefined): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
