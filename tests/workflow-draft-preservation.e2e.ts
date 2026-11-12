import { beforeEach, test, type Browser } from '@e2e-dev/web';
import { expect } from 'e2e';
import { createDesignerFoundation } from './support/designer-foundation.js';
import { type Detail } from './support/activity-forms.js';
import {
  configureStep,
  emptyMapping,
  literal,
  noteBinding,
  publishOwnedTree,
  startOwnedTree,
} from './support/mapping-runtime.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('draft-preservation');

interface Item {
  id: string;
  status: string;
  formVisitId: string;
  formDraftKey?: string | null;
  selectedForm?: unknown;
  formValues?: Record<string, unknown>;
  formHistory: { answers: Record<string, unknown> }[];
}

beforeEach(async ({ app, screen }) => {
  const user = accounts.next();
  await app.open('/login');
  await screen.getByLabel('Username').fill(user.username);
  await screen.getByLabel('Password').fill(user.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'Workflows', { exact: true })).toBeVisible();
});

async function mappedTask(browser: Browser) {
  const fixture = await createDesignerFoundation(browser);
  await configureStep(fixture, {
    ...emptyMapping(),
    stepBindings: [
      noteBinding(fixture),
      {
        ...noteBinding(fixture),
        id: 'editable',
        fieldKey: 'approved',
        mode: 'Prefill',
        value: literal('initial-prefill'),
      },
    ],
  });
  await publishOwnedTree(fixture);
  const rootId = await startOwnedTree(fixture);
  const detail = await fixture.api<Detail>('workflow/workspace/instances/' + rootId);
  const task = detail.tree.flatMap((r) => r.activities.flatMap((a) => a.tasks))[0].task;
  const get = () => fixture.api<Item>('workflow/work-items/' + task.id);
  expect((await get()).formDraftKey).toBeTruthy();
  return { ...fixture, rootId, task, get };
}

const identity = (browser: Browser) =>
  browser.evaluate(() => JSON.parse(localStorage.getItem('nwfm_user') ?? '{}').userId || '');
const drafts = async (browser: Browser) => {
  const serialized = await browser.evaluate(() =>
    JSON.stringify(
      Object.keys(sessionStorage)
        .filter((key) => key.startsWith('nwfm.stepDraft.v1.'))
        .map((key) => JSON.parse(sessionStorage.getItem(key)!).answers),
    ),
  );
  return JSON.parse(String(serialized)) as Record<string, unknown>[];
};

test('real bootstrap repairs a legacy blank user ID without 401 and preserves a mapped edit on hard reload', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await mappedTask(browser);
  const verified = await identity(browser);
  expect(verified).toBeTruthy();
  await app.open('/org/workflow/tasks/' + fixture.task.id);
  await screen.getByRole('button', 'Claim', { exact: true }).click();
  await expect(screen.getByRole('button', 'Release', { exact: true })).toBeVisible();
  await browser.evaluate(() => {
    const user = JSON.parse(localStorage.getItem('nwfm_user')!);
    user.userId = '';
    localStorage.setItem('nwfm_user', JSON.stringify(user));
    return null;
  });
  let refreshes = 0;
  await browser.route('**/api/v1/auth/refresh', async (route) => {
    refreshes++;
    await route.continue();
  });
  const [profile] = await Promise.all([
    browser.waitForResponse('**/api/v1/auth/me'),
    browser.reload(),
  ]);
  expect(profile.status).toBe(200);
  expect((await profile.json<{ value: { userId: string } }>()).value.userId).toBe(verified);
  await expect.poll(() => identity(browser)).toBe(verified);
  expect(refreshes).toBe(0);
  await screen.getByRole('tab', 'Action', { exact: true }).click();
  const approved = screen.getByRole('textbox', 'Approved', { exact: false, disabled: false });
  await expect(approved).toHaveValue('initial-prefill');
  await approved.fill('legacy-bootstrap-edit');
  await expect
    .poll(() => drafts(browser))
    .toEqual([expect.objectContaining({ approved: 'legacy-bootstrap-edit' })]);
  await browser.reload();
  await screen.getByRole('tab', 'Action', { exact: true }).click();
  await expect(approved).toHaveValue('legacy-bootstrap-edit');
  await expect(screen.getByRole('textbox', 'Notes', { exact: false, disabled: true })).toHaveValue(
    '',
  );
  expect(refreshes).toBe(0);
});

