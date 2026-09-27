import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { TasksService } from './tasks.service';
import { TASKS_PATH } from './tasks.models';

describe('TasksService.list', () => {
  let service: TasksService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(TasksService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function paramsOf(query: Parameters<TasksService['list']>[0]) {
    service.list(query).subscribe();
    const request = http.expectOne((r) => r.url === TASKS_PATH);
    request.flush({ isSuccess: true, value: { items: [], totalCount: 0 } });
    return request.request.params;
  }

  it('leaves unset and blank filters off the query string', () => {
    const params = paramsOf({
      pageNumber: 2,
      pageSize: 25,
      search: '   ',
      taskTypeId: null,
      cbuCode: '',
      statuses: null,
    });

    expect(params.keys().sort()).toEqual(['pageNumber', 'pageSize']);
    expect(params.get('pageNumber')).toBe('2');
  });

  it('trims the search and repeats statuses for the API to bind as a list', () => {
    const params = paramsOf({
      pageNumber: 1,
      pageSize: 10,
      search: '  TSK-0001 ',
      statuses: ['SUBMITTED', 'RETURNED'],
    });

    expect(params.get('search')).toBe('TSK-0001');
    expect(params.getAll('statuses')).toEqual(['SUBMITTED', 'RETURNED']);
  });

  it('sends the sort and its direction', () => {
    const params = paramsOf({ pageNumber: 1, pageSize: 10, sortField: 'dueDate', sortDescending: false });

    expect(params.get('sortField')).toBe('dueDate');
    expect(params.get('sortDescending')).toBe('false');
  });
});
