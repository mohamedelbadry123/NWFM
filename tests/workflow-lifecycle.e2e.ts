import { test } from '@e2e-dev/web';
import { credentials, expect } from 'e2e';
import { randomUUID } from 'node:crypto';

test('a workflow progresses through child approvals to persisted completion', async ({
  app,
  browser,
  screen,
  agent,
}) => {
  const user = credentials.user('admin');
  const reference = 'E2E lifecycle ' + randomUUID();
  await app.open('/login');
  await screen.getByLabel('Username').fill(user.username);
  await screen.getByLabel('Password').fill(user.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'New Instance', { exact: false })).toBeVisible();
  await app.open('/workflow/start');
  await screen
    .getByLabel('Published workflow')
    .selectOption({ label: 'Demo — Water isolation — Visual · Version 1' });
  await screen.getByLabel('Reference (optional)').fill(reference);
  await screen.getByRole('checkbox', 'Demo instance — allow testing as demo users').check();
  await agent.act('Start the configured workflow instance using Start instance.');
  await expect(browser).toHaveURL(/\/admin\/workflow\/instances\/[0-9a-f-]+$/);
  await expect(screen.getByRole('heading', 'Demo — Water isolation — Visual')).toBeVisible();
  const progress = screen.getByRole('region', 'Workflow progress');
  await expect(progress.getByText('Waiting for child workflow')).toBeVisible();

  for (const [stage, actor] of [
    ['Request Review', 'Demo reviewer'],
    ['Isolation Execution', 'Demo isolation engineer'],
    ['Closure', 'Demo closure officer'],
  ]) {
    await screen.getByLabel('Test as user').selectOption({ label: actor });
    const child = progress
      .getByRole('article')
      .filter({ has: screen.getByRole('heading', stage + ' checklist') })
      .last();
    await expect(child.getByRole('button', 'Accept')).toBeEnabled();
    await agent.act(
      'Accept the {stage} checklist child task only. Leave the parent approval for the next step.',
      { params: { stage } },
    );
    const parent = progress
      .getByRole('article')
      .filter({ has: screen.getByRole('heading', stage, { exact: true }) })
      .first();
    await expect(parent.getByText('Awaiting approval')).toBeVisible();
    await expect(child.getByRole('button', 'Accept')).toHaveCount(0);
    if (stage === 'Closure') {
      await agent.act(
        'Complete the workflow by accepting the parent Closure activity. Required events finish asynchronously; use Refresh while they are processing until the page header shows Completed.',
      );
      await expect(
        browser.locator('app-workflow-instance-workspace header > div > p'),
      ).toContainText('Completed');
    } else {
      await agent.act('Accept the parent {stage} activity after its checklist has completed.', {
        params: { stage },
      });
    }
    await expect(parent.getByRole('button', 'Accept')).toHaveCount(0);
  }
  // The header is the root status; activity headings named Completed are not proof.
  await expect(browser.locator('app-workflow-instance-workspace header > div > p')).toContainText(
    'Completed',
  );
  await expect(screen.getByRole('listitem').filter({ hasText: 'InstanceCompleted' })).toHaveCount(
    4,
  );
  const url = await browser.url();
  await browser.reload();
  await expect(browser).toHaveURL(url);
  await expect(browser.locator('app-workflow-instance-workspace header > div > p')).toContainText(
    'Completed',
  );
  await expect(screen.getByRole('listitem').filter({ hasText: 'InstanceCompleted' })).toHaveCount(
    4,
  );
  await app.open('/admin/workflow/instances');
  await screen.getByPlaceholder('Search by name or reference').fill(reference);
  await screen.getByRole('button', 'Search', { exact: true }).click();
  await expect(screen.getByRole('row').filter({ hasText: reference })).toContainText('Completed');
});
