import { test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { randomUUID } from 'node:crypto';
import { apiFor } from './support/activity-forms.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('scope-rows');

test('scope rows reject duplicates, edit, remove, disclose and reopen with identical coverage', async ({
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
  await screen.getByRole('button', 'Create workflow', { exact: true }).click();
  const name = 'Scope rows ' + randomUUID();
  await screen.getByLabel('Workflow name', { exact: true }).fill(name);
  const api = apiFor(browser);
  const activities = await api<{ id: string; code: string }[]>(
    'workflow/workspace/lookups/field-activity-types',
  );
  const activity = activities.find((item) => item.code === 'UNRC');
  expect(activity).toBeDefined();
  await screen.getByLabel('Activity', { exact: false }).selectOption({ value: activity!.id });
  const editor = browser.locator('app-workflow-scope-editor');
  const cluster = editor.getByRole('combobox', 'Cluster', { exact: true });
  await expect(cluster).toBeEnabled();
  const codes = await browser.evaluate(() =>
    Array.from(document.querySelectorAll('select[data-level="clusterCode"] option'))
      .map((option) => (option as HTMLOptionElement).value)
      .filter(Boolean),
  );
  expect(codes.length).toBeGreaterThanOrEqual(4);
  for (const code of codes.slice(0, 3)) {
    await cluster.selectOption({ value: code });
    await editor.getByRole('button', 'Add', { exact: true }).press('Enter');
    await expect(cluster).toBeFocused();
  }
  await expect(editor.getByRole('listitem')).toHaveCount(2);
  await editor.getByRole('button', '+1 more').press('Enter');
  await expect(editor.getByRole('listitem')).toHaveCount(3);
  await cluster.selectOption({ value: codes[0] });
  await editor.getByRole('button', 'Add', { exact: true }).click();
  await expect(editor.getByRole('alert')).toContainText('This entry is already in the list.');
  await expect(editor.getByRole('listitem')).toHaveCount(3);
  await editor.getByRole('button', 'Cancel', { exact: true }).click();
  const firstRowId = await editor.getByRole('listitem').first().getAttribute('data-scope-entry');
  await editor
    .getByRole('button', /^Edit:/)
    .first()
    .press('Enter');
  await expect(editor.getByRole('group', 'Edit scope entry')).toBeVisible();
  // A real edit changes the selected Cluster while retaining the row identity.
  await cluster.selectOption({ value: codes[3] });
  await editor.getByRole('button', 'Save changes', { exact: true }).press('Enter');
  expect(await editor.getByRole('listitem').first().getAttribute('data-scope-entry')).toBe(
    firstRowId,
  );
  await editor
    .getByRole('button', /^Remove:/)
    .last()
    .click();
  await expect(editor.getByRole('listitem')).toHaveCount(2);
  await cluster.selectOption({ value: codes[2] });
  await editor.getByRole('button', 'Add', { exact: true }).click();
  await screen.getByRole('button', 'Create and open designer').click();
  await expect(browser).toHaveURL(/\/versions\/[^/]+\/designer/);
  const library = await api<{ items: { name: string; id: string; versionId: string }[] }>(
    'workflow/workspace/library?search=' + encodeURIComponent(name),
  );
  const saved = library.items.find((row) => row.name === name)!;
  expect(saved).toBeDefined();
  const path = `workflow/definitions/${saved.id}/versions/${saved.versionId}`;
  const before = JSON.parse((await api<{ workspaceJson: string }>(path)).workspaceJson);
  expect(before.scopeRows).toHaveLength(3);
  expect(before.organizationScopes).toHaveLength(3);
  expect(
    before.organizationScopes.map((scope: { clusterCode: string }) => scope.clusterCode),
  ).toEqual([codes[3], codes[1], codes[2]]);
  expect(
    before.organizationScopes.every((scope: { level: string }) => scope.level === 'Cluster'),
  ).toBe(true);
  await browser.reload();
  await screen.getByRole('region', 'Workflow context').getByRole('button', 'Review setup').click();
  await expect(editor.getByRole('listitem')).toHaveCount(2);
  await browser.setViewport({ width: 390, height: 844 });
  await editor.getByRole('button', '+1 more').press('Enter');
  await expect(editor.getByRole('listitem')).toHaveCount(3);
  // Locale storage is the app's documented persisted preference, not a product-state fixture.
  await browser.evaluate(() => {
    localStorage.setItem('nwfm_locale', 'ar');
    return true;
  });
  await browser.reload();
  await expect(browser.locator('html')).toHaveAttribute('dir', 'rtl');
  await screen
    .getByRole('region', 'سياق سير العمل')
    .getByRole('button', 'مراجعة الإعدادات')
    .click();
  await expect(editor.getByRole('listitem')).toHaveCount(2);
  await editor.getByRole('button', 'و1 أخرى').press('Enter');
  await expect(editor.getByRole('listitem')).toHaveCount(3);
  const bounds = await editor.boundingBox();
  expect(bounds).toBeTruthy();
  expect(bounds!.width).toBeLessThanOrEqual(390);
  const after = JSON.parse((await api<{ workspaceJson: string }>(path)).workspaceJson);
  expect(after.organizationScopes).toEqual(before.organizationScopes);
  expect(after.scopeRows).toEqual(before.scopeRows);
});
