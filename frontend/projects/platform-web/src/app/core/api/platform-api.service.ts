import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { AppConfigService } from '../config/app-config.service';
import { ApiResult, unwrap } from '@platform/shared';

export interface PlatformLogin {
  readonly accessToken: string;
  readonly expiresInSeconds: number;
  readonly userId: string;
  readonly email: string;
  readonly displayName: string;
  readonly role: string;
}

export interface PlatformProfile {
  readonly userId: string;
  readonly email: string;
  readonly displayName: string;
  readonly role: string;
}

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly totalCount: number;
  readonly page: number;
  readonly pageSize: number;
}

export interface PlatformTenantListItem {
  readonly id: string;
  readonly key: string;
  readonly name: string;
  readonly status: string;
  readonly statusReason?: string | null;
  readonly contactEmail: string;
  readonly planCode?: string | null;
  readonly subscriptionStatus?: string | null;
  readonly trialEndsAtUtc?: string | null;
  readonly periodEndsAtUtc?: string | null;
  readonly databaseMode?: string | null;
  readonly schemaVersion?: string | null;
  readonly createdAtUtc: string;
}

export interface PlatformTenantSettings {
  readonly ssoEnabled: boolean;
  readonly listedPublicly: boolean;
  readonly ssoEntityId?: string | null;
  readonly ssoIdentityProviderEntityId?: string | null;
  readonly ssoMetadataUrl?: string | null;
  readonly defaultLanguage: string;
}

export interface PlatformTenantDatabase {
  readonly mode: string;
  readonly databaseName?: string | null;
  readonly databaseServerId?: number | null;
  readonly connectionStringName?: string | null;
  readonly schemaVersion?: string | null;
  readonly lastMigratedAtUtc?: string | null;
  readonly verifiedAtUtc?: string | null;
  readonly runtimePrincipal?: string | null;
  readonly lastMigrationError?: string | null;
}

export interface PlatformTenantSubscription {
  readonly id: string;
  readonly planCode: string;
  readonly planNameEn: string;
  readonly planNameAr: string;
  readonly status: string;
  readonly startedAtUtc: string;
  readonly trialEndsAtUtc?: string | null;
  readonly periodEndsAtUtc?: string | null;
  readonly notes?: string | null;
  readonly maxUsers?: number | null;
  readonly maxTeams?: number | null;
  readonly maxStorageGb?: number | null;
}

export interface PlatformTenantDetail {
  readonly id: string;
  readonly key: string;
  readonly name: string;
  readonly status: string;
  readonly statusReason?: string | null;
  readonly contactName: string;
  readonly contactEmail: string;
  readonly contactPhone?: string | null;
  readonly seedProfile: string;
  readonly storagePath?: string | null;
  readonly createdAtUtc: string;
  readonly activatedAtUtc?: string | null;
  readonly hosts: readonly string[];
  readonly settings: PlatformTenantSettings;
  readonly database?: PlatformTenantDatabase | null;
  readonly subscription?: PlatformTenantSubscription | null;
}

export const PlatformTenantStatusAction = {
  Activate: 1,
  Suspend: 2,
  Maintenance: 3,
} as const;

/** Mirrors `PlatformSubscriptionAction` on the API. */
export const PlatformSubscriptionAction = {
  Activate: 1,
  ExtendTrial: 2,
  ChangePlan: 3,
  Suspend: 4,
  Cancel: 5,
} as const;

/** Tenant status values the list can filter on (`TenantStatus` on the API). */
export const PLATFORM_TENANT_STATUSES = [
  'Provisioning',
  'Active',
  'Suspended',
  'Maintenance',
  'Deleted',
] as const;

export interface UpdatePlatformTenantRequest {
  readonly name: string;
  readonly contactName: string;
  readonly contactEmail: string;
  readonly contactPhone?: string | null;
  readonly ssoEnabled: boolean;
  readonly ssoEntityId?: string | null;
  readonly ssoIdentityProviderEntityId?: string | null;
  readonly ssoMetadataUrl?: string | null;
  readonly defaultLanguage: string;
  readonly listedPublicly: boolean;
  readonly hosts: readonly string[];
}

export interface UpdatePlatformSubscriptionRequest {
  readonly action: number;
  readonly periodEndsAtUtc?: string | null;
  readonly trialDays?: number | null;
  readonly planCode?: string | null;
  readonly notes?: string | null;
}

