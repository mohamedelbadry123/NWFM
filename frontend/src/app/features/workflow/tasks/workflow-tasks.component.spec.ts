import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';

import { WorkflowTasksComponent } from './workflow-tasks.component';
import { WorkflowWorkItemsService } from '../workflow-work-items.service';
import { WorkflowWorkloadService } from '../workflow-workload.service';
import type { WorkflowWorkItemView } from '../workflow-work-items.service';

const ITEM: WorkflowWorkItemView = {
  id: 'wi-1',
  requestNumber: 'WF-2026-000002',
  serviceNameEn: 'Consent Management',
  requestDate: '2026-08-12T09:00:00Z',
  status: 'Pending',
  slaDurationMinutes: 120,
  remainingSlaMinutes: 45,
};

describe('WorkflowTasksComponent', () => {
  let fixture: ComponentFixture<WorkflowTasksComponent>;
  let claimSpy: jasmine.Spy;

  beforeEach(() => {
    claimSpy = jasmine.createSpy('claim').and.returnValue(of(ITEM));
    TestBed.configureTestingModule({
      imports: [WorkflowTasksComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        {
          provide: WorkflowWorkItemsService,
          useValue: {
            getAvailable: () => of([ITEM]),
            getMyWorkItems: () => of([]),
            getOverdue: () => of([]),
            claim: claimSpy,
            release: () => of(undefined),
          },
        },
        {
          provide: WorkflowWorkloadService,
          useValue: { getWorkload: () => of({ totals: { completedToday: 12 } }) },
        },
      ],
    });
    fixture = TestBed.createComponent(WorkflowTasksComponent);
    fixture.detectChanges();
  });

  it('lists available tasks with request columns and a Claim action', () => {
    const host = fixture.nativeElement as HTMLElement;
    const text = host.textContent ?? '';
    expect(text).toContain('WF-2026-000002');
    expect(text).toContain('Consent Management');
    expect(text).toContain('00h 45m');
    expect(host.querySelector('button')?.textContent).toBeTruthy();
  });
});
