import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';
import { OrgScopeAssignment } from '../../shared/components/org-scope/org-scope.model';

export const TEAMS_PATH = '/api/v1/teams';

/** A field team: the crew record, its login, and the territory it works. */
export interface Team {
  id: string;
  name: string;
  mobile: string | null;
  isActive: boolean;
  userCode: string | null;
  email: string | null;
  loginEnabled: boolean;
  lastActiveAt: string | null;
  createdAt: string;
  scopes: OrgScopeAssignment[];
}

export interface TeamPayload {
  name: string;
  mobile: string | null;
  email: string | null;
  scopes: OrgScopeAssignment[];
}

export interface CreateTeamPayload extends TeamPayload {
  userCode: string;
  password: string;
  isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class TeamsService {
  private readonly http = inject(HttpClient);

  list(pageNumber: number, pageSize: number, searchTerm?: string | null, isActive?: boolean | null):
    Observable<ApiResult<PaginatedResult<Team>>> {
    let params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', pageSize);

    if (searchTerm) {
      params = params.set('searchTerm', searchTerm);
    }

    if (isActive === true || isActive === false) {
      params = params.set('isActive', isActive);
    }

    return this.http.get<ApiResult<PaginatedResult<Team>>>(TEAMS_PATH, { params });
  }

  create(payload: CreateTeamPayload): Observable<ApiResult<Team>> {
    return this.http.post<ApiResult<Team>>(TEAMS_PATH, payload);
  }

  update(id: string, payload: TeamPayload): Observable<ApiResult<Team>> {
    return this.http.put<ApiResult<Team>>(`${TEAMS_PATH}/${id}`, payload);
  }

  setStatus(id: string, isActive: boolean): Observable<ApiResult<Team>> {
    return this.http.put<ApiResult<Team>>(`${TEAMS_PATH}/${id}/status`, { isActive });
  }

  resetPassword(id: string, newPassword: string): Observable<ApiResult<string>> {
    return this.http.post<ApiResult<string>>(`${TEAMS_PATH}/${id}/reset-password`, { newPassword });
  }
}
