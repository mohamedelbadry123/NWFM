import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Dialog } from 'primeng/dialog';
import { TextareaModule } from 'primeng/textarea';
import { Select } from 'primeng/select';
import { TooltipModule } from 'primeng/tooltip';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import {
  PLATFORM_SIGNUP_STATUSES,
  PlatformApiService,
  PlatformSignup,
  SIGNUP_FINAL_OR_RUNNING_STATUSES,
  SIGNUP_RETRYABLE_STATUSES,
  SIGNUP_STATUS_AWAITING_APPROVAL,
} from '../../core/api/platform-api.service';
import { PlatformAuthStore } from '../../core/auth/platform-auth.store';
import { platformStatusSeverity } from './platform-status';
import { HelpTipComponent } from './help-tip.component';

@Component({
  selector: 'app-platform-signups',
  imports: [
    FormsModule,
    I18nDirective,
    TableModule,
    TagModule,
    ButtonModule,
    InputText,
    Dialog,
    TextareaModule,
    Select,
    TooltipModule,
    HelpTipComponent,
  ],
  template: `
    <ng-container *appI18n="let t">
      <div class="card p-4">
        <p-table
          [value]="rows()"
          [lazy]="true"
          (onLazyLoad)="load($event)"
          [paginator]="true"
          [rows]="20"
          [totalRecords]="total()"
          [loading]="loading()"
          paginatorDropdownAppendTo="body"
        >
          <ng-template pTemplate="caption">
            <div class="flex flex-wrap gap-2 items-center">
              <input
                pInputText
                [(ngModel)]="search"
                [placeholder]="t('common.search')"
                (keyup.enter)="reload()"
              />
              <p-select
                [options]="statuses"
                [(ngModel)]="statusFilter"
                [placeholder]="t('platform.signups.allStatuses')"
                [showClear]="true"
                appendTo="body"
                (onChange)="reload()"
              />
            </div>
            @if (statusFilter === awaitingApproval) {
              <small class="block mt-2">{{ t('platform.signups.awaitingApprovalHint') }}</small>
            }
          </ng-template>
          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('signup.tenantKey') }}</th>
              <th>{{ t('signup.organizationName') }}</th>
              <th>
                {{ t('platform.tenants.status')
                }}<app-help-tip [text]="t('platform.tips.signupStatus')" />
              </th>
              <th>{{ t('common.actions') }}</th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr>
              <td>{{ row.tenantKey }}</td>
              <td>{{ row.organizationName }}</td>
              <td><p-tag [value]="row.status" [severity]="severity(row.status)" /></td>
              <td class="flex gap-2">
                @if (store.isAdmin()) {
                  @if (row.status === awaitingApproval) {
                    <p-button
                      [label]="t('platform.signups.approve')"
                      [pTooltip]="t('platform.tips.approveSignup')"
                      tooltipPosition="top"
                      size="small"
                      severity="success"
                      [loading]="busyId() === row.id"
                      [disabled]="busyId() !== null"
                      (onClick)="approve(row)"
                    />
                  } @else if (canRetry(row.status)) {
                    <p-button
                      [label]="t('platform.signups.retry')"
                      size="small"
                      [pTooltip]="t('platform.tips.retrySignup')"
                      tooltipPosition="top"
                      [loading]="busyId() === row.id"
                      [disabled]="busyId() !== null"
                      (onClick)="retry(row.id)"
                    />
                  }
                  @if (canReject(row.status)) {
                    <p-button
                      [label]="t('platform.signups.reject')"
                      size="small"
                      severity="danger"
                      [pTooltip]="t('platform.tips.rejectSignup')"
                      tooltipPosition="top"
                      [disabled]="busyId() !== null"
                      (onClick)="openReject(row.id)"
                    />
                  }
                }
              </td>
            </tr>
          </ng-template>
        </p-table>
      </div>
      <p-dialog
        [visible]="rejectOpen()"
        (visibleChange)="rejectOpen.set($event)"
        [modal]="true"
        [header]="t('platform.signups.reject')"
      >
        <label class="block mb-2">{{ t('platform.signups.rejectReason') }}</label>
        <textarea pTextarea [(ngModel)]="rejectReason" rows="3" class="w-full"></textarea>
        <div class="flex justify-end gap-2 mt-3">
          <p-button
            [label]="t('common.cancel')"
            [outlined]="true"
            [disabled]="busyId() !== null"
            (onClick)="rejectOpen.set(false)"
          />
          <p-button
            [label]="t('platform.signups.reject')"
            severity="danger"
            [loading]="busyId() !== null"
            [disabled]="busyId() !== null"
            (onClick)="reject()"
          />
        </div>
      </p-dialog>
    </ng-container>
  `,
})
export class PlatformSignupsComponent {
  private readonly api = inject(PlatformApiService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  protected readonly store = inject(PlatformAuthStore);
  protected readonly severity = platformStatusSeverity;
  protected readonly rows = signal<PlatformSignup[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly busyId = signal<string | null>(null);
  protected readonly rejectOpen = signal(false);
  protected readonly statuses = [...PLATFORM_SIGNUP_STATUSES];
  protected readonly awaitingApproval = SIGNUP_STATUS_AWAITING_APPROVAL;
  protected search = '';
  protected statusFilter: string | null = null;
  protected rejectReason = '';
  private rejectId: string | null = null;
  private pageSize = 20;

  protected load(event: TableLazyLoadEvent): void {
    const page = Math.floor((event.first ?? 0) / (event.rows ?? this.pageSize)) + 1;
    this.pageSize = event.rows ?? this.pageSize;
    this.loading.set(true);
    this.api
      .getSignups(page, this.pageSize, this.search || undefined, this.statusFilter ?? undefined)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (result) => {
          this.rows.set([...result.items]);
          this.total.set(result.totalCount);
        },
      });
  }

  /** Mirrors the API: retry resumes a failed, queued, stalled or database-waiting run. */
  protected canRetry(status: string): boolean {
    return (SIGNUP_RETRYABLE_STATUSES as readonly string[]).includes(status);
  }

  /** Mirrors the API: a signup that is provisioning, done or already rejected cannot be rejected. */
  protected canReject(status: string): boolean {
    return !(SIGNUP_FINAL_OR_RUNNING_STATUSES as readonly string[]).includes(status);
  }

  protected approve(row: PlatformSignup): void {
    this.busyId.set(row.id);
    this.api
      .approveSignup(row.id)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: () => {
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('platform.signups.approved', { key: row.tenantKey }),
          });
          this.reload();
        },
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }

  protected reload(): void {
    this.load({ first: 0, rows: this.pageSize });
  }

  protected retry(id: string): void {
    this.busyId.set(id);
    this.api
      .retrySignup(id)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: () => this.reload(),
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }

  protected openReject(id: string): void {
    this.rejectId = id;
    this.rejectReason = '';
    this.rejectOpen.set(true);
  }

  protected reject(): void {
    if (!this.rejectId || !this.rejectReason) {
      return;
    }
    this.busyId.set(this.rejectId);
    this.api
      .rejectSignup(this.rejectId, this.rejectReason)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: () => {
          this.rejectOpen.set(false);
          this.reload();
        },
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }
}
