import type { Browser } from '@e2e-dev/web';
import { expect } from 'e2e';
import { randomUUID } from 'node:crypto';

export function apiFor(browser: Browser) {
  return async <T>(path: string, method = 'GET', body?: unknown, status = 200): Promise<T> => {
    const response = JSON.parse(
      String(
        await browser.evaluate(async (input) => {
          const { path, method, body } = JSON.parse(String(input));
          const response = await fetch('/api/' + path, {
            method,
            headers: {
              Authorization: 'Bearer ' + localStorage.getItem('nwfm_access_token'),
              'Content-Type': 'application/json',
            },
            body: body === undefined ? undefined : JSON.stringify(body),
          });
          const text = await response.text();
          const resultBody = text
            ? response.headers.get('content-type')?.includes('json')
              ? JSON.parse(text)
              : text
            : null;
          return JSON.stringify({ status: response.status, body: resultBody });
        }, JSON.stringify({ path, method, body })),
      ),
    ) as { status: number; body: T };
    expect(response.status, `${method} ${path}: ${JSON.stringify(response.body)}`).toBe(status);
    return response.body as T;
  };
}

export interface Task {
  id: string;
  status: string;
  activeFormStepId: string;
  formVisitId: string;
  dueAt: string;
  assignmentGroupId: string;
  formHistory: {
    visitId: string;
    stepId: string;
    versionNo: number;
    ruleId?: string;
    answers: Record<string, unknown>;
  }[];
}
export interface Detail {
  tree: { id: string; status: string; activities: { name: string; tasks: { task: Task }[] }[] }[];
}
export const activeTasks = (detail: Detail) =>
  detail.tree
    .flatMap((run) =>
      run.activities.flatMap((activity) => activity.tasks.map((entry) => entry.task)),
    )
    .filter((task) => ['Pending', 'Claimed'].includes(task.status));

