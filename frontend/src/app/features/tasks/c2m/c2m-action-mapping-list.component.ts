import { Component, DestroyRef, inject, signal } from '@angular/core';
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
import { MessageModule } from 'primeng/message';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { TooltipModule } from 'primeng/tooltip';

import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../core/i18n/locale.service';
import { apiErrorMessage } from '../../../core/api/api-error-message';
import { C2mActionMappingsService } from '../../../core/tasks/c2m-action-mappings.service';
import { C2mActionMapping } from '../../../core/tasks/tasks.models';
import { C2mActionMappingDialogComponent } from './c2m-action-mapping-dialog.component';

/**
 * The C2M action mappings: what each Action Taken code closes a field activity with. The table is
 * the fallback behind the form — an option that names its own status on the closing form wins.
 */
@Component({
  selector: 'app-c2m-action-mapping-list',
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
    MessageModule,
    TableModule,
    TagModule,
    ToastModule,
    TooltipModule,
    C2mActionMappingDialogComponent,
  ],
  providers: [MessageService, ConfirmationService],
  template: `
    <ng-container *translateContext="let t">
      <p-toast />
      <p-confirmDialog />

      <div class="mb-4 flex flex-wrap items-center justify-between gap-2">
        <p-message severity="info" [text]="t('c2mMappings.hint')" styleClass="max-w-3xl" />
        <p-button [label]="t('c2mMappings.new')" icon="pi pi-plus" (onClick)="openNew()" />
      </div>

      <div class="card p-4">
        <p-table
          [value]="mappings()"
          [lazy]="true"
          (onLazyLoad)="load($event)"
          [rows]="10"
          [paginator]="true"
          [totalRecords]="totalRecords()"
          [loading]="loading()"
          [rowsPerPageOptions]="[10, 25, 50]"
          styleClass="app-table p-datatable-sm p-datatable-striped"
          [scrollable]="true"
          [tableStyle]="{ 'min-width': '56rem' }"
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
                  [placeholder]="t('c2mMappings.searchPlaceholder')"
                  class="w-full"
                />
              </p-iconfield>
              <p-button [label]="t('common.apply')" icon="pi pi-filter" severity="secondary" [outlined]="true" (onClick)="reload()" />
            </div>
          </ng-template>

          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('c2mMappings.actionCode') }}</th>
              <th>{{ t('taskTypes.name') }}</th>
              <th>{{ t('c2mMappings.faStatus') }}</th>
              <th>{{ t('c2mMappings.reason') }}</th>
              <th>{{ t('taskTypes.status') }}</th>
              <th class="w-32 text-center">{{ t('common.actions') }}</th>
            </tr>
          </ng-template>

          <ng-template pTemplate="body" let-mapping>
            <tr>
              <td class="font-mono text-sm">{{ mapping.actionCode }}</td>
              <td>{{ name(mapping) }}</td>
              <td>
                <span
                  class="app-badge app-badge--code"
                  [class.app-badge--success]="mapping.faStatus === 'C'"
                  [class.app-badge--warn]="mapping.faStatus === 'X'"
                >
                  {{ mapping.faStatus }} · {{ t(mapping.faStatus === 'C' ? 'tasks.c2m.completed' : 'tasks.c2m.cancelled') }}
                </span>
              </td>
              <td class="font-mono text-sm">{{ mapping.cancelReason || mapping.closureReason || '—' }}</td>
              <td>
                <p-tag [value]="t(mapping.isActive ? 'common.active' : 'common.inactive')" [severity]="mapping.isActive ? 'success' : 'secondary'" />
              </td>
              <td class="text-center">
                <div class="flex justify-center gap-1">
                  <p-button
                    icon="pi pi-pencil"
                    severity="secondary"
                    size="small"
                    [text]="true"
                    [rounded]="true"
                    [pTooltip]="t('common.edit')"
                    (onClick)="openEdit(mapping)"
                  />
                  <p-button
                    [icon]="mapping.isActive ? 'pi pi-ban' : 'pi pi-check-circle'"
                    [severity]="mapping.isActive ? 'danger' : 'success'"
                    size="small"
                    [text]="true"
                    [rounded]="true"
                    [loading]="busyId() === mapping.id"
                    [pTooltip]="t(mapping.isActive ? 'taskTypes.deactivate' : 'taskTypes.activate')"
                    (onClick)="toggleStatus(mapping)"
                  />
                </div>
              </td>
            </tr>
          </ng-template>

          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="6" class="p-6 text-center text-surface-500">{{ t('c2mMappings.empty') }}</td>
            </tr>
          </ng-template>
        </p-table>
      </div>

      <app-c2m-action-mapping-dialog [(visible)]="dialogVisible" [mapping]="selected()" (saved)="reload()" />
    </ng-container>
  `,
})
export class C2mActionMappingListComponent {
  private readonly api = inject(C2mActionMappingsService);
  private readonly messages = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly mappings = signal<C2mActionMapping[]>([]);
  protected readonly totalRecords = signal(0);
  protected readonly loading = signal(false);
  protected readonly busyId = signal<string | null>(null);
  protected readonly dialogVisible = signal(false);
  protected readonly selected = signal<C2mActionMapping | null>(null);

  protected search = '';
  private page = 1;
  private pageSize = 10;

  protected name(mapping: C2mActionMapping): string {
    return this.locale.locale() === 'ar' ? mapping.nameAr : mapping.nameEn;
  }

  protected load(event?: TableLazyLoadEvent): void {
    if (event) {
      this.pageSize = event.rows ?? this.pageSize;
      this.page = Math.floor((event.first ?? 0) / this.pageSize) + 1;
    }

    this.loading.set(true);
    this.api
      .list({ search: this.search, pageNumber: this.page, pageSize: this.pageSize })
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: (res) => {
          this.mappings.set(res.value?.items ?? []);
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

  protected openEdit(mapping: C2mActionMapping): void {
    this.selected.set(mapping);
    this.dialogVisible.set(true);
  }

  protected toggleStatus(mapping: C2mActionMapping): void {
    const activate = !mapping.isActive;

    this.confirm.confirm({
      header: this.translate.instant('common.confirm'),
      message: this.translate.instant(activate ? 'c2mMappings.confirmActivate' : 'c2mMappings.confirmDeactivate', {
        code: mapping.actionCode,
      }),
      accept: () => {
        this.busyId.set(mapping.id);
        this.api
          .setStatus(mapping.id, activate)
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
