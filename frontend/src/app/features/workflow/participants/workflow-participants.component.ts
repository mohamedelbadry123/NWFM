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
import { TranslateModule } from '@ngx-translate/core';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { WorkflowParticipantsService } from '../workflow-participants.service';
import { AppContextService } from '@core/context/app-context.service';
import { ToastService } from '@core/notifications/toast.service';
import type { WorkflowParticipantDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-participant-dto';
import { PrvEmptyStateComponent, PrvStatusPillComponent } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-participants',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslateModule,    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-participants.component.html',
})
export class WorkflowParticipantsComponent implements OnInit {
  private readonly context = inject(AppContextService);
  private readonly service      = inject(WorkflowParticipantsService);
  private readonly toast        = inject(ToastService);
  private readonly fb           = inject(FormBuilder);
  private readonly destroyRef   = inject(DestroyRef);
  private readonly searchSubject = new Subject<string>();

  protected readonly items       = signal<WorkflowParticipantDto[]>([]);
  protected readonly totalCount  = signal(0);
  protected readonly isLoading   = signal(true);
  protected readonly loadError   = signal<string | null>(null);
  protected readonly page        = signal(1);
  protected readonly pageSize    = signal(20);

  // Register modal state
  protected readonly showModal     = signal(false);
  protected readonly saving        = signal(false);

  // Confirmation dialog state
  protected readonly confirmTarget  = signal<WorkflowParticipantDto | null>(null);
  protected readonly deactivating   = signal(false);

  protected readonly registerForm = this.fb.group({
    employeeNumber: [''],
    displayName: ['', Validators.required],
    displayNameAr: [''],
    email: ['', [Validators.required, Validators.email]],
  });


  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize()))
  );
  protected readonly rangeStart = computed(() =>
    this.totalCount() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1
  );
  protected readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize(), this.totalCount())
  );
  protected readonly pageNumbers = computed(() => {
    const total = this.totalPages();
    const current = this.page();
    const pages: (number | '...')[] = [];
    for (let i = 1; i <= total; i++) {
      if (i === 1 || i === total || (i >= current - 2 && i <= current + 2)) {
        pages.push(i);
      } else if (pages[pages.length - 1] !== '...') {
        pages.push('...');
      }
    }
    return pages;
  });

  protected readonly searchCtrl = this.fb.control('');

  ngOnInit(): void {
    this.load();

    this.searchCtrl.valueChanges.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(() => {
      this.page.set(1);
      this.load();
    });


  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    const search = this.searchCtrl.value?.trim() || undefined;
    this.service.getPaged(this.page(), this.pageSize(), search).subscribe({
      next: result => {
        this.items.set(result.items ?? []);
        this.totalCount.set(result.totalCount ?? 0);
        this.isLoading.set(false);
      },
      error: (err: { error?: { message?: string; title?: string } }) => {
        this.loadError.set(err?.error?.message ?? err?.error?.title ?? 'Failed to load.');
        this.isLoading.set(false);
      },
    });
  }

  protected openModal(): void {
    this.registerForm.reset();
    this.showModal.set(true);
  }

  protected closeModal(): void {
    this.showModal.set(false);
  }

  protected save(): void {
    const user = {fullName: this.registerForm.value.displayName};
    if (this.registerForm.invalid || this.saving()) return;
    this.saving.set(true);
    const empNum = this.registerForm.value.employeeNumber?.trim() || undefined;
    this.service.register({ displayName: this.registerForm.value.displayName!, displayNameAr: this.registerForm.value.displayNameAr || null, email: this.registerForm.value.email!, employeeNumber: empNum ?? null }).subscribe({
      next: () => {
        this.saving.set(false);
        this.showModal.set(false);
        this.toast.success(`${user.fullName} registered as a workflow participant.`);
        void this.context.load();
        this.load();
      },
      error: (err: { error?: { message?: string } }) => {
        this.saving.set(false);
        this.toast.error(err?.error?.message ?? 'Failed to register participant.');
      },
    });
  }

  protected confirmDeactivate(p: WorkflowParticipantDto): void {
    this.confirmTarget.set(p);
  }

  protected cancelDeactivate(): void {
    this.confirmTarget.set(null);
  }

  protected executeDeactivate(): void {
    const p = this.confirmTarget();
    if (!p?.id || this.deactivating()) return;
    this.deactivating.set(true);
    this.service.deactivate(p.id).subscribe({
      next: () => {
        this.deactivating.set(false);
        this.confirmTarget.set(null);
        this.toast.success(`${p.displayName} has been deactivated.`);
        void this.context.load();
        this.load();
      },
      error: (err: { error?: { message?: string } }) => {
        this.deactivating.set(false);
        this.confirmTarget.set(null);
        this.toast.error(err?.error?.message ?? 'Failed to deactivate participant.');
      },
    });
  }

  protected prevPage(): void { if (this.page() <= 1) return; this.page.update(p => p - 1); this.load(); }
  protected nextPage(): void { if (this.page() >= this.totalPages()) return; this.page.update(p => p + 1); this.load(); }
  protected goToPage(p: number | '...'): void { if (p === '...' || p === this.page()) return; this.page.set(p as number); this.load(); }
  protected isNumber(v: number | '...'): v is number { return typeof v === 'number'; }

  protected avatarBg(name: string): string {
    const colors = ['#5040D6', '#7c3aed', '#0891b2', '#059669', '#d97706', '#db2777', '#0284c7'];
    return colors[name.charCodeAt(0) % colors.length];
  }

  protected avatarInitial(name: string): string { return name.charAt(0).toUpperCase(); }

  protected formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
