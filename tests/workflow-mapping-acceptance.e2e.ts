import { beforeEach, test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { randomUUID } from 'node:crypto';
import {
  createDesignerFoundation,
  type DesignerVersion,
  xmlAttribute,
} from './support/designer-foundation.js';
import { createMappingFixture, openMappingEditor } from './support/event-mapping.js';
import { type Detail } from './support/activity-forms.js';
import {
  configureStep,
  emptyMapping,
  literal,
  noteBinding,
  publishOwnedTree,
  receipts,
  startOwnedTree,
} from './support/mapping-runtime.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('mapping-acceptance');

beforeEach(async ({ app, screen }) => {
  const user = accounts.next();
  await app.open('/login');
  await screen.getByLabel('Username').fill(user.username);
  await screen.getByLabel('Password').fill(user.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'Workflows', { exact: true })).toBeVisible();
});

test('readonly tampering is rejected with the current receipt and edited prefill reaches immutable history', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createDesignerFoundation(browser);
  await configureStep(fixture, {
    ...emptyMapping(),
    stepBindings: [
      noteBinding(fixture),
      {
        ...noteBinding(fixture),
        id: 'edit',
        fieldKey: 'approved',
        mode: 'Prefill',
        value: literal('initial-prefill'),
      },
    ],
  });
  await publishOwnedTree(fixture);
  const id = await startOwnedTree(fixture);
  const all = async () =>
    (await fixture.api<Detail>('workflow/workspace/instances/' + id)).tree
      .flatMap((run) => run.activities)
      .flatMap((a) => a.tasks.map((t) => t.task));
  const item = (await all())[0];
  await app.open('/org/workflow/tasks/' + item.id);
  await screen.getByRole('button', 'Claim', { exact: true }).click();
  await screen.getByRole('button', 'Accept', { exact: true }).click();
  const claimed = await fixture.api<{ formReceiptKey: string; formVisitId: string }>(
    'workflow/work-items/' + item.id,
  );
  expect(claimed.formReceiptKey).toBeTruthy();
  expect(claimed.formReceiptKey).not.toBe(claimed.formVisitId);
  const rejected = await fixture.api<{ error?: { code?: string }; code?: string }>(
    `v1/form-engine/forms/${fixture.forms[0].id}/submissions`,
    'POST',
    {
      versionNo: 1,
      contextType: 'WorkItem',
      contextId: item.id + ':' + claimed.formVisitId,
      clientSubmissionId: claimed.formReceiptKey,
      answers: { route: 'END', notes: 'tampered', approved: 'edited-prefill' },
    },
    400,
  );
  expect(JSON.stringify(rejected)).toContain('Validation.Failed');
  expect(JSON.stringify(rejected)).toContain('recorded through that module');
  const rejectedCompletion = await fixture.api<{ code: string }>(
    `workflow/workspace/tasks/${item.id}/actions`,
    'POST',
    {
      requestId: randomUUID(),
      action: 'APPROVE',
      formVisitId: claimed.formVisitId,
      formValues: { route: 'END', notes: 'tampered', approved: 'edited-prefill' },
    },
    400,
  );
  expect(rejectedCompletion.code).toBe('Workflow.Form.Context');
  await expect(screen.getByRole('textbox', 'Notes', { exact: false, disabled: true })).toHaveValue(
    '',
  );
  await screen
    .getByRole('textbox', 'Routing choice', { exact: false, disabled: false })
    .fill('END');
  await screen
    .getByRole('textbox', 'Approved', { exact: false, disabled: false })
    .fill('edited-prefill');
  await screen.getByRole('link', 'Instances', { exact: true }).click();
  await screen.getByLabel('Search instances').fill(fixture.name);
  await screen.getByRole('button', 'Search', { exact: true }).click();
  await screen.getByRole('link', fixture.name + ' designer foundation', { exact: true }).click();
  await expect(
    screen.getByRole('textbox', 'Approved', { exact: false, disabled: false }),
  ).toHaveValue('edited-prefill');
  await screen.getByRole('button', 'Submit and continue').click();
  await expect
    .poll(async () => (await all()).find((task) => task.id === item.id)?.formHistory[0]?.answers)
    .toMatchObject({ route: 'END', notes: null, approved: 'edited-prefill' });
  await app.open('/org/workflow/tasks/' + item.id);
  await screen.getByRole('tab', 'Request details', { exact: true }).click();
  await screen.getByText('Form submission history (1)').click();
  await expect(screen.getByText(/Shared pinned form.*v1/)).toBeVisible();
});

