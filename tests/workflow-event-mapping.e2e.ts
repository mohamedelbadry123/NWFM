import { beforeEach, test } from '@e2e-dev/web';
import { expect } from 'e2e';
import {
  createMappingFixture,
  mappingOpenApi,
  openMappingEditor,
} from './support/event-mapping.js';
import type { DesignerVersion } from './support/designer-foundation.js';
import { activeTasks, type Detail } from './support/activity-forms.js';
import { createDesignerFoundation, xmlAttribute } from './support/designer-foundation.js';
import { randomUUID } from 'node:crypto';
import { pointerDrag } from './support/pointer.js';
import { writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { publishOwnedTree, receipts, startOwnedTree } from './support/mapping-runtime.js';
import { designerAccounts } from './support/designer-accounts.js';
import { testArtifactDirectory } from './support/artifacts.js';
const accounts = designerAccounts('event-mapping');

beforeEach(async ({ app, screen }) => {
  const admin = accounts.next();
  await app.open('/login');
  await screen.getByLabel('Username').fill(admin.username);
  await screen.getByLabel('Password').fill(admin.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'Workflows', { exact: true })).toBeVisible();
});

test('authors five mapping stages with keyboard sources, previews typed values and sends only the local test', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createMappingFixture(browser);
  const { editor, mapper } = await openMappingEditor(fixture, app, browser, screen);
  await mapper.getByText('Import structure from an OpenAPI document', { exact: true }).click();
  await mapper.getByLabel('…or paste the document').fill(JSON.stringify(mappingOpenApi));
  await mapper.getByLabel('Operation path', { exact: true }).fill('/echo');
  await mapper.getByLabel('Operation method', { exact: true }).selectOption({ value: 'POST' });
  const [imported] = await Promise.all([
    browser.waitForResponse('**/mapping/import-openapi'),
    mapper.getByRole('button', 'Import operation').click(),
  ]);
  expect(imported.status).toBe(200);
  await mapper.getByRole('button', 'Check endpoint with the server').click();
  await expect(mapper.getByText('The server accepted this endpoint.')).toBeVisible();
  await mapper.getByRole('tab', /^2\. Inputs/).click();
  await mapper.getByRole('button', 'Map this input').click();
  const body = browser.locator('[data-slot="input:body:$body"]');
  for (const [index, name] of ['customer', 'amount', 'details', 'items'].entries()) {
    await body.getByRole('button', 'Add field', { exact: true }).click();
    await body.getByLabel('Field name', { exact: true }).nth(index).fill(name);
    await body.getByLabel('Field name', { exact: true }).nth(index).press('Tab');
  }
  const sourceBrowser = mapper.getByRole('region', 'Source browser');
  await sourceBrowser.getByLabel('Search sources').fill('Routing choice');
  const customer = browser.locator('[data-source="route"]').filter({ hasText: 'Origin Step' });
  await expect(customer).toHaveCount(1);
  await customer.getByRole('checkbox').press('Space');
  await expect(sourceBrowser.getByRole('list', 'Source tray').getByRole('listitem')).toHaveCount(1);
  await body
    .getByRole('button', 'Use customer as the mapping target', { exact: true })
    .press('Enter');
  await customer.getByRole('button', /^Map .* to customer$/).press('Enter');
  await expect(browser.locator('[data-slot="input:body:$body.customer"]')).toContainText(
    'Routing choice',
  );
  await sourceBrowser.getByLabel('Search sources').fill('Amount');
  const amount = browser.locator('[data-source="amount"]').filter({ hasText: 'Other Step' });
  await expect(amount).toHaveCount(1);
  await body.getByRole('button', 'Use amount as the mapping target', { exact: true }).click();
  await amount.getByRole('button', /^Map .* to amount$/).click();
  await expect(browser.locator('[data-slot="input:body:$body.amount"]')).toContainText('Amount');
  const details = browser.locator('[data-slot="input:body:$body.details"]');
  await details.getByRole('combobox', 'details', { exact: true }).selectOption({ value: 'object' });
  await details.getByRole('button', 'Add field', { exact: true }).click();
  await details.getByLabel('Field name', { exact: true }).fill('enabled');
  await details.getByLabel('Field name', { exact: true }).press('Tab');
  const enabled = browser.locator('[data-slot="input:body:$body.details.enabled"]');
  await enabled.getByLabel('Type', { exact: true }).selectOption({ value: 'boolean' });
  await enabled.getByLabel('Value', { exact: true }).selectOption({ value: 'true' });
  const items = browser.locator('[data-slot="input:body:$body.items"]');
  await items.getByRole('combobox', 'items', { exact: true }).selectOption({ value: 'map' });
  await sourceBrowser.getByLabel('Search sources').fill('Numbers');
  const numbers = browser.locator('[data-source="numbers"]').filter({ hasText: 'Earlier event' });
  await expect(numbers).toHaveCount(1);
  await items.getByRole('button', 'Use List as the mapping target', { exact: true }).click();
  await numbers.getByRole('button', /^Map .* to List$/).click();
  // The list transform's second slot defaults to the current item: a real nested typed map, not JSON text.
  await expect(items.getByRole('combobox', 'For each item', { exact: true })).toHaveValue('item');
  await mapper.getByRole('tab', /^3\. Outputs/).click();
  await expect(mapper.getByRole('tab', /^3\. Outputs/)).toHaveAttribute('aria-selected', 'true');
  await mapper.getByRole('button', 'Add output', { exact: true }).click();
  await mapper.getByLabel('Output name', { exact: true }).fill('accepted');
  await mapper.getByLabel('Output name', { exact: true }).press('Tab');
  await mapper.getByRole('tab', /^4\. Follow-up actions/).click();
  await mapper.getByRole('button', 'Add follow-up action').click();
  await expect(mapper.getByLabel('Call this event')).toContainText('Callable receiver');
  await mapper.getByLabel('What to do').selectOption({ value: 'MapOnly' });
  await mapper.getByRole('tab', /^5\. Test/).click();
  const panel = browser.locator('app-mapping-test-stage');
  await browser.locator('[data-sample="route"] textarea').fill('"customer-42"');
  await browser.locator('[data-sample="amount"] textarea').fill('12.5');
  await browser.locator('[data-sample="numbers"] textarea').fill('[1,2,3]');
  // A real running control tree must remain untouched by authoring Preview/Send Test.
  const live = await createDesignerFoundation(browser);
  await publishOwnedTree(live);
  const liveId = await startOwnedTree(live);
  const liveContract = async () =>
    (await live.api<Detail>('workflow/workspace/instances/' + liveId)).tree.map((run) => ({
      id: run.id,
      status: run.status,
      activities: run.activities.map((activity) => ({
        name: activity.name,
        tasks: activity.tasks.map(({ task }) => ({
          id: task.id,
          status: task.status,
          visit: task.formVisitId,
          history: task.formHistory,
        })),
      })),
    }));
  const runningBefore = await liveContract();
  const receiptsBefore = await receipts(live, liveId);
  const stats = async () =>
    (await (
      await fetch('http://127.0.0.1:5091/stats', {
        // The existing local transport counts a stats GET unless its key is reused.
        headers: { 'Idempotency-Key': fixture.name + '-stats' },
      })
    ).json()) as { operations: number };
  const before = await stats();
  const [preview] = await Promise.all([
    browser.waitForResponse('**/mapping/preview'),
    panel.getByRole('button', 'Preview (no network)').click(),
  ]);
  expect(preview.status).toBe(200);
  const prepared = (await preview.json()) as { errors: string[]; request: { body: unknown } };
  expect(prepared.errors).toEqual([]);
  expect(prepared.request.body).toEqual({
    customer: 'customer-42',
    amount: 12.5,
    details: { enabled: true },
    items: [1, 2, 3],
  });
  expect(await stats()).toEqual(before);
  await expect(panel.getByTestId('preview-result')).toContainText('customer-42');
  await panel.getByRole('checkbox', 'I want to send this real request now').check();
  const [sent] = await Promise.all([
    browser.waitForResponse('**/integrations/http/test'),
    panel.getByRole('button', 'Send Test', { exact: true }).click(),
  ]);
  expect(sent.status).toBe(200);
  await expect(panel.getByTestId('send-result')).toContainText('Succeeded');
  expect((await stats()).operations).toBe(before.operations + 1);
  expect(await liveContract()).toEqual(runningBefore);
  expect(await receipts(live, liveId)).toEqual(receiptsBefore);
  await editor.getByRole('button', 'Apply settings').click();
  await expect
    .poll(
      async () =>
        (await fixture.configuration()).typedMapping?.inputs?.[0]?.value?.properties?.customer
          ?.source?.fieldKey,
    )
    .toBe('route');
  const saved = (await fixture.configuration()).typedMapping;
  expect(saved.inputs[0].value.properties.amount.source.nodeKey).toBe('b');
  expect(saved.inputs[0].value.properties.items.args[0].source.nodeKey).toBe('upstream');
  expect(saved.outputs[0].name).toBe('accepted');
  expect(saved.actions[0].mode).toBe('MapOnly');
  expect(JSON.stringify(saved)).not.toContain('customer-42');
});

