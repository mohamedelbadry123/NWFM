import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslateService } from '@ngx-translate/core';
import { finalize } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ConfirmationService, MessageService } from 'primeng/api';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { TooltipModule } from 'primeng/tooltip';

import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../core/i18n/locale.service';
import { HasPermissionDirective, PERMISSIONS } from '../../../core/auth/permissions';
import { apiErrorMessage } from '../../../core/api/api-error-message';
import { OrgNamesService } from '../../../core/lookups/org-names.service';
import { TaskTypesService } from '../../../core/tasks/task-types.service';
import { TaskType } from '../../../core/tasks/tasks.models';
import { TaskTypeDialogComponent } from './task-type-dialog.component';

/** Task types: each kind of field work, and the form its tasks are filled with. */
@Component({
  selector: 'app-task-type-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslateContextDirective,
    ButtonModule,
    ConfirmDialogModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    TableModule,
    TagModule,
    ToastModule,
    TooltipModule,
    HasPermissionDirective,
    TaskTypeDialogComponent,
  ],
  providers: [MessageService, ConfirmationService],
  template: `
    <ng-container *translateContext="let t">
      <p-toast />
      <p-confirmDialog />

      <div class="mb-4 flex flex-wrap items-center justify-end gap-2">
        <ng-container *hasPermission="PERMISSIONS.manageTaskTypes">
          <p-button [label]="t('taskTypes.new')" icon="pi pi-plus" (onClick)="openNew()" />
        </ng-container>
      </div>

      <div class="card p-4">
        <p-table
          [value]="types()"
          [lazy]="true"
          (onLazyLoad)="load($event)"
          [rows]="10"
          [paginator]="true"
          [totalRecords]="totalRecords()"
          [loading]="loading()"
          [rowsPerPageOptions]="[10, 25, 50]"
          styleClass="app-table p-datatable-sm p-datatable-striped"
          [scrollable]="true"
          [tableStyle]="{ 'min-width': '64rem' }"
          [rowHover]="true"
          [showCurrentPageReport]="true"
          [currentPageReportTemplate]="t('common.pageReport')"
        >
          <ng-template pTemplate="caption">
            <div class="flex flex-wrap items-center gap-2.5 p-1">
              <p-iconfield iconPosition="left" class="w-full sm:w-72">
                <p-inputicon styleClass="pi pi-search" />
                <input
                  pInputText
                  type="text"
                  [(ngModel)]="search"
                  (keyup.enter)="reload()"
                  [placeholder]="t('taskTypes.searchPlaceholder')"
                  class="w-full"
                />
              </p-iconfield>
              <p-button [label]="t('common.apply')" icon="pi pi-filter" severity="secondary" [outlined]="true" (onClick)="reload()" />
            </div>
          </ng-template>

          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('taskTypes.code') }}</th>
              <th>{{ t('taskTypes.name') }}</th>
              <th>{{ t('taskTypes.form') }}</th>
              <th>{{ t('tasks.fields.department') }}</th>
              <th>{{ t('taskTypes.sla') }}</th>
              <th>{{ t('taskTypes.status') }}</th>
              <th class="w-32 text-center">{{ t('common.actions') }}</th>
            </tr>
          </ng-template>

          <ng-template pTemplate="body" let-type>
            <tr>
              <td class="font-mono text-sm">{{ type.code }}</td>
              <td>{{ name(type) }}</td>
              <td>
                @if (type.formCode) {
                  <span>{{ type.formCode }}</span>
                  @if (type.formCurrentVersionNo) {
                    <span class="app-badge app-badge--code ms-1.5">v{{ type.formCurrentVersionNo }}</span>
                  } @else {
                    <!-- New tasks of this type are refused until its form has a version that takes fills. -->
                    <p-tag class="ms-1.5" [value]="t('taskTypes.formNotPublished')" severity="warn" />
                  }
                } @else {
                  <p-tag [value]="t('taskTypes.formMissing')" severity="danger" />
                }
              </td>
              <td>{{ orgNames.label('Department', type.departmentCode) }}</td>
              <td>{{ type.fillSlaHours ?? '—' }} / {{ type.completionSlaHours ?? '—' }} {{ t('tasks.detail.hours') }}</td>
              <td>
                <p-tag [value]="t(type.isActive ? 'common.active' : 'common.inactive')" [severity]="type.isActive ? 'success' : 'secondary'" />
              </td>
              <td class="text-center">
                <ng-container *hasPermission="PERMISSIONS.manageTaskTypes">
                  <div class="flex justify-center gap-1">
                    <p-button
                      icon="pi pi-pencil"
                      severity="secondary"
                      size="small"
                      [text]="true"
                      [rounded]="true"
                      [pTooltip]="t('common.edit')"
                      (onClick)="openEdit(type)"
                    />
                    <p-button
                      [icon]="type.isActive ? 'pi pi-ban' : 'pi pi-check-circle'"
                      [severity]="type.isActive ? 'danger' : 'success'"
                      size="small"
                      [text]="true"
                      [rounded]="true"
                      [loading]="busyId() === type.id"
                      [pTooltip]="t(type.isActive ? 'taskTypes.deactivate' : 'taskTypes.activate')"
                      (onClick)="toggleStatus(type)"
                    />
                  </div>
                </ng-container>
              </td>
            </tr>
          </ng-template>

          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="7" class="p-6 text-center text-surface-500">{{ t('taskTypes.empty') }}</td>
            </tr>
          </ng-template>
        </p-table>
      </div>

      <app-task-type-dialog [(visible)]="dialogVisible" [taskType]="selected()" (saved)="reload()" />
    </ng-container>
  `,
})
export class TaskTypeListComponent implements OnInit {
  private readonly api = inject(TaskTypesService);
  private readonly messages = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly orgNames = inject(OrgNamesService);

