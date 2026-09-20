import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';
import {
  FORM_ENGINE_FORMS_PATH,
  FormSubmissionCreated,
  FormSubmissionRow,
  SubmitFormPayload,
} from './form-engine.models';

@Injectable({ providedIn: 'root' })
export class FormSubmissionsService {
  private readonly http = inject(HttpClient);

  /**
   * Records a fill. The payload carries a client key, so a retry after a lost response answers with
   * the original submission instead of writing a second row.
   */
  submit(formId: string, payload: SubmitFormPayload): Observable<ApiResult<FormSubmissionCreated>> {
    return this.http.post<ApiResult<FormSubmissionCreated>>(
      `${FORM_ENGINE_FORMS_PATH}/${formId}/submissions`,
      payload,
    );
  }

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