/** Creates uniquely named data only inside the launcher's disposable database. */
export async function createActivityForms(browser: Browser) {
  const api = apiFor(browser);
  const suffix = randomUUID().replaceAll('-', '').slice(0, 10);
  const name = 'E2E forms ' + suffix;
  const catalog = await api<{ definitionKey: string; workspaceJson: string }[]>(
    'workflow/workspace/catalog',
  );
  const demo = catalog.find((row) => row.definitionKey === 'DEMO-MAIN-STANDARD-VISUAL');
  expect(demo).toBeDefined();
  const mainSettings = JSON.parse(demo!.workspaceJson) as Record<string, unknown>;
  const group = await api<{ id: string }>(
    'workflow/assignment-groups',
    'POST',
    { code: 'E2E' + suffix, name, assignmentStrategy: 'RoundRobin' },
    201,
  );
  const profile = await api<{ value: { userId: string } }>('v1/auth/me');
  const participants = await api<{ items: { id: string; userId: string }[] }>(
    'workflow/participants?pageSize=200',
  );
  const participant =
    participants.items.find((p) => p.userId === profile.value.userId) ??
    (await api<{ id: string }>(
      'workflow/participants',
      'POST',
      {
        userId: profile.value.userId,
        displayName: 'E2E forms reviewer',
        email: 'forms@example.test',
      },
      201,
    ));
  await api(
    `workflow/assignment-groups/${group.id}/members`,
    'POST',
    { participantId: participant.id, canClaim: true },
    201,
  );
  const forms: { id: string; nameEn: string }[] = [];
  for (let i = 1; i <= 3; i++) {
    const form = (
      await api<{ value: { id: string; nameEn: string } }>(
        'v1/form-engine/forms',
        'POST',
        {
          code: `E2E${suffix}F${i}`,
          nameEn: `${name} Form ${i}`,
          nameAr: `Form ${i}`,
          category: 'GENERAL',
          departmentCode: '10',
          fieldActivityCode: 'ISOLATION',
        },
        201,
      )
    ).value;
    const schema = {
      name_en: form.nameEn,
      name_ar: form.nameEn,
      elements: [
        { type: 'text', data_name: 'route', label_en: 'Routing choice', required: true },
        { type: 'numeric', data_name: 'amount', label_en: 'Amount' },
        { type: 'text', data_name: 'approved', label_en: 'Approved' },
        { type: 'memo', data_name: 'notes', label_en: 'Notes' },
      ],
    };
    await api(`v1/form-engine/forms/${form.id}/schema`, 'PUT', {
      schemaJson: JSON.stringify(schema),
    });
    await api(`v1/form-engine/forms/${form.id}/publish`, 'POST', {});
    forms.push(form);
  }
  const type = (
    await api<{ value: { id: string } }>(
      'v1/task-types',
      'POST',
      {
        code: 'E2E' + suffix,
        nameEn: name,
        nameAr: name,
        formDefinitionIds: forms.map((f) => f.id),
      },
      201,
    )
  ).value;
  const condition = (field: string, value: unknown, operator = 'equal') => ({
    field,
    operator,
    value,
  });
  const steps = (other: string) =>
    forms.map((form, index) => ({
      id: `form${index + 1}`,
      formId: form.id,
      versionNo: 1,
      name: `Form ${index + 1}`,
      rules: [
        ...[1, 2, 3].map((n) => ({
          id: 'to' + n,
          name: 'Go to Form ' + n,
          when: { match: 'All', conditions: [condition('route', 'FORM' + n)], groups: [] },
          destination: { kind: 'Form', target: 'form' + n },
        })),
        {
          id: 'repeat',
          name: 'Repeat',
          when: { match: 'All', conditions: [condition('route', 'REPEAT')], groups: [] },
          destination: { kind: 'Form', target: 'form' + (index + 1) },
        },
        {
          id: 'other',
          name: 'Other activity',
          when: { match: 'All', conditions: [condition('route', 'OTHER')], groups: [] },
          destination: { kind: 'Activity', target: other },
        },
        {
          id: 'finish',
          name: 'Finish',
          when: { match: 'All', conditions: [condition('route', 'END')], groups: [] },
          destination: { kind: 'Activity', target: 'end' },
        },
        {
          id: 'nested',
          name: 'Nested AND OR',
          when: {
            match: 'All',
            conditions: [condition('route', 'CONDITIONAL')],
            groups: [
              {
                match: 'Any',
                conditions: [
                  condition('amount', 1000, 'greater_than'),
                  condition('approved', 'yes'),
                ],
                groups: [],
              },
            ],
          },
          destination: { kind: 'Activity', target: other },
        },
      ],
      otherwise: { kind: 'Next' },
    }));
  const config = (other: string) => ({
    taskTypeId: type.id,
    departmentCode: '10',
    fieldActivityCode: 'ISOLATION',
    formSteps: steps(other),
    rejectTargetNodeKey: 'a',
  });
  const escape = (value: string) =>
    value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
  const activity = (key: string, kind: string, title: string, configuration: object, x: number) =>
    `<Activity nodeKey="${key}" type="${kind}" name="${title}" positionX="${x}" positionY="180" assignmentGroupId="${group.id}" configurationJson="${escape(JSON.stringify(configuration))}"><Outcomes><Outcome key="APPROVE" name="Approve" isDefault="true"/><Outcome key="REJECT" name="Reject" requiresComment="true"/></Outcomes></Activity>`;
  const xml = (settings: object, nodes: string, keys: string[]) =>
    `<Workflow xmlns="https://privora.io/workflow/v1" workspaceJson="${escape(JSON.stringify(settings))}"><Activities><Activity nodeKey="start" type="Start" name="Start" positionX="60" positionY="180"/>${nodes}<Activity nodeKey="end" type="End" name="End" positionX="950" positionY="180"/></Activities><Transitions>${['start', ...keys].map((key, i) => `<Transition key="t${i}" from="${key}" to="${[...keys, 'end'][i]}"/>`).join('')}</Transitions></Workflow>`;
  const childSettings = {
    ...mainSettings,
    kind: 'Child',
    taskTypeId: type.id,
    fieldActivityTypeId: null,
    fieldActivityCode: null,
    departmentCode: null,
  };
  const child = await api<{ definitionId: string; versionId: string }>(
    'workflow/workspace/definitions',
    'POST',
    { name: name + ' Child', settings: childSettings },
  );
  const childPath = `workflow/definitions/${child.definitionId}/versions/${child.versionId}`;
  await api(childPath + '/xml', 'PUT', {
    xmlContent: xml(childSettings, activity('a', 'UserTask', 'Child review', config('end'), 300), [
      'a',
    ]),
  });
  const validation = await api<{ isValid: boolean; errors: unknown[] }>(
    childPath + '/validate',
    'POST',
    {},
  );
  expect(validation.errors).toEqual([]);
  expect(validation.isValid).toBe(true);
  await api(childPath + '/publish', 'POST', {});
  const childInfo = await api<{ definitionKey: string }>(
    `workflow/definitions/${child.definitionId}`,
  );
  const main = await api<{ definitionId: string; versionId: string }>(
    'workflow/workspace/definitions',
    'POST',
    { name: name + ' Main', settings: mainSettings },
  );
  const mainPath = `workflow/definitions/${main.definitionId}/versions/${main.versionId}`;
  const mainXml = xml(
    mainSettings,
    activity(
      'a',
      'MainActivity',
      'Activity A',
      { ...config('b'), versionId: child.versionId, definitionKey: childInfo.definitionKey },
      300,
    ) +
      activity(
        'b',
        'MainActivity',
        'Activity B',
        { ...config('a'), versionId: child.versionId, definitionKey: childInfo.definitionKey },
        630,
      ),
    ['a', 'b'],
  );
  await api(mainPath + '/xml', 'PUT', { xmlContent: mainXml });
  const publish = async () => {
    const result = await api<{ isValid: boolean; errors: unknown[] }>(
      mainPath + '/validate',
      'POST',
      {},
    );
    expect(result.errors).toEqual([]);
    await api(mainPath + '/publish', 'POST', {});
  };
  const start = async () =>
    (
      await api<{ instanceId: string }>(
        `workflow/workspace/definitions/${main.definitionId}/instances`,
        'POST',
        { requestId: randomUUID(), reference: name, isDemo: false },
      )
    ).instanceId;
  return { api, name, forms, type, child, main, mainPath, mainXml, publish, start };
}
