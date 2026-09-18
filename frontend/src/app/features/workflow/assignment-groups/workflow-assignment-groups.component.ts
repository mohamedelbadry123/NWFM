import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { debounceTime, distinctUntilChanged, startWith } from 'rxjs';
import { WorkflowAssignmentGroupsService } from '../workflow-assignment-groups.service';
import { WorkflowParticipantsService } from '../workflow-participants.service';
import { ToastService } from '@core/notifications/toast.service';
import type { WorkflowAssignmentGroupDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-assignment-group-dto';
import type { WorkflowAssignmentGroupDetailDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-assignment-group-detail-dto';
import type { WorkflowParticipantDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-participant-dto';
import type { AssignmentStrategy } from '@shared/models/models/Workflow/Domain/Enums/assignment-strategy';
import { PrvEmptyStateComponent, PrvStatusPillComponent } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../ui/workflow-table-shell.component';
import { workflowApiErrorMessage } from '../workflow-api-error';

const STRATEGIES: AssignmentStrategy[] = ['Manual', 'RoundRobin', 'LeastBusy', 'Random', 'FirstAvailable'];

/** Mirrors backend AssignmentGroupCodeGenerator.FromName for UI preview. */
export function suggestAssignmentGroupCode(name: string): string {
  const trimmed = (name ?? '').trim();
  if (!trimmed) return 'GROUP';

  let out = '';
  let prevSep = true;
  for (const ch of trimmed.toUpperCase()) {
    if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) {
      out += ch;
      prevSep = false;
    } else if (!prevSep) {
      out += '_';
      prevSep = true;
    }
  }
  out = out.replace(/^_+|_+$/g, '');
  if (!out) out = 'GROUP';
  if (out.length > 50) out = out.slice(0, 50).replace(/_+$/, '');
  return out;
}

@Component({
  selector: 'app-workflow-assignment-groups',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslateModule,    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-assignment-groups.component.html',
})
export class WorkflowAssignmentGroupsComponent implements OnInit {
  private readonly service      = inject(WorkflowAssignmentGroupsService);
  private readonly participantSvc = inject(WorkflowParticipantsService);
  private readonly toast        = inject(ToastService);
  private readonly translate    = inject(TranslateService);
  private readonly fb           = inject(FormBuilder);
  private readonly destroyRef   = inject(DestroyRef);

  protected readonly strategies = STRATEGIES;

  protected readonly items      = signal<WorkflowAssignmentGroupDto[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly isLoading  = signal(true);
  protected readonly loadError  = signal<string | null>(null);
  protected readonly page       = signal(1);
  protected readonly pageSize   = signal(20);

  // Create/Edit panel
  protected readonly showForm    = signal(false);
  protected readonly editTarget  = signal<WorkflowAssignmentGroupDto | null>(null);
  protected readonly saving      = signal(false);

  // Detail panel (members)
  protected readonly detailGroup = signal<WorkflowAssignmentGroupDetailDto | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly showDetail  = signal(false);

  // Add member panel
  protected readonly showAddMember   = signal(false);
  protected readonly participants    = signal<WorkflowParticipantDto[]>([]);
  protected readonly participantsLoading = signal(false);
  protected readonly addingMember    = signal(false);

  // Confirm remove member
  protected readonly confirmRemoveMember = signal<{ groupId: string; participantId: string; name: string } | null>(null);
  protected readonly removingMember  = signal(false);

  protected readonly groupForm = this.fb.group({
    code:               ['', [Validators.maxLength(50)]],
    name:               ['', [Validators.required, Validators.maxLength(200)]],
    nameAr:             [''],
    assignmentStrategy: ['Manual' as AssignmentStrategy, Validators.required],
  });

  protected readonly addMemberForm = this.fb.group({
    participantId: ['', Validators.required],
    canClaim:      [true],
    isPrimary:     [false],
    validFrom:     [null as string | null],
    validTo:       [null as string | null],
  });

  protected readonly searchCtrl = this.fb.control('');

  protected readonly isCreateMode = computed(() => this.editTarget() === null);

  private readonly nameValue = toSignal(
    this.groupForm.controls.name.valueChanges.pipe(startWith(this.groupForm.controls.name.value ?? '')),
    { initialValue: '' as string | null },
  );

  /** Live preview of the code the API will generate from Name (EN). */
  protected readonly suggestedCode = computed(() =>
    suggestAssignmentGroupCode(this.nameValue() ?? '')
  );

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
      if (i === 1 || i === total || (i >= current - 2 && i <= current + 2)) pages.push(i);
      else if (pages[pages.length - 1] !== '...') pages.push('...');
    }
    return pages;
  });

  ngOnInit(): void {
    this.load();

    this.searchCtrl.valueChanges.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(() => { this.page.set(1); this.load(); });
  }

  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    const search = this.searchCtrl.value?.trim() || undefined;
    this.service.getPaged(this.page(), this.pageSize(), search).subscribe({
      next: r => { this.items.set(r.items ?? []); this.totalCount.set(r.totalCount ?? 0); this.isLoading.set(false); },
      error: (err: { error?: { message?: string; title?: string } }) => {
        this.loadError.set(err?.error?.message ?? err?.error?.title ?? 'Failed to load.');
        this.isLoading.set(false);
      },
    });
  }

  protected openCreate(): void {
    this.editTarget.set(null);
    this.groupForm.reset({ assignmentStrategy: 'Manual', code: '' });
    this.groupForm.get('code')?.clearValidators();
    this.groupForm.get('code')?.updateValueAndValidity();
    this.groupForm.get('code')?.disable();
    this.showForm.set(true);
  }

  protected openEdit(g: WorkflowAssignmentGroupDto): void {
    this.editTarget.set(g);
    this.groupForm.get('code')?.enable();
    this.groupForm.get('code')?.setValidators([Validators.required, Validators.maxLength(50)]);
    this.groupForm.get('code')?.updateValueAndValidity();
    this.groupForm.patchValue({
      code: g.code ?? '',
      name: g.name ?? '',
      nameAr: g.nameAr ?? '',
      assignmentStrategy: g.assignmentStrategy ?? 'Manual',
    });
    this.showForm.set(true);
  }

  protected closeForm(): void { this.showForm.set(false); }

  protected normalizeCode(): void {
    const ctrl = this.groupForm.get('code');
    if (!ctrl || ctrl.disabled) return;
    const next = (ctrl.value ?? '').toString().trim().toUpperCase();
    if (ctrl.value !== next) ctrl.setValue(next);
  }

  /** Fill Code from Name (EN) — used to correct legacy values like `1`. */
  protected applySuggestedCode(): void {
    const suggested = suggestAssignmentGroupCode(this.groupForm.controls.name.value ?? '');
    this.groupForm.get('code')?.setValue(suggested);
    this.groupForm.get('code')?.markAsDirty();
  }

  protected saveGroup(): void {
    if (this.groupForm.invalid || this.saving()) return;
    this.saving.set(true);
    const v = this.groupForm.getRawValue();
    const edit = this.editTarget();

    const obs = edit
      ? this.service.update(edit.id!, {
          code: (v.code ?? '').trim().toUpperCase(),
          name: v.name!,
          nameAr: v.nameAr ?? null,
          assignmentStrategy: v.assignmentStrategy as AssignmentStrategy,
        })
      : this.service.create({
          name: v.name!,
          nameAr: v.nameAr ?? null,
          assignmentStrategy: v.assignmentStrategy as AssignmentStrategy,
        });

    obs.subscribe({
      next: () => {
        this.saving.set(false);
        this.showForm.set(false);
        this.toast.success(edit ? 'Assignment group updated.' : 'Assignment group created.');
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.toast.error(workflowApiErrorMessage(err, this.translate, 'error.generic'));
      },
    });
  }

  protected viewDetail(g: WorkflowAssignmentGroupDto): void {
    this.showDetail.set(true);
    this.detailLoading.set(true);
    this.detailGroup.set(null);
    this.service.getById(g.id!).subscribe({
      next: d => { this.detailGroup.set(d); this.detailLoading.set(false); },
      error: () => { this.detailLoading.set(false); },
    });
  }

  protected closeDetail(): void { this.showDetail.set(false); }

  protected openAddMember(): void {
    this.addMemberForm.reset({ canClaim: true, isPrimary: false });
    this.showAddMember.set(true);
    this.loadParticipants();
  }

  protected closeAddMember(): void { this.showAddMember.set(false); }

  protected loadParticipants(): void {
    this.participantsLoading.set(true);
    this.participantSvc.getPaged(1, 100).subscribe({
      next: r => {
        const group = this.detailGroup();
        const memberIds = new Set((group?.members ?? []).map(m => m.participantId));
        this.participants.set((r.items ?? []).filter(p => p.isActive && !memberIds.has(p.id)));
        this.participantsLoading.set(false);
      },
      error: () => { this.participantsLoading.set(false); },
    });
  }

  protected addMember(): void {
    if (this.addMemberForm.invalid || this.addingMember()) return;
    const group = this.detailGroup();
    if (!group?.id) return;
    this.addingMember.set(true);
    const v = this.addMemberForm.value;
    this.service.addMember(group.id, {
      participantId: v.participantId!,
      canClaim: v.canClaim ?? true,
      isPrimary: v.isPrimary ?? false,
      validFrom: v.validFrom ?? null,
      validTo: v.validTo ?? null,
    }).subscribe({
      next: () => {
        this.addingMember.set(false);
        this.showAddMember.set(false);
        this.toast.success('Member added to group.');
        this.viewDetail(group as WorkflowAssignmentGroupDto);
      },
      error: (err: { error?: { message?: string } }) => {
        this.addingMember.set(false);
        this.toast.error(err?.error?.message ?? 'Failed to add member.');
      },
    });
  }

  protected confirmRemove(groupId: string, participantId: string, name: string): void {
    this.confirmRemoveMember.set({ groupId, participantId, name });
  }

  protected cancelRemove(): void { this.confirmRemoveMember.set(null); }

  protected executeRemove(): void {
    const c = this.confirmRemoveMember();
    if (!c || this.removingMember()) return;
    this.removingMember.set(true);
    this.service.removeMember(c.groupId, c.participantId).subscribe({
      next: () => {
        this.removingMember.set(false);
        this.confirmRemoveMember.set(null);
        this.toast.success(`${c.name} removed from group.`);
        const g = this.detailGroup();
        if (g) this.viewDetail(g as WorkflowAssignmentGroupDto);
      },
      error: (err: { error?: { message?: string } }) => {
        this.removingMember.set(false);
        this.confirmRemoveMember.set(null);
        this.toast.error(err?.error?.message ?? 'Failed to remove member.');
      },
    });
  }

  protected prevPage(): void { if (this.page() <= 1) return; this.page.update(p => p - 1); this.load(); }
  protected nextPage(): void { if (this.page() >= this.totalPages()) return; this.page.update(p => p + 1); this.load(); }
  protected goToPage(p: number | '...'): void { if (p === '...' || p === this.page()) return; this.page.set(p as number); this.load(); }
  protected isNumber(v: number | '...'): v is number { return typeof v === 'number'; }

  protected formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
