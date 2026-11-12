import { test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { randomUUID } from 'node:crypto';
import { apiFor } from './support/activity-forms.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('routing');

test('opens routing directly, edits a Condition, and explains ALL and ANY simulation', async ({
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
  const catalog = await api<{ workspaceJson: string }[]>('workflow/workspace/catalog');
  const settings = { ...JSON.parse(catalog[0].workspaceJson), kind: 'Main', designerVersion: 3 };
  const created = await api<{ definitionId: string; versionId: string }>(
    'workflow/workspace/definitions',
    'POST',
    { name: 'Routing demonstration ' + randomUUID(), settings },
  );
  const path = `workflow/definitions/${created.definitionId}/versions/${created.versionId}`;
  await app.open('/admin/' + path + '/designer');
  await expect(screen.getByRole('button', 'Upgrade routing', { exact: true })).toHaveCount(0);
  await expect(screen.getByRole('button', 'Simulate routing', { exact: true })).toBeVisible();
  const upgraded = await api<{ revision: string; xmlContent: string; workspaceJson: string }>(path);
  expect(JSON.parse(upgraded.workspaceJson).designerVersion).toBe(4);
  const esc = (v: unknown) =>
    String(typeof v === 'string' ? v : JSON.stringify(v))
      .replaceAll('&', '&amp;')
      .replaceAll('"', '&quot;')
      .replaceAll('<', '&lt;');
  const rule = (edge: string) => ({
    contractVersion: 1,
    matchMode: 'Unique',
    routes: [
      { transitionKey: edge, always: true, when: { match: 'All', conditions: [], groups: [] } },
    ],
    sources: [],
    waitFor: 'All',
    remainingBranches: 'Continue',
  });
  const nodes = [
    ['start', 'Start', 'Start', {}, 60, 60],
    [
      'fork',
      'ParallelGateway',
      'Start three branches',
      { routing: { ...rule('unused'), routes: [], matchMode: 'All', joinNodeKey: 'join' } },
      300,
      60,
    ],
    ['registration', 'UserTask', 'Registration', {}, 60, 260],
    ['location', 'UserTask', 'Go to Location', {}, 330, 260],
    ['event16', 'WaitEvent', 'Event 16', {}, 600, 260],
    [
      'registrationRule',
      'ExclusiveGateway',
      'Registration route',
      { routing: rule('registrationMet') },
      60,
      440,
    ],
    [
      'locationRule',
      'ExclusiveGateway',
      'Location route',
      { routing: rule('locationMet') },
      330,
      440,
    ],
    ['eventRule', 'ExclusiveGateway', 'Event route', { routing: rule('eventMet') }, 600, 440],
    [
      'join',
      'JoinGateway',
      'Wait for all results',
      { routing: { ...rule('unused'), routes: [] } },
      330,
      640,
    ],
    ['end', 'End', 'End', {}, 330, 800],
  ];
  const edges = [
    ['entry', 'start', 'fork'],
    ['branch1', 'fork', 'registration'],
    ['branch2', 'fork', 'location'],
    ['branch3', 'fork', 'event16'],
    ['check1', 'registration', 'registrationRule'],
    ['check2', 'location', 'locationRule'],
    ['check3', 'event16', 'eventRule'],
    ['registrationMet', 'registrationRule', 'join'],
    ['locationMet', 'locationRule', 'join'],
    ['eventMet', 'eventRule', 'join'],
    ['finish', 'join', 'end'],
  ];
  const xml = `<Workflow xmlns="https://privora.io/workflow/v1" workspaceJson="${esc(upgraded.workspaceJson)}"><Activities>${nodes.map(([key, type, name, configuration, x, y]) => `<Activity nodeKey="${key}" type="${type}" name="${name}" configurationJson="${esc(configuration)}" positionX="${x}" positionY="${y}"/>`).join('')}</Activities><Transitions>${edges.map(([key, from, to]) => `<Transition key="${key}" from="${from}" to="${to}"/>`).join('')}</Transitions></Workflow>`;
  await api(path + '/xml', 'PUT', {
    xmlContent: xml,
    designerJson: '{}',
    expectedRevision: upgraded.revision,
  });
  await browser.reload();
  await screen.getByRole('button', 'Select Registration route', { exact: true }).press('Enter');
  await screen.getByText('Advanced · matching policy', { exact: true }).click();
  await expect(screen.getByLabel('Matching policy', { exact: true })).toHaveValue('Unique');
  await expect(screen.getByLabel('Run when', { exact: true })).toContainText('Always (explicit)');
  await screen.getByLabel('Matching policy', { exact: true }).selectOption({ value: 'First' });
  // The inspector makes the canvas/topbar aria-hidden until configuration closes.
  await screen.getByRole('button', 'Close configuration', { exact: true }).click();
  await expect(screen.getByRole('status', 'Save status', { exact: true })).toContainText('Saved');
  const latest = await api<{
    revision: string;
    activities: { nodeKey: string; configurationJson: string }[];
  }>(path);
  expect(
    JSON.parse(latest.activities.find((n) => n.nodeKey === 'registrationRule')!.configurationJson)
      .routing.matchMode,
  ).toBe('First');
  await api(path + '/xml', 'PUT', { xmlContent: xml, expectedRevision: upgraded.revision }, 409);
  await screen.getByRole('button', 'Simulate routing', { exact: true }).click();
  await screen.getByRole('button', '2. Run simulation', { exact: true }).click();
  await expect(screen.getByText(/0 of 3 requirements met/)).toBeVisible();
  const complete = async (name: string) => {
    const option = screen
      .getByLabel('Complete a waiting element', { exact: true })
      .getByRole('option', new RegExp('^' + name + ' ·'));
    // A run is a server round trip: wait for its waiting options before reading one.
    await expect(option).toHaveCount(1);
    const value = await option.getAttribute('value');
    await screen
      .getByLabel('Complete a waiting element', { exact: true })
      .selectOption({ value: value! });
    await screen.getByRole('button', 'Submit mock result', { exact: true }).click();
    await expect(
      screen.getByRole('button', new RegExp('\\d+\\. ' + name + ' · Completed')),
    ).toBeVisible();
  };
  await complete('Go to Location');
  await complete('Registration');
  await expect(screen.getByText(/2 of 3 requirements met/)).toBeVisible();
  await complete('Event 16');
  await expect(
    screen.getByText('Requirements met; continuation released once.', { exact: true }),
  ).toHaveCount(1);
  await screen.getByRole('button', 'Select Wait for all results', { exact: true }).press('Enter');
  await screen.getByLabel('Wait for', { exact: true }).selectOption({ value: 'Any' });
  await screen.getByText('Advanced', { exact: true }).click();
  await screen
    .getByLabel('After the first qualifying result', { exact: true })
    .selectOption({ value: 'Cancel' });
  await screen.getByRole('button', 'Close configuration', { exact: true }).click();
  await expect(screen.getByRole('status', 'Save status', { exact: true })).toContainText('Saved');
  await screen.getByRole('button', 'Simulate routing', { exact: true }).click();
  await screen.getByRole('button', '2. Run simulation', { exact: true }).click();
  await complete('Registration');
  await expect(
    screen.getByText('Requirements met; continuation released once.', { exact: true }),
  ).toHaveCount(1);
  await expect(
    screen.getByLabel('Complete a waiting element', { exact: true }).getByRole('option'),
  ).toHaveCount(1);
  await screen.getByRole('button', 'Toggle theme', { exact: true }).click();
  await screen.getByRole('button', 'Language', { exact: true }).click();
  await expect(screen.getByLabel('انتظار', { exact: true })).toHaveValue('Any');
  expect(await browser.evaluate(() => document.documentElement.dir)).toBe('rtl');
  await browser.setViewport({ width: 390, height: 844 });
  await browser.reload();
  await screen.getByRole('button', 'اختيار Wait for all results', { exact: true }).press('Enter');
  await expect(screen.getByLabel('انتظار', { exact: true })).toHaveValue('Any');
  expect(
    await browser.evaluate(() => {
      const panel = document.querySelector('.inspector-panel')!.getBoundingClientRect();
      return panel.left >= 0 && panel.right <= window.innerWidth + 1;
    }),
  ).toBe(true);
});
