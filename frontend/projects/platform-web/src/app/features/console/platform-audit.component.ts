import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { I18nDirective } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { ChipModule } from 'primeng/chip';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { finalize } from 'rxjs';
import { PlatformApiService, PlatformAuditEntry } from '../../core/api/platform-api.service';

/** Action families (`PlatformAuditActions` on the API). A trailing '.' filters by prefix. */
const ACTION_GROUPS = [
  'tenant.',
  'subscription.',
  'signup.',
  'support.',
  'plan.',
  'database_server.',
  'platform_user.',
  'tenant_admin.',
  'platform.',
] as const;

/** Platform audit trail, newest first. `?tenantId=` narrows it to one workspace (linked from tenant detail). */
@Component({
  selector: 'app-platform-audit',
  imports: [
    DatePipe,
    FormsModule,
    RouterLink,
    I18nDirective,
    ButtonModule,
    ChipModule,
    InputText,
    Select,
    TableModule,
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
          [rowsPerPageOptions]="[50, 100, 200]"
          paginatorDropdownAppendTo="body"
          [tableStyle]="{ 'min-width': '64rem' }"
        >
          <ng-template #caption>
            <div class="flex flex-wrap items-center gap-2">
              <input
                pInputText
                [(ngModel)]="search"
                [placeholder]="t('platform.audit.searchHint')"
                (keyup.enter)="reload()"
              />
              <p-select
                [(ngModel)]="action"
                [options]="actionGroups"
                [placeholder]="t('platform.audit.allActions')"
                [showClear]="true"
                appendTo="body"
                (onChange)="reload()"
              >
                <ng-template #selectedItem let-item>{{
                  t('platform.audit.groups.' + item.slice(0, -1))
                }}</ng-template>
                <ng-template #item let-item>{{
                  t('platform.audit.groups.' + item.slice(0, -1))
                }}</ng-template>
              </p-select>
              <p-button
                icon="pi pi-search"
                [text]="true"
                [disabled]="loading()"
                (onClick)="reload()"
                [ariaLabel]="t('common.search')"
              />
              @if (tenantId) {
                <p-chip
                  [label]="t('platform.audit.tenantFilter')"
                  [removable]="true"
                  (onRemove)="clearTenant()"
                />
              }
            </div>
          </ng-template>
          <ng-template #header>
            <tr>
              <th>{{ t('platform.audit.at') }}</th>
              <th>{{ t('platform.audit.actor') }}</th>
              <th>{{ t('platform.audit.action') }}</th>
              <th>{{ t('platform.audit.tenant') }}</th>
              <th>{{ t('platform.audit.target') }}</th>
              <th>{{ t('platform.audit.details') }}</th>
              <th>{{ t('platform.audit.ip') }}</th>
            </tr>
          </ng-template>
          <ng-template #body let-row>
            <tr>
              <td class="whitespace-nowrap">{{ row.atUtc | date: 'short' }}</td>
              <td>{{ row.actorName || '—' }}</td>
              <td>
                <code>{{ row.action }}</code>
              </td>
              <td>
                @if (row.tenantId) {
                  <a [routerLink]="['/console/tenants', row.tenantId]">{{
                    row.tenantKey || row.tenantId
                  }}</a>
                } @else {
                  —
                }
              </td>
              <td>{{ row.targetType ? row.targetType + ' ' + (row.targetId || '') : '—' }}</td>
              <td class="details">{{ row.details || '—' }}</td>
              <td>{{ row.ipAddress || '—' }}</td>
            </tr>
          </ng-template>
          <ng-template #emptymessage>
            <tr>
              <td colspan="7" class="text-center p-4">{{ t('platform.audit.empty') }}</td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    </ng-container>
  `,
  styles: `
    .details {
      max-width: 28rem;
      overflow-wrap: anywhere;
    }
  `,
})
export class PlatformAuditComponent {
  private readonly api = inject(PlatformApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly rows = signal<PlatformAuditEntry[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly actionGroups = [...ACTION_GROUPS];
  protected search = '';
  protected action: string | null = null;
  protected tenantId: string | null = this.route.snapshot.queryParamMap.get('tenantId');
  protected pageSize = 50;
  protected first = 0;

  protected load(event: TableLazyLoadEvent): void {
    this.pageSize = event.rows ?? this.pageSize;
    this.first = event.first ?? 0;
    const page = Math.floor(this.first / this.pageSize) + 1;
    this.loading.set(true);
    this.api
      .getAudit(page, this.pageSize, {
        tenantId: this.tenantId ?? undefined,
        action: this.action ?? undefined,
        searchTerm: this.search.trim() || undefined,
      })
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

  protected clearTenant(): void {
    this.tenantId = null;
    void this.router.navigate([], { relativeTo: this.route, queryParams: {} });
    this.reload();
  }
}
