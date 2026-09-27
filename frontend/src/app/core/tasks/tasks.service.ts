import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';
import {
  AssignTaskPayload,
  CreateTaskPayload,
  EligibleTeam,
  FillTaskPayload,
  FormOption,
  ReturnTaskPayload,
  TASKS_PATH,
  C2mDispatchLog,
  C2mRetryResult,
  TaskDetail,
  TaskDetailsPayload,
  TaskFile,
  TaskFill,
  TaskFillResult,
  TaskHistoryEntry,
  TaskComputedColumn,
  TaskListItem,
  TaskListQuery,
} from './tasks.models';

/** The tasks API. Every response is the module's `{ isSuccess, value, error }` envelope. */
@Injectable({ providedIn: 'root' })
export class TasksService {
  private readonly http = inject(HttpClient);

  list(query: TaskListQuery): Observable<ApiResult<PaginatedResult<TaskListItem>>> {
    let params = new HttpParams()
      .set('pageNumber', query.pageNumber)
      .set('pageSize', query.pageSize);

    const scalars: Array<[string, string | boolean | null | undefined]> = [
      ['search', query.search?.trim()],
      ['taskTypeId', query.taskTypeId],
      ['source', query.source],
      ['priority', query.priority],
      ['clusterCode', query.clusterCode],
      ['cbuCode', query.cbuCode],
      ['branchCode', query.branchCode],
      ['operationAreaCode', query.operationAreaCode],
      ['departmentCode', query.departmentCode],
      ['teamId', query.teamId],
      ['returnReasonCode', query.returnReasonCode],
      ['createdFrom', query.createdFrom],
      ['createdTo', query.createdTo],
      ['sortField', query.sortField],
    ];

    for (const [name, value] of scalars) {
      if (value !== null && value !== undefined && value !== '') {
        params = params.set(name, value);
      }
    }

    for (const status of query.statuses ?? []) {
      params = params.append('statuses', status);
    }

    if (query.overdueOnly) {
      params = params.set('overdueOnly', true);
    }

    if (query.sortDescending !== undefined) {
      params = params.set('sortDescending', query.sortDescending);
    }

    return this.http.get<ApiResult<PaginatedResult<TaskListItem>>>(TASKS_PATH, { params });
  }

  get(id: string): Observable<ApiResult<TaskDetail>> {
    return this.http.get<ApiResult<TaskDetail>>(`${TASKS_PATH}/${id}`);
  }

  /** The task as a PDF report in `language` (`en` or `ar`); the file name comes in Content-Disposition. */
  exportPdf(id: string, language: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${TASKS_PATH}/${id}/export-pdf`, {
      params: new HttpParams().set('language', language),
      responseType: 'blob',
      observe: 'response',
    });
  }

  /** Every attempt to close the task's C2M field activity, newest first. */
  c2mLogs(id: string): Observable<ApiResult<C2mDispatchLog[]>> {
    return this.http.get<ApiResult<C2mDispatchLog[]>>(`${TASKS_PATH}/${id}/c2m/logs`);
  }

  /** Sends an approved task's refused or failed C2M closure again, now. */
  retryC2m(id: string): Observable<ApiResult<C2mRetryResult>> {
    return this.http.post<ApiResult<C2mRetryResult>>(`${TASKS_PATH}/${id}/c2m/retry`, {});
  }

  timeline(id: string): Observable<ApiResult<TaskHistoryEntry[]>> {
    return this.http.get<ApiResult<TaskHistoryEntry[]>>(`${TASKS_PATH}/${id}/timeline`);
  }

  /** Every fill, form by form in the task's order and newest first within each; `formId` narrows it to one form. */
  fills(id: string, formId?: string | null): Observable<ApiResult<TaskFill[]>> {
    const params = formId ? new HttpParams().set('formId', formId) : undefined;
    return this.http.get<ApiResult<TaskFill[]>>(`${TASKS_PATH}/${id}/fills`, { params });
  }

  files(id: string): Observable<ApiResult<TaskFile[]>> {
    return this.http.get<ApiResult<TaskFile[]>>(`${TASKS_PATH}/${id}/files`);
  }

  eligibleTeams(id: string): Observable<ApiResult<EligibleTeam[]>> {
    return this.http.get<ApiResult<EligibleTeam[]>>(`${TASKS_PATH}/${id}/eligible-teams`);
  }

  create(payload: CreateTaskPayload): Observable<ApiResult<string>> {
    return this.http.post<ApiResult<string>>(TASKS_PATH, payload);
  }

  update(id: string, payload: TaskDetailsPayload): Observable<ApiResult<unknown>> {
    return this.http.put<ApiResult<unknown>>(`${TASKS_PATH}/${id}`, payload);
  }

  assign(id: string, payload: AssignTaskPayload): Observable<ApiResult<unknown>> {
    return this.http.post<ApiResult<unknown>>(`${TASKS_PATH}/${id}/assign`, payload);
  }

  fill(id: string, payload: FillTaskPayload): Observable<ApiResult<TaskFillResult>> {
    return this.http.post<ApiResult<TaskFillResult>>(`${TASKS_PATH}/${id}/fill`, payload);
  }

  complete(id: string, note: string | null): Observable<ApiResult<unknown>> {
    return this.http.post<ApiResult<unknown>>(`${TASKS_PATH}/${id}/complete`, { note });
  }

  returnTask(id: string, payload: ReturnTaskPayload): Observable<ApiResult<unknown>> {
    return this.http.post<ApiResult<unknown>>(`${TASKS_PATH}/${id}/return`, payload);
  }

  expire(id: string, note: string | null): Observable<ApiResult<unknown>> {
    return this.http.post<ApiResult<unknown>>(`${TASKS_PATH}/${id}/expire`, { note });
  }

  /** Moves one unfilled form — or, with no `formId`, every unfilled form a newer version has overtaken. Answers how many moved. */
  migrateVersion(id: string, formId?: string | null): Observable<ApiResult<number>> {
    const url = formId
      ? `${TASKS_PATH}/${id}/forms/${formId}/migrate-version`
      : `${TASKS_PATH}/${id}/migrate-version`;
    return this.http.post<ApiResult<number>>(url, {});
  }

  /** The computed columns the worklist can show — a type's forms' when `taskTypeId` is given, else every type's. */
  computedColumns(taskTypeId?: string | null): Observable<ApiResult<TaskComputedColumn[]>> {
    const params = taskTypeId ? new HttpParams().set('taskTypeId', taskTypeId) : undefined;
    return this.http.get<ApiResult<TaskComputedColumn[]>>(`${TASKS_PATH}/computed-columns`, { params });
  }

  /** Published forms that can be added to a task on top of its type's. */
  formOptions(search?: string | null): Observable<ApiResult<FormOption[]>> {
    const params = search ? new HttpParams().set('search', search) : undefined;
    return this.http.get<ApiResult<FormOption[]>>(`${TASKS_PATH}/form-options`, { params });
  }

  attachForm(id: string, formDefinitionId: string): Observable<ApiResult<unknown>> {
    return this.http.post<ApiResult<unknown>>(`${TASKS_PATH}/${id}/forms`, { formDefinitionId });
  }

  detachForm(id: string, formDefinitionId: string): Observable<ApiResult<unknown>> {
    return this.http.delete<ApiResult<unknown>>(`${TASKS_PATH}/${id}/forms/${formDefinitionId}`);
  }
}
