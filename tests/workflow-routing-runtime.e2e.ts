import { test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { randomUUID } from 'node:crypto';
import { apiFor, type Task } from './support/activity-forms.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('routing-runtime');

interface Version {
  id: string;
  revision: string;
  workspaceJson: string;
  activities: { nodeKey: string; configurationJson: string }[];
}
interface Run {
  tree: {
    id: string;
    status: string;
    activities: { nodeKey: string; tasks: { task: Task }[] }[];
  }[];
  operations: { name: string; status: string }[];
}

test('published UNRC child coordinates two submitted forms and a local Event before Step 3', async ({
  app,
  browser,
  screen,
}) => {
  const admin = accounts.next();
  await app.open('/login');
  await screen.getByLabel('Username').fill(admin.username);
  await screen.getByLabel('Password').fill(admin.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'Workflows', { exact: true })).toBeVisible();
  const api = apiFor(browser);
  const catalog = await api<
    { id: string; versionId: string; definitionKey: string; workspaceJson: string }[]
  >('workflow/workspace/catalog');
  const original = catalog.find((d) => d.definitionKey === 'UNRC-LOCAL')!;
  expect(original).toBeDefined();
  const sourceMain = await api<Version>(
    `workflow/definitions/${original.id}/versions/${original.versionId}`,
  );
  const meter = JSON.parse(
    sourceMain.activities.find((a) => a.nodeKey === 'meter')!.configurationJson,
  );
  const children = await api<{ id: string; versionId: string; definitionKey: string }[]>(
    'workflow/workspace/children',
  );
  const sourceChild = children.find((c) => c.definitionKey === meter.definitionKey)!;
  const source = await api<Version>(
    `workflow/definitions/${sourceChild.id}/versions/${sourceChild.versionId}`,
  );
  const roles = await api<{ id: string; code: string }[]>('workflow/workspace/lookups/roles');
  const role = roles.find((r) => r.code === 'Administrator')!.id;
  const settings = { ...JSON.parse(original.workspaceJson), designerVersion: 4 };
  const created = await api<{ definitionId: string; versionId: string }>(
    'workflow/workspace/definitions',
    'POST',
    { name: 'UNRC Routing ' + randomUUID(), settings },
  );
  const mainPath = `workflow/definitions/${created.definitionId}/versions/${created.versionId}`;
  const esc = (v: unknown) =>
    (typeof v === 'string' ? v : JSON.stringify(v))
      .replaceAll('&', '&amp;')
      .replaceAll('"', '&quot;')
      .replaceAll('<', '&lt;');
  const node = (key: string, type: string, name: string, config: object = {}) =>
    `<Activity nodeKey="${key}" type="${type}" name="${name}" configurationJson="${esc(config)}"><Outcomes><Outcome key="APPROVE" name="Accept" isDefault="true"/><Outcome key="REJECT" name="Reject" requiresComment="true"/></Outcomes></Activity>`;
  const xml = (scope: unknown, nodes: string[], edges: string[][]) =>
    `<Workflow xmlns="https://privora.io/workflow/v1" workspaceJson="${esc(scope)}"><Activities>${nodes.join('')}</Activities><Transitions>${edges.map(([key, from, to]) => `<Transition key="${key}" from="${from}" to="${to}"/>`).join('')}</Transitions></Workflow>`;
  const always = (key: string) => ({
    routing: { routes: [{ transitionKey: key, always: true }], matchMode: 'Unique' },
  });
  const save = async (path: string, content: string) => {
    const current = await api<Version>(path);
    await api(path + '/xml', 'PUT', {
      xmlContent: content,
      expectedRevision: current.revision,
      designerJson: '{}',
    });
  };
  await save(
    mainPath,
    xml(
      settings,
      [
        node('start', 'Start', 'Start'),
        node('meter', 'MainActivity', 'Meter Installation', {
          taskTypeId: meter.taskTypeId,
          assignmentRoleIds: [role],
          departmentCode: meter.departmentCode,
          fieldActivityCode: meter.fieldActivityCode,
        }),
        node('mainRoute', 'ExclusiveGateway', 'Child completed', always('finish')),
        node('end', 'End', 'End'),
      ],
      [
        ['entry', 'start', 'meter'],
        ['check', 'meter', 'mainRoute'],
        ['finish', 'mainRoute', 'end'],
      ],
    ),
  );
  const child = await api<{ id: string; versionId: string; definitionKey: string }>(
    'workflow/workspace/children',
    'POST',
    { parentVersionId: created.versionId, nodeKey: 'meter' },
  );
  const childPath = `workflow/definitions/${child.id}/versions/${child.versionId}`;
  const childVersion = await api<Version>(childPath);
  const step = (key: string) => {
    const cfg = JSON.parse(source.activities.find((a) => a.nodeKey === key)!.configurationJson);
    delete cfg.rejectTargetNodeKey;
    cfg.assignmentRoleIds = [role];
    cfg.formSteps[0].rules = [];
    cfg.formSteps[0].otherwise = { kind: 'Next' };
    return cfg;
  };
  const registration = step('registration');
  const location = step('location');
  const feedback = step('feedback');
  const event = JSON.parse(
    source.activities.find((a) => a.nodeKey === 'release')!.configurationJson,
  );
  event.body = JSON.stringify({ reference: 'ROUTING-' + randomUUID() });
  event.routingOutputs = [{ key: 'releaseAccepted', type: 'yes_no', label: 'Release accepted' }];
  const rule = (
    key: string,
    field: string,
    type: string,
    edge: string,
    form?: { formId: string; versionNo: number },
  ) => ({
    routing: {
      matchMode: 'Unique',
      sources: [
        {
          key: `${key}.${field}`,
          nodeKey: key,
          field,
          type,
          label: field,
          ...(form ? { formId: form.formId, formVersion: form.versionNo } : {}),
        },
      ],
      routes: [
        {
          transitionKey: edge,
          when: {
            match: 'All',
            conditions: [
              {
                field: `${key}.${field}`,
                operator: type === 'yes_no' ? 'equal' : 'is_not_empty',
                value: type === 'yes_no' ? true : null,
              },
            ],
            groups: [],
          },
        },
      ],
    },
  });
  await save(
    childPath,
    xml(
      childVersion.workspaceJson,
      [
        node('start', 'Start', 'Start'),
        node('fork', 'ParallelGateway', 'Start requirements', {
          routing: { joinNodeKey: 'join', matchMode: 'All' },
        }),
        node('registration', 'UserTask', 'Registration', registration),
        node('location', 'UserTask', 'Go to Location', location),
        node('event16', 'ServiceTask', 'Event 16', event).replace(
          'type="ServiceTask"',
          'type="ServiceTask" actionKey="http.request"',
        ),
        node(
          'c1',
          'ExclusiveGateway',
          'Registration submitted',
          rule('registration', 'customer_name', 'text', 'met1', registration.formSteps[0]),
        ),
        node(
          'c2',
          'ExclusiveGateway',
          'Location submitted',
          rule('location', 'site_address', 'memo', 'met2', location.formSteps[0]),
        ),
        node(
          'c3',
          'ExclusiveGateway',
          'Event accepted',
          rule('event16', 'releaseAccepted', 'yes_no', 'met3'),
        ),
        node('join', 'JoinGateway', 'Wait for three requirements', {
          routing: { waitFor: 'All', remainingBranches: 'Continue' },
        }),
        node('feedback', 'UserTask', 'Step 3', feedback),
        node('finishRoute', 'ExclusiveGateway', 'Complete child', always('finish')),
        node('end', 'End', 'End'),
      ],
      [
        ['entry', 'start', 'fork'],
        ['b1', 'fork', 'registration'],
        ['b2', 'fork', 'location'],
        ['b3', 'fork', 'event16'],
        ['r1', 'registration', 'c1'],
        ['r2', 'location', 'c2'],
        ['r3', 'event16', 'c3'],
        ['met1', 'c1', 'join'],
        ['met2', 'c2', 'join'],
        ['met3', 'c3', 'join'],
        ['next', 'join', 'feedback'],
        ['last', 'feedback', 'finishRoute'],
        ['finish', 'finishRoute', 'end'],
      ],
    ),
  );
  await save(
    mainPath,
    xml(
      settings,
      [
        node('start', 'Start', 'Start'),
        node('meter', 'MainActivity', 'Meter Installation', {
          taskTypeId: meter.taskTypeId,
          assignmentRoleIds: [role],
          departmentCode: meter.departmentCode,
          fieldActivityCode: meter.fieldActivityCode,
          definitionKey: child.definitionKey,
          versionId: child.versionId,
        }),
        node('mainRoute', 'ExclusiveGateway', 'Child completed', always('finish')),
        node('end', 'End', 'End'),
      ],
      [
        ['entry', 'start', 'meter'],
        ['check', 'meter', 'mainRoute'],
        ['finish', 'mainRoute', 'end'],
      ],
    ),
  );
  for (const path of [childPath, mainPath]) {
    const validation = await api<{ isValid: boolean; errors: unknown[] }>(
      path + '/validate',
      'POST',
      {},
    );
    expect(validation.errors).toEqual([]);
    expect(validation.isValid).toBe(true);
    await api(path + '/publish', 'POST', {});
  }
  const scope = settings.organizationScopes[0];
  const started = await api<{ instanceId: string }>(
    `workflow/workspace/definitions/${created.definitionId}/instances`,
    'POST',
    {
      requestId: randomUUID(),
      reference: 'UNRC routing acceptance',
      location: { clusterCode: scope.clusterCode, cbuCode: scope.cbuCode },
    },
  );
  const detail = () => api<Run>('workflow/workspace/instances/' + started.instanceId);
  const active = (run: Run, key: string) =>
    run.tree
      .flatMap((r) => r.activities)
      .filter((a) => a.nodeKey === key)
      .flatMap((a) => a.tasks.map((t) => t.task))
      .filter((t) => ['Pending', 'Claimed'].includes(t.status));
  const submit = async (key: string, values: Record<string, unknown>) => {
    await expect.poll(async () => active(await detail(), key).length).toBe(1);
    const task = active(await detail(), key)[0];
    await api(
      `workflow/workspace/tasks/${task.id}/actions`,
      'POST',
      {
        requestId: randomUUID(),
        action: 'APPROVE',
        formVisitId: task.formVisitId,
        formValues: values,
      },
      204,
    );
  };
  await expect
    .poll(async () => (await detail()).operations.find((o) => o.name === 'Event 16')?.status)
    .toBe('Completed');
  expect(active(await detail(), 'feedback')).toHaveLength(0);
  await submit('registration', {
    request_reference: 'UNRC-ROUTING',
    customer_name: 'Demo customer',
  });
  const progress = await api<{ met: number; total: number; status: string }[]>(
    `workflow/workspace/instances/${started.instanceId}/routing`,
  );
  expect(progress).toHaveLength(1);
  expect(progress[0].met).toBe(2);
  expect(progress[0].total).toBe(3);
  expect(active(await detail(), 'feedback')).toHaveLength(0);
  await submit('location', { site_address: 'Local test site' });
  await submit('feedback', { release_ready: 'yes', feedback: 'All three requirements completed' });
  await expect
    .poll(async () => (await detail()).tree.every((r) => r.status === 'Completed'))
    .toBe(true);
  expect(
    (await detail()).tree.flatMap((r) => r.activities).filter((a) => a.nodeKey === 'feedback'),
  ).toHaveLength(1);
});
