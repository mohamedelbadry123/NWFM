import { EMPTY_ORG_LOCATION } from '../../../shared/components/org-scope/org-scope.model';
import { TaskListState, buildTaskListQuery, toDateParam } from './task-list-query';

describe('buildTaskListQuery', () => {
  const blank: TaskListState = {
    page: 1,
    pageSize: 10,
    search: '',
    status: null,
    taskTypeId: null,
    priority: null,
    source: null,
    returnReasonCode: null,
    createdFrom: null,
    createdTo: null,
    orgFilter: { ...EMPTY_ORG_LOCATION },
    sortField: null,
    sortOrder: -1,
  };

  it('asks for the page in hand, newest first, when nothing is set', () => {
    const query = buildTaskListQuery(blank);

    expect(query.pageNumber).toBe(1);
    expect(query.pageSize).toBe(10);
    expect(query.statuses).toBeNull();
    expect(query.sortField).toBeNull();
    expect(query.sortDescending).toBeTrue();
    expect(query.clusterCode).toBeNull();
    expect(query.createdFrom).toBeNull();
  });

  it('sends the one picked status as the set the API filters on', () => {
    expect(buildTaskListQuery({ ...blank, status: 'RETURNED' }).statuses).toEqual(['RETURNED']);
  });

  it('passes the caption filters through', () => {
    const query = buildTaskListQuery({
      ...blank,
      page: 3,
      pageSize: 25,
      search: 'TSK-2609',
      taskTypeId: 'type-1',
      priority: 'URGENT',
      source: 'MANUAL',
      returnReasonCode: 'WRONG_LOCATION',
    });

    expect(query).toEqual(jasmine.objectContaining({
      pageNumber: 3,
      pageSize: 25,
      search: 'TSK-2609',
      taskTypeId: 'type-1',
      priority: 'URGENT',
      source: 'MANUAL',
      returnReasonCode: 'WRONG_LOCATION',
    }));
  });

  it('sends every level of the applied org filter', () => {
    const query = buildTaskListQuery({
      ...blank,
      orgFilter: { clusterCode: 'CENTRAL', cbuCode: 'RCBU', branchCode: 'R-16', operationAreaCode: null },
    });

    expect(query.clusterCode).toBe('CENTRAL');
    expect(query.cbuCode).toBe('RCBU');
    expect(query.branchCode).toBe('R-16');
    expect(query.operationAreaCode).toBeNull();
  });

  it('sends picked days as local calendar dates', () => {
    const query = buildTaskListQuery({
      ...blank,
      createdFrom: new Date(2026, 0, 5, 23, 30),
      createdTo: new Date(2026, 8, 21),
    });

    expect(query.createdFrom).toBe('2026-01-05');
    expect(query.createdTo).toBe('2026-09-21');
  });

  it('maps a sortable column and its direction', () => {
    expect(buildTaskListQuery({ ...blank, sortField: 'dueDate', sortOrder: 1 })).toEqual(
      jasmine.objectContaining({ sortField: 'dueDate', sortDescending: false }),
    );
    expect(buildTaskListQuery({ ...blank, sortField: 'priority', sortOrder: -1 })).toEqual(
      jasmine.objectContaining({ sortField: 'priority', sortDescending: true }),
    );
  });

  it('drops a column the API cannot sort by', () => {
    expect(buildTaskListQuery({ ...blank, sortField: 'teamName', sortOrder: 1 }).sortField).toBeNull();
  });
});

describe('toDateParam', () => {
  it('pads month and day', () => {
    expect(toDateParam(new Date(2026, 2, 7))).toBe('2026-03-07');
  });

  it('is null for no date', () => {
    expect(toDateParam(null)).toBeNull();
  });
});
