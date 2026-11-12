import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Component, Directive, EventEmitter, Input, Output, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, RouterLink, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AuthStore } from '@core/auth/auth.store';
import { AppContextService } from '@core/context/app-context.service';
import { LocaleService } from '@core/i18n/locale.service';
import { provideDynamicForms } from '@shared/components/dynamic-form/provide-dynamic-forms';
import { WorkflowRoutingService } from '../designer/workflow-routing.service';
import { WorkflowIntegrationsService } from '../integrations/workflow-integrations.service';
import { WorkflowMappingInvocationsComponent } from '../mapping/workflow-mapping-invocations.component';
import { WorkflowInstanceWorkspaceComponent } from './workflow-instance-workspace.component';
import { WorkflowWorkspaceService, type WorkspaceDetail } from './workflow-workspace.service';

@Component({ selector: 'app-workflow-mapping-invocations', standalone: true, template: '' })
class InvocationsStub {
  @Input() rootInstanceId = '';
  @Input() refreshToken: unknown;
  @Input() canManage = false;
  @Output() loaded = new EventEmitter<unknown[]>();
}

@Directive({ selector: '[routerLink]', standalone: true })
class RouterLinkStub {
  @Input() routerLink: unknown;
}

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

/** The workspace detail the server returns for the one task, in the state the polling would report. */
const detailFor = (state: {
  canAct: boolean;
  draftKey: string | null;
  values: Record<string, unknown>;
  status?: string;
  /** A task without a pinned form: the host renders its own formFields inputs from formValues. */
  legacy?: boolean;
}) =>
  ({
    id: 'instance-1',
    isDemo: false,
    location: [],
    history: [],
    operations: [],
    tree: [
      {
        id: 'run-1',
        name: 'Run',
        status: 'Active',
        activities: [
          {
            id: 'act-1',
            nodeKey: 'n',
            name: 'Inspect',
            type: 'Business',
            status: 'Active',
            startedAt: '2026-11-11T00:00:00Z',
            tasks: [
              {
                canAct: state.canAct,
                disabledReason: 'Claimed by another user.',
                task: {
                  id: 'task-1',
                  status: state.status ?? (state.canAct ? 'Claimed' : 'Pending'),
                  assignmentGroupName: 'Group',
                  selectedForm: state.legacy ? undefined : selectedForm,
                  formFields: state.legacy
                    ? [
                        {
                          key: 'notes',
                          labelEn: 'Notes',
                          labelAr: 'ملاحظات',
                          type: 'text',
                          required: false,
                        },
                      ]
                    : [],
                  formVisitId: 'visit-1',
                  formDraftKey: state.draftKey,
                  formValues: state.values,
                  mappingSnapshot: state.legacy ? null : mapping,
                  availableOutcomes: [],
                },
              },
            ],
          },
        ],
      },
    ],
  }) as unknown as WorkspaceDetail;

