import { test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { createActivityForms } from './support/activity-forms.js';
import { designerAccounts } from './support/designer-accounts.js';
import { testArtifactDirectory } from './support/artifacts.js';
const accounts = designerAccounts('guided-designer');

test('opens an existing draft directly with preserved repairs and creates paired Fork and Join', async ({
  app,
  browser,
  screen,
}) => {
  const user = accounts.next();
  await app.open('/login');
  await screen.getByLabel('Username').fill(user.username);
  await screen.getByLabel('Password').fill(user.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'Workflows', { exact: true })).toBeVisible();
  const fixture = await createActivityForms(browser);
  const api = fixture.api;
  const catalog = await api<{ workspaceJson: string }[]>('workflow/workspace/catalog');
  const settings = {
    ...JSON.parse(catalog[0].workspaceJson),
    kind: 'Child',
    designerVersion: 3,
    taskTypeId: fixture.type.id,
  };
  const definition = await api<{ id: string }>(
    'workflow/definitions',
    'POST',
    { definitionKey: 'GUIDED_' + randomUUID(), name: fixture.name + ' guided practice' },
    201,
  );
  const draft = await api<{ id: string; revision: string }>(
    `workflow/definitions/${definition.id}/versions`,
    'POST',
    {},
    201,
  );
  const path = `workflow/definitions/${definition.id}/versions/${draft.id}`;
  const esc = (v: unknown) =>
    JSON.stringify(v).replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
  const base = {
    taskTypeId: fixture.type.id,
    formSteps: [
      {
        id: 'registrationForm',
        formId: fixture.forms[0].id,
        versionNo: 1,
        name: 'Registration',
        rules: [
          {
            id: 'customerPresent',
            name: 'Customer is present',
            when: {
              match: 'All',
              conditions: [{ field: 'route', operator: 'is_not_empty', value: null }],
              groups: [],
            },
            destination: { kind: 'Next' },
          },
        ],
        otherwise: { kind: 'Next' },
      },
    ],
  };
  const xml = `<Workflow xmlns="https://privora.io/workflow/v1" workspaceJson="${esc(settings)}"><Activities><Activity nodeKey="start" type="Start" name="Start" positionX="100" positionY="100"/><Activity nodeKey="registration" type="UserTask" name="Registration" positionX="100" positionY="250" configurationJson="${esc(base)}"/><Activity nodeKey="location" type="UserTask" name="Go to Location" positionX="500" positionY="250" configurationJson="${esc({ ...base, formSteps: [{ ...base.formSteps[0], rules: [] }] })}"/><Activity nodeKey="end" type="End" name="End" positionX="500" positionY="500"/></Activities><Transitions><Transition key="entry" from="start" to="registration"/><Transition key="finish" from="location" to="end"/></Transitions></Workflow>`;
  await api(path + '/xml', 'PUT', { xmlContent: xml, designerJson: '{}' });
  await app.open('/admin/' + path + '/designer');
  await expect(screen.getByRole('button', 'Upgrade routing', { exact: true })).toHaveCount(0);
  const palette = browser.locator('app-workflow-hierarchy-palette');
  await expect(palette.getByRole('button', /^Condition/)).toBeVisible();
  await expect(palette.getByRole('button', /^Fork/)).toBeVisible();
  await expect(palette.getByRole('button', /^Join \/ Wait/)).toBeVisible();
  await screen.getByRole('button', 'Select Registration routing', { exact: true }).click();
  await expect(
    screen
      .getByText('Choose where Registration continues when this rule matches.', { exact: true })
      .first(),
  ).toBeVisible();
  await browser.locator('app-workflow-routing-editor .setup-review summary').click();
  await expect(screen.getByLabel('Setup checklist')).toBeVisible();
  const prepared = await api<{
    revision: string;
    xmlContent: string;
    workspaceJson: string;
    activities: { nodeKey: string; configurationJson: string }[];
  }>(path);
  expect(JSON.parse(prepared.workspaceJson).designerVersion).toBe(4);
  const preparedXml = await api<string>(path + '/xml');
  expect(preparedXml).toContain('originalJson');
  await api(path + '/routing/prepare', 'POST', { expectedRevision: 'already-prepared' });
  const validation = await api<{
    editId: number;
    result: { isValid: boolean; errors: { code: string }[] };
  }>(path + '/routing/validate-design', 'POST', { xmlContent: preparedXml, editId: 417 });
  expect(validation.editId).toBe(417);
  expect(validation.result.isValid).toBe(false);
  expect(validation.result.errors.some((e) => e.code === 'ROUTING_REPAIR')).toBe(true);
  await screen.getByLabel('Destination', { exact: true }).selectOption({ label: 'Go to Location' });
  await screen.getByRole('button', 'Use configured route', { exact: true }).click();
  await screen.getByRole('button', 'Close configuration', { exact: true }).click();
  await expect(screen.getByRole('status', 'Save status', { exact: true })).toContainText('Saved');
  await expect
    .poll(async () => await api<string>(path + '/xml'))
    .not.toContain('Choose where Registration continues');
  await palette.getByRole('button', /^Fork/).click();
  await expect(screen.getByLabel('Destination', { exact: true })).toHaveCount(2);
  await expect(screen.getByLabel('Paired Join', { exact: true })).not.toHaveValue('');
  await screen.getByRole('button', 'Close configuration', { exact: true }).click();
  await expect(
    screen.getByRole('button', 'Select Wait for branches', { exact: true }),
  ).toBeVisible();
  await browser
    .locator('.designer-topbar')
    .getByRole('button', 'Review issues', { exact: false })
    .click();
  await expect(
    browser
      .locator('.designer-drawer')
      .getByText(/Choose a destination for a route in/)
      .first(),
  ).toBeVisible();
  // Exercise the maintenance transformation only against this run's disposable draft.
  await expect(screen.getByRole('status', 'Save status', { exact: true })).toContainText('Saved');
  const before = await api<{
    status: string;
    revision: string;
    designerJson: string;
    workspaceJson: string;
  }>(path);
  const beforeXml = await api<string>(path + '/xml');
  const folder = testArtifactDirectory('guided-ux-reset');
  const snapshotPath = join(folder, 'snapshot.json');
  const payloadPath = join(folder, 'payload.json');
  writeFileSync(snapshotPath, JSON.stringify({ ...before, xmlContent: beforeXml }));
  execFileSync(
    process.platform === 'win32' ? 'powershell.exe' : 'pwsh',
    [
      '-NoProfile',
      '-File',
      resolve('tests/support/workflow-practice-reset.ps1'),
      '-SnapshotPath',
      snapshotPath,
      '-OutputPath',
      payloadPath,
    ],
    { windowsHide: true },
  );
  const payload = JSON.parse(readFileSync(payloadPath, 'utf8'));
  const originalCanvas = JSON.parse(before.designerJson);
  const resetCanvas = JSON.parse(payload.designerJson);
  expect(resetCanvas.edges).toHaveLength(1);
  expect(resetCanvas.nodes.some((n: { type: string }) => n.type.includes('Gateway'))).toBe(false);
  for (const node of resetCanvas.nodes) {
    const original = originalCanvas.nodes.find(
      (n: { nodeKey: string }) => n.nodeKey === node.nodeKey,
    );
    expect([node.name, node.x, node.y]).toEqual([original.name, original.x, original.y]);
    if (node.type === 'UserTask') {
      const oldConfig = JSON.parse(original.configurationJson);
      const newConfig = JSON.parse(node.configurationJson);
      expect(newConfig.taskTypeId).toBe(oldConfig.taskTypeId);
      expect(newConfig.formSteps[0].formId).toBe(oldConfig.formSteps[0].formId);
    }
  }
  await api(path + '/xml', 'PUT', payload);
  const after = await api<{ status: string; workspaceJson: string; designerJson: string }>(path);
  expect(after.status).toBe('Draft');
  expect(JSON.parse(after.workspaceJson)).toEqual(JSON.parse(before.workspaceJson));
  expect(JSON.parse(after.designerJson)).toEqual(resetCanvas);
  const resetCheck = await api<{ result: { isValid: boolean } }>(
    path + '/routing/validate-design',
    'POST',
    {
      xmlContent: await api<string>(path + '/xml'),
      designerJson: after.designerJson,
      editId: 500,
    },
  );
  expect(resetCheck.result.isValid).toBe(false);
});