test('one simulated 401 uses real same-user silent refresh, then edits survive reload and logout clears drafts', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await mappedTask(browser);
  const verified = await identity(browser);
  let simulated = 0;
  let refreshes = 0;
  const path = '**/api/workflow/work-items/' + fixture.task.id;
  await browser.route(path, async (route) => {
    if (route.request.method === 'GET' && simulated === 0) {
      simulated++;
      await route.fulfill({ status: 401, json: { code: 'E2E.SimulatedExpiredAccess' } });
    } else await route.continue();
  });
  await browser.route('**/api/v1/auth/refresh', async (route) => {
    refreshes++;
    await route.continue();
  });
  const [refreshed] = await Promise.all([
    browser.waitForResponse('**/api/v1/auth/refresh'),
    app.open('/org/workflow/tasks/' + fixture.task.id),
  ]);
  expect(refreshed.status).toBe(200);
  await screen.getByRole('button', 'Claim', { exact: true }).click();
  expect(await identity(browser)).toBe(verified);
  expect(simulated).toBe(1);
  expect(refreshes).toBe(1);
  const approved = screen.getByRole('textbox', 'Approved', { exact: false, disabled: false });
  await approved.fill('after-real-refresh');
  await expect
    .poll(() => drafts(browser))
    .toEqual([expect.objectContaining({ approved: 'after-real-refresh' })]);
  await browser.reload();
  await screen.getByRole('tab', 'Action', { exact: true }).click();
  await expect(approved).toHaveValue('after-real-refresh');
  await browser.locator('a[title="Sign out"]').click();
  await expect(screen.getByLabel('Username')).toBeVisible();
  expect(await drafts(browser)).toEqual([]);
  expect(await identity(browser)).toBe('');
  const user = accounts.current();
  await screen.getByLabel('Username').fill(user.username);
  await screen.getByLabel('Password').fill(user.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'Workflows', { exact: true })).toBeVisible();
  await app.open('/org/workflow/tasks/' + fixture.task.id);
  await screen.getByRole('tab', 'Action', { exact: true }).click();
  await expect(approved).toHaveValue('initial-prefill');
});

test('task Details stay authoritative and sparse reassign plus enriched claim cannot submit the old edit', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await mappedTask(browser);
  const target = await fixture.api<{ id: string }>(
    'workflow/assignment-groups',
    'POST',
    {
      code: 'DRAFT' + fixture.task.id.replaceAll('-', '').slice(0, 12),
      name: fixture.name + ' reassignment',
      assignmentStrategy: 'RoundRobin',
    },
    201,
  );
  const profile = await fixture.api<{ value: { userId: string } }>('v1/auth/me');
  const participants = await fixture.api<{ items: { id: string; userId: string }[] }>(
    'workflow/participants?pageSize=200',
  );
  const participant = participants.items.find((row) => row.userId === profile.value.userId)!;
  expect(participant).toBeDefined();
  await fixture.api(
    `workflow/assignment-groups/${target.id}/members`,
    'POST',
    { participantId: participant.id, canClaim: true },
    201,
  );
  await app.open('/org/workflow/tasks/' + fixture.task.id);
  await screen.getByRole('button', 'Claim', { exact: true }).click();
  const before = await fixture.get();
  const approved = screen.getByRole('textbox', 'Approved', { exact: false, disabled: false });
  await approved.fill('obsolete-before-reassign');
  await screen.getByRole('tab', 'Request details', { exact: true }).click();
  await expect(
    screen.getByRole('textbox', 'Approved', { exact: false, disabled: true }),
  ).toHaveValue(String(before.formValues?.approved ?? ''));
  await expect(screen.getByRole('textbox', 'Notes', { exact: false, disabled: true })).toHaveValue(
    '',
  );
  await screen.getByLabel('Target group').selectOption({ value: target.id });
  const [reassigned] = await Promise.all([
    browser.waitForResponse('**/api/workflow/work-items/' + fixture.task.id + '/reassign'),
    screen.getByRole('button', 'Reassign', { exact: true }).click(),
  ]);
  expect(reassigned.status).toBe(200);
  const sparse = await reassigned.json<Partial<Item>>();
  expect(sparse.status).toBe('Pending');
  expect(sparse.selectedForm ?? null).toBeNull();
  expect(sparse.formDraftKey ?? null).toBeNull();
  const [claimed] = await Promise.all([
    browser.waitForResponse('**/api/workflow/work-items/' + fixture.task.id + '/claim'),
    screen.getByRole('button', 'Claim', { exact: true }).click(),
  ]);
  expect(claimed.status).toBe(200);
  const enriched = await claimed.json<Item>();
  expect(enriched.selectedForm).toBeTruthy();
  expect(enriched.formVisitId).toBe(before.formVisitId);
  expect(enriched.formDraftKey).toBeTruthy();
  expect(enriched.formDraftKey).not.toBe(before.formDraftKey);
  await expect(
    screen.getByRole('textbox', 'Approved', { exact: false, disabled: true }),
  ).toHaveValue(String(enriched.formValues?.approved ?? ''));
  await screen.getByRole('tab', 'Action', { exact: true }).click();
  await expect(approved).toHaveValue('initial-prefill');
  await screen
    .getByRole('textbox', 'Routing choice', { exact: false, disabled: false })
    .fill('END');
  await screen.getByRole('button', 'Accept', { exact: true }).click();
  let payload: { formValues?: Record<string, unknown> } | undefined;
  await browser.route(
    '**/api/workflow/work-items/' + fixture.task.id + '/complete',
    async (route) => {
      payload = JSON.parse(route.request.postData!);
      await route.continue();
    },
  );
  const [completed] = await Promise.all([
    browser.waitForResponse('**/api/workflow/work-items/' + fixture.task.id + '/complete'),
    screen.getByRole('button', 'Submit and continue', { exact: true }).click(),
  ]);
  expect(completed.status).toBe(200);
  expect(payload?.formValues).toMatchObject({ approved: 'initial-prefill', notes: null });
  expect(JSON.stringify(payload)).not.toContain('obsolete-before-reassign');
  await expect
    .poll(async () => (await fixture.get()).formHistory[0]?.answers)
    .toMatchObject({ approved: 'initial-prefill', notes: null });
});

