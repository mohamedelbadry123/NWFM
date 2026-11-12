import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthTokenResponse, CurrentUser, SsoStatus } from './auth.model';
import { SKIP_REFRESH } from './auth-request-context';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/auth';

  login(userName: string, password: string): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.baseUrl}/login`, { userName, password });
  }

  refresh(refreshToken: string): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.baseUrl}/refresh`, { refreshToken });
  }

  logout(refreshToken?: string): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/logout`, { refreshToken });
  }

  /** `refresh: false` makes a 401 reach the caller instead of triggering the interceptor's silent refresh. */
  getProfile(options?: {
    refresh?: boolean;
  }): Observable<{ value: CurrentUser; isSuccess: boolean }> {
    const context = new HttpContext().set(SKIP_REFRESH, options?.refresh === false);
    return this.http.get<{ value: CurrentUser; isSuccess: boolean }>(`${this.baseUrl}/me`, {
      context,
    });
  }

  getSsoStatus(): Observable<{ value: SsoStatus; isSuccess: boolean }> {
    return this.http.get<{ value: SsoStatus; isSuccess: boolean }>(`${this.baseUrl}/sso/status`);
  }

  exchangeSsoCode(code: string): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.baseUrl}/sso/exchange`, { code });
  }
}