for (const repeatedTarget of [true, false]) {
  test(
    repeatedTarget
      ? 'comment-driven independent callable and prepare-only receipts leave the owning Step graph unchanged'
      : 'distinct callable targets preserve owning Step and expose prepared input receipts',
    async ({ app, browser, screen }) => {
      const fixture = await createDesignerFoundation(browser);
      const connection = (
        await fixture.api<{ id: string; name: string }[]>('workflow/integrations/connections')
      ).find((c) => c.name === 'Demo — Local HTTP')!;
      const target = {
        workflowDefinitionId: fixture.draft.definitionId,
        workflowVersionId: fixture.draft.versionId,
        nodeKey: 'receiver',
      };
      const schema = { type: 'object', properties: { marker: { type: 'string', required: true } } };
      const http = {
        connectionId: connection.id,
        mappingMode: 'Typed',
        protocol: 'Rest',
        method: 'POST',
        path: '/echo',
        required: false,
      };
      const receiver = {
        ...http,
        typedMapping: {
          ...emptyMapping(),
          callable: true,
          invocationInputs: schema,
          endpoint: {
            method: 'POST',
            path: '/echo',
            parameters: [],
            body: schema,
            response: {
              type: 'object',
              properties: { id: { type: 'string' }, accepted: { type: 'boolean' } },
            },
          },
          inputs: [
            {
              location: 'body',
              name: '$body',
              value: {
                op: 'source',
                source: { ...target, kind: 'InvocationInput', fieldKey: '$inputs' },
              },
            },
          ],
          outputs: [
            {
              id: 'accepted',
              name: 'Accepted',
              schema: { type: 'boolean' },
              value: {
                op: 'source',
                source: {
                  ...target,
                  kind: 'Response',
                  fieldKey: '$response',
                  jsonPointer: '/accepted',
                },
              },
            },
          ],
        },
      };
      const caller = {
        ...http,
        triggerBinding: { sourceNodeKey: 'a', trigger: 'OnComment' },
        typedMapping: {
          ...emptyMapping(),
          endpoint: {
            method: 'POST',
            path: '/echo',
            parameters: [],
            body: { type: 'object', properties: {} },
          },
          inputs: [{ location: 'body', name: '$body', value: literal({}) }],
          actions: ['MapOnly', 'Run'].map((mode) => ({
            id: mode,
            target:
              mode === 'MapOnly' && !repeatedTarget
                ? { ...target, nodeKey: 'prepared-receiver' }
                : target,
            mode,
            inputs: literal({ marker: 'independent' }),
          })),
        },
      };
      const version = await fixture.api<DesignerVersion>(fixture.path);
      const preparedReceiverXml = repeatedTarget
        ? ''
        : `<Activity nodeKey="prepared-receiver" type="ServiceTask" name="Prepared receiver" actionKey="http.request" configurationJson="${xmlAttribute(JSON.stringify(receiver).replaceAll('"nodeKey":"receiver"', '"nodeKey":"prepared-receiver"'))}"/>`;
      const xml = (await fixture.api<string>(fixture.path + '/xml')).replace(
        '</Activities>',
        `<Activity nodeKey="caller" type="ServiceTask" name="Comment event" actionKey="http.request" configurationJson="${xmlAttribute(caller)}"/><Activity nodeKey="receiver" type="ServiceTask" name="Callable receiver" actionKey="http.request" configurationJson="${xmlAttribute(receiver)}"/>${preparedReceiverXml}</Activities>`,
      );
      await fixture.api(fixture.path + '/xml', 'PUT', {
        xmlContent: xml,
        expectedRevision: version.revision,
      });
      await publishOwnedTree(fixture);
      const id = await startOwnedTree(fixture);
      const detail = () => fixture.api<Detail>('workflow/workspace/instances/' + id);
      const child = (await detail()).tree.find((run) => run.id !== id)!;
      const before = child.activities.find((a) => a.name === 'Origin Step')!.tasks[0].task;
      const graphPath = 'workflow/runtime/instances/' + child.id + '/live-graph';
      const progressPath = 'workflow/runtime/instances/' + child.id + '/progress';
      const progressBefore = await fixture.api<{ activities: object[] }>(progressPath);
      const graphBefore = await fixture.api<{
        currentNodeKey: string;
        nodes: { nodeKey: string; runtimeStatus: string }[];
      }>(graphPath);
      await app.open('/admin/workflow/instances/' + id);
      await screen.getByLabel('Comment', { exact: true }).fill('Invoke independent follow-ups');
      await screen.getByRole('button', 'Add comment', { exact: true }).click();
      await expect
        .poll(
          async () =>
            (await receipts(fixture, id)).filter(
              (r) => ['Callable', 'MapOnly'].includes(r.executionKind) && r.status === 'Completed',
            ).length,
          { timeout: 60_000 },
        )
        .toBe(2);
      await screen.getByRole('button', 'Refresh', { exact: true }).click();
      const rows = await receipts(fixture, id);
      const prepared = rows.find((r) => r.executionKind === 'MapOnly')!;
      const called = rows.find((r) => r.executionKind === 'Callable')!;
      expect(called.graphAdvanced).toBe(false);
      expect(called.operationId).toBeTruthy();
      expect(prepared.operationId).toBeNull();
      expect(prepared.prepared).toBe(true);
      const card = browser.locator(`[data-invocation="${prepared.id}"]`);
      await expect(card).toContainText(
        'Independent call: it does not complete the destination Step',
      );
      await card.getByRole('button', 'View prepared inputs').click();
      await expect(card).toContainText('"marker": "independent"');
      const after = (await detail()).tree
        .find((run) => run.id === child.id)!
        .activities.find((a) => a.name === 'Origin Step')!.tasks[0].task;
      expect({
        id: after.id,
        visit: after.formVisitId,
        status: after.status,
        history: after.formHistory,
      }).toEqual({
        id: before.id,
        visit: before.formVisitId,
        status: before.status,
        history: before.formHistory,
      });
      const graphAfter = await fixture.api<typeof graphBefore>(graphPath);
      expect(graphAfter.currentNodeKey).toBe(graphBefore.currentNodeKey);
      const owningNodes = (graph: typeof graphBefore) =>
        graph.nodes.filter((node) =>
          ['a', 'b', 'receiver', 'prepared-receiver'].includes(node.nodeKey),
        );
      expect(owningNodes(graphAfter)).toEqual(owningNodes(graphBefore));
      expect((await fixture.api<{ activities: object[] }>(progressPath)).activities).toEqual(
        progressBefore.activities,
      );
    },
  );
}

