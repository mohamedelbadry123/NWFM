import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { EMPTY, expand, map, Observable, reduce } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';

export type LookupType = 'Department' | 'Cluster' | 'Cbu' | 'Branch' | 'OperationArea' | 'FieldActivityType' | 'ActivitySource';

export type ActivitySourceKind = 'Internal' | 'External';

export interface LookupItem {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  isActive: boolean;
  parentCode?: string | null;
  /** Activity sources only. */
  kind?: ActivitySourceKind | null;
  /** Activity sources only; required when External. */
  url?: string | null;
  /** Activity types only: the sources allowed to create one. */
  sourceCodes?: string[] | null;
}

/** What a lookup is created and edited with; each type reads the fields it has. */
export interface LookupWrite {
  nameEn: string;
  nameAr: string;
  parentCode?: string | null;
  kind?: ActivitySourceKind | null;
  url?: string | null;
  sourceCodes?: string[] | null;
}

const PATHS: Record<LookupType, string> = {
  Department: 'departments',
  FieldActivityType: 'field-activity-types',
  ActivitySource: 'activity-sources',
  Cluster: 'clusters',
  Cbu: 'cbus',
  Branch: 'branches',
  OperationArea: 'operation-areas',
};

@Injectable({ providedIn: 'root' })
export class LookupsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/lookups';

  list(
    type: LookupType,
    pageNumber: number,
    pageSize: number,
    searchTerm?: string,
    options?: { parentCode?: string; isActive?: boolean },
  ): Observable<ApiResult<PaginatedResult<LookupItem>>> {
    let params = new HttpParams()
      .set('pageNumber', pageNumber)
      .set('pageSize', pageSize);
    if (searchTerm) params = params.set('searchTerm', searchTerm);
    if (options?.parentCode) params = params.set('parentCode', options.parentCode);
    if (options?.isActive === true || options?.isActive === false) {
      params = params.set('isActive', options.isActive);
    }
    return this.http.get<ApiResult<PaginatedResult<LookupItem>>>(`${this.baseUrl}/${PATHS[type]}`, { params });
  }

  listAll(type: LookupType, options?: { parentCode?: string; isActive?: boolean }): Observable<LookupItem[]> {
    const pageSize = 500;
    const page = (number: number) => this.list(type, number, pageSize, undefined, options)
      .pipe(map(result => ({ number, value: result.value })));
    return page(1).pipe(
      expand(({ number, value }) => value?.items.length && number * pageSize < value.totalCount ? page(number + 1) : EMPTY),
      reduce((items, { value }) => [...items, ...(value?.items ?? [])], [] as LookupItem[]),
    );
  }

  create(type: LookupType, body: { code: string } & LookupWrite): Observable<ApiResult<LookupItem>> {
    return this.http.post<ApiResult<LookupItem>>(`${this.baseUrl}/${PATHS[type]}`, body);
  }

  update(type: LookupType, id: string, body: LookupWrite): Observable<ApiResult<LookupItem>> {
    return this.http.put<ApiResult<LookupItem>>(`${this.baseUrl}/${PATHS[type]}/${id}`, body);
  }

  setStatus(type: LookupType, id: string, isActive: boolean): Observable<ApiResult<LookupItem>> {
    return this.http.put<ApiResult<LookupItem>>(`${this.baseUrl}/${PATHS[type]}/${id}/status`, { isActive });
  }
}
