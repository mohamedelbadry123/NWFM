import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { AppConfigService } from '../config/app-config.service';
import { ApiResult, unwrap } from '@platform/shared';

export interface SignupPlan {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly descriptionEn?: string | null;
  readonly descriptionAr?: string | null;
  readonly maxUsers?: number | null;
  readonly maxTeams?: number | null;
  readonly maxStorageGb?: number | null;
  readonly trialDays: number;
}

export interface SignupOptions {
  readonly plans: readonly SignupPlan[];
  readonly defaultPlanCode: string;
  readonly captchaProvider: string;
  readonly captchaSiteKey: string;
  readonly workspaceUrlTemplate: string;
  readonly egressIps: readonly string[];
}

export interface TenantKeyAvailability {
  readonly key: string;
  readonly isAvailable: boolean;
  readonly reason?: string | null;
}

export interface SubmitSignupRequest {
  readonly tenantKey: string;
  readonly organizationName: string;
  readonly adminFullName: string;
  readonly adminEmail: string;
  readonly adminPhone?: string | null;
  readonly planCode?: string | null;
  readonly language?: string | null;
  readonly databaseMode: 1 | 2;
  readonly captchaToken?: string | null;
}

export interface SignupStatus {
  readonly signupId: string;
  readonly tenantKey: string;
  readonly organizationName: string;
  readonly status: string;
  readonly databaseMode: string;
  readonly isEmailVerified: boolean;
  readonly lastCompletedStep?: string | null;
  readonly completedSteps: number;
  readonly totalSteps: number;
  readonly message?: string | null;
  readonly workspaceUrl?: string | null;
}

export interface ByoSetupScript {
  readonly script: string;
  readonly egressIps: readonly string[];
}

export interface ByoDatabaseCheck {
  readonly code: string;
  readonly passed: boolean;
  readonly message: string;
}

export interface ByoDatabaseInspection {
  readonly succeeded: boolean;
  readonly checks: readonly ByoDatabaseCheck[];
}

export interface ByoDatabaseRequest {
  readonly token: string;
  readonly server: string;
  readonly port?: number | null;
  readonly database: string;
  readonly userName: string;
  readonly password: string;
  readonly trustServerCertificate: boolean;
}

/** A workspace the landing page offers a sign-in card for. Only serving workspaces are listed. */
export interface PublicTenantCard {
  readonly key: string;
  readonly name: string;
  readonly status: string;
  readonly loginUrl: string;
}

@Injectable({ providedIn: 'root' })
export class PublicApiService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfigService);

  private get base(): string {
    return `${this.config.snapshot.apiBaseUrl}/api/v1/public`;
  }

  getTenants(): Observable<readonly PublicTenantCard[]> {
    return this.http
      .get<ApiResult<readonly PublicTenantCard[]>>(`${this.base}/workspaces`)
      .pipe(map(unwrap));
  }

  getSignupOptions(): Observable<SignupOptions> {
    return this.http.get<ApiResult<SignupOptions>>(`${this.base}/signup/options`).pipe(map(unwrap));
  }

  checkTenantKey(key: string): Observable<TenantKeyAvailability> {
    return this.http
      .get<ApiResult<TenantKeyAvailability>>(`${this.base}/signup/key-availability`, {
        params: new HttpParams().set('key', key),
      })
      .pipe(map(unwrap));
  }

  submitSignup(body: SubmitSignupRequest): Observable<{ signupId: string; adminEmail: string }> {
    return this.http
      .post<ApiResult<{ signupId: string; adminEmail: string }>>(`${this.base}/signup`, body)
      .pipe(map(unwrap));
  }

  getSignupStatus(id: string): Observable<SignupStatus> {
    return this.http.get<ApiResult<SignupStatus>>(`${this.base}/signup/${id}`).pipe(map(unwrap));
  }

  verifyEmail(id: string, token: string): Observable<SignupStatus> {
    return this.http
      .post<ApiResult<SignupStatus>>(`${this.base}/signup/${id}/verify`, { token })
      .pipe(map(unwrap));
  }

  resendVerification(id: string): Observable<boolean> {
    return this.http
      .post<ApiResult<boolean>>(`${this.base}/signup/${id}/resend`, {})
      .pipe(map(unwrap));
  }

  getDatabaseScript(
    database: string,
    userName: string,
    runtimeUserName: string,
  ): Observable<ByoSetupScript> {
    return this.http
      .get<ApiResult<ByoSetupScript>>(`${this.base}/signup/database/script`, {
        params: new HttpParams()
          .set('database', database)
          .set('userName', userName)
          .set('runtimeUserName', runtimeUserName),
      })
      .pipe(map(unwrap));
  }

  testDatabase(id: string, body: ByoDatabaseRequest): Observable<ByoDatabaseInspection> {
    return this.http
      .post<ApiResult<ByoDatabaseInspection>>(`${this.base}/signup/${id}/database/test`, body)
      .pipe(map(unwrap));
  }

  saveDatabase(
    id: string,
    body: ByoDatabaseRequest,
  ): Observable<{ inspection: ByoDatabaseInspection; status: SignupStatus }> {
    return this.http
      .post<ApiResult<{ inspection: ByoDatabaseInspection; status: SignupStatus }>>(
        `${this.base}/signup/${id}/database`,
        body,
      )
      .pipe(map(unwrap));
  }
}
