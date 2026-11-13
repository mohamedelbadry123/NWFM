import { test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { createActivityForms, type Detail, type Task } from './support/activity-forms.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('role-assignment');

interface Version {
  revision: string;
  activities: { nodeKey: string; configurationJson: string; assignmentRules: unknown[] }[];
}
test('multiple existing roles survive Task Type and Step edits and authorize a real task', async ({
  app,
  browser,
  screen,
}) => {
  const admin = accounts.next();
  await app.open('/login');
  await screen.getByLabel('Username').fill(admin.username);
  await screen.getByLabel('Password').fill(admin.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'New Instance', { exact: false })).toBeVisible();
  const fixture = await createActivityForms(browser);
  const roles = await fixture.api<{ id: string; nameEn: string; code: string }[]>(
    'workflow/workspace/lookups/roles',
  );
  const administrator = roles.find((r) => r.code === 'Administrator')!;
  const other = roles.find((r) => r.id !== administrator.id)!;
  expect(administrator).toBeDefined();
  expect(other).toBeDefined();
  const selected = [administrator.id, other.id];
  const assign = async (path: string, node: string) => {
    await app.open('/admin/' + path + '/designer');
    await screen.getByRole('button', 'Select ' + node, { exact: true }).press('Enter');
    await screen.getByRole('tab', 'Assignment', { exact: true }).click();
    const control = screen.getByRole('combobox', 'Roles', { exact: true });
    await expect(control).toBeEnabled();
    await control.press('Space');
    for (const role of [administrator, other])
      await screen
        .getByRole('listbox', 'Option List')
        .getByRole('option', role.nameEn, { exact: true })
        .click();
    await control.press('Escape');
    await screen.getByRole('button', 'Apply', { exact: true }).click();
    await screen.getByRole('button', 'Close configuration', { exact: true }).click();
    await screen.getByRole('button', 'Save', { exact: true }).click();
    await expect
      .poll(async () => {
        const version = await fixture.api<Version>(path);
        return JSON.parse(version.activities.find((a) => a.nodeKey === 'a')!.configurationJson)
          .assignmentRoleIds;
      })
      .toEqual(selected);
    const saved = await fixture.api<Version>(path);
    expect(saved.activities.find((a) => a.nodeKey === 'a')!.assignmentRules).toHaveLength(0);
    await app.open('/admin/' + path + '/designer');
    await screen.getByRole('button', 'Select ' + node, { exact: true }).press('Enter');
    await screen.getByRole('tab', 'Assignment', { exact: true }).click();
    await expect(browser.locator('app-workflow-role-assignment')).toContainText(
      administrator.nameEn,
    );
    await expect(browser.locator('app-workflow-role-assignment')).toContainText(other.nameEn);
    await screen.getByRole('button', 'Close configuration', { exact: true }).click();
  };
  await assign(fixture.mainPath, 'Activity A');
  const childOldPath = `workflow/definitions/${fixture.child.definitionId}/versions/${fixture.child.versionId}`;
  const draft = await fixture.api<{ id: string }>(childOldPath + '/clone', 'POST', {}, 201);
  const childPath = `workflow/definitions/${fixture.child.definitionId}/versions/${draft.id}`;
  await assign(childPath, 'Child review');
  const validation = await fixture.api<{ errors: unknown[] }>(childPath + '/validate', 'POST', {});
  expect(validation.errors).toEqual([]);
  await fixture.api(childPath + '/publish', 'POST', {});
  const parentXml = await fixture.api<string>(fixture.mainPath + '/xml');
  const parentVersion = await fixture.api<Version>(fixture.mainPath);
  await fixture.api(fixture.mainPath + '/xml', 'PUT', {
    expectedRevision: parentVersion.revision,
    xmlContent: parentXml.replaceAll(fixture.child.versionId, draft.id),
  });
  await fixture.publish();
  const id = await fixture.start();
  const detail = () => fixture.api<Detail>('workflow/workspace/instances/' + id);
  const first = (await detail()).tree
    .flatMap((execution) => execution.activities)
    .flatMap((activity) => activity.tasks.map((t) => t.task))
    .find((task) => task.status === 'Pending')!;
  expect(first).toBeDefined();
  const available = await fixture.api<
    { id: string; assignmentGroupName: string; assignmentGroupId: string }[]
  >('workflow/work-items/available');
  const task = available.find((t) => t.id === first.id)!;
  expect(task).toBeDefined();
  expect(task.assignmentGroupName).toContain(administrator.nameEn);
  expect(task.assignmentGroupName).toContain(other.nameEn);
  expect(task.assignmentGroupId).toBe('00000000-0000-0000-0000-000000000000');
  await fixture.api(`workflow/work-items/${task.id}/claim`, 'POST', {});
  const claimed = await fixture.api<Task>(`workflow/work-items/${task.id}`);
  const completed = await fixture.api<Task>(`workflow/work-items/${task.id}/complete`, 'POST', {
    actionTaken: 'APPROVE',
    formVisitId: claimed.formVisitId,
    formValues: { route: 'END' },
  });
  expect(completed.status).toBe('Completed');
  expect((await fixture.api<Task>(`workflow/work-items/${task.id}`)).status).toBe('Completed');
});
