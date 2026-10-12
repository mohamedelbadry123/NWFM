import { beforeEach, test } from '@e2e-dev/web';
import { credentials, expect } from 'e2e';
import { randomUUID } from 'node:crypto';
import {
  activeTasks,
  createActivityForms,
  type Detail,
  type Task,
} from './support/activity-forms.js';

beforeEach(async ({ app, screen, browser }) => {
  const user = credentials.user('admin');
  await app.open('/login');
  await screen.getByLabel('Username').fill(user.username);
  await screen.getByLabel('Password').fill(user.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'New Instance', { exact: false })).toBeVisible();
  await expect(browser).toHaveURL(/\/admin\/workflow\/definitions$/);
});

test('activity forms route forward, backward and to fresh child executions with persistent history', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createActivityForms(browser);
  await fixture.publish();
  const id = await fixture.start();
  const detail = () => fixture.api<Detail>('workflow/workspace/instances/' + id);
  await app.open('/admin/workflow/instances/' + id);
  await expect(
    screen.getByText('Complete the child workflow before approving this activity.'),
  ).toBeVisible();
  const submit = screen.getByRole('button', 'Submit and continue');
  const current = browser.locator('[aria-current="step"]');
  await expect(current).toContainText('Form 1');
  // Required-field validation prevents a visit and submission from advancing.
  const first = activeTasks(await detail())[0];
  await submit.click();
  expect(activeTasks(await detail())[0].formVisitId).toBe(first.formVisitId);
  const route = screen.getByRole('textbox', 'Routing choice', { exact: false, disabled: false });
  const send = async (value: string, next: string) => {
    const before = activeTasks(await detail())[0];
    await route.fill(value);
    await submit.click();
    await expect
      .poll(async () => activeTasks(await detail())[0]?.formVisitId ?? before.formVisitId)
      .not.toBe(before.formVisitId);
    await expect(current).toContainText(next);
  };
  await send('FORM3', 'Form 3');
  let task = activeTasks(await detail())[0];
  expect(task.id).toBe(first.id);
  expect(task.dueAt).toBe(first.dueAt);
  expect(task.assignmentGroupId).toBe(first.assignmentGroupId);
  await send('FORM1', 'Form 1');
  await send('REPEAT', 'Form 1');
  await expect(route).toHaveValue('');
  await route.fill('NEXT');
  await screen.getByRole('button', 'Refresh', { exact: true }).click();
  await expect(route).toHaveValue('NEXT');
  await send('NEXT', 'Form 2');
  task = activeTasks(await detail())[0];
  // Retry a recorded visit and reject an unknown visit without changing state.
  const replay = await fixture.api<Task>(`workflow/work-items/${task.id}/complete`, 'POST', {
    actionTaken: 'APPROVE',
    formVisitId: first.formVisitId,
    formValues: { route: 'END' },
  });
  expect(replay.formHistory).toHaveLength(4);
  expect(replay.formVisitId).toBe(task.formVisitId);
  const stale = await fixture.api<{ code: string }>(
    `workflow/work-items/${task.id}/complete`,
    'POST',
    { actionTaken: 'APPROVE', formVisitId: randomUUID(), formValues: { route: 'END' } },
    400,
  );
  expect(stale.code).toBe('Workflow.Form.StaleVisit');
  await send('NEXT', 'Form 3');
  await send('NEXT', 'Form 1'); // Child finishes; parent opens its own first form.
  await send('FORM2', 'Form 2');
  await screen.getByRole('spinbutton', 'Amount', { exact: false, disabled: false }).fill('1500');
  await send('CONDITIONAL', 'Form 1'); // Activity B starts a fresh child.
  await send('END', 'Form 1'); // B's parent form becomes actionable.
  await send('OTHER', 'Form 1'); // Back to A, with another fresh child.
  await send('END', 'Form 1');
  await route.fill('END');
  await submit.click();
  await expect(browser.locator('app-workflow-instance-workspace header > div > p')).toContainText(
    'Completed',
  );
  await browser.reload();
  await expect(browser.locator('app-workflow-instance-workspace header > div > p')).toContainText(
    'Completed',
  );
  await expect(submit).toHaveCount(0);
  const result = await detail();
  expect(result.tree).toHaveLength(4);
  expect(result.tree.every((run) => run.status === 'Completed')).toBe(true);
  expect(
    result.tree[0].activities.filter((activity) => activity.name === 'Activity A'),
  ).toHaveLength(2);
  const history = result.tree.flatMap((run) =>
    run.activities.flatMap((activity) => activity.tasks.flatMap((entry) => entry.task.formHistory)),
  );
  expect(history).toHaveLength(12);
  expect(new Set(history.map((visit) => visit.visitId)).size).toBe(12);
  expect(history.every((visit) => visit.versionNo === 1)).toBe(true);
  expect(history.find((visit) => visit.ruleId === 'nested')?.answers.amount).toBe(1500);
});

