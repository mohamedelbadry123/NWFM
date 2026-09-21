import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { finalize } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { MenuModule } from 'primeng/menu';
import { ContextMenuModule } from 'primeng/contextmenu';
import { MenuItem, MessageService } from 'primeng/api';
import { SelectModule } from 'primeng/select';
import { TableModule, TableLazyLoadEvent } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { TooltipModule } from 'primeng/tooltip';

import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../core/i18n/locale.service';
import { AuthStore } from '../../../core/auth/auth.store';
import { ADMINISTRATOR_ROLE, HasPermissionDirective, PERMISSIONS } from '../../../core/auth/permissions';
import { apiErrorMessage } from '../../../core/api/api-error-message';
import { OrgNamesService } from '../../../core/lookups/org-names.service';
import { TasksService } from '../../../core/tasks/tasks.service';
import { TaskTypesService } from '../../../core/tasks/task-types.service';
import { TaskListItem, TaskType } from '../../../core/tasks/tasks.models';
import { EMPTY_ORG_LOCATION, OrgLocation } from '../../../shared/components/org-scope/org-scope.model';
import { OrgFilterDialogComponent } from '../../../shared/components/org-scope/org-filter-dialog.component';
import {
  TASK_PRIORITIES,
  TASK_RETURN_REASONS,
  TASK_SOURCES,
  TASK_STATUSES,
  c2mStatusSeverity,
  canMigrateVersion,
  canReassign,
  canRunTaskAction,
  isOverdue,
  taskPrioritySeverity,
  taskReturnReasonSeverity,
  taskStatusSeverity,
  wasSubmittedAgain,
} from '../task-status';
import { TaskCreateDialogComponent } from './modals/task-create-dialog.component';
import { TaskEditDialogComponent } from './modals/task-edit-dialog.component';
import { TaskAssignDialogComponent } from './modals/task-assign-dialog.component';
import { TaskActionDialogComponent, TaskNoteAction } from './modals/task-action-dialog.component';
import { TaskReturnDialogComponent } from './modals/task-return-dialog.component';
import { TaskFillDialogComponent } from './modals/task-fill-dialog.component';
import { TaskDetailDialogComponent } from './modals/task-detail-dialog.component';
import { TaskPreviewDialogComponent } from './modals/task-preview-dialog.component';
import { TaskExportService } from '../task-export.service';
import { buildTaskListQuery } from './task-list-query';

interface FilterOption {
  readonly label: string;
  readonly value: string;
}

/**
 * The task worklist. Every filter, the sort and the paging run on the server — the list spans the
 * whole back office, and a page is never all there is to filter or sort. (The reference survey list
 * sorted only the page in hand; this one does not.)
 */
@Component({
  selector: 'app-task-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslateContextDirective,
    ButtonModule,
    DatePickerModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    MenuModule,
    ContextMenuModule,
    SelectModule,
    TableModule,
    TagModule,
    ToastModule,
    TooltipModule,
    HasPermissionDirective,
    OrgFilterDialogComponent,
    TaskCreateDialogComponent,
    TaskEditDialogComponent,
    TaskAssignDialogComponent,
    TaskActionDialogComponent,
    TaskReturnDialogComponent,
    TaskFillDialogComponent,
    TaskDetailDialogComponent,
    TaskPreviewDialogComponent,
  ],
  providers: [MessageService],
  templateUrl: './task-list.component.html',
})
export class TaskListComponent implements OnInit {
  private readonly tasksApi = inject(TasksService);
  private readonly taskTypesApi = inject(TaskTypesService);
  private readonly messageService = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly exporter = inject(TaskExportService);
  protected readonly orgNames = inject(OrgNamesService);

  protected readonly PERMISSIONS = PERMISSIONS;

