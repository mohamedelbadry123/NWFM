import { test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { createActivityForms } from './support/activity-forms.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('hierarchy');

test('main workflow stays behind a child modal and restores saved steps on reopening', async ({
  app,
  browser,
  screen,
}) => {
  const user = accounts.next();
  await app.open('/login');
  await screen.getByLabel('Username').fill(user.username);
  await screen.getByLabel('Password').fill(user.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'New Instance', { exact: false })).toBeVisible();
  const fixture = await createActivityForms(browser);
  const catalog = await fixture.api<{ workspaceJson: string }[]>('workflow/workspace/catalog');
  const settings = { ...JSON.parse(catalog[0].workspaceJson), kind: 'Main', designerVersion: 3 };
  const main = await fixture.api<{ definitionId: string; versionId: string }>(
    'workflow/workspace/definitions',
    'POST',
    {
      name: fixture.name + ' hierarchy',
      settings,
    },
  );
  const escape = (value: unknown) =>
    JSON.stringify(value)
      .replaceAll('&', '&amp;')
      .replaceAll('"', '&quot;')
      .replaceAll('<', '&lt;');
  const config = {
    taskTypeId: fixture.type.id,
    departmentCode: settings.departmentCode,
    fieldActivityCode: settings.fieldActivityCode,
  };
  const mainPath = `workflow/definitions/${main.definitionId}/versions/${main.versionId}`;
  const current = await fixture.api<{ revision: string; workspaceJson: string }>(mainPath);
  Object.assign(settings, JSON.parse(current.workspaceJson));
  await fixture.api(mainPath + '/xml', 'PUT', {
    expectedRevision: current.revision,
    xmlContent: `<Workflow xmlns="https://privora.io/workflow/v1" workspaceJson="${escape(settings)}"><Activities><Activity nodeKey="start" type="Start" name="Start" positionX="200" positionY="20"/><Activity nodeKey="meter" type="MainActivity" name="Meter Installation" positionX="200" positionY="160" configurationJson="${escape(config)}"/><Activity nodeKey="end" type="End" name="End" positionX="200" positionY="400"/></Activities><Transitions><Transition key="begin" from="start" to="meter"/><Transition key="finish" from="meter" to="end"/></Transitions></Workflow>`,
  });
  await app.open(
    `/admin/workflow/definitions/${main.definitionId}/versions/${main.versionId}/designer`,
  );
  const palette = browser.locator('app-workflow-hierarchy-palette');
  await expect(palette.getByRole('button')).toHaveCount(6);
  await expect(palette.getByRole('button', /^Step/)).toBeDisabled();
  await screen.getByRole('button', 'Select Meter Installation').click();
  await screen.getByRole('button', 'Create child workflow').click();
  const childWindow = screen.getByRole('dialog', 'Meter Installation — child workflow');
  await expect(childWindow).toBeVisible();
  await expect(childWindow.getByRole('region', 'Workflow context')).toContainText(
    'Child workflow designer',
  );
  await expect(childWindow.getByRole('region', 'Workflow context')).toContainText(
    fixture.name + ' hierarchy',
  );
  await childWindow.getByRole('button', /^Step/).click();
  await childWindow.getByRole('button', /^Identity/).click();
  await browser
    .locator('.child-editor-content app-workflow-designer input[formcontrolname="name"]')
    .fill('Registration');
  await childWindow.getByRole('button', /^Form/).click();
  await childWindow.getByRole('button', 'Select form').first().click();
  await expect(childWindow.getByText('Selected step form')).toBeVisible();
  await expect(childWindow).toHaveAttribute('aria-modal', 'true');
  const mainContent = browser.locator('.designer-shell.has-focused-child > .designer-main').first();
  await expect(mainContent).toHaveAttribute('inert', '');
  await childWindow.getByRole('button', 'Full screen child workflow').click();
  await expect(browser.locator('.child-editor-overlay.child-editor-fullscreen')).toBeVisible();
  await childWindow.getByRole('button', 'Minimize child workflow').click();
  await expect(childWindow).toBeHidden();
  const dock = screen.getByRole('navigation', 'Minimized child workflows');
  await expect(dock).toBeVisible();
  // The parent's properties dialog stays open and makes the canvas aria-hidden, so read the DOM, not the accessibility tree.
  await expect(
    browser.locator(
      'app-workflow-designer-context:not(.child-editor-content *) [aria-label="Workflow context"]',
    ),
  ).toContainText('Task Type: Meter Installation');
  await dock.getByRole('button', 'Restore Meter Installation', { exact: true }).click();
  await expect(childWindow).toBeVisible();
  await expect(childWindow.getByText('Selected step form')).toBeVisible();
  // The restored Step keeps its modal inspector open, so the child canvas is aria-hidden behind it.
  await expect(childWindow.getByRole('dialog', 'Registration', { exact: true })).toBeVisible();
  await expect(browser.locator('.child-editor-overlay.child-editor-fullscreen')).toBeVisible();
  await childWindow.getByRole('button', 'Restore modal size').click();
  await expect(browser.locator('.child-editor-overlay.child-editor-fullscreen')).toHaveCount(0);
  await childWindow.getByRole('button', 'Close child workflow', { exact: true }).click();
  await expect(childWindow).toBeHidden();
  // The parent's properties dialog stays open and makes the canvas aria-hidden, so read the DOM, not the accessibility tree.
  await expect(
    browser.locator(
      'app-workflow-designer-context:not(.child-editor-content *) [aria-label="Workflow context"]',
    ),
  ).toContainText('Task Type: Meter Installation');
  await screen.getByRole('button', 'Open child workflow').click();
  await expect(childWindow).toBeVisible();
  await expect(childWindow.getByRole('button', 'Select Registration')).toBeVisible();
  await childWindow.getByRole('button', 'Select Registration').click();
  await childWindow.getByRole('button', /^Form/).click();
  await expect(childWindow.getByText('Selected step form')).toBeVisible();
  const child = await fixture.api<{ id: string; versionId: string }>(
    'workflow/workspace/children',
    'POST',
    { parentVersionId: main.versionId, nodeKey: 'meter' },
  );
  const childPath = `workflow/definitions/${child.id}/versions/${child.versionId}`;
  await expect
    .poll(async () => JSON.stringify(await fixture.api(childPath)))
    .toContain('Registration');
  const reopened = await fixture.api<{ id: string; versionId: string }>(
    'workflow/workspace/children',
    'POST',
    { parentVersionId: main.versionId, nodeKey: 'meter' },
  );
  expect(reopened.id).toBe(child.id);
  expect(reopened.versionId).toBe(child.versionId);
  await childWindow.getByRole('button', 'Close child workflow', { exact: true }).click();
  await expect(childWindow).toBeHidden();
  await expect(screen.getByText('Saved', { exact: true })).toBeVisible();
  await browser.reload();
  await screen.getByRole('button', 'Select Meter Installation').click();
  await screen.getByRole('button', /^Child workflow Build/).click();
  await screen.getByRole('button', 'Open child workflow').click();
  await expect(childWindow.getByRole('button', 'Select Registration')).toBeVisible();
});
