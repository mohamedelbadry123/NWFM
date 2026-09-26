import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiResult, PaginatedResult } from '../api/api-result';
import {
  CreateTaskTypePayload,
  FormOption,
  TASK_TYPES_PATH,
  TaskType,
  TaskTypePayload,
} from './tasks.models';

@Injectable({ providedIn: 'root' })
export class TaskTypesService {
  private readonly http = inject(HttpClient);

  list(pageNumber: number, pageSize: number, searchTerm?: string | null, isActive?: boolean | null):
    Observable<ApiResult<PaginatedResult<TaskType>>> {
    let params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', pageSize);

    if (searchTerm) {
      params = params.set('searchTerm', searchTerm);
    }

    if (isActive === true || isActive === false) {
      params = params.set('isActive', isActive);
    }

    return this.http.get<ApiResult<PaginatedResult<TaskType>>>(TASK_TYPES_PATH, { params });
  }

  active(): Observable<ApiResult<TaskType[]>> {
    return this.http.get<ApiResult<TaskType[]>>(`${TASK_TYPES_PATH}/active`);
  }

  formOptions(search?: string | null): Observable<ApiResult<FormOption[]>> {
    const params = search ? new HttpParams().set('search', search) : undefined;
    return this.http.get<ApiResult<FormOption[]>>(`${TASK_TYPES_PATH}/form-options`, { params });
  }

  create(payload: CreateTaskTypePayload): Observable<ApiResult<TaskType>> {
    return this.http.post<ApiResult<TaskType>>(TASK_TYPES_PATH, payload);
  }

  update(id: string, payload: TaskTypePayload): Observable<ApiResult<TaskType>> {
    return this.http.put<ApiResult<TaskType>>(`${TASK_TYPES_PATH}/${id}`, payload);
  }

  setStatus(id: string, isActive: boolean): Observable<ApiResult<TaskType>> {
    return this.http.put<ApiResult<TaskType>>(`${TASK_TYPES_PATH}/${id}/status`, { isActive });
  }
}
