import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';

import { WorkflowRequestsComponent } from './workflow-requests.component';
import { WorkflowRuntimeService } from '../workflow-runtime.service';
import { WorkflowAssignmentGroupsService } from '../workflow-assignment-groups.service';
import type { WorkflowRequestPageView } from '@core/models/workflow-ops.models';

const PAGE: WorkflowRequestPageView = {
  totalCount: 1,
  pageNumber: 1,
  pageSize: 10,
  kpis: { total: 1, inProgress: 1, completed: 0, breached: 0 },
  items: [
    {
      id: 'req-1',
      requestNumber: 'WF-2026-000001',
      workflowInstanceId: 'inst-1',
      serviceNameEn: 'Consent Management',
      serviceNameAr: 'إدارة الموافقات',
      requestDate: '2026-08-12T10:00:00Z',
      status: 'Running',
      currentActivityNameEn: 'Privacy Review',
      originalAssignedGroupName: 'Privacy Reviewers',
      currentTaskSlaMinutes: 60,
      remainingSlaMinutes: 32,
    },
  ],
};

describe('WorkflowRequestsComponent', () => {
  let fixture: ComponentFixture<WorkflowRequestsComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorkflowRequestsComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        {
          provide: WorkflowRuntimeService,
          useValue: { listRequests: () => of(PAGE) },
        },
        {
          provide: WorkflowAssignmentGroupsService,
          useValue: { getPaged: () => of({ items: [] }) },
        },
      ],
    });
    fixture = TestBed.createComponent(WorkflowRequestsComponent);
    fixture.detectChanges();
  });

  it('renders the eight required request columns', () => {
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('WF-2026-000001');
    expect(text).toContain('Consent Management');
    expect(text).toContain('Privacy Review');
    expect(text).toContain('Privacy Reviewers');
    expect(text).toContain('01h 00m');
    expect(text).toContain('00h 32m');
  });
});
