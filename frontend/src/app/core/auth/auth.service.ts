import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthTokenResponse, CurrentUser, SsoStatus } from './auth.model';

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

  logout(refreshToken?: string): Observable<any> {
    return this.http.post(`${this.baseUrl}/logout`, { refreshToken });
  }

  getProfile(): Observable<{ value: CurrentUser; isSuccess: boolean }> {
    return this.http.get<any>(`${this.baseUrl}/me`);
  }

  getSsoStatus(): Observable<{ value: SsoStatus; isSuccess: boolean }> {
    return this.http.get<any>(`${this.baseUrl}/sso/status`);
  }

  exchangeSsoCode(code: string): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.baseUrl}/sso/exchange`, { code });
  }
}
