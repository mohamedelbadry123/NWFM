import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, LanguageService, apiErrorMessage } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { SkeletonModule } from 'primeng/skeleton';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { TooltipModule } from 'primeng/tooltip';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import {
  PlatformApiService,
  PlatformTenantDetail,
  PlatformTenantStatusAction,
  SupportGrant,
  SupportSession,
  TENANT_ADMIN_STATUSES,
  TENANT_CREDENTIALS_DELIVERY,
  TenantAdminCredentialsResult,
  TenantAdministrator,
  TenantConnection,
} from '../../core/api/platform-api.service';
import { PlatformAuthStore } from '../../core/auth/platform-auth.store';
import { PlatformTenantEditDialogComponent } from './modals/platform-tenant-edit-dialog.component';
import { PlatformSubscriptionDialogComponent } from './modals/platform-subscription-dialog.component';
import { PlatformSupportSessionDialogComponent } from './modals/platform-support-session-dialog.component';
import { PlatformConnectionStringDialogComponent } from './modals/platform-connection-string-dialog.component';
import { PlatformTenantAdminDialogComponent } from './modals/platform-tenant-admin-dialog.component';
import { PlatformTenantAdminSuspendDialogComponent } from './modals/platform-tenant-admin-suspend-dialog.component';
import { platformStatusSeverity } from './platform-status';
import { HelpTipComponent } from './help-tip.component';

