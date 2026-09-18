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
import { ReactiveFormsModule, FormBuilder } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { workflowApiErrorKey, workflowApiErrorMessage, workflowApiErrorStatus } from '../workflow-api-error';
import { WorkflowVersionsService, type WorkflowPublishPreviewView } from '../workflow-versions.service';
import { WorkflowDefinitionsService } from '../workflow-definitions.service';
import type { WorkflowVersionDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-version-dto';
import type { WorkflowDefinitionDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-definition-dto';
import { ToastService } from '@core/notifications/toast.service';
import { PrvEmptyStateComponent, PrvStatusPillComponent, type PrvTone } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-versions',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslateModule,
    RouterLink,
    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-versions.component.html',
})
export class WorkflowVersionsComponent implements OnInit {
  private readonly route         = inject(ActivatedRoute);
  private readonly versionsService    = inject(WorkflowVersionsService);
  private readonly definitionsService = inject(WorkflowDefinitionsService);
  private readonly toast         = inject(ToastService);
  private readonly translate     = inject(TranslateService);
  private readonly fb            = inject(FormBuilder);
  private readonly destroyRef    = inject(DestroyRef);

  protected readonly definitionId = signal('');
  protected readonly definition   = signal<WorkflowDefinitionDto | null>(null);

  protected readonly items      = signal<WorkflowVersionDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly isLoading  = signal(true);
  protected readonly loadError  = signal<string | null>(null);
  protected readonly page       = signal(1);
  protected readonly pageSize   = signal(20);

  protected readonly showDraftModal = signal(false);
  protected readonly showCloneModal = signal(false);
  protected readonly cloneTargetId  = signal<string | null>(null);
  protected readonly saving         = signal(false);
  protected readonly draftError     = signal<string | null>(null);

  protected readonly confirmPublishTarget = signal<WorkflowVersionDto | null>(null);
  protected readonly publishPreview       = signal<WorkflowPublishPreviewView | null>(null);
  protected readonly confirmRetireTarget  = signal<WorkflowVersionDto | null>(null);
  protected readonly actioning            = signal(false);

