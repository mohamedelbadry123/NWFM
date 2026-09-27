import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';
import { OrgScopeAssignment } from '../../shared/components/org-scope/org-scope.model';

export interface UserListItem {
  id: string;
  userName: string;
  email?: string | null;
  phoneNumber?: string | null;
  isEnabled: boolean;
  roles: string[];
}

export interface UserDetail extends UserListItem {
  teamId?: number | null;
  scopes?: OrgScopeAssignment[];
}

export interface UserWriteBody {
  email?: string | null;
  phoneNumber?: string | null;
  roles: string[];
  scopes: OrgScopeAssignment[];
}

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/users';

  list(pageNumber: number, pageSize: number, searchTerm?: string, isEnabled?: boolean | null): Observable<ApiResult<PaginatedResult<UserListItem>>> {
    let params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', pageSize);
    if (searchTerm) params = params.set('searchTerm', searchTerm);
    if (isEnabled === true || isEnabled === false) params = params.set('isEnabled', isEnabled);
    return this.http.get<ApiResult<PaginatedResult<UserListItem>>>(this.baseUrl, { params });
  }

  get(id: string): Observable<ApiResult<UserDetail>> {
    return this.http.get<ApiResult<UserDetail>>(`${this.baseUrl}/${id}`);
  }

  getAssignableRoles(): Observable<ApiResult<string[]>> {
    return this.http.get<ApiResult<string[]>>(`${this.baseUrl}/roles`);
  }

  create(body: UserWriteBody & { userName: string; password: string }): Observable<ApiResult<UserDetail>> {
    return this.http.post<ApiResult<UserDetail>>(this.baseUrl, body);
  }

  update(id: string, body: UserWriteBody): Observable<ApiResult<UserDetail>> {
    return this.http.put<ApiResult<UserDetail>>(`${this.baseUrl}/${id}`, body);
  }

  setStatus(id: string, isEnabled: boolean): Observable<ApiResult<string>> {
    return this.http.put<ApiResult<string>>(`${this.baseUrl}/${id}/status`, { isEnabled });
  }

  resetPassword(id: string, newPassword: string): Observable<ApiResult<string>> {
    return this.http.post<ApiResult<string>>(`${this.baseUrl}/${id}/reset-password`, { newPassword });
  }
}