@Component({
  selector: 'app-platform-tenant-detail',
  imports: [
    DatePipe,
    FormsModule,
    RouterLink,
    I18nDirective,
    ButtonModule,
    SkeletonModule,
    TableModule,
    TagModule,
    TextareaModule,
    TooltipModule,
    HelpTipComponent,
    PlatformTenantEditDialogComponent,
    PlatformSubscriptionDialogComponent,
    PlatformSupportSessionDialogComponent,
    PlatformConnectionStringDialogComponent,
    PlatformTenantAdminDialogComponent,
    PlatformTenantAdminSuspendDialogComponent,
  ],
  templateUrl: './platform-tenant-detail.component.html',
  styleUrl: './platform-tenant-detail.component.scss',
})
export class PlatformTenantDetailComponent implements OnInit {
  private readonly api = inject(PlatformApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  protected readonly store = inject(PlatformAuthStore);
  protected readonly language = inject(LanguageService);

  protected readonly tenant = signal<PlatformTenantDetail | null>(null);
  protected readonly grants = signal<SupportGrant[]>([]);
  protected readonly sessions = signal<SupportSession[]>([]);
  protected readonly pendingMigrations = signal<readonly string[] | null>(null);
  protected readonly loading = signal(true);
  protected readonly supportLoading = signal(false);
  protected readonly pendingLoading = signal(false);
  protected readonly statusBusy = signal<number | null>(null);
  protected readonly migrating = signal(false);
  protected readonly verifying = signal(false);
  protected readonly endingSessionId = signal<string | null>(null);
  protected readonly editOpen = signal(false);
  protected readonly subscriptionOpen = signal(false);
  protected readonly sessionOpen = signal(false);
  protected readonly connection = signal<TenantConnection | null>(null);
  protected readonly connectionLoading = signal(false);
  protected readonly revealOpen = signal(false);
  protected readonly admins = signal<TenantAdministrator[]>([]);
  protected readonly adminsLoading = signal(false);
  /** Null when the workspace cannot list administrators yet (still provisioning, for instance). */
  protected readonly adminsError = signal<string | null>(null);
  protected readonly adminBusyId = signal<string | null>(null);
  protected readonly addAdminOpen = signal(false);
  protected readonly suspendOpen = signal(false);
  protected readonly suspendTarget = signal<TenantAdministrator | null>(null);
  protected readonly adminStatuses = TENANT_ADMIN_STATUSES;
  protected readonly statusActions = PlatformTenantStatusAction;
  protected readonly severity = platformStatusSeverity;
  protected reason = '';

  ngOnInit(): void {
    this.reload();
  }

  /** Suspend and maintenance refuse traffic, so the API requires a reason for both. */
  protected canChangeStatus(action: number): boolean {
    return action === PlatformTenantStatusAction.Activate || this.reason.trim().length > 0;
  }

  protected changeStatus(action: number): void {
    const id = this.tenant()?.id;
    if (!id || this.statusBusy() !== null || !this.canChangeStatus(action)) {
      return;
    }
    this.statusBusy.set(action);
    this.api
      .changeTenantStatus(id, action, this.reason.trim() || undefined)
      .pipe(finalize(() => this.statusBusy.set(null)))
      .subscribe({
        next: (tenant) => {
          this.tenant.set(tenant);
          this.reason = '';
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('platform.tenants.statusChanged', {
              status: tenant.status,
            }),
          });
        },
        error: (error: unknown) => this.showError(error),
      });
  }

  protected migrate(): void {
    const id = this.tenant()?.id;
    if (!id || this.migrating()) {
      return;
    }
    this.migrating.set(true);
    this.api
      .migrateTenant(id)
      .pipe(finalize(() => this.migrating.set(false)))
      .subscribe({
        next: (outcome) => {
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('platform.schema.migrated', {
              version: outcome.schemaVersion ?? '—',
            }),
          });
          this.reloadTenant(id);
          this.loadPending(id);
        },
        error: (error: unknown) => {
          this.showError(error);
          this.reloadTenant(id);
        },
      });
  }

  /** Proves the runtime credential is still confined to this workspace's database. */
  protected verifyDatabase(): void {
    const id = this.tenant()?.id;
    if (!id || this.verifying()) {
      return;
    }
    this.verifying.set(true);
    this.api
      .verifyTenantDatabase(id)
      .pipe(finalize(() => this.verifying.set(false)))
      .subscribe({
        next: () => {
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('platform.schema.verified'),
          });
          this.reloadTenant(id);
        },
        error: (error: unknown) => this.showError(error),
      });
  }

  protected endSession(id: string): void {
    if (this.endingSessionId() !== null) {
      return;
    }
    this.endingSessionId.set(id);
    this.api
      .endSupportSession(id)
      .pipe(finalize(() => this.endingSessionId.set(null)))
      .subscribe({
        next: () => {
          const tenantId = this.tenant()?.id;
          if (tenantId) {
            this.loadSupport(tenantId);
          }
        },
        error: (error: unknown) => this.showError(error),
      });
  }

  protected onTenantSaved(tenant: PlatformTenantDetail): void {
    this.tenant.set(tenant);
  }

  protected adminStatusLabel(status: string): string {
    const key = `platform.admins.statuses.${status}`;
    const label = this.translate.instant(key);
    return label === key ? status : label;
  }

  protected openSuspend(admin: TenantAdministrator): void {
    this.suspendTarget.set(admin);
    this.suspendOpen.set(true);
  }

  protected onAdminSuspended(admin: TenantAdministrator): void {
    this.replaceAdmin(admin);
    this.messages.add({
      severity: 'success',
      summary: this.translate.instant('platform.admins.suspended', { user: admin.userName }),
    });
  }

  protected reactivateAdmin(admin: TenantAdministrator): void {
    const tenantId = this.tenant()?.id;
    if (!tenantId || this.adminBusyId() !== null) {
      return;
    }
    this.adminBusyId.set(admin.userId);
    this.api
      .changeTenantAdminStatus(tenantId, admin.userId, true)
      .pipe(finalize(() => this.adminBusyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.replaceAdmin(updated);
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('platform.admins.reactivated', {
              user: updated.userName,
            }),
          });
        },
        error: (error: unknown) => this.showError(error),
      });
  }

  protected resendCredentials(admin: TenantAdministrator): void {
    const tenantId = this.tenant()?.id;
    if (!tenantId || this.adminBusyId() !== null) {
      return;
    }
    this.adminBusyId.set(admin.userId);
    this.api
      .resendTenantAdminCredentials(tenantId, admin.userId)
      .pipe(finalize(() => this.adminBusyId.set(null)))
      .subscribe({
        next: (result) => {
          this.showDelivery(result);
          this.loadAdmins(tenantId);
        },
        error: (error: unknown) => this.showError(error),
      });
  }

  protected onAdminCreated(result: TenantAdminCredentialsResult): void {
    this.showDelivery(result);
    const tenantId = this.tenant()?.id;
    if (tenantId) {
      this.loadAdmins(tenantId);
    }
  }

  /** `Dk` is the bootstrap seed of the Digital Key workspace; unknown values show as-is. */
  protected seedProfileLabel(profile: string): string {
    const key = `platform.tenants.seedProfiles.${profile}`;
    const label = this.translate.instant(key);
    return label === key ? profile : label;
  }

  private reload(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }
    this.loading.set(true);
    this.api
      .getTenant(id)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (tenant) => this.tenant.set(tenant) });
    this.loadSupport(id);
    this.loadPending(id);
    this.loadAdmins(id);
    if (this.store.isAdmin()) {
      this.loadConnection(id);
    }
  }

  private loadAdmins(id: string): void {
    this.adminsLoading.set(true);
    this.api
      .getTenantAdmins(id)
      .pipe(finalize(() => this.adminsLoading.set(false)))
      .subscribe({
        next: (admins) => {
          this.admins.set([...admins]);
          this.adminsError.set(null);
        },
        error: (error: unknown) => {
          this.admins.set([]);
          this.adminsError.set(apiErrorMessage(error));
        },
      });
  }

  private loadConnection(id: string): void {
    this.connectionLoading.set(true);
    this.api
      .getTenantConnection(id)
      .pipe(finalize(() => this.connectionLoading.set(false)))
      .subscribe({
        next: (connection) => this.connection.set(connection),
        error: () => this.connection.set(null),
      });
  }

  private replaceAdmin(updated: TenantAdministrator): void {
    this.admins.update((admins) => admins.map((a) => (a.userId === updated.userId ? updated : a)));
  }

  private showDelivery(result: TenantAdminCredentialsResult): void {
    this.messages.add({
      severity: result.delivery === TENANT_CREDENTIALS_DELIVERY.emailDisabled ? 'warn' : 'success',
      summary: this.translate.instant(`platform.admins.delivery.${result.delivery}`),
      life: 6000,
    });
  }

  private reloadTenant(id: string): void {
    this.api.getTenant(id).subscribe({ next: (tenant) => this.tenant.set(tenant) });
  }

  private loadSupport(id: string): void {
    this.supportLoading.set(true);
    let pending = 2;
    const done = () => {
      pending -= 1;
      if (pending === 0) {
        this.supportLoading.set(false);
      }
    };
    this.api
      .getSupportGrants(id)
      .pipe(finalize(done))
      .subscribe({ next: (grants) => this.grants.set([...grants]) });
    this.api
      .getSupportSessions(id)
      .pipe(finalize(done))
      .subscribe({ next: (sessions) => this.sessions.set([...sessions]) });
  }

  private loadPending(id: string): void {
    this.pendingLoading.set(true);
    this.api
      .getPendingMigrations(id)
      .pipe(finalize(() => this.pendingLoading.set(false)))
      .subscribe({
        next: (pending) => this.pendingMigrations.set(pending),
        // The database may be unreachable (e.g. still provisioning); show "unknown" rather than an empty list.
        error: () => this.pendingMigrations.set(null),
      });
  }

  private showError(error: unknown): void {
    this.messages.add({
      severity: 'error',
      summary: this.translate.instant('common.error'),
      detail: apiErrorMessage(error),
    });
  }
}