test('persists related selector pins and Step Values modes through the real designer', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createMappingFixture(browser);
  await fixture.publish(); // A distinct, authorized published main workflow becomes a real related choice.
  const { child, editor, mapper } = await openMappingEditor(fixture, app, browser, screen);
  await mapper.getByRole('tab', /^2\. Inputs/).click();
  const related = mapper.getByRole('region', 'Related workflows');
  const choices = related.getByLabel('Add a related workflow');
  await expect(choices).toBeEnabled();
  const offered = choices.getByRole('option', new RegExp(fixture.name + ' Main'));
  await expect(offered).toHaveCount(1);
  const pin = await offered.getAttribute('value');
  await choices.selectOption({ value: pin! });
  await expect(related.getByLabel('Match instances by')).toHaveValue('BusinessKey');
  await related.getByLabel('If several instances match').selectOption({ value: 'Latest' });
  const key = browser.locator('app-mapping-selectors [data-slot$=".key"]');
  await key.getByLabel('Value', { exact: true }).fill('related-key');
  await key.getByLabel('Value', { exact: true }).press('Tab');
  await editor.getByRole('button', 'Apply settings').click();
  await expect
    .poll(
      async () => (await fixture.configuration()).typedMapping?.selectors?.[0]?.workflowVersionId,
    )
    .toBe(pin);
  const selector = (await fixture.configuration()).typedMapping.selectors[0];
  expect(selector.workflowDefinitionId).toBe(fixture.main.definitionId);
  expect(selector.multipleMatches).toBe('Latest');
  expect(selector.keyValue.value).toBe('related-key');
  await child.getByRole('button', 'Close configuration', { exact: true }).click();
  await child.getByRole('button', 'Select Origin Step', { exact: true }).press('Enter');
  await child.getByRole('button', /^Values/).click();
  const values = screen.getByRole('region', 'Step field values', { exact: true });
  const choose = values.getByLabel('Field to fill', { exact: true });
  const notes = choose.getByRole('option', /^Notes —/);
  await expect(notes).toHaveCount(1);
  await choose.selectOption({ value: (await notes.getAttribute('value'))! });
  await values.getByRole('button', 'Map this field').click();
  await values.getByLabel('Behaviour', { exact: true }).selectOption({ value: 'Prefill' });
  const binding = browser.locator('app-workflow-step-mapping [data-binding]');
  await binding.getByLabel('Type', { exact: true }).selectOption({ value: 'null' });
  await values.getByRole('button', 'Check these bindings').click();
  await expect
    .poll(async () => {
      const version = await fixture.api<DesignerVersion>(fixture.path);
      return JSON.parse(version.activities.find((n) => n.nodeKey === 'a')!.configurationJson)
        .typedMapping?.stepBindings?.[0];
    })
    .toMatchObject({
      fieldKey: 'notes',
      mode: 'Prefill',
      formId: fixture.forms[0].id,
      formVersion: 1,
      value: { op: 'literal', value: null },
    });
  await values.getByLabel('Behaviour', { exact: true }).selectOption({ value: 'ReadOnly' });
  await expect
    .poll(async () => {
      const version = await fixture.api<DesignerVersion>(fixture.path);
      return JSON.parse(version.activities.find((n) => n.nodeKey === 'a')!.configurationJson)
        .typedMapping?.stepBindings?.[0]?.mode;
    })
    .toBe('ReadOnly');
});