for (const mode of ['none', 'multiple', 'deadline'] as const) {
  test(`related ${mode} matches show the real Preparing and attention receipts`, async ({
    app,
    browser,
    screen,
  }) => {
    const fixture = await createDesignerFoundation(browser);
    await fixture.publish();
    const reference = fixture.name + '-related';
    if (mode === 'multiple') {
      const ids = [];
      for (let n = 0; n < 2; n++)
        ids.push(
          (
            await fixture.api<{ instanceId: string }>(
              `workflow/workspace/definitions/${fixture.main.definitionId}/instances`,
              'POST',
              { requestId: randomUUID(), reference, isDemo: false },
            )
          ).instanceId,
        );
      expect(ids[0]).not.toBe(ids[1]);
    }
    await configureStep(fixture, {
      ...emptyMapping(),
      stepBindings: [noteBinding(fixture)],
      selectors: [
        {
          id: 'related',
          workflowDefinitionId: fixture.main.definitionId,
          workflowVersionId: fixture.main.versionId,
          keyKind: 'CorrelationKey',
          keyValue: literal(reference),
          multipleMatches: 'Attention',
          missing:
            mode === 'deadline'
              ? { mode: 'Wait', deadlineSeconds: 10, timeoutOutcome: 'missing-data' }
              : { mode: 'Attention' },
        },
      ],
    });
    await publishOwnedTree(fixture);
    const id = await startOwnedTree(fixture);
    await expect
      .poll(async () => (await receipts(fixture, id))[0]?.status)
      .toBe(mode === 'deadline' ? 'Waiting' : 'Attention');
    const receipt = (await receipts(fixture, id))[0];
    const detail = await fixture.api<Detail>('workflow/workspace/instances/' + id);
    const item = detail.tree.flatMap((run) => run.activities).find((a) => a.name === 'Origin Step')!
      .tasks[0].task;
    expect(item.status).toBe('Preparing');
    await app.open('/org/workflow/tasks/' + item.id);
    await expect(screen.getByTestId('preparing-notice')).toContainText('Preparing this Step');
    await expect(screen.getByRole('button', 'Claim', { exact: true })).toHaveCount(0);
    await app.open('/admin/workflow/instances/' + id);
    const card = browser.locator(`[data-invocation="${receipt.id}"]`);
    if (mode === 'deadline') {
      expect(receipt.deadline).toBeTruthy();
      await expect(card).toContainText('Waiting for data until');
      await expect
        .poll(async () => (await receipts(fixture, id)).find((r) => r.id === receipt.id)?.status, {
          timeout: 60_000,
        })
        .toBe('Attention');
      await screen.getByRole('button', 'Refresh', { exact: true }).click();
    }
    await expect(card.getByRole('alert')).toContainText('Needs attention');
    const final = (await receipts(fixture, id)).find((r) => r.id === receipt.id)!;
    expect(final.error).toMatch(
      mode === 'multiple'
        ? /Multiple related instances/
        : mode === 'none'
          ? /not registered/
          : /expired|deadline|missing-data|wait/i,
    );
    expect(final.prepared).toBe(false);
    expect(final.operationId).toBeNull();
    await expect(card.getByRole('button', 'Retry', { exact: true })).toBeVisible();
  });
}

