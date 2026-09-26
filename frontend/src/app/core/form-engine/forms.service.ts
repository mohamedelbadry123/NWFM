import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';
import {
  CloneFormPayload,
  CreateFormPayload,
  FORM_ENGINE_FORMS_PATH,
  FormDetail,
  FormListItem,
  FormListQuery,
  FormVersionDetail,
  FormVersionSummary,
  UpdateFormPayload,
} from './form-engine.models';

/** The forms API. Every response is the module's `{ isSuccess, value, error }` envelope. */
@Injectable({ providedIn: 'root' })
export class FormsService {
  private readonly http = inject(HttpClient);

  list(query: FormListQuery): Observable<ApiResult<PaginatedResult<FormListItem>>> {
    let params = new HttpParams()
      .set('pageNumber', query.pageNumber)
      .set('pageSize', query.pageSize);

    if (query.searchTerm) {
      params = params.set('searchTerm', query.searchTerm);
    }

    if (query.category) {
      params = params.set('category', query.category);
    }

    if (query.status) {
      params = params.set('status', query.status);
    }

    if (query.departmentCode) {
      params = params.set('departmentCode', query.departmentCode);
    }

    if (query.excludeArchived) {
      params = params.set('excludeArchived', true);
    }

    return this.http.get<ApiResult<PaginatedResult<FormListItem>>>(FORM_ENGINE_FORMS_PATH, { params });
  }

  get(id: string): Observable<ApiResult<FormDetail>> {
    return this.http.get<ApiResult<FormDetail>>(`${FORM_ENGINE_FORMS_PATH}/${id}`);
  }

  create(payload: CreateFormPayload): Observable<ApiResult<FormDetail>> {
    return this.http.post<ApiResult<FormDetail>>(FORM_ENGINE_FORMS_PATH, payload);
  }

  update(id: string, payload: UpdateFormPayload): Observable<ApiResult<FormDetail>> {
    return this.http.put<ApiResult<FormDetail>>(`${FORM_ENGINE_FORMS_PATH}/${id}`, payload);
  }

  /** Saves the builder document as the working draft; nothing is published by this. */
  saveSchema(id: string, schemaJson: string): Observable<ApiResult<FormDetail>> {
    return this.http.put<ApiResult<FormDetail>>(`${FORM_ENGINE_FORMS_PATH}/${id}/schema`, { schemaJson });
  }

  publish(id: string): Observable<ApiResult<FormDetail>> {
    return this.http.post<ApiResult<FormDetail>>(`${FORM_ENGINE_FORMS_PATH}/${id}/publish`, {});
  }

  clone(id: string, payload: CloneFormPayload): Observable<ApiResult<FormDetail>> {
    return this.http.post<ApiResult<FormDetail>>(`${FORM_ENGINE_FORMS_PATH}/${id}/clone`, payload);
  }

  deprecate(id: string): Observable<ApiResult<FormDetail>> {
    return this.http.post<ApiResult<FormDetail>>(`${FORM_ENGINE_FORMS_PATH}/${id}/deprecate`, {});
  }

  archive(id: string): Observable<ApiResult<FormDetail>> {
    return this.http.post<ApiResult<FormDetail>>(`${FORM_ENGINE_FORMS_PATH}/${id}/archive`, {});
  }

  versions(formId: string): Observable<ApiResult<FormVersionSummary[]>> {
    return this.http.get<ApiResult<FormVersionSummary[]>>(`${FORM_ENGINE_FORMS_PATH}/${formId}/versions`);
  }

  /** One published version with its frozen schema; omit the number for the current one. */
  version(formId: string, versionNo?: number | null): Observable<ApiResult<FormVersionDetail>> {
    const suffix = versionNo == null ? 'current' : String(versionNo);

    return this.http.get<ApiResult<FormVersionDetail>>(`${FORM_ENGINE_FORMS_PATH}/${formId}/versions/${suffix}`);
  }
}
