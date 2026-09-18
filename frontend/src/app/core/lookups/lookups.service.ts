import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';

export type LookupType = 'Department' | 'Cluster' | 'Cbu' | 'Branch' | 'OperationArea';

export interface LookupItem {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  isActive: boolean;
  parentCode?: string | null;
}

const PATHS: Record<LookupType, string> = {
  Department: 'departments',
  Cluster: 'clusters',
  Cbu: 'cbus',
  Branch: 'branches',
  OperationArea: 'operation-areas',
};

@Injectable({ providedIn: 'root' })
export class LookupsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/lookups';

  list(type: LookupType, pageNumber: number, pageSize: number, searchTerm?: string): Observable<ApiResult<PaginatedResult<LookupItem>>> {
    let params = new HttpParams()
      .set('pageNumber', pageNumber)
      .set('pageSize', pageSize);
    if (searchTerm) params = params.set('searchTerm', searchTerm);
    return this.http.get<ApiResult<PaginatedResult<LookupItem>>>(`${this.baseUrl}/${PATHS[type]}`, { params });
  }

  create(type: LookupType, body: { code: string; nameEn: string; nameAr: string; parentCode?: string | null }): Observable<ApiResult<LookupItem>> {
    return this.http.post<ApiResult<LookupItem>>(`${this.baseUrl}/${PATHS[type]}`, body);
  }

  update(type: LookupType, id: string, body: { nameEn: string; nameAr: string; parentCode?: string | null }): Observable<ApiResult<LookupItem>> {
    return this.http.put<ApiResult<LookupItem>>(`${this.baseUrl}/${PATHS[type]}/${id}`, body);
  }

  setStatus(type: LookupType, id: string, isActive: boolean): Observable<ApiResult<LookupItem>> {
    return this.http.put<ApiResult<LookupItem>>(`${this.baseUrl}/${PATHS[type]}/${id}/status`, { isActive });
  }
}