  protected readonly PERMISSIONS = PERMISSIONS;
  protected readonly types = signal<TaskType[]>([]);
  protected readonly totalRecords = signal(0);
  protected readonly loading = signal(false);
  protected readonly busyId = signal<string | null>(null);
  protected readonly dialogVisible = signal(false);
  protected readonly selected = signal<TaskType | null>(null);

  protected search = '';
  private page = 1;
  private pageSize = 10;

  ngOnInit(): void {
    this.orgNames.ensureLoaded();
  }

  protected name(type: TaskType): string {
    return this.locale.locale() === 'ar' ? type.nameAr : type.nameEn;
  }

  protected load(event?: TableLazyLoadEvent): void {
    if (event) {
      this.pageSize = event.rows ?? this.pageSize;
      this.page = Math.floor((event.first ?? 0) / this.pageSize) + 1;
    }

    this.loading.set(true);
    this.api
      .list(this.page, this.pageSize, this.search.trim() || null)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: (res) => {
          this.types.set(res.value?.items ?? []);
          this.totalRecords.set(res.value?.totalCount ?? 0);
        },
        error: (error: unknown) => this.fail(error),
      });
  }

  protected reload(): void {
    this.page = 1;
    this.load();
  }

  protected openNew(): void {
    this.selected.set(null);
    this.dialogVisible.set(true);
  }

  protected openEdit(type: TaskType): void {
    this.selected.set(type);
    this.dialogVisible.set(true);
  }

  protected toggleStatus(type: TaskType): void {
    const activate = !type.isActive;

    this.confirm.confirm({
      header: this.translate.instant('common.confirm'),
      message: this.translate.instant(activate ? 'taskTypes.confirmActivate' : 'taskTypes.confirmDeactivate', { name: this.name(type) }),
      accept: () => {
        this.busyId.set(type.id);
        this.api
          .setStatus(type.id, activate)
          .pipe(finalize(() => this.busyId.set(null)))
          .subscribe({
            next: () => this.load(),
            error: (error: unknown) => this.fail(error),
          });
      },
    });
  }

  private fail(error: unknown): void {
    this.messages.add({
      severity: 'error',
      summary: this.translate.instant('common.error'),
      detail: apiErrorMessage(error, this.translate),
      life: 8000,
    });
  }
}