test('a hierarchical Step exposes its Values editor to keyboard navigation', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createMappingFixture(browser);
  const { child } = await openMappingEditor(fixture, app, browser, screen);
  await child.getByRole('button', 'Close configuration', { exact: true }).click();
  await child.getByRole('button', 'Select Origin Step', { exact: true }).press('Enter');
  await expect(child.getByRole('button', /^Values/)).toBeVisible();
  await child.getByRole('button', /^Values/).press('Enter');
  await expect(screen.getByRole('region', 'Step field values', { exact: true })).toBeVisible();
});

test('real mapped work item displays locked null over a default and preserves edited prefill after reopening', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createDesignerFoundation(browser);
  const form = (
    await fixture.api<{ value: { schemaJson: string } }>(
      `v1/form-engine/forms/${fixture.forms[0].id}`,
    )
  ).value;
  const schema = JSON.parse(form.schemaJson);
  schema.elements.find(
    (field: { data_name: string }) => field.data_name === 'notes',
  ).default_value = 'Cairo';
  await fixture.api(`v1/form-engine/forms/${fixture.forms[0].id}/schema`, 'PUT', {
    schemaJson: JSON.stringify(schema),
  });
  await fixture.api(`v1/form-engine/forms/${fixture.forms[0].id}/publish`, 'POST', {});
  const path = fixture.path;
  const version = await fixture.api<DesignerVersion>(path);
  const config = JSON.parse(
    version.activities.find((node) => node.nodeKey === 'a')!.configurationJson,
  );
  config.formSteps[0].versionNo = 2;
  const binding = (id: string, fieldKey: string, mode: string, value: unknown) => ({
    id,
    formStepId: 'aForm',
    formId: fixture.forms[0].id,
    formVersion: 2,
    fieldKey,
    mode,
    schema: { type: 'string', nullable: true },
    value: { op: 'literal', value },
  });
  config.typedMapping = {
    contractVersion: 1,
    inputs: [],
    outputs: [],
    selectors: [],
    actions: [],
    callable: false,
    stepBindings: [
      binding('null-notes', 'notes', 'ReadOnly', null),
      binding('editable', 'approved', 'Prefill', 'initial-prefill'),
    ],
  };
  let xml = await fixture.api<string>(path + '/xml');
  xml = xml.replace(
    /(<Activity\b[^>]*type="UserTask")/g,
    `$1 assignmentGroupId="${fixture.group.id}"`,
  );
  xml = xml.replace(
    /(<Activity\b[^>]*type="UserTask"[^>]*?)\s*\/>/g,
    '$1><Outcomes><Outcome key="APPROVE" name="Accept" isDefault="true"/><Outcome key="REJECT" name="Reject" requiresComment="true"/></Outcomes></Activity>',
  );
  xml = xml.replace(
    /(<Activity\b[^>]*nodeKey="a"[^>]*configurationJson=")[^"]*(")/,
    '$1' + xmlAttribute(config) + '$2',
  );
  await fixture.api(path + '/xml', 'PUT', { xmlContent: xml, expectedRevision: version.revision });
  const validation = await fixture.api<{ isValid: boolean; errors: unknown[] }>(
    path + '/validate',
    'POST',
    {},
  );
  expect(validation.errors).toEqual([]);
  expect(validation.isValid).toBe(true);
  await fixture.api(path + '/publish', 'POST', {});
  const main = await fixture.api<DesignerVersion>(fixture.parentPath);
  const owner = JSON.parse(
    main.activities.find((node) => node.nodeKey === 'owner')!.configurationJson,
  );
  const childInfo = await fixture.api<{ definitionKey: string }>(
    `workflow/definitions/${fixture.draft.definitionId}`,
  );
  const parentXml = (await fixture.api<string>(fixture.parentPath + '/xml')).replace(
    /(<Activity\b[^>]*nodeKey="owner"[^>]*configurationJson=")[^"]*(")/,
    '$1' +
      xmlAttribute({
        ...owner,
        versionId: fixture.draft.versionId,
        definitionKey: childInfo.definitionKey,
      }) +
      '$2',
  );
  await fixture.api(fixture.parentPath + '/xml', 'PUT', {
    xmlContent: parentXml,
    expectedRevision: main.revision,
  });
  const validParent = await fixture.api<{ errors: unknown[] }>(
    fixture.parentPath + '/validate',
    'POST',
    {},
  );
  expect(validParent.errors).toEqual([]);
  await fixture.api(fixture.parentPath + '/publish', 'POST', {});
  const { instanceId: id } = await fixture.api<{ instanceId: string }>(
    `workflow/workspace/definitions/${fixture.parent.definitionId}/instances`,
    'POST',
    { requestId: randomUUID(), reference: fixture.name, isDemo: false },
  );
  await app.open('/admin/workflow/instances/' + id);
  const notes = screen.getByRole('textbox', 'Notes', { exact: false, disabled: true });
  await expect(notes).toHaveValue('');
  await expect(notes).toBeDisabled();
  const approved = screen.getByRole('textbox', 'Approved', { exact: false, disabled: false });
  await expect(approved).toHaveValue('initial-prefill');
  await approved.fill('edited-prefill');
  await screen.getByRole('button', 'Refresh', { exact: true }).click();
  await expect(approved).toHaveValue('edited-prefill');
  await expect(notes).toHaveValue('');
  const before = activeTasks(await fixture.api<Detail>('workflow/workspace/instances/' + id))[0];
  await browser.reload();
  await expect(notes).toBeVisible();
  const reloaded = await fixture.api<Detail>('workflow/workspace/instances/' + id);
  writeFileSync(
    resolve(testArtifactDirectory('mapped-runtime'), 'mapped-runtime-reload-context.json'),
    JSON.stringify(
      {
        request: { method: 'GET', path: 'workflow/workspace/instances/' + id },
        before,
        response: reloaded,
        expectedUnsavedPrefill: 'edited-prefill',
      },
      null,
      2,
    ),
  );
  await expect(approved).toHaveValue('edited-prefill');
  await expect(notes).toHaveValue('');
  const after = activeTasks(await fixture.api<Detail>('workflow/workspace/instances/' + id))[0];
  expect(after.id).toBe(before.id);
  expect(after.formVisitId).toBe(before.formVisitId);
  await expect(screen.getByRole('region', 'Mapped values and follow-up calls')).toBeVisible();
  await screen
    .getByRole('textbox', 'Routing choice', { exact: false, disabled: false })
    .fill('END');
  await screen.getByRole('button', 'Submit and continue').click();
  await expect
    .poll(async () => {
      const detail = await fixture.api<Detail>('workflow/workspace/instances/' + id);
      return detail.tree
        .flatMap((run) =>
          run.activities.flatMap((activity) => activity.tasks.map((entry) => entry.task)),
        )
        .find((task) => task.id === before.id)
        ?.formHistory.find((visit) => visit.visitId === before.formVisitId)?.answers;
    })
    .toMatchObject({ route: 'END', notes: null, approved: 'edited-prefill' });
});

test('real source drag maps a form field onto the request body', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createMappingFixture(browser);
  const { mapper } = await openMappingEditor(fixture, app, browser, screen);
  // The request body is an input only once the endpoint declares it.
  await mapper.getByText('Import structure from an OpenAPI document', { exact: true }).click();
  await mapper.getByLabel('…or paste the document').fill(JSON.stringify(mappingOpenApi));
  await mapper.getByLabel('Operation path', { exact: true }).fill('/echo');
  await mapper.getByLabel('Operation method', { exact: true }).selectOption({ value: 'POST' });
  const [imported] = await Promise.all([
    browser.waitForResponse('**/mapping/import-openapi'),
    mapper.getByRole('button', 'Import operation').click(),
  ]);
  expect(imported.status).toBe(200);
  await mapper.getByRole('tab', /^2\. Inputs/).click();
  await mapper.getByRole('button', 'Map this input').click();
  const body = browser.locator('[data-slot="input:body:$body"]');
  await body.getByRole('button', 'Add field', { exact: true }).click();
  await body.getByLabel('Field name', { exact: true }).fill('amount');
  await body.getByLabel('Field name', { exact: true }).press('Tab');
  await mapper.getByRole('region', 'Source browser').getByLabel('Search sources').fill('Amount');
  const amount = browser.locator('[data-source="amount"]').filter({ hasText: 'Other Step' });
  await expect(amount).toHaveCount(1);
  // The body object already holds an expression, and a drop never replaces one, so drop onto an empty field.
  const field = browser.locator('[data-slot="input:body:$body.amount"]');
  // A real multi-move mouse drag from a leaf whose position is checked, so the grab cannot land on the neighbouring Step's Amount.
  await pointerDrag(
    browser,
    '[data-source="amount"]:has(.mx-crumbs[title*="Other Step"])',
    '[data-slot="input:body:$body.amount"]',
  );
  await expect(field).toContainText('Other Step');
});