describe('Workflow instance workspace draft attempts (rendered host)', () => {
  let server = detailFor({
    canAct: true,
    draftKey: 'attempt-1',
    values: { approved: 'initial-prefill' },
  });

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    server = detailFor({
      canAct: true,
      draftKey: 'attempt-1',
      values: { approved: 'initial-prefill' },
    });
    TestBed.configureTestingModule({
      imports: [WorkflowInstanceWorkspaceComponent, TranslateModule.forRoot()],
      providers: [
        provideDynamicForms(),
        provideNoopAnimations(),
        { provide: LocaleService, useValue: { locale: () => 'en', isRtl: () => false } },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ id: 'instance-1' })) },
        },
        { provide: WorkflowWorkspaceService, useValue: { detail: () => of(server) } },
        { provide: WorkflowRoutingService, useValue: { progress: () => of([]) } },
        { provide: WorkflowIntegrationsService, useValue: {} },
        {
          provide: AppContextService,
          useValue: { tenant: signal({ id: 'tenant-1' }), selectedParticipantId: () => '' },
        },
      ],
    });
    TestBed.overrideComponent(WorkflowInstanceWorkspaceComponent, {
      remove: { imports: [WorkflowMappingInvocationsComponent, RouterLink] },
      add: { imports: [InvocationsStub, RouterLinkStub] },
    });
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

  /**
   * The host polls with a 4 s `interval`, so `whenStable()` would never resolve; macrotask turns let the microtask
   * emissions and the renderer's ngModel writes settle instead.
   */
  const settle = async (fixture: { detectChanges(): void }) => {
    for (let turn = 0; turn < 3; turn++) {
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve));
    }
    fixture.detectChanges();
  };
  const open = async () => {
    const fixture = TestBed.createComponent(WorkflowInstanceWorkspaceComponent);
    await settle(fixture);
    return fixture;
  };
  const input = (fixture: { nativeElement: HTMLElement }, value: string) =>
    Array.from(fixture.nativeElement.querySelectorAll('input')).find(
      (i) => (i as HTMLInputElement).value === value,
    ) as HTMLInputElement | undefined;
  const type = async (fixture: Awaited<ReturnType<typeof open>>, from: string, to: string) => {
    const field = input(fixture, from)!;
    field.value = to;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    await settle(fixture);
  };
  const poll = async (
    fixture: Awaited<ReturnType<typeof open>>,
    next: Parameters<typeof detailFor>[0],
  ) => {
    server = detailFor(next);
    fixture.componentInstance.load(true);
    await settle(fixture);
  };

  it('renders the claimed form and records the typed answers in the host', async () => {
    const fixture = await open();
    expect(input(fixture, 'initial-prefill')).toBeDefined();
    await type(fixture, 'initial-prefill', 'edited-by-me');
    expect(fixture.componentInstance.formValues['task-1']['approved']).toBe('edited-by-me');
  });

  it('shows the server answers and clears the host draft when a reassignment makes the same visit read-only', async () => {
    const fixture = await open();
    await type(fixture, 'initial-prefill', 'edited-by-me');

    // Reassigned elsewhere while this page stays open: same visit, another attempt, canAct=false.
    await poll(fixture, {
      canAct: false,
      draftKey: 'attempt-2',
      values: { approved: 'server-value' },
    });

    expect(input(fixture, 'edited-by-me')).toBeUndefined();
    expect(input(fixture, 'server-value')).toBeDefined();
    expect(fixture.componentInstance.formValues['task-1']).toEqual({ approved: 'server-value' });
    expect(fixture.nativeElement.textContent).toContain('Claimed by another user.');
  });

  it('does not bring the previous attempt back when the item returns to this user under a new attempt', async () => {
    const fixture = await open();
    await type(fixture, 'initial-prefill', 'edited-by-me');
    await poll(fixture, {
      canAct: false,
      draftKey: 'attempt-2',
      values: { approved: 'server-value' },
    });
    await poll(fixture, {
      canAct: true,
      draftKey: 'attempt-3',
      values: { approved: 'server-value' },
    });

    expect(input(fixture, 'edited-by-me')).toBeUndefined();
    expect(input(fixture, 'server-value')).toBeDefined();
    expect(fixture.componentInstance.formValues['task-1']['approved']).toBe('server-value');
  });

  it('keeps the edit and the host answers when the same user claims the item (same attempt)', async () => {
    server = detailFor({
      canAct: true,
      draftKey: 'attempt-1',
      values: { approved: 'initial-prefill' },
      status: 'Pending',
    });
    const fixture = await open();
    await type(fixture, 'initial-prefill', 'edited-by-me');

    await poll(fixture, {
      canAct: true,
      draftKey: 'attempt-1',
      values: { approved: 'initial-prefill' },
      status: 'Claimed',
    });

    expect(input(fixture, 'edited-by-me')).toBeDefined();
    expect(fixture.componentInstance.formValues['task-1']['approved']).toBe('edited-by-me');
  });

  it('keeps the edit through ordinary polling of an unchanged attempt', async () => {
    const fixture = await open();
    await type(fixture, 'initial-prefill', 'edited-by-me');
    await poll(fixture, {
      canAct: true,
      draftKey: 'attempt-1',
      values: { approved: 'initial-prefill' },
    });
    await poll(fixture, {
      canAct: true,
      draftKey: 'attempt-1',
      values: { approved: 'initial-prefill' },
    });
    expect(input(fixture, 'edited-by-me')).toBeDefined();
    expect(fixture.componentInstance.formValues['task-1']['approved']).toBe('edited-by-me');
  });

  it('resets the host answers of a task without a pinned form when the attempt changes', async () => {
    server = detailFor({
      canAct: true,
      draftKey: 'attempt-1',
      values: { notes: 'server-notes' },
      legacy: true,
    });
    const fixture = await open();
    await type(fixture, 'server-notes', 'typed-notes');
    expect(fixture.componentInstance.formValues['task-1']['notes']).toBe('typed-notes');

    // Same visit, same activation, but the attempt rotated (reassigned and returned): nothing typed carries over.
    await poll(fixture, {
      canAct: true,
      draftKey: 'attempt-2',
      values: { notes: 'server-notes' },
      legacy: true,
    });
    expect(fixture.componentInstance.formValues['task-1']).toEqual({ notes: 'server-notes' });
    expect(input(fixture, 'typed-notes')).toBeUndefined();

    // Polling the same attempt keeps what is typed afterwards.
    await type(fixture, 'server-notes', 'typed-again');
    await poll(fixture, {
      canAct: true,
      draftKey: 'attempt-2',
      values: { notes: 'server-notes' },
      legacy: true,
    });
    expect(fixture.componentInstance.formValues['task-1']['notes']).toBe('typed-again');
  });
});
