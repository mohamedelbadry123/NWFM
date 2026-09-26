import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';
import { FORM_ENGINE_FORMS_PATH, FormSubmissionRow } from './form-engine.models';

@Injectable({ providedIn: 'root' })
export class FormSubmissionsService {
  private readonly http = inject(HttpClient);

  list(formId: string, pageNumber: number, pageSize: number, context?: { type?: string | null; id?: string | null }):
    Observable<ApiResult<PaginatedResult<FormSubmissionRow>>> {
    let params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', pageSize);

    if (context?.type && context?.id) {
      params = params.set('contextType', context.type).set('contextId', context.id);
    }

    return this.http.get<ApiResult<PaginatedResult<FormSubmissionRow>>>(
      `${FORM_ENGINE_FORMS_PATH}/${formId}/submissions`,
      { params },
    );
  }

  get(formId: string, submissionId: string): Observable<ApiResult<FormSubmissionRow>> {
    return this.http.get<ApiResult<FormSubmissionRow>>(
      `${FORM_ENGINE_FORMS_PATH}/${formId}/submissions/${submissionId}`,
    );
  }
}
