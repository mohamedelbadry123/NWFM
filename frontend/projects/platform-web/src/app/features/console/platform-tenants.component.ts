import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { InputText } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { Select } from 'primeng/select';
import { TooltipModule } from 'primeng/tooltip';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import {
  PLATFORM_TENANT_STATUSES,
  PlatformApiService,
  PlatformTenantListItem,
} from '../../core/api/platform-api.service';
import { PlatformAuthStore } from '../../core/auth/platform-auth.store';
import { PlatformTenantCreateDialogComponent } from './modals/platform-tenant-create-dialog.component';
import { platformStatusSeverity } from './platform-status';
import { HelpTipComponent } from './help-tip.component';

@Component({
  selector: 'app-platform-tenants',
  imports: [
    DatePipe,
    FormsModule,
    RouterLink,
    I18nDirective,
    TableModule,
    TagModule,
    InputText,
    ButtonModule,
    Select,
    TooltipModule,
    HelpTipComponent,
    PlatformTenantCreateDialogComponent,
  ],
  template: `
    <ng-container *appI18n="let t">
      <div class="card p-4">
        <p-table
          [value]="rows()"
          [lazy]="true"
          (onLazyLoad)="load($event)"
          [paginator]="true"
          [rows]="pageSize"
          [first]="first"
          [totalRecords]="total()"
          [loading]="loading()"
          [rowsPerPageOptions]="[20, 50, 100]"
          paginatorDropdownAppendTo="body"
          [tableStyle]="{ 'min-width': '60rem' }"
        >
          <ng-template #caption>
            <div class="flex flex-wrap items-center gap-2">
              <input
                pInputText
                [(ngModel)]="search"
                [placeholder]="t('common.search')"
                (keyup.enter)="reload()"
              />
              <p-select
                [(ngModel)]="status"
                [options]="statuses"
                [placeholder]="t('platform.tenants.allStatuses')"
                [showClear]="true"
                appendTo="body"
                (onChange)="reload()"
              />
              <p-button
                icon="pi pi-search"
                [text]="true"
                [disabled]="loading()"
                (onClick)="reload()"
                [ariaLabel]="t('common.search')"
              />
              <span class="flex-1"></span>
              @if (store.isAdmin()) {
                <p-button
                  [label]="t('platform.tenants.migrateAll')"
                  icon="pi pi-sync"
                  severity="secondary"
                  [outlined]="true"
                  [loading]="migratingAll()"
                  [disabled]="migratingAll()"
                  [pTooltip]="t('platform.tips.migrateAll')"
                  tooltipPosition="bottom"
                  (onClick)="migrateAll()"
                />
                <p-button
                  [label]="t('platform.tenants.create.open')"
                  icon="pi pi-plus"
                  [pTooltip]="t('platform.tips.addWorkspace')"
                  tooltipPosition="bottom"
                  (onClick)="createOpen.set(true)"
                />
              }
            </div>
          </ng-template>
          <ng-template #header>
            <tr>
              <th>{{ t('platform.tenants.key') }}</th>
              <th>{{ t('platform.tenants.name') }}</th>
              <th>
                {{ t('platform.tenants.status')
                }}<app-help-tip [text]="t('platform.tips.tenantStatus')" />
              </th>
              <th>
                {{ t('platform.tenants.plan') }}<app-help-tip [text]="t('platform.tips.plan')" />
              </th>
              <th>
                {{ t('platform.subscription.status')
                }}<app-help-tip [text]="t('platform.tips.subscriptionStatus')" />
              </th>
              <th>
                {{ t('platform.tenants.schema')
                }}<app-help-tip [text]="t('platform.tips.schema')" />
              </th>
              <th>{{ t('platform.tenants.created') }}</th>
              <th>{{ t('common.actions') }}</th>
            </tr>
          </ng-template>
          <ng-template #body let-row>
            <tr>
              <td>{{ row.key }}</td>
              <td>{{ row.name }}</td>
              <td><p-tag [value]="row.status" [severity]="severity(row.status)" /></td>
              <td>{{ row.planCode || '—' }}</td>
              <td>{{ row.subscriptionStatus || '—' }}</td>
              <td>{{ row.schemaVersion || '—' }}</td>
              <td>{{ row.createdAtUtc | date: 'mediumDate' }}</td>
              <td>
                <a class="font-semibold" [routerLink]="['/console/tenants', row.id]">{{
                  t('platform.tenants.open')
                }}</a>
              </td>
            </tr>
          </ng-template>
          <ng-template #emptymessage>
            <tr>
              <td colspan="8" class="text-center p-4">{{ t('platform.tenants.empty') }}</td>
            </tr>
          </ng-template>
        </p-table>
      </div>

      <app-platform-tenant-create-dialog [(visible)]="createOpen" (created)="onCreated()" />
    </ng-container>
  `,
})
export class PlatformTenantsComponent {
  private readonly api = inject(PlatformApiService);
  private readonly router = inject(Router);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  protected readonly store = inject(PlatformAuthStore);
  protected readonly rows = signal<PlatformTenantListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly migratingAll = signal(false);
  protected readonly createOpen = signal(false);
  protected readonly statuses = [...PLATFORM_TENANT_STATUSES];
  protected readonly severity = platformStatusSeverity;
  protected search = '';
  protected status: string | null = null;
  protected pageSize = 20;
  protected first = 0;

  protected load(event: TableLazyLoadEvent): void {
    this.pageSize = event.rows ?? this.pageSize;
    this.first = event.first ?? 0;
    const page = Math.floor(this.first / this.pageSize) + 1;
    this.loading.set(true);
    this.api
      .getTenants(page, this.pageSize, this.search || undefined, this.status ?? undefined)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (result) => {
          this.rows.set([...result.items]);
          this.total.set(result.totalCount);
        },
      });
  }

  protected reload(): void {
    this.load({ first: 0, rows: this.pageSize });
  }

  protected migrateAll(): void {
    if (this.migratingAll()) {
      return;
    }
    this.migratingAll.set(true);
    this.api
      .migrateAllTenants()
      .pipe(finalize(() => this.migratingAll.set(false)))
      .subscribe({
        next: () =>
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('platform.tenants.migrateAllQueued'),
          }),
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }

  /** Provisioning runs in the background; the signups page is where its progress shows. */
  protected onCreated(): void {
    void this.router.navigate(['/console/signups']);
  }
}