test('designer shows independent child previews and blocks invalid form routes before publication', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createActivityForms(browser);
  await app.open(
    `/admin/workflow/definitions/${fixture.main.definitionId}/versions/${fixture.main.versionId}/designer`,
  );
  const previews = screen.getByRole('region', 'Linked child workflow, read only');
  await expect(previews).toHaveCount(2);
  await expect(previews.first().getByText('Child review')).toBeVisible();
  await expect(previews.first().getByRole('link', 'Open child workflow ↗')).toHaveAttribute(
    'href',
    `/admin/workflow/definitions/${fixture.child.definitionId}/versions/${fixture.child.versionId}/designer`,
  );
  const xml = await fixture.api<string>(fixture.mainPath + '/xml');
  expect(xml).not.toContain('name="Child review"');
  const invalid = fixture.mainXml.replaceAll(
    '&quot;target&quot;:&quot;form3&quot;',
    '&quot;target&quot;:&quot;missing-form&quot;',
  );
  await fixture.api(fixture.mainPath + '/xml', 'PUT', { xmlContent: invalid });
  const validation = await fixture.api<{ isValid: boolean; errors: { code: string }[] }>(
    fixture.mainPath + '/validate',
    'POST',
    {},
  );
  expect(validation.isValid).toBe(false);
  expect(validation.errors.some((error) => error.code === 'ROUTING_DESTINATION')).toBe(true);
  await fixture.api(fixture.mainPath + '/publish', 'POST', {}, 400);
  await fixture.api(fixture.mainPath + '/xml', 'PUT', { xmlContent: fixture.mainXml });
  await fixture.publish();
});

test('task detail claims and submits the pinned form sequence then survives a reload', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createActivityForms(browser);
  await fixture.publish();
  const id = await fixture.start();
  const detail = () => fixture.api<Detail>('workflow/workspace/instances/' + id);
  const first = activeTasks(await detail())[0];
  await app.open('/org/workflow/tasks/' + first.id);
  await screen.getByRole('button', 'Claim', { exact: true }).click();
  await screen.getByRole('button', 'Approve', { exact: true }).click();
  const route = screen.getByRole('textbox', 'Routing choice', { exact: false, disabled: false });
  await route.fill('FORM3');
  await screen.getByRole('button', 'Submit and continue').click();
  await expect(browser.locator('[aria-current="step"]')).toContainText('Form 3');
  await expect(browser).toHaveURL('/org/workflow/tasks/' + first.id);
  await browser.reload();
  await expect(browser.locator('[aria-current="step"]')).toContainText('Form 3');
  await screen.getByRole('tab', 'Action', { exact: true }).click();
  await expect(route).toHaveValue('');
  const after = activeTasks(await detail())[0];
  expect(after.id).toBe(first.id);
  expect(after.status).toBe('Claimed');
  expect(after.formHistory).toHaveLength(1);
  expect(after.formHistory[0].answers.route).toBe('FORM3');
  await route.fill('END');
  await screen.getByRole('button', 'Approve', { exact: true }).click();
  await screen.getByRole('button', 'Submit and continue').click();
  await expect(browser).toHaveURL(/\/org\/workflow\/tasks\?view=claimedByMe$/);
  await expect.poll(async () => activeTasks(await detail())[0]?.id).not.toBe(first.id);
  const done = await fixture.api<Task>('workflow/work-items/' + first.id);
  expect(done.status).toBe('Completed');
  expect(done.formHistory).toHaveLength(2);
});