export interface TenantMigrationOutcome {
  readonly succeeded: boolean;
  readonly schemaVersion?: string | null;
  readonly error?: string | null;
}

export interface CreatePlatformTenantRequest {
  readonly tenantKey: string;
  readonly organizationName: string;
  readonly adminFullName: string;
  readonly adminEmail: string;
  readonly adminPhone?: string | null;
  readonly planCode?: string | null;
  readonly language?: string | null;
}

export interface CreatePlatformTenantResult {
  readonly signupId: string;
  readonly tenantKey: string;
}

export interface PlatformDatabaseServer {
  readonly id: number;
  readonly name: string;
  readonly useContainedUsers: boolean;
  readonly maxDatabases: number;
  readonly databaseCount: number;
  readonly isActive: boolean;
  readonly createdAtUtc: string;
}

export interface SavePlatformDatabaseServerRequest {
  readonly id?: number | null;
  readonly name: string;
  /** Empty on update keeps the stored connection string. */
  readonly adminConnectionString?: string | null;
  readonly useContainedUsers: boolean;
  readonly maxDatabases: number;
  readonly isActive: boolean;
}

/** Platform staff roles (`PlatformRoles` on the API). */
export const PLATFORM_ROLES = {
  admin: 'PlatformAdmin',
  support: 'PlatformSupport',
} as const;

export interface PlatformUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly role: string;
  readonly isActive: boolean;
  readonly lastLoginAtUtc?: string | null;
  readonly lockoutEndUtc?: string | null;
  readonly createdAtUtc: string;
}

export interface SavePlatformUserRequest {
  readonly id?: string | null;
  readonly email: string;
  readonly displayName: string;
  readonly role: string;
  readonly isActive: boolean;
  /** Required for a new user; empty on update keeps the current password. */
  readonly password?: string | null;
}

export interface PlatformAuditEntry {
  readonly id: number;
  readonly atUtc: string;
  readonly actorId?: string | null;
  readonly actorName?: string | null;
  readonly action: string;
  readonly tenantId?: string | null;
  readonly tenantKey?: string | null;
  readonly targetType?: string | null;
  readonly targetId?: string | null;
  readonly details?: string | null;
  readonly ipAddress?: string | null;
}

export interface PlatformAuditFilter {
  readonly tenantId?: string;
  /** Exact action, or a prefix ending in '.' (for example `support.`). */
  readonly action?: string;
  readonly searchTerm?: string;
}

export interface PlatformPlan {
  readonly id: number;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly descriptionEn?: string | null;
  readonly descriptionAr?: string | null;
  readonly maxUsers?: number | null;
  readonly maxTeams?: number | null;
  readonly maxStorageGb?: number | null;
  readonly trialDays: number;
  readonly isPublic: boolean;
  readonly isActive: boolean;
  readonly sortOrder: number;
  readonly tenantCount: number;
}

export interface PlatformSignup {
  readonly id: string;
  readonly tenantKey: string;
  readonly organizationName: string;
  readonly adminFullName: string;
  readonly adminEmail: string;
  readonly planCode: string;
  readonly databaseMode: string;
  readonly status: string;
  readonly isEmailVerified: boolean;
  readonly approvalRequired: boolean;
  readonly approvedAtUtc?: string | null;
  readonly approvedBy?: string | null;
  readonly lastError?: string | null;
  readonly createdAtUtc: string;
}

/** Signup status values the list can filter on (`SignupStatus` on the API). */
export const PLATFORM_SIGNUP_STATUSES = [
  'PendingEmailVerification',
  'AwaitingApproval',
  'AwaitingDatabase',
  'Queued',
  'Provisioning',
  'Completed',
  'Failed',
  'Rejected',
] as const;

export const SIGNUP_STATUS_AWAITING_APPROVAL = 'AwaitingApproval';

/** Statuses `RetryPlatformSignupCommand` accepts (Provisioning only once it has stalled; the API checks). */
export const SIGNUP_RETRYABLE_STATUSES = [
  'Failed',
  'Queued',
  'AwaitingDatabase',
  'Provisioning',
] as const;

/** Statuses `RejectPlatformSignupCommand` refuses. */
export const SIGNUP_FINAL_OR_RUNNING_STATUSES = ['Provisioning', 'Completed', 'Rejected'] as const;

