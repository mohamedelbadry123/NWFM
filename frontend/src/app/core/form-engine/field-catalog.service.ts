import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';
import { FORM_ENGINE_FIELD_CATALOG_PATH, FieldCatalogItem } from './form-engine.models';

/**
 * The catalog of canonical field names. Reusing a name in a new form reuses its stored column, so
 * the same answer stays comparable across forms — which is what the builder's autocomplete is for.
 */
@Injectable({ providedIn: 'root' })
export class FieldCatalogService {
  private readonly http = inject(HttpClient);

  search(term: string, take = 20): Observable<ApiResult<FieldCatalogItem[]>> {
    let params = new HttpParams().set('take', take);

    if (term) {
      params = params.set('search', term);
    }

    return this.http.get<ApiResult<FieldCatalogItem[]>>(FORM_ENGINE_FIELD_CATALOG_PATH, { params });
  }

  paged(pageNumber: number, pageSize: number, searchTerm?: string | null):
    Observable<ApiResult<PaginatedResult<FieldCatalogItem>>> {
    let params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', pageSize);

    if (searchTerm) {
      params = params.set('searchTerm', searchTerm);
    }

    return this.http.get<ApiResult<PaginatedResult<FieldCatalogItem>>>(
      `${FORM_ENGINE_FIELD_CATALOG_PATH}/paged`,
      { params },
    );
  }
}
