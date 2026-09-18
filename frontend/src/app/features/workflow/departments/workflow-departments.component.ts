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
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { WorkflowDepartmentsService } from '../workflow-departments.service';
import { WorkflowParticipantsService } from '../workflow-participants.service';
import { WorkflowAssignmentGroupsService } from '../workflow-assignment-groups.service';
import { ToastService } from '@core/notifications/toast.service';
import type { WorkflowDepartmentDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-department-dto';
import type { WorkflowParticipantDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-participant-dto';
import type { WorkflowAssignmentGroupDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-assignment-group-dto';
import { PrvEmptyStateComponent, PrvStatusPillComponent } from '@shared/components';
import { WorkflowPageHeaderComponent } from '../ui/workflow-page-header.component';
import { WorkflowTableShellComponent } from '../ui/workflow-table-shell.component';

@Component({
  selector: 'app-workflow-departments',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslateModule,    PrvEmptyStateComponent,
    PrvStatusPillComponent,
    WorkflowPageHeaderComponent,
    WorkflowTableShellComponent,
  ],
  templateUrl: './workflow-departments.component.html',
})
export class WorkflowDepartmentsComponent implements OnInit {
  private readonly service        = inject(WorkflowDepartmentsService);
  private readonly participantSvc = inject(WorkflowParticipantsService);
  private readonly groupsSvc      = inject(WorkflowAssignmentGroupsService);
  private readonly toast          = inject(ToastService);
  private readonly fb             = inject(FormBuilder);
  private readonly destroyRef     = inject(DestroyRef);

  protected readonly items       = signal<WorkflowDepartmentDto[]>([]);
  protected readonly totalCount  = signal(0);
  protected readonly isLoading   = signal(true);
  protected readonly loadError   = signal<string | null>(null);
  protected readonly page        = signal(1);
  protected readonly pageSize    = signal(20);

  // Form panel
  protected readonly showForm   = signal(false);
  protected readonly editTarget = signal<WorkflowDepartmentDto | null>(null);
  protected readonly saving     = signal(false);

  // Active groups for the default group selector
  protected readonly activeGroups        = signal<WorkflowAssignmentGroupDto[]>([]);
  protected readonly activeGroupsLoading = signal(false);

  // Members panel
  protected readonly showMembers       = signal(false);
  protected readonly selectedDept      = signal<WorkflowDepartmentDto | null>(null);
  protected readonly participants      = signal<WorkflowParticipantDto[]>([]);
  protected readonly participantsLoading = signal(false);
  protected readonly addingMember      = signal(false);

  // Confirm remove member
  protected readonly confirmRemoveMember = signal<{ deptId: string; participantId: string; name: string } | null>(null);
  protected readonly removingMember      = signal(false);

  protected readonly deptForm = this.fb.group({
    name:                    ['', [Validators.required, Validators.maxLength(200)]],
    nameAr:                  [''],
    code:                    [''],
    defaultAssignmentGroupId: [null as string | null],
  });

  protected readonly addMemberForm = this.fb.group({
    participantId: ['', Validators.required],
  });

  protected readonly searchCtrl = this.fb.control('');

  protected readonly isCreateMode = computed(() => this.editTarget() === null);

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

  private loadActiveGroups(): void {
    this.activeGroupsLoading.set(true);
    this.groupsSvc.getPaged(1, 100).subscribe({
      next: r => {
        this.activeGroups.set((r.items ?? []).filter(g => g.isActive));
        this.activeGroupsLoading.set(false);
      },
      error: () => { this.activeGroupsLoading.set(false); },
    });
  }

  protected openCreate(): void {
    this.editTarget.set(null);
    this.deptForm.reset();
    this.showForm.set(true);
    this.loadActiveGroups();
  }

  protected openEdit(d: WorkflowDepartmentDto): void {
    this.editTarget.set(d);
    this.deptForm.patchValue({
      name: d.name ?? '',
      nameAr: d.nameAr ?? '',
      code: d.code ?? '',
      defaultAssignmentGroupId: d.defaultAssignmentGroupId ?? null,
    });
    this.showForm.set(true);
    this.loadActiveGroups();
  }

  protected closeForm(): void { this.showForm.set(false); }

  protected saveDept(): void {
    if (this.deptForm.invalid || this.saving()) return;
    this.saving.set(true);
    const v = this.deptForm.getRawValue();
    const edit = this.editTarget();

    const obs = edit
      ? this.service.update(edit.id!, {
          name: v.name!,
          nameAr: v.nameAr ?? null,
          code: v.code ?? null,
          defaultAssignmentGroupId: v.defaultAssignmentGroupId ?? null,
        })
      : this.service.create({
          name: v.name!,
          nameAr: v.nameAr ?? null,
          code: v.code ?? null,
          defaultAssignmentGroupId: v.defaultAssignmentGroupId ?? null,
        });

    obs.subscribe({
      next: () => {
        this.saving.set(false);
        this.showForm.set(false);
        this.toast.success(edit ? 'Department updated.' : 'Department created.');
        this.load();
      },
      error: (err: { error?: { message?: string } }) => {
        this.saving.set(false);
        this.toast.error(err?.error?.message ?? 'Operation failed.');
      },
    });
  }

  protected openMembers(d: WorkflowDepartmentDto): void {
    this.selectedDept.set(d);
    this.showMembers.set(true);
    this.loadParticipants();
  }

  protected closeMembers(): void { this.showMembers.set(false); }

  protected loadParticipants(): void {
    this.participantsLoading.set(true);
    this.participantSvc.getPaged(1, 100).subscribe({
      next: r => {
        this.participants.set((r.items ?? []).filter(p => p.isActive));
        this.participantsLoading.set(false);
      },
      error: () => { this.participantsLoading.set(false); },
    });
  }

  protected addMember(): void {
    if (this.addMemberForm.invalid || this.addingMember()) return;
    const dept = this.selectedDept();
    if (!dept?.id) return;
    this.addingMember.set(true);
    const v = this.addMemberForm.value;
    this.service.addMember(dept.id, { participantId: v.participantId! }).subscribe({
      next: () => {
        this.addingMember.set(false);
        this.addMemberForm.reset();
        this.toast.success('Member added to department.');
        this.load();
      },
      error: (err: { error?: { message?: string } }) => {
        this.addingMember.set(false);
        this.toast.error(err?.error?.message ?? 'Failed to add member.');
      },
    });
  }

  protected confirmRemove(deptId: string, participantId: string, name: string): void {
    this.confirmRemoveMember.set({ deptId, participantId, name });
  }

  protected cancelRemove(): void { this.confirmRemoveMember.set(null); }

  protected executeRemove(): void {
    const c = this.confirmRemoveMember();
    if (!c || this.removingMember()) return;
    this.removingMember.set(true);
    this.service.removeMember(c.deptId, c.participantId).subscribe({
      next: () => {
        this.removingMember.set(false);
        this.confirmRemoveMember.set(null);
        this.toast.success(`${c.name} removed from department.`);
        this.load();
      },
      error: (err: { error?: { message?: string } }) => {
        this.removingMember.set(false);
        this.confirmRemoveMember.set(null);
        this.toast.error(err?.error?.message ?? 'Failed to remove member.');
      },
    });
  }

  protected groupName(id: string | null | undefined): string {
    if (!id) return '—';
    return this.activeGroups().find(g => g.id === id)?.name ?? '—';
  }

  protected prevPage(): void { if (this.page() <= 1) return; this.page.update(p => p - 1); this.load(); }
  protected nextPage(): void { if (this.page() >= this.totalPages()) return; this.page.update(p => p + 1); this.load(); }
  protected goToPage(p: number | '...'): void { if (p === '...' || p === this.page()) return; this.page.set(p as number); this.load(); }
  protected isNumber(v: number | '...'): v is number { return typeof v === 'number'; }
}