  protected readonly tasks = signal<TaskListItem[]>([]);
  protected readonly totalRecords = signal(0);
  protected readonly loading = signal(false);
  protected readonly statusSeverity = taskStatusSeverity;
  protected readonly prioritySeverity = taskPrioritySeverity;
  protected readonly returnReasonSeverity = taskReturnReasonSeverity;
  protected readonly wasSubmittedAgain = wasSubmittedAgain;
  protected readonly isOverdue = isOverdue;
  protected readonly c2mSeverity = c2mStatusSeverity;

  // Server-side filters.
  protected search = '';
  protected status: string | null = null;
  protected taskTypeId: string | null = null;
  protected priority: string | null = null;
  protected source: string | null = null;
  protected returnReasonCode: string | null = null;
  protected createdFrom: Date | null = null;
  protected createdTo: Date | null = null;

  /**
   * Where in the geography to narrow to. The caller's own territory still applies on the server —
   * these filters can only narrow what they were already allowed to see.
   */
  protected orgFilter: OrgLocation = { ...EMPTY_ORG_LOCATION };

  /** The narrowest code in force; the trigger button's badge, so the filter stays readable while closed. */
  protected get orgFilterLabel(): string | null {
    return (
      this.orgFilter.operationAreaCode ??
      this.orgFilter.branchCode ??
      this.orgFilter.cbuCode ??
      this.orgFilter.clusterCode ??
      null
    );
  }

  protected sortField: string | null = null;
  protected sortOrder = -1;

  protected page = 1;
  protected pageSize = 10;

  protected readonly statusOptions = signal<FilterOption[]>([]);
  protected readonly priorityOptions = signal<FilterOption[]>([]);
  protected readonly sourceOptions = signal<FilterOption[]>([]);
  protected readonly returnReasonOptions = signal<FilterOption[]>([]);
  protected readonly taskTypes = signal<TaskType[]>([]);

  protected readonly taskTypeOptions = computed<FilterOption[]>(() =>
    this.taskTypes().map((type) => ({ label: `${type.code} — ${this.typeName(type)}`, value: type.id })),
  );

  // Dialog state — each dialog is its own component; the page owns visibility and the subject.
  protected readonly createVisible = signal(false);
  protected readonly editVisible = signal(false);
  protected readonly assignVisible = signal(false);
  protected readonly actionVisible = signal(false);
  protected readonly returnVisible = signal(false);
  protected readonly fillVisible = signal(false);
  protected readonly detailVisible = signal(false);
  protected readonly previewVisible = signal(false);
  protected readonly orgFilterVisible = signal(false);
  protected readonly selectedTask = signal<TaskListItem | null>(null);
  protected readonly currentAction = signal<TaskNoteAction>('complete');

  protected menuItems: MenuItem[] = [];
  protected contextMenuItems: MenuItem[] = [];
  protected contextTask: TaskListItem | null = null;

  /** The task whose version migration is in flight; drives the busy state on its control. */
  protected readonly migratingTaskId = signal<string | null>(null);

  ngOnInit(): void {
    this.orgNames.ensureLoaded();
    this.buildFilterOptions();
    this.translate.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.buildFilterOptions());

