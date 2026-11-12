import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AuthStore } from '@core/auth/auth.store';
import { AppContextService } from '@core/context/app-context.service';
import { LocaleService } from '@core/i18n/locale.service';
import { provideDynamicForms } from '@shared/components/dynamic-form/provide-dynamic-forms';
import { WorkflowAssignmentGroupsService } from '../workflow-assignment-groups.service';
import { WorkflowDepartmentsService } from '../workflow-departments.service';
import { WorkflowMappingService } from '../mapping/workflow-mapping.service';
import { WorkflowRuntimeService } from '../workflow-runtime.service';
import {
  WorkflowWorkItemsService,
  type WorkflowWorkItemView,
} from '../workflow-work-items.service';
import { WorkflowTaskDetailComponent } from './workflow-task-detail.component';

const schemaJson = JSON.stringify({
  name_en: 'Text form',
  elements: [
    { type: 'text', data_name: 'notes', label_en: 'Notes' },
    { type: 'text', data_name: 'approved', label_en: 'Approved' },
  ],
});
const selectedForm = {
  formId: 'form-1',
  versionNo: 2,
  schemaJson,
  visitId: 'visit-1',
  stepId: 'step-1',
};
const mapping = {
  invocationId: 'inv-1',
  visitId: 'visit-1',
  status: 'Completed',
  values: [
    {
      bindingId: 'b-notes',
      formStepId: 'step-1',
      formId: 'form-1',
      formVersion: 2,
      fieldKey: 'notes',
      mode: 'ReadOnly',
      value: null,
    },
    {
      bindingId: 'b-approved',
      formStepId: 'step-1',
      formId: 'form-1',
      formVersion: 2,
      fieldKey: 'approved',
      mode: 'Prefill',
      value: 'initial-prefill',
    },
  ],
};

/** The work item the server returns in the state of one draft attempt (same visit, its own draft key). */
const claimedItem = (draftKey: string, approved: string): WorkflowWorkItemView =>
  ({
    id: 'task-1',
    status: 'Claimed',
    assignmentGroupName: 'Group',
    selectedForm,
    formFields: [],
    formVisitId: 'visit-1',
    formDraftKey: draftKey,
    formValues: { approved },
    mappingSnapshot: mapping,
    availableOutcomes: [],
  }) as unknown as WorkflowWorkItemView;

/** What reassign returns: the new assignment only, without the form, the answers or a draft key. */
const sparseReassigned = (): WorkflowWorkItemView =>
  ({
    id: 'task-1',
    status: 'Pending',
    assignmentGroupName: 'Other group',
    availableOutcomes: [],
  }) as unknown as WorkflowWorkItemView;