/** Where a tenant's database is. `full` is only set by an audited reveal. */
export interface TenantConnection {
  readonly server?: string | null;
  readonly database?: string | null;
  readonly userId?: string | null;
  readonly integratedSecurity: boolean;
  readonly masked: string;
  readonly full?: string | null;
}

/** `TenantAdministratorStatuses` on the API. */
export const TENANT_ADMIN_STATUSES = {
  active: 'Active',
  suspended: 'Suspended',
  pendingActivation: 'PendingActivation',
} as const;

export interface TenantAdministrator {
  readonly userId: string;
  readonly userName: string;
  readonly email?: string | null;
  readonly phoneNumber?: string | null;
  readonly status: string;
  readonly createdAt?: string | null;
}

/** `TenantCredentialsDelivery` on the API, as its name. */
export const TENANT_CREDENTIALS_DELIVERY = {
  sent: 'Sent',
  mocked: 'WrittenToMockFolder',
  emailDisabled: 'EmailDisabled',
} as const;

export interface TenantAdminCredentialsResult {
  readonly userId: string;
  readonly delivery: string;
}

export interface CreateTenantAdminRequest {
  readonly email: string;
  readonly phoneNumber?: string | null;
}

/**
 * The console never receives a tenant token. It gets a single-use link into the workspace; the
 * tenant app spends the code in its fragment against the tenant API, which mints the token.
 */
export interface SupportSessionHandoff {
  readonly sessionId: string;
  readonly tenantId: string;
  readonly tenantKey: string;
  readonly handoffUrl: string;
  readonly handoffExpiresAtUtc: string;
  readonly sessionExpiresAtUtc: string;
}

export interface SupportGrant {
  readonly id: string;
  readonly scope: string;
  readonly reason: string;
  readonly grantedByUserName: string;
  readonly createdAtUtc: string;
  readonly expiresAtUtc: string;
  readonly revokedAtUtc?: string | null;
  readonly isActive: boolean;
}

export interface SupportSession {
  readonly id: string;
  readonly grantId: string;
  readonly platformUserName: string;
  readonly scope: string;
  readonly ticketReference: string;
  readonly impersonatedUserName: string;
  readonly startedAtUtc: string;
  readonly expiresAtUtc: string;
  readonly endedAtUtc?: string | null;
  readonly isActive: boolean;
}

export interface ImpersonationCandidate {
  readonly userId: string;
  readonly userName: string;
  readonly email?: string | null;
  readonly roles: readonly string[];
}