    this.taskTypesApi
      .active()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (res) => this.taskTypes.set(res.value ?? []) });
  }

  protected loadTasks(event?: TableLazyLoadEvent): void {
    if (event) {
      const rows = event.rows ?? this.pageSize;
      this.page = event.first !== undefined && rows ? Math.floor(event.first / rows) + 1 : 1;
      this.pageSize = rows;
      const field = Array.isArray(event.sortField) ? event.sortField[0] : event.sortField;
      this.sortField = field ?? null;
      this.sortOrder = event.sortOrder ?? -1;
    }

    this.loading.set(true);
    this.tasksApi
      .list(
        buildTaskListQuery({
          page: this.page,
          pageSize: this.pageSize,
          search: this.search,
          status: this.status,
          taskTypeId: this.taskTypeId,
          priority: this.priority,
          source: this.source,
          returnReasonCode: this.returnReasonCode,
          createdFrom: this.createdFrom,
          createdTo: this.createdTo,
          orgFilter: this.orgFilter,
          sortField: this.sortField,
          sortOrder: this.sortOrder,
        }),
      )
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (res) => {
          this.tasks.set(res.value?.items ?? []);
          this.totalRecords.set(res.value?.totalCount ?? 0);
        },
        error: (error: unknown) => {
          this.tasks.set([]);
          this.totalRecords.set(0);
          this.fail(error, 'tasks.messages.loadFailed');
        },
      });
  }

  protected applyFilters(): void {
    this.page = 1;
    this.loadTasks();
  }

  /** The dialog stages the cascade and hands over the finished location once. */
  protected onOrgFilterApplied(location: OrgLocation): void {
    this.orgFilter = location;
    this.applyFilters();
  }

  protected clearFilters(): void {
    this.search = '';
    this.status = null;
    this.taskTypeId = null;
    this.priority = null;
    this.source = null;
    this.returnReasonCode = null;
    this.createdFrom = null;
    this.createdTo = null;
    this.orgFilter = { ...EMPTY_ORG_LOCATION };
    this.applyFilters();
  }

  protected get hasFilters(): boolean {
    return !!(
      this.search ||
      this.status ||
      this.taskTypeId ||
      this.priority ||
      this.source ||
      this.returnReasonCode ||
      this.createdFrom ||
      this.createdTo ||
      this.orgFilterLabel
    );
  }

  protected typeName(type: { nameEn: string | null; nameAr: string | null }): string {
    return (this.locale.locale() === 'ar' ? type.nameAr : type.nameEn) ?? '';
  }

  protected taskTypeLabel(task: TaskListItem): string {
    const name = this.typeName({ nameEn: task.taskTypeNameEn, nameAr: task.taskTypeNameAr });
    return name || task.taskTypeCode || '—';
  }

  protected openCreate(): void {
    this.createVisible.set(true);
  }

  protected openDetail(task: TaskListItem): void {
    this.selectedTask.set(task);
    this.detailVisible.set(true);
  }

  protected openPreview(task: TaskListItem): void {
    this.selectedTask.set(task);
    this.previewVisible.set(true);
  }

  /** The menu closes on click, so there is no row to spin; a toast reports a failure. */
  protected exportPdf(task: TaskListItem): void {
    this.exporter
      .exportPdf(task)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => this.fail(null, 'tasks.messages.exportFailed') });
  }

  protected openEdit(task: TaskListItem): void {
    this.selectedTask.set(task);
    this.editVisible.set(true);
  }

  protected openAssign(task: TaskListItem): void {
    this.selectedTask.set(task);
    this.assignVisible.set(true);
  }

  protected openFill(task: TaskListItem): void {
    this.selectedTask.set(task);
    this.fillVisible.set(true);
  }

  protected openAction(task: TaskListItem, action: TaskNoteAction): void {
    this.selectedTask.set(task);
    this.currentAction.set(action);
    this.actionVisible.set(true);
  }

  /** A return carries a reason and an optional hand-over, so it has its own dialog. */
  protected openReturn(task: TaskListItem): void {
    this.selectedTask.set(task);
    this.returnVisible.set(true);
  }

  protected canMigrate(task: TaskListItem): boolean {
    return canMigrateVersion(task) && this.may(PERMISSIONS.manageTasks);
  }

  /** Moves the task onto its form's newest published version. */
  protected migrateVersion(task: TaskListItem): void {
    if (this.migratingTaskId() !== null) {
      return;
    }

    this.migratingTaskId.set(task.id);
    this.tasksApi
      .migrateVersion(task.id)
      .pipe(finalize(() => this.migratingTaskId.set(null)))
      .subscribe({
        next: (res) => {
          this.messageService.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('tasks.migrateVersion.success', { version: res.value ?? '' }),
          });
          this.loadTasks();
        },
        error: (error: unknown) => this.fail(error, 'tasks.migrateVersion.failed'),
      });
  }

  /** Only transitions the domain would accept — and the caller may perform — are offered. */
  protected buildMenu(task: TaskListItem): MenuItem[] {
    return [
      {
        label: this.translate.instant('tasks.actions.viewDetail'),
        icon: 'pi pi-eye',
        command: () => this.openDetail(task),
      },
      {
        label: this.translate.instant('tasks.actions.previewForm'),
        icon: 'pi pi-file',
        disabled: task.submissionCount === 0,
        command: () => this.openPreview(task),
      },
      {
        label: this.translate.instant('tasks.actions.exportPdf'),
        icon: 'pi pi-file-pdf',
        command: () => this.exportPdf(task),
      },
      {
        label: this.translate.instant('tasks.actions.edit'),
        icon: 'pi pi-map-marker',
        visible: this.may(PERMISSIONS.manageTasks),
        disabled: !canRunTaskAction('edit', task.status),
        command: () => this.openEdit(task),
      },
      {
        // A task already out with a crew is being moved, not handed out — say which one this is.
        label: this.translate.instant(task.assignedTeamId ? 'tasks.actions.reassign' : 'tasks.actions.assign'),
        icon: 'pi pi-send',
        visible: this.may(PERMISSIONS.assignTasks),
        disabled: !canReassign(task.status, task.submissionCount),
        command: () => this.openAssign(task),
      },
      {
        label: this.translate.instant('tasks.actions.fill'),
        icon: 'pi pi-pencil',
        visible: this.may(PERMISSIONS.submitTasks),
        disabled: !canRunTaskAction('fill', task.status),
        command: () => this.openFill(task),
      },
      {
        label: this.translate.instant('tasks.actions.migrateVersion'),
        icon: 'pi pi-arrow-up-right',
        visible: this.may(PERMISSIONS.manageTasks),
        disabled: !canMigrateVersion(task) || this.migratingTaskId() !== null,
        command: () => this.migrateVersion(task),
      },
      { separator: true },
      {
        label: this.translate.instant('tasks.actions.complete'),
        icon: 'pi pi-check-circle',
        visible: this.may(PERMISSIONS.reviewTasks),
        disabled: !canRunTaskAction('complete', task.status),
        command: () => this.openAction(task, 'complete'),
      },
      {
        label: this.translate.instant('tasks.actions.return'),
        icon: 'pi pi-replay',
        visible: this.may(PERMISSIONS.reviewTasks),
        disabled: !canRunTaskAction('return', task.status),
        command: () => this.openReturn(task),
      },
      {
        label: this.translate.instant('tasks.actions.expire'),
        icon: 'pi pi-ban',
        visible: this.may(PERMISSIONS.manageTasks),
        disabled: !canRunTaskAction('expire', task.status),
        command: () => this.openAction(task, 'expire'),
      },
    ];
  }

  protected toggleMenu(event: Event, task: TaskListItem, menu: { toggle: (e: Event) => void }): void {
    this.menuItems = this.buildMenu(task);
    menu.toggle(event);
  }

  /** The table raises this before it opens the context menu, so assigning the model here is what it renders. */
  protected onContextMenu(event: { data: TaskListItem }): void {
    this.contextMenuItems = this.buildMenu(event.data);
  }

  private may(permission: string): boolean {
    return this.authStore.hasAnyPermission(permission) || this.authStore.roles().includes(ADMINISTRATOR_ROLE);
  }

  private fail(error: unknown, fallbackKey: string): void {
    this.messageService.add({
      severity: 'error',
      summary: this.translate.instant('common.error'),
      detail: apiErrorMessage(error, this.translate, fallbackKey),
      life: 8000,
    });
  }

  private buildFilterOptions(): void {
    const options = (values: readonly string[], prefix: string): FilterOption[] =>
      values.map((value) => ({ label: this.translate.instant(`${prefix}.${value}`), value }));

    this.statusOptions.set(options(TASK_STATUSES, 'tasks.status'));
    this.priorityOptions.set(options(TASK_PRIORITIES, 'tasks.priority'));
    this.sourceOptions.set(options(TASK_SOURCES, 'tasks.source'));
    this.returnReasonOptions.set(options(TASK_RETURN_REASONS, 'tasks.returnReason'));
  }
}