for (const boundary of ['release', 'reassign']) {
  test(`pending workspace edit cannot return after ${boundary} elsewhere while its view is absent`, async ({
    app,
    browser,
    screen,
  }) => {
    const fixture = await mappedTask(browser);
    await app.open('/admin/workflow/instances/' + fixture.rootId);
    const approved = screen.getByRole('textbox', 'Approved', { exact: false, disabled: false });
    await expect(approved).toHaveValue('initial-prefill');
    const pending = await fixture.get();
    expect(pending.status).toBe('Pending');
    await approved.fill('absent-view-obsolete');
    await expect
      .poll(() => drafts(browser))
      .toEqual([expect.objectContaining({ approved: 'absent-view-obsolete' })]);
    await screen.getByRole('link', 'Workflows', { exact: true }).click();
    await expect(approved).toHaveCount(0);
    // Same authenticated actor through another-tab-equivalent API command, with the editing view absent.
    const claimed = await fixture.api<Item>(`workflow/work-items/${pending.id}/claim`, 'POST', {});
    expect(claimed.formVisitId).toBe(pending.formVisitId);
    expect(claimed.formDraftKey).toBe(pending.formDraftKey);
    if (boundary === 'release')
      await fixture.api(`workflow/work-items/${pending.id}/release`, 'POST', {}, 204);
    else
      await fixture.api(`workflow/work-items/${pending.id}/reassign`, 'POST', {
        newAssignmentGroupId: fixture.group.id,
      });
    const rotated = await fixture.get();
    expect(rotated.formVisitId).toBe(pending.formVisitId);
    expect(rotated.formDraftKey).toBeTruthy();
    expect(rotated.formDraftKey).not.toBe(pending.formDraftKey);
    await screen.getByRole('link', 'Instances', { exact: true }).click();
    await screen.getByLabel('Search instances').fill(fixture.name);
    await screen.getByRole('button', 'Search', { exact: true }).click();
    await screen.getByRole('link', fixture.name + ' designer foundation', { exact: true }).click();
    await expect(approved).toHaveValue('initial-prefill');
    await expect(
      screen.getByRole('textbox', 'Notes', { exact: false, disabled: true }),
    ).toHaveValue('');
    await browser.reload();
    await expect(approved).toHaveValue('initial-prefill');
    expect(JSON.stringify(await drafts(browser))).not.toContain('absent-view-obsolete');
  });
}