@Injectable({ providedIn: 'root' })
export class PlatformApiService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfigService);

  private get base(): string {
    return `${this.config.snapshot.apiBaseUrl}/api/v1/platform`;
  }

  login(email: string, password: string): Observable<PlatformLogin> {
    return this.http
      .post<ApiResult<PlatformLogin>>(`${this.base}/auth/login`, { email, password })
      .pipe(map(unwrap));
  }

  me(): Observable<PlatformProfile> {
    return this.http.get<ApiResult<PlatformProfile>>(`${this.base}/auth/me`).pipe(map(unwrap));
  }

  getTenants(
    pageNumber: number,
    pageSize: number,
    searchTerm?: string,
    status?: string,
  ): Observable<PagedResult<PlatformTenantListItem>> {
    let params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', pageSize);
    if (searchTerm) {
      params = params.set('searchTerm', searchTerm);
    }
    if (status) {
      params = params.set('status', status);
    }
    return this.http
      .get<ApiResult<PagedResult<PlatformTenantListItem>>>(`${this.base}/tenants`, { params })
      .pipe(map(unwrap));
  }

  getTenant(id: string): Observable<PlatformTenantDetail> {
    return this.http
      .get<ApiResult<PlatformTenantDetail>>(`${this.base}/tenants/${id}`)
      .pipe(map(unwrap));
  }

  createTenant(body: CreatePlatformTenantRequest): Observable<CreatePlatformTenantResult> {
    return this.http
      .post<ApiResult<CreatePlatformTenantResult>>(`${this.base}/tenants`, body)
      .pipe(map(unwrap));
  }

  updateTenant(id: string, body: UpdatePlatformTenantRequest): Observable<PlatformTenantDetail> {
    return this.http
      .put<ApiResult<PlatformTenantDetail>>(`${this.base}/tenants/${id}`, body)
      .pipe(map(unwrap));
  }

  changeTenantStatus(
    id: string,
    action: number,
    reason?: string,
  ): Observable<PlatformTenantDetail> {
    return this.http
      .post<ApiResult<PlatformTenantDetail>>(`${this.base}/tenants/${id}/status`, {
        action,
        reason,
      })
      .pipe(map(unwrap));
  }

  updateSubscription(
    id: string,
    body: UpdatePlatformSubscriptionRequest,
  ): Observable<PlatformTenantDetail> {
    return this.http
      .put<ApiResult<PlatformTenantDetail>>(`${this.base}/tenants/${id}/subscription`, body)
      .pipe(map(unwrap));
  }

  getPendingMigrations(id: string): Observable<readonly string[]> {
    return this.http
      .get<ApiResult<readonly string[]>>(`${this.base}/tenants/${id}/migrations/pending`)
      .pipe(map(unwrap));
  }

  migrateTenant(id: string): Observable<TenantMigrationOutcome> {
    return this.http
      .post<ApiResult<TenantMigrationOutcome>>(`${this.base}/tenants/${id}/migrate`, {})
      .pipe(map(unwrap));
  }

  /** Re-runs the isolation checks against the runtime credential and records when they passed. */
  verifyTenantDatabase(id: string): Observable<boolean> {
    return this.http
      .post<ApiResult<boolean>>(`${this.base}/tenants/${id}/database/verify`, {})
      .pipe(map(unwrap));
  }

  migrateAllTenants(): Observable<boolean> {
    return this.http
      .post<ApiResult<boolean>>(`${this.base}/tenants/migrate-all`, {})
      .pipe(map(unwrap));
  }

  getDatabaseServers(): Observable<readonly PlatformDatabaseServer[]> {
    return this.http
      .get<ApiResult<readonly PlatformDatabaseServer[]>>(`${this.base}/database-servers`)
      .pipe(map(unwrap));
  }

  saveDatabaseServer(body: SavePlatformDatabaseServerRequest): Observable<number> {
    return body.id
      ? this.http
          .put<ApiResult<number>>(`${this.base}/database-servers/${body.id}`, body)
          .pipe(map(unwrap))
      : this.http.post<ApiResult<number>>(`${this.base}/database-servers`, body).pipe(map(unwrap));
  }

  getUsers(): Observable<readonly PlatformUser[]> {
    return this.http
      .get<ApiResult<readonly PlatformUser[]>>(`${this.base}/users`)
      .pipe(map(unwrap));
  }

  saveUser(body: SavePlatformUserRequest): Observable<string> {
    return body.id
      ? this.http.put<ApiResult<string>>(`${this.base}/users/${body.id}`, body).pipe(map(unwrap))
      : this.http.post<ApiResult<string>>(`${this.base}/users`, body).pipe(map(unwrap));
  }

  getAudit(
    pageNumber: number,
    pageSize: number,
    filter: PlatformAuditFilter = {},
  ): Observable<PagedResult<PlatformAuditEntry>> {
    let params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', pageSize);
    if (filter.tenantId) {
      params = params.set('tenantId', filter.tenantId);
    }
    if (filter.action) {
      params = params.set('action', filter.action);
    }
    if (filter.searchTerm) {
      params = params.set('searchTerm', filter.searchTerm);
    }
    return this.http
      .get<ApiResult<PagedResult<PlatformAuditEntry>>>(`${this.base}/audit`, { params })
      .pipe(map(unwrap));
  }

  getPlans(): Observable<readonly PlatformPlan[]> {
    return this.http
      .get<ApiResult<readonly PlatformPlan[]>>(`${this.base}/plans`)
      .pipe(map(unwrap));
  }

  savePlan(body: Partial<PlatformPlan> & { id?: number | null }): Observable<number> {
    return body.id
      ? this.http.put<ApiResult<number>>(`${this.base}/plans/${body.id}`, body).pipe(map(unwrap))
      : this.http.post<ApiResult<number>>(`${this.base}/plans`, body).pipe(map(unwrap));
  }

  getSignups(
    pageNumber: number,
    pageSize: number,
    searchTerm?: string,
    status?: string,
  ): Observable<PagedResult<PlatformSignup>> {
    let params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', pageSize);
    if (searchTerm) {
      params = params.set('searchTerm', searchTerm);
    }
    if (status) {
      params = params.set('status', status);
    }
    return this.http
      .get<ApiResult<PagedResult<PlatformSignup>>>(`${this.base}/signups`, { params })
      .pipe(map(unwrap));
  }

  approveSignup(id: string): Observable<boolean> {
    return this.http
      .post<ApiResult<boolean>>(`${this.base}/signups/${id}/approve`, {})
      .pipe(map(unwrap));
  }

  getTenantConnection(tenantId: string): Observable<TenantConnection> {
    return this.http
      .get<ApiResult<TenantConnection>>(`${this.base}/tenants/${tenantId}/database/connection`)
      .pipe(map(unwrap));
  }

  revealTenantConnection(tenantId: string, reason: string): Observable<TenantConnection> {
    return this.http
      .post<ApiResult<TenantConnection>>(
        `${this.base}/tenants/${tenantId}/database/connection/reveal`,
        { reason },
      )
      .pipe(map(unwrap));
  }

  getTenantAdmins(tenantId: string): Observable<readonly TenantAdministrator[]> {
    return this.http
      .get<ApiResult<readonly TenantAdministrator[]>>(`${this.base}/tenants/${tenantId}/admins`)
      .pipe(map(unwrap));
  }

  createTenantAdmin(
    tenantId: string,
    body: CreateTenantAdminRequest,
  ): Observable<TenantAdminCredentialsResult> {
    return this.http
      .post<ApiResult<TenantAdminCredentialsResult>>(
        `${this.base}/tenants/${tenantId}/admins`,
        body,
      )
      .pipe(map(unwrap));
  }

  changeTenantAdminStatus(
    tenantId: string,
    userId: string,
    isEnabled: boolean,
    reason?: string,
  ): Observable<TenantAdministrator> {
    return this.http
      .post<ApiResult<TenantAdministrator>>(
        `${this.base}/tenants/${tenantId}/admins/${encodeURIComponent(userId)}/status`,
        { isEnabled, reason },
      )
      .pipe(map(unwrap));
  }

  resendTenantAdminCredentials(
    tenantId: string,
    userId: string,
  ): Observable<TenantAdminCredentialsResult> {
    return this.http
      .post<ApiResult<TenantAdminCredentialsResult>>(
        `${this.base}/tenants/${tenantId}/admins/${encodeURIComponent(userId)}/resend-credentials`,
        {},
      )
      .pipe(map(unwrap));
  }

  retrySignup(id: string): Observable<boolean> {
    return this.http
      .post<ApiResult<boolean>>(`${this.base}/signups/${id}/retry`, {})
      .pipe(map(unwrap));
  }

  rejectSignup(id: string, reason: string): Observable<boolean> {
    return this.http
      .post<ApiResult<boolean>>(`${this.base}/signups/${id}/reject`, { reason })
      .pipe(map(unwrap));
  }

  getSupportGrants(tenantId: string): Observable<readonly SupportGrant[]> {
    return this.http
      .get<ApiResult<readonly SupportGrant[]>>(`${this.base}/tenants/${tenantId}/support/grants`)
      .pipe(map(unwrap));
  }

  getSupportSessions(tenantId: string): Observable<readonly SupportSession[]> {
    return this.http
      .get<ApiResult<readonly SupportSession[]>>(
        `${this.base}/tenants/${tenantId}/support/sessions`,
      )
      .pipe(map(unwrap));
  }

  getTenantUsers(
    tenantId: string,
    searchTerm?: string,
  ): Observable<readonly ImpersonationCandidate[]> {
    let params = new HttpParams();
    if (searchTerm) {
      params = params.set('searchTerm', searchTerm);
    }
    return this.http
      .get<ApiResult<readonly ImpersonationCandidate[]>>(`${this.base}/tenants/${tenantId}/users`, {
        params,
      })
      .pipe(map(unwrap));
  }

  startSupportSession(
    tenantId: string,
    ticketReference: string,
    impersonatedUserId?: string,
  ): Observable<SupportSessionHandoff> {
    return this.http
      .post<ApiResult<SupportSessionHandoff>>(`${this.base}/support/sessions`, {
        tenantId,
        ticketReference,
        impersonatedUserId,
      })
      .pipe(map(unwrap));
  }

  endSupportSession(id: string): Observable<boolean> {
    return this.http
      .post<ApiResult<boolean>>(`${this.base}/support/sessions/${id}/end`, {})
      .pipe(map(unwrap));
  }
}
