import { test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { randomUUID } from 'node:crypto';
import { apiFor } from './support/activity-forms.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('create-scope');

test('creates a main workflow with only a cluster and a department-independent activity', async ({
  app,
  browser,
  screen,
}) => {
  const admin = accounts.next();
  await app.open('/login');
  await screen.getByLabel('Username').fill(admin.username);
  await screen.getByLabel('Password').fill(admin.password);
  await screen.getByRole('button', 'Sign in').click();
  await screen.getByRole('link', 'Workflows', { exact: true }).click();
  const api = apiFor(browser);
  const activities = await api<{ id: string; code: string; parentCode?: string }[]>(
    'workflow/workspace/lookups/field-activity-types',
  );
  const activity = activities.find((item) => item.code === 'UNRC')!;
  expect(activity).toBeDefined();
  expect(activity.parentCode == null).toBe(true);
  const name = 'Cluster-only creation ' + randomUUID();
  await screen.getByRole('button', 'Create workflow', { exact: true }).click();
  await screen.getByLabel('Workflow name', { exact: true }).fill(name);
  await screen.getByLabel('Activity', { exact: false }).selectOption({ value: activity.id });
  const create = screen.getByRole('button', 'Create and open designer');
  await expect(browser).toHaveURL(/definitions\/new$/);
  await create.click();
  await expect(screen.getByRole('alert')).toContainText('at least one Cluster');
  // The scope is a list of entries: choose a Cluster, then Add it. A chosen-but-not-added entry must not be dropped.
  const clusterName = (
    await browser.locator('select[data-level="clusterCode"] option:nth-child(2)').textContent()
  )?.trim();
  expect(clusterName).toBeTruthy();
  await screen.getByLabel('Cluster', { exact: false }).selectOption(clusterName!);
  await create.click();
  await expect(screen.getByRole('alert')).toContainText(
    'Add or cancel the organization scope entry',
  );
  await screen.getByRole('button', 'Add', { exact: true }).click();
  await expect(browser.locator('[data-scope-entry]')).toContainText(clusterName!);
  await expect(create).toBeEnabled();
  await create.click();
  await expect(browser).toHaveURL(
    /\/admin\/workflow\/definitions\/[^/]+\/versions\/[^/]+\/designer/,
  );
  const library = await api<{ items: { name: string; id: string; versionId: string }[] }>(
    'workflow/workspace/library?search=' + encodeURIComponent(name),
  );
  const saved = library.items.find((item) => item.name === name)!;
  expect(saved).toBeDefined();
  const version = await api<{ workspaceJson: string }>(
    `workflow/definitions/${saved.id}/versions/${saved.versionId}`,
  );
  const settings = JSON.parse(version.workspaceJson);
  expect(settings.organizationScopes).toHaveLength(1);
  expect(settings.organizationScopes[0].level).toBe('Cluster');
  expect(settings.organizationScopes[0].cbuCode == null).toBe(true);
  expect(settings.scopeRows).toHaveLength(1);
  expect(settings.scopeRows[0].clusterCode).toBe(settings.organizationScopes[0].clusterCode);
  expect(settings.fieldActivityTypeId).toBe(activity.id);
  const context = screen.getByRole('region', 'Workflow context');
  await expect(context.getByRole('heading', name)).toBeVisible();
  await expect(context).toContainText('Main workflow designer');
  await expect(context).toContainText('Unregistered Connection');
  await expect(context.getByRole('button', 'Add Task Type')).toBeVisible();
  await context.getByRole('button', 'Review setup').click();
  await expect(screen.getByRole('combobox', 'Activity', { exact: false })).toBeVisible();
});
