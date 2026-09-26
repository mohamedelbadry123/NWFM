import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';
import { C2M_ACTION_MAPPINGS_PATH, C2mActionMapping, C2mActionMappingPayload } from './tasks.models';

/** The C2M action mapping lookup — what each Action Taken code closes a field activity with. */
@Injectable({ providedIn: 'root' })
export class C2mActionMappingsService {
  private readonly http = inject(HttpClient);

  list(query: { search?: string | null; pageNumber: number; pageSize: number }): Observable<ApiResult<PaginatedResult<C2mActionMapping>>> {
    let params = new HttpParams().set('pageNumber', query.pageNumber).set('pageSize', query.pageSize);
    const search = query.search?.trim();
    if (search) {
      params = params.set('search', search);
    }

    return this.http.get<ApiResult<PaginatedResult<C2mActionMapping>>>(C2M_ACTION_MAPPINGS_PATH, { params });
  }

  create(payload: C2mActionMappingPayload & { actionCode: string }): Observable<ApiResult<string>> {
    return this.http.post<ApiResult<string>>(C2M_ACTION_MAPPINGS_PATH, payload);
  }

  update(id: string, payload: C2mActionMappingPayload): Observable<ApiResult<void>> {
    return this.http.put<ApiResult<void>>(`${C2M_ACTION_MAPPINGS_PATH}/${id}`, payload);
  }

  setStatus(id: string, isActive: boolean): Observable<ApiResult<void>> {
    return this.http.put<ApiResult<void>>(`${C2M_ACTION_MAPPINGS_PATH}/${id}/status`, { isActive });
  }
}