  protected readonly draftForm = this.fb.group({ changeSummary: [''] });
  protected readonly cloneForm = this.fb.group({ changeSummary: [''] });

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));
  protected readonly rangeStart = computed(() =>
    this.totalCount() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1);
  protected readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize(), this.totalCount()));

  protected readonly hasPublishedVersion = computed(() => this.items().some(v => v.status === 'Published'));
  protected readonly hasDraftVersion     = computed(() => this.items().some(v => v.status === 'Draft'));
  protected readonly draftNeedsValidation = computed(() =>
    this.items().some(v => v.status === 'Draft' && v.validationStatus !== 'Valid'));
  protected readonly draftReadyToPublish  = computed(() =>
    this.items().some(v => v.status === 'Draft' && v.validationStatus === 'Valid'));

  protected readonly guidedNextStep = computed((): { key: string; style: string } | null => {
    if (this.items().length === 0)
      return { key: 'workflow.versions.guide_create_draft', style: 'blue' };
    if (this.draftNeedsValidation())
      return { key: 'workflow.versions.guide_validate_draft', style: 'amber' };
    if (this.draftReadyToPublish())
      return { key: 'workflow.versions.guide_publish_draft', style: 'green' };
    if (this.hasPublishedVersion() && !this.hasDraftVersion())
      return { key: 'workflow.versions.guide_clone_for_new_version', style: 'purple' };
    return null;
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('definitionId') ?? '';
    this.definitionId.set(id);
    this.loadDefinition(id);
    this.load();
  }

  private loadDefinition(id: string): void {
    this.definitionsService.getById(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: d => this.definition.set(d),
        error: () => {},
      });
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.versionsService.getPaged(this.definitionId(), this.page(), this.pageSize())
      .subscribe({
        next: r => {
          this.items.set(r.items ?? []);
          this.totalCount.set(r.totalCount ?? 0);
          this.isLoading.set(false);
        },
        error: (err: unknown) => {
          this.loadError.set(workflowApiErrorMessage(err, this.translate, 'workflow.versions.error_load'));
          this.isLoading.set(false);
        },
      });
  }

  protected openDraftModal(): void {
    this.draftForm.reset();
    this.draftError.set(null);
    this.showDraftModal.set(true);
  }

  protected closeDraftModal(): void { this.showDraftModal.set(false); }

  protected submitDraft(): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.draftError.set(null);
    const summary = this.draftForm.value.changeSummary || undefined;
    this.versionsService.createDraft(this.definitionId(), summary).subscribe({
      next: () => {
        this.saving.set(false);
        this.showDraftModal.set(false);
        this.toast.success(this.translate.instant('workflow.versions.toast_draft_created'));
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        if (workflowApiErrorStatus(err) === 403) return;
        const key = workflowApiErrorKey(err, 'workflow.versions.error_create_draft');
        const message = workflowApiErrorMessage(err, this.translate, 'workflow.versions.error_create_draft');
        this.draftError.set(message);
        this.toast.error(message);
        if (key === 'error.Workflow.Version.DraftAlreadyExists') this.load();
      },
    });
  }

  protected openCloneModal(v: WorkflowVersionDto): void {
    if (!v.id) return;
    this.cloneTargetId.set(v.id);
    this.cloneForm.reset();
    this.showCloneModal.set(true);
  }

  protected closeCloneModal(): void { this.showCloneModal.set(false); }

  protected submitClone(): void {
    const targetId = this.cloneTargetId();
    if (!targetId || this.saving()) return;
    this.saving.set(true);
    const summary = this.cloneForm.value.changeSummary || undefined;
    this.versionsService.clone(this.definitionId(), targetId, summary).subscribe({
      next: () => {
        this.saving.set(false);
        this.showCloneModal.set(false);
        this.toast.success(this.translate.instant('workflow.versions.toast_cloned'));
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        if (workflowApiErrorStatus(err) === 403) return;
        this.toast.error(workflowApiErrorMessage(err, this.translate, 'workflow.versions.error_clone'));
      },
    });
  }

  protected validate(v: WorkflowVersionDto): void {
    if (!v.id) return;
    this.versionsService.validate(this.definitionId(), v.id).subscribe({
      next: result => {
        if (result.isValid) {
          this.toast.success('Validation passed — no issues found.');
        } else {
          const count = result.errors?.length ?? 0;
          this.toast.error(`Validation failed: ${count} error(s).`);
        }
        this.load();
      },
      error: (err: unknown) =>
        this.toast.error(workflowApiErrorMessage(err, this.translate, 'workflow.versions.error_validate')),
    });
  }

  protected confirmPublish(v: WorkflowVersionDto): void {
    this.confirmPublishTarget.set(v);
    this.publishPreview.set(null);
    if (!v.id) return;
    this.versionsService.getPublishPreview(this.definitionId(), v.id).subscribe({
      next: preview => this.publishPreview.set(preview),
      error: () => this.publishPreview.set(null),
    });
  }
  protected cancelPublish(): void {
    this.confirmPublishTarget.set(null);
    this.publishPreview.set(null);
  }

  protected executePublish(): void {
    const v = this.confirmPublishTarget();
    if (!v?.id || this.actioning()) return;
    const preview = this.publishPreview();
    if (preview && !preview.canPublish) {
      this.toast.error(preview.blockingReasons[0] ?? this.translate.instant('workflow.versions.error_publish'));
      return;
    }
    this.actioning.set(true);
    this.versionsService.publish(this.definitionId(), v.id).subscribe({
      next: () => {
        this.actioning.set(false);
        this.confirmPublishTarget.set(null);
        this.publishPreview.set(null);
        this.toast.success(`Version v${v.versionNumber} published.`);
        this.load();
      },
      error: (err: unknown) => {
        this.actioning.set(false);
        this.confirmPublishTarget.set(null);
        this.publishPreview.set(null);
        this.toast.error(workflowApiErrorMessage(err, this.translate, 'workflow.versions.error_publish'));
      },
    });
  }

  protected confirmRetire(v: WorkflowVersionDto): void {
    this.confirmRetireTarget.set(v);
  }
  protected cancelRetire(): void { this.confirmRetireTarget.set(null); }

  protected executeRetire(): void {
    const v = this.confirmRetireTarget();
    if (!v?.id || this.actioning()) return;
    this.actioning.set(true);
    this.versionsService.retire(this.definitionId(), v.id).subscribe({
      next: () => {
        this.actioning.set(false);
        this.confirmRetireTarget.set(null);
        this.toast.success(`Version v${v.versionNumber} retired.`);
        this.load();
      },
      error: (err: unknown) => {
        this.actioning.set(false);
        this.confirmRetireTarget.set(null);
        this.toast.error(workflowApiErrorMessage(err, this.translate, 'workflow.versions.error_retire'));
      },
    });
  }

  protected prevPage(): void { if (this.page() <= 1) return; this.page.update(p => p - 1); this.load(); }
  protected nextPage(): void { if (this.page() >= this.totalPages()) return; this.page.update(p => p + 1); this.load(); }

  protected formatDate(iso: string | null | undefined): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  protected statusTone(status: string | null | undefined): PrvTone {
    switch (status) {
      case 'Published': return 'success';
      case 'Retired':   return 'neutral';
      default:          return 'warning';
    }
  }

  protected statusClass(status: string | null | undefined): string {
    switch (status) {
      case 'Published': return 'bg-green-900/40 text-green-400 border-green-800/50';
      case 'Retired':   return 'bg-dark-700 text-dark-400 border-dark-600';
      default:          return 'bg-amber-900/40 text-amber-400 border-amber-800/50';
    }
  }

  protected validationClass(status: string | null | undefined): string {
    switch (status) {
      case 'Valid':   return 'text-green-400';
      case 'Invalid': return 'text-red-400';
      default:        return 'text-dark-400';
    }
  }
}
