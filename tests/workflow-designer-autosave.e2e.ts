import { test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { createActivityForms } from './support/activity-forms.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('designer-autosave');

interface Version {
  activities: {
    id: string;
    nodeKey: string;
    name: string;
    nameAr: string;
    configurationJson: string;
  }[];
  transitions: { transitionKey: string }[];
}

test('child designer autosaves typed step data and permanently deletes connections with mouse and keyboard', async ({
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
  const settings = { ...JSON.parse(catalog[0].workspaceJson), kind: 'Main', designerVersion: 4 };
  const main = await fixture.api<{ definitionId: string; versionId: string }>(
    'workflow/workspace/definitions',
    'POST',
    { name: fixture.name + ' autosave', settings },
  );
  const escape = (value: unknown) =>
    JSON.stringify(value)
      .replaceAll('&', '&amp;')
      .replaceAll('"', '&quot;')
      .replaceAll('<', '&lt;');
  const mainPath = `workflow/definitions/${main.definitionId}/versions/${main.versionId}`;
  const mainRevision = await fixture.api<{ revision: string }>(mainPath);
  await fixture.api(mainPath + '/xml', 'PUT', {
    expectedRevision: mainRevision.revision,
    xmlContent: `<Workflow xmlns="https://privora.io/workflow/v1" workspaceJson="${escape(settings)}"><Activities><Activity nodeKey="start" type="Start" name="Start" positionX="200" positionY="20"/><Activity nodeKey="meter" type="MainActivity" name="Meter Installation" positionX="200" positionY="160" configurationJson="${escape({ taskTypeId: fixture.type.id })}"/><Activity nodeKey="end" type="End" name="End" positionX="200" positionY="400"/></Activities><Transitions><Transition key="begin" from="start" to="meter"/><Transition key="finish" from="meter" to="end"/></Transitions></Workflow>`,
  });
  const child = await fixture.api<{ id: string; versionId: string }>(
    'workflow/workspace/children',
    'POST',
    { parentVersionId: main.versionId, nodeKey: 'meter' },
  );
  const childPath = `workflow/definitions/${child.id}/versions/${child.versionId}`;
  const initial = await fixture.api<{ workspaceJson: string; revision: string }>(childPath);
  const config = { taskTypeId: fixture.type.id };
  await fixture.api(childPath + '/xml', 'PUT', {
    expectedRevision: initial.revision,
    xmlContent: `<Workflow xmlns="https://privora.io/workflow/v1" workspaceJson="${escape(JSON.parse(initial.workspaceJson))}"><Activities><Activity nodeKey="start" type="Start" name="Start" positionX="100" positionY="30"/><Activity nodeKey="registration" type="UserTask" name="Step" positionX="120" positionY="200" configurationJson="${escape(config)}"/><Activity nodeKey="location" type="UserTask" name="Second Step" positionX="450" positionY="200" configurationJson="${escape(config)}"/><Activity nodeKey="end" type="End" name="End" positionX="450" positionY="30"/></Activities><Transitions><Transition key="begin" from="start" to="registration"/><Transition key="finish" from="registration" to="end"/></Transitions></Workflow>`,
  });
  await app.open('/admin/' + mainPath + '/designer');
  await screen.getByRole('button', 'Select Meter Installation', { exact: true }).press('Enter');
  await screen.getByRole('button', /^(Create|Open) child workflow$/).click();
  const window = screen.getByRole('dialog', 'Meter Installation — child workflow');
  const name = browser.locator('.child-editor-content input[formcontrolname="name"]');
  const nameAr = browser.locator('.child-editor-content input[formcontrolname="nameAr"]');
  const openSection = async (section: RegExp) => {
    const button = window.getByRole('button', section);
    if ((await button.getAttribute('aria-expanded')) !== 'true') await button.click();
  };
  await window.getByRole('button', 'Select Step', { exact: true }).press('Enter');
  await openSection(/^Identity(?: |$)/);
  await name.fill('Registration');
  await nameAr.fill('تسجيل الطلب');
  // Switch immediately, without Apply or Save; both canvas and persistence must retain the edits.
  await window.getByRole('button', 'Close configuration', { exact: true }).click();
  await window.getByRole('button', 'Select Second Step', { exact: true }).press('Enter');
  await openSection(/^Identity(?: |$)/);
  await name.fill('Go to Location');
  await window.getByRole('button', 'Close configuration', { exact: true }).click();
  await window.getByRole('button', 'Select Registration', { exact: true }).press('Enter');
  await openSection(/^Identity(?: |$)/);
  await expect(name).toHaveValue('Registration');
  await expect(nameAr).toHaveValue('تسجيل الطلب');
  await openSection(/^Form(?: |$)/);
  await window.getByRole('button', 'Select form').first().click();
  await expect
    .poll(async () => {
      const saved = await fixture.api<Version>(childPath);
      return saved.activities
        .filter((a) => ['registration', 'location'].includes(a.nodeKey))
        .map((a) => a.name)
        .sort();
    })
    .toEqual(['Go to Location', 'Registration']);
  await expect
    .poll(
      async () =>
        JSON.parse(
          (await fixture.api<Version>(childPath)).activities.find(
            (a) => a.nodeKey === 'registration',
          )!.configurationJson,
        ).formSteps?.length,
    )
    .toBe(1);
  const connection = () =>
    window.getByRole('button', 'Select connection from Registration to End', { exact: true });
  await window.getByRole('button', 'Close configuration', { exact: true }).click();
  await connection().press('Enter');
  await window.getByRole('button', 'Delete connection', { exact: true }).click();
  await expect(connection()).toHaveCount(0);
  await expect
    .poll(async () =>
      (await fixture.api<Version>(childPath)).transitions.some((t) => t.transitionKey === 'finish'),
    )
    .toBe(false);
  await window.getByRole('button', 'Undo', { exact: true }).click();
  await connection().press('Enter');
  await window.getByRole('button', 'Close configuration', { exact: true }).click();
  await connection().press('Delete');
  await expect(connection()).toHaveCount(0);
  await expect
    .poll(async () =>
      (await fixture.api<Version>(childPath)).transitions.some((t) => t.transitionKey === 'finish'),
    )
    .toBe(false);
  expect(
    (await fixture.api<Version>(mainPath)).transitions.some((t) => t.transitionKey === 'finish'),
  ).toBe(true);
  await expect(window.getByRole('status', 'Save status', { exact: true })).toContainText('Saved');
  await browser.reload();
  await screen.getByRole('button', 'Select Meter Installation', { exact: true }).press('Enter');
  const childSection = screen.getByRole('button', /^Child workflow /);
  if ((await childSection.getAttribute('aria-expanded')) !== 'true') await childSection.click();
  await screen.getByRole('button', 'Open child workflow', { exact: true }).click();
  await expect(window.getByRole('button', 'Select Registration', { exact: true })).toBeVisible();
  await expect(window.getByRole('button', 'Select Go to Location', { exact: true })).toBeVisible();
  await expect(connection()).toHaveCount(0);
  await window.getByRole('button', 'Select Registration', { exact: true }).press('Enter');
  await openSection(/^Form(?: |$)/);
  await expect(window.getByText('Selected step form')).toBeVisible();
});
