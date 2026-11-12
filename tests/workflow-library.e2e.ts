import { test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { apiFor } from './support/activity-forms.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('library');

interface LibraryItem {
  id: string;
  name: string;
  definitionKey: string;
  versionId: string;
  versionStatus: string;
  scopes: { level: string; code: string; nameEn: string }[];
  children: LibraryItem[];
}

test('workflow library groups children, filters, paginates and opens the selected designer', async ({
  app,
  browser,
  screen,
}) => {
  const admin = accounts.next();
  await app.open('/login');
  await screen.getByLabel('Username').fill(admin.username);
  await screen.getByLabel('Password').fill(admin.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'Workflows', { exact: true })).toBeVisible();
  const api = apiFor(browser);
  const initial = await api<{ items: LibraryItem[]; totalGroups: number }>(
    'workflow/workspace/library?pageSize=50',
  );
  const root =
    initial.items.find((i) => i.definitionKey === 'UNRC-LOCAL') ??
    initial.items.find((i) => i.children.length > 0);
  expect(root).toBeDefined();
  expect(root!.children.length).toBeGreaterThan(0);
  const child = root!.children[0];
  await screen.getByRole('link', 'Workflows', { exact: true }).click();
  await expect(screen.getByRole('heading', 'Workflow library')).toBeVisible();
  await screen.getByRole('textbox', 'Find a workflow', { exact: false }).fill(root!.name);
  await screen.getByRole('button', 'Search', { exact: true }).click();
  await expect(screen.getByRole('heading', root!.name)).toBeVisible();
  const expand = screen.getByRole('button', 'Toggle children of ' + root!.name);
  // Earlier suites can push this group off the initial page, whose groups alone
  // are expanded by default. Search preserves that expansion state.
  if (initial.items.slice(0, 10).some((item) => item.id === root!.id)) {
    await expect(expand).toHaveAttribute('aria-expanded', 'true');
  } else if ((await expand.getAttribute('aria-expanded')) !== 'true') {
    await expand.click();
  }
  await expect(screen.getByRole('heading', child.name)).toBeVisible();
  await expand.click();
  await expect(screen.getByRole('heading', child.name)).toBeHidden();
  await expand.click();
  await expect(screen.getByRole('heading', child.name)).toBeVisible();
  await screen.getByRole('textbox', 'Find a workflow', { exact: false }).fill(child.name);
  await screen.getByRole('button', 'Search', { exact: true }).click();
  await expect(screen.getByRole('heading', root!.name)).toBeVisible();
  await expect(screen.getByRole('heading', child.name)).toBeVisible();
  await screen
    .getByRole('textbox', 'Find a workflow', { exact: false })
    .fill('no-workflow-matches-this-search');
  await screen.getByRole('button', 'Search', { exact: true }).click();
  await expect(screen.getByRole('heading', 'No workflows found')).toBeVisible();
  await screen.getByRole('button', 'Reset filters').click();

  // Extra definitions are created only in the launcher's disposable test database.
  for (let i = 0; i < 6; i++) {
    await api(
      'workflow/definitions',
      'POST',
      { definitionKey: 'library-page-' + i, name: 'Library pagination ' + i },
      201,
    );
  }
  await screen.getByRole('button', 'Search', { exact: true }).click();
  await screen.getByRole('combobox', 'Groups per page').selectOption('5');
  await expect(screen.getByRole('button', 'Next', { exact: true })).toBeEnabled();
  await screen.getByRole('button', 'Next', { exact: true }).click();
  await expect(screen.getByRole('navigation', 'Workflow pages')).toContainText('Page 2');
  await screen.getByRole('textbox', 'Find a workflow', { exact: false }).fill(child.name);
  await screen.getByRole('button', 'Search', { exact: true }).click();
  await expect(screen.getByRole('navigation', 'Workflow pages')).toContainText('Page 1');
  await screen
    .getByRole(
      'link',
      (child.versionStatus === 'Draft' ? 'Continue editing ' : 'View workflow ') + child.name,
    )
    .click();
  await expect(browser).toHaveURL(
    `/admin/workflow/definitions/${child.id}/versions/${child.versionId}/designer`,
  );
  await screen.getByRole('button', 'Workflows', { exact: true }).click();
  await expect(screen.getByRole('textbox', 'Find a workflow', { exact: false })).toHaveValue(
    child.name,
  );
  await expect(screen.getByRole('combobox', 'Groups per page')).toHaveValue('0: 5');
  await expect(screen.getByRole('heading', child.name)).toBeVisible();
});
