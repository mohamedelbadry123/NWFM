import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthTokenResponse, CurrentUser, SsoStatus, SupportSessionToken } from './auth.model';

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

  getProfile(): Observable<{ value: CurrentUser; isSuccess: boolean }> {
    return this.http.get<{ value: CurrentUser; isSuccess: boolean }>(`${this.baseUrl}/me`);
  }

  getSsoStatus(): Observable<{ value: SsoStatus; isSuccess: boolean }> {
    return this.http.get<{ value: SsoStatus; isSuccess: boolean }>(`${this.baseUrl}/sso/status`);
  }

  exchangeSsoCode(code: string): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.baseUrl}/sso/exchange`, { code });
  }

  /** Sets the first password from an activation link; the workspace comes from the host. */
  activate(userId: string, token: string, newPassword: string): Observable<{ isSuccess: boolean }> {
    return this.http.post<{ isSuccess: boolean }>(`${this.baseUrl}/activate`, {
      userId,
      token,
      newPassword,
    });
  }

  exchangeSupportHandoff(
    code: string,
  ): Observable<{ value: SupportSessionToken; isSuccess: boolean }> {
    return this.http.post<{ value: SupportSessionToken; isSuccess: boolean }>(
      `${this.baseUrl}/support/exchange`,
      { code },
    );
  }
}
