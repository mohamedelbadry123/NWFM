import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';
import {
  AssignTaskPayload,
  CreateTaskPayload,
  EligibleTeam,
  FillTaskPayload,
  ReturnTaskPayload,
  TASKS_PATH,
  TaskDetail,
  TaskDetailsPayload,
  TaskFile,
  TaskFill,
  TaskFillResult,
  TaskHistoryEntry,
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

  timeline(id: string): Observable<ApiResult<TaskHistoryEntry[]>> {
    return this.http.get<ApiResult<TaskHistoryEntry[]>>(`${TASKS_PATH}/${id}/timeline`);
  }

  fills(id: string): Observable<ApiResult<TaskFill[]>> {
    return this.http.get<ApiResult<TaskFill[]>>(`${TASKS_PATH}/${id}/fills`);
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

  migrateVersion(id: string): Observable<ApiResult<number>> {
    return this.http.post<ApiResult<number>>(`${TASKS_PATH}/${id}/migrate-version`, {});
  }
}
