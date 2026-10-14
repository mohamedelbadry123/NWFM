import { beforeEach, test } from '@e2e-dev/web';
import { credentials, expect } from 'e2e';
import { randomUUID } from 'node:crypto';
import { apiFor } from './support/activity-forms.js';

interface TaskType {
  id: string;
  nameEn: string;
}
interface Scope {
  level?: string | null;
  code?: string | null;
  departmentCodes: string[];
  taskTypeIds: string[];
}
interface Team {
  id: string;
  scopes: Scope[];
}

beforeEach(async ({ app, screen, browser }) => {
  const user = credentials.user('admin');
  await app.open('/login');
  await screen.getByLabel('Username').fill(user.username);
  await screen.getByLabel('Password').fill(user.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(browser).toHaveURL(/\/admin\/workflow\/definitions$/);
});

test('a team covers task types, named on the list and kept through an edit', async ({
  app,
  browser,
  screen,
}) => {
  const api = apiFor(browser);
  const types = await api<{ value: TaskType[] }>('v1/task-types/active');
  expect(types.value.length).toBeGreaterThan(0);
  const type = types.value[0];

  // Uniquely named, inside the launcher's disposable database only.
  const suffix = randomUUID().replaceAll('-', '').slice(0, 10);
  const name = 'E2E crew ' + suffix;
  const created = await api<{ value: Team }>(
    'v1/teams',
    'POST',
    {
      name,
      userCode: 'E2E' + suffix,
      password: 'E2E-' + suffix + '!',
      isActive: true,
      scopes: [{ level: null, code: null, departmentCodes: [], taskTypeIds: [type.id] }],
    },
    201,
  );
  expect(created.value.scopes).toEqual([expect.objectContaining({ taskTypeIds: [type.id] })]);

  await app.open('/admin/teams');
  await screen.getByPlaceholder('Name or mobile').fill(name);
  await screen.getByPlaceholder('Name or mobile').press('Enter');
  const row = screen.getByRole('row').filter({ hasText: name });
  await expect(row).toContainText('All territories · ' + type.nameEn);

  await row.getByRole('button', 'Edit').click();
  const dialog = screen.getByRole('dialog');
  await expect(dialog.getByText('Task types')).toBeVisible();
  await expect(dialog.getByText('All task types')).toBeVisible();
  await expect(
    dialog.getByText('All territories · All departments · ' + type.nameEn),
  ).toBeVisible();

  // Saving unchanged sends the selector's rows back: the task type must survive the round trip.
  await dialog.getByRole('button', 'Save').click();
  await expect(screen.getByText('Team updated.')).toBeVisible();
  const saved = await api<{ value: Team }>('v1/teams/' + created.value.id);
  expect(saved.value.scopes).toEqual([
    expect.objectContaining({
      level: null,
      code: null,
      departmentCodes: [],
      taskTypeIds: [type.id],
    }),
  ]);
});