test('mapper remains usable in Arabic at narrow width and published mapping refuses edits', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createMappingFixture(browser);
  const { child, editor, mapper } = await openMappingEditor(fixture, app, browser, screen);
  await editor.getByRole('button', 'Expand editor', { exact: true }).click();
  await browser.setViewport({ width: 390, height: 844 });
  await mapper.getByRole('tab', /^1\. Endpoint/).press('ArrowRight');
  await expect(mapper.getByRole('tab', /^2\. Inputs/)).toHaveAttribute('aria-selected', 'true');
  await screen.getByRole('button', 'Language', { exact: true }).press('Enter');
  expect(await browser.evaluate(() => document.documentElement.dir)).toBe('rtl');
  const arabic = screen.getByTestId('event-mapper');
  await expect(arabic.getByRole('tablist', 'مراحل الربط')).toBeVisible();
  await arabic.getByRole('tab').nth(1).press('End');
  await expect(arabic.getByRole('tab').nth(4)).toHaveAttribute('aria-selected', 'true');
  expect(
    await browser.evaluate(() => {
      const r = document.querySelector('[data-testid="event-mapper"]')!.getBoundingClientRect();
      return r.left >= -1 && r.right <= window.innerWidth + 1;
    }),
  ).toBe(true);
  await screen.getByRole('button', 'اللغة', { exact: true }).press('Enter');
  await browser.setViewport({ width: 1440, height: 1000 });
  await editor.getByRole('button', 'Close expanded editor', { exact: true }).click();
  // Persist typed mode through the normal editor before publication.
  await editor.getByRole('button', 'Apply settings').click();
  await expect
    .poll(async () => (await fixture.configuration()).typedMapping?.contractVersion)
    .toBe(1);
  await child.getByRole('button', 'Close configuration', { exact: true }).click();
  await expect(child.getByRole('status', 'Save status', { exact: true })).toContainText('Saved');
  await child.getByRole('button', 'Close child workflow', { exact: true }).click();
  await screen.getByRole('button', 'Close configuration', { exact: true }).click();
  await expect(screen.getByRole('status', 'Save status', { exact: true })).toContainText('Saved');
  await screen.getByRole('link', 'Workflows', { exact: true }).click();
  await expect(browser).toHaveURL('/admin/workflow/definitions');
  await publishOwnedTree(fixture);
  await app.open('/admin/' + fixture.path + '/designer');
  await screen.getByRole('button', 'Select Mapping event', { exact: true }).press('Enter');
  await screen.getByRole('button', /^Action and inputs/).click();
  const readonly = screen.getByTestId('event-mapper');
  await expect(readonly).toContainText('This version is read-only.');
  await readonly.getByRole('tab', /^2\. Inputs/).click();
  await expect(readonly.getByRole('button', 'Map this input')).toHaveCount(0);
  const version = await fixture.api<DesignerVersion>(fixture.path);
  const denied = await fixture.api<{ code: string }>(
    fixture.path + '/xml',
    'PUT',
    {
      expectedRevision: version.revision,
      xmlContent: await fixture.api<string>(fixture.path + '/xml'),
    },
    422,
  );
  expect(denied.code).toMatch(/Published|Draft|Immutable|Version/i);
  expect((await fixture.api<DesignerVersion>(fixture.path)).revision).toBe(version.revision);
});