describe('Workflow task detail draft attempts (rendered host)', () => {
  let current: WorkflowWorkItemView;
  let claimResponse: WorkflowWorkItemView;
  let reassignResponse: WorkflowWorkItemView;
  let submitted: { id: string; payload: { formValues?: Record<string, unknown> } }[];
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    current = claimedItem('attempt-1', 'initial-prefill');
    claimResponse = claimedItem('attempt-2', 'server-value');
    reassignResponse = sparseReassigned();
    submitted = [];
    TestBed.configureTestingModule({
      imports: [WorkflowTaskDetailComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        provideDynamicForms(),
        provideNoopAnimations(),
        { provide: LocaleService, useValue: { locale: () => 'en', isRtl: () => false } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'task-1' }) } },
        },
        {
          provide: WorkflowWorkItemsService,
          useValue: {
            getById: () => of(current),
            claim: () => of(claimResponse),
            reassign: () => of(reassignResponse),
            complete: (id: string, payload: { formValues?: Record<string, unknown> }) => {
              submitted.push({ id, payload });
              return of({ id, status: 'Completed' } as WorkflowWorkItemView);
            },
            release: () => of(undefined),
          },
        },
        { provide: WorkflowRuntimeService, useValue: {} },
        { provide: WorkflowMappingService, useValue: {} },
        {
          provide: WorkflowAssignmentGroupsService,
          useValue: { getPaged: () => of({ items: [{ id: 'group-2', name: 'Other group' }] }) },
        },
        { provide: WorkflowDepartmentsService, useValue: { getPaged: () => of({ items: [] }) } },
        {
          provide: AppContextService,
          useValue: { tenant: signal({ id: 'tenant-1' }), selectedParticipantId: () => '' },
        },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const auth = TestBed.inject(AuthStore);
    auth.setSession({
      isSuccess: true,
      value: {
        accessToken: 'a',
        refreshToken: 'r',
        expiresInSeconds: 60,
        userName: 'sara',
        roles: [],
        permissions: [],
      },
      error: { code: '', message: '' },
    });
    auth.updateProfile({
      userId: 'user-1',
      userName: 'sara',
      email: null,
      roles: [],
      permissions: [],
      teamId: null,
      isUnrestrictedScope: false,
    });
  });
  afterEach(() => {
    sessionStorage.clear();
    localStorage.clear();
  });

  /** The host polls with a 4 s `interval`, so `whenStable()` never resolves; macrotask turns settle the emissions. */
  const settle = async (fixture: { detectChanges(): void }) => {
    for (let turn = 0; turn < 3; turn++) {
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve));
    }
    fixture.detectChanges();
  };
  type Fixture = Awaited<ReturnType<typeof open>>;
  const open = async () => {
    const fixture = TestBed.createComponent(WorkflowTaskDetailComponent);
    await settle(fixture);
    return fixture;
  };
  const host = (fixture: Fixture) =>
    fixture.componentInstance as unknown as {
      formValues: Record<string, unknown>;
      claim(): void;
      reloadItem(): void;
    };
  const input = (fixture: Fixture, value: string) =>
    Array.from(fixture.nativeElement.querySelectorAll('input')).find(
      (i) => (i as HTMLInputElement).value === value,
    ) as HTMLInputElement | undefined;
  const type = async (fixture: Fixture, from: string, to: string) => {
    const field = input(fixture, from)!;
    field.value = to;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    await settle(fixture);
  };
  const clickTab = async (fixture: Fixture, index: number) => {
    (fixture.nativeElement.querySelectorAll('button[role="tab"]')[index] as HTMLElement).click();
    await settle(fixture);
  };
  const clickByText = async (fixture: Fixture, text: string) => {
    const button = (
      Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLElement[]
    ).find((b) => b.textContent?.includes(text));
    button!.click();
    await settle(fixture);
  };
  const reassignToOtherGroup = async (fixture: Fixture) => {
    const select = fixture.nativeElement.querySelector('#reassignGroup') as HTMLSelectElement;
    select.value = 'group-2';
    select.dispatchEvent(new Event('change', { bubbles: true }));
    await settle(fixture);
    await clickByText(fixture, 'workflow.runtime.task_detail.reassign_btn');
  };
  /** Types the action, submits the Action tab and returns what was posted. */
  const submit = async (fixture: Fixture) => {
    const action = fixture.nativeElement.querySelector('#actionTaken') as HTMLInputElement;
    action.value = 'APPROVE';
    action.dispatchEvent(new Event('input', { bubbles: true }));
    await settle(fixture);
    // The pinned form renders its own <form>; the completion form is the one holding the action input.
    action.closest('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);
    return submitted;
  };

  it('shows the server answers on the Details tab, not the edits typed in the Action tab', async () => {
    const fixture = await open();
    await clickTab(fixture, 0);
    await type(fixture, 'initial-prefill', 'edited-by-me');
    expect(host(fixture).formValues['approved']).toBe('edited-by-me');

    await clickTab(fixture, 1);
    expect(input(fixture, 'edited-by-me')).toBeUndefined();
    expect(input(fixture, 'initial-prefill')).toBeDefined();
  });

  it('does not submit the previous attempt after a sparse reassign and an enriched claim under a new draft key', async () => {
    const fixture = await open();
    await clickTab(fixture, 0);
    await type(fixture, 'initial-prefill', 'edited-by-me');
    await clickTab(fixture, 1);

    // Reassigned: the response is sparse (no form, answers or key); the claim then returns the same visit under a new key.
    await reassignToOtherGroup(fixture);
    expect(host(fixture).formValues).toEqual({});
    host(fixture).claim();
    await settle(fixture);

    expect(host(fixture).formValues).toEqual({ approved: 'server-value' });
    expect(input(fixture, 'edited-by-me')).toBeUndefined();
    expect(input(fixture, 'server-value')).toBeDefined();

    await clickTab(fixture, 0);
    expect(input(fixture, 'edited-by-me')).toBeUndefined();
    expect(input(fixture, 'server-value')).toBeDefined();
    const posts = await submit(fixture);
    expect(posts).toHaveLength(1);
    expect(posts[0].payload.formValues).toEqual(
      expect.objectContaining({ approved: 'server-value' }),
    );
    expect(JSON.stringify(posts[0].payload)).not.toContain('edited-by-me');
    expect(navigate).toHaveBeenCalled();
  });

  it('resets the host answers when a claim returns the same visit under a new draft key', async () => {
    const fixture = await open();
    await clickTab(fixture, 0);
    await type(fixture, 'initial-prefill', 'edited-by-me');

    host(fixture).claim();
    await settle(fixture);

    expect(host(fixture).formValues).toEqual({ approved: 'server-value' });
    expect(input(fixture, 'edited-by-me')).toBeUndefined();
    expect(input(fixture, 'server-value')).toBeDefined();
    const posts = await submit(fixture);
    expect(JSON.stringify(posts[0].payload)).not.toContain('edited-by-me');
  });

  describe('a task without a pinned form (inline fields rendered by the host)', () => {
    const legacyItem = (draftKey: string, notes: string): WorkflowWorkItemView =>
      ({
        id: 'task-1',
        status: 'Claimed',
        assignmentGroupName: 'Group',
        formFields: [
          { key: 'notes', labelEn: 'Notes', labelAr: 'ملاحظات', type: 'text', required: false },
        ],
        formVisitId: 'visit-1',
        formDraftKey: draftKey,
        formValues: { notes },
        availableOutcomes: [],
      }) as unknown as WorkflowWorkItemView;

    it('does not submit the previous attempt after a sparse reassign and an enriched claim under a new draft key', async () => {
      current = legacyItem('attempt-1', 'server-notes');
      claimResponse = legacyItem('attempt-2', 'server-notes');
      const fixture = await open();
      await clickTab(fixture, 0);
      await type(fixture, 'server-notes', 'typed-notes');
      expect(host(fixture).formValues['notes']).toBe('typed-notes');

      await reassignToOtherGroup(fixture);
      host(fixture).claim();
      await settle(fixture);
      await clickTab(fixture, 0);

      expect(host(fixture).formValues).toEqual({ notes: 'server-notes' });
      expect(input(fixture, 'typed-notes')).toBeUndefined();
      const posts = await submit(fixture);
      expect(posts[0].payload.formValues).toEqual({ notes: 'server-notes' });
    });

    it('resets when a claim returns a new draft key and keeps the edit when the key is unchanged', async () => {
      current = legacyItem('attempt-1', 'server-notes');
      const fixture = await open();
      await clickTab(fixture, 0);
      await type(fixture, 'server-notes', 'typed-notes');

      claimResponse = legacyItem('attempt-1', 'server-notes');
      host(fixture).claim();
      await settle(fixture);
      expect(host(fixture).formValues['notes']).toBe('typed-notes');
      expect(input(fixture, 'typed-notes')).toBeDefined();

      claimResponse = legacyItem('attempt-2', 'server-notes');
      host(fixture).claim();
      await settle(fixture);
      expect(host(fixture).formValues).toEqual({ notes: 'server-notes' });
      expect(input(fixture, 'typed-notes')).toBeUndefined();
    });
  });

  it('keeps and submits the edits when the same attempt is claimed or re-delivered', async () => {
    const fixture = await open();
    await clickTab(fixture, 0);
    await type(fixture, 'initial-prefill', 'edited-by-me');

    claimResponse = claimedItem('attempt-1', 'initial-prefill');
    host(fixture).claim();
    await settle(fixture);
    host(fixture).reloadItem();
    await settle(fixture);
    // A reload of a claimed item lands on the Details tab; the Action form is mounted again from the host answers.
    await clickTab(fixture, 0);

    expect(host(fixture).formValues['approved']).toBe('edited-by-me');
    expect(input(fixture, 'edited-by-me')).toBeDefined();
    const posts = await submit(fixture);
    expect(posts[0].payload.formValues).toEqual(
      expect.objectContaining({ approved: 'edited-by-me' }),
    );
  });
});
