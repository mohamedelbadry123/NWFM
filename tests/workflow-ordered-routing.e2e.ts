import { beforeEach, test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { createDesignerFoundation, type DesignerVersion } from './support/designer-foundation.js';
import { pointerDrag } from './support/pointer.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('ordered-routing');

interface Simulation {
  trace: {
    nodeKey: string;
    status: string;
    ruleOrder?: number;
    rule?: { transitionKey: string; status: string; problem?: string | null };
  }[];
  diagnostics: { code: string; message: string }[];
}

beforeEach(async ({ app, screen }) => {
  const admin = accounts.next();
  await app.open('/login');
  await screen.getByLabel('Username').fill(admin.username);
  await screen.getByLabel('Password').fill(admin.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'Workflows', { exact: true })).toBeVisible();
});

// Drop onto the route's handle: a large card's midpoint selects a different CDK position.
// Keep transition keys stable while moving the card through the real pointer interaction.
// Persisted configuration and canvas badges must agree with the resulting order.
test('ordered routes persist keyboard and drag order and update canvas badges', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createDesignerFoundation(browser, true);
  await app.open('/admin/' + fixture.path + '/designer');
  await screen.getByRole('button', 'Select Ordered choice', { exact: true }).press('Enter');
  const editor = browser.locator('app-workflow-routing-editor');
  const order = async () =>
    (await fixture.api<DesignerVersion>(fixture.path)).activities.find(
      (node) => node.nodeKey === 'condition',
    )!.configurationJson;
  await expect(editor.getByRole('button', 'Move route 1 up')).toBeDisabled();
  await editor.getByRole('button', 'Move route 1 down').press('Enter');
  await expect(editor.getByRole('button', 'Move route 2 down')).toBeFocused();
  await expect
    .poll(async () =>
      JSON.parse(await order()).routing.routes.map(
        (r: { transitionKey: string }) => r.transitionKey,
      ),
    )
    .toEqual(['route2', 'route1', 'route3']);
  await expect(browser.locator('[data-edge-order="1"]')).toHaveCount(1);
  await expect(browser.locator('[data-edge-order="2"]')).toHaveCount(1);
  await expect(browser.locator('[data-edge-order="3"]')).toHaveCount(1);
  await editor.getByRole('button', 'Move route 2 up').press('Enter');
  await expect(editor.getByRole('button', 'Move route 1 down')).toBeFocused();
  // Collapse the visible editors so source and destination handles share the viewport.
  for (const key of ['route1', 'route2', 'route3']) {
    for (let detail = 0; detail < 2; detail++) {
      await browser
        .locator(`app-workflow-routing-editor [data-route-key="${key}"] details[open] > summary`)
        .first()
        .click();
    }
  }
  // Real pointer drag through the browser mouse, not a synthetic CDK event. CDK starts a drag on the first
  // over-threshold move and sorts on the following ones, so the pointer must travel in several moves.
  await pointerDrag(
    browser,
    'app-workflow-routing-editor [data-route-key="route3"] [data-route-handle]',
    'app-workflow-routing-editor [data-route-key="route1"] [data-route-handle]',
  );
  await expect
    .poll(async () => JSON.parse(await order()).routing.routes[0].transitionKey)
    .toBe('route3');
  const persisted = JSON.parse(await order()).routing;
  expect(persisted.contractVersion).toBe(2);
  expect(persisted.matchMode).toBe('First');
  expect(persisted.routes.map((r: { transitionKey: string }) => r.transitionKey).sort()).toEqual([
    'route1',
    'route2',
    'route3',
  ]);
  await browser.reload();
  await screen.getByRole('button', 'Select Ordered choice', { exact: true }).click();
  await expect(
    browser.locator('app-workflow-routing-editor [data-route-key]').first(),
  ).toHaveAttribute('data-route-key', 'route3');
  await expect(browser.locator('[data-edge-order="1"]')).toBeVisible();
  const result = await fixture.api<Simulation>(fixture.path + '/routing/simulate', 'POST', {
    inputs: {},
    signals: [{ nodeKey: 'a', outcome: 'APPROVE', values: { route: 'MATCH', approved: 'yes' } }],
  });
  const rules = result.trace.filter((trace) => trace.nodeKey === 'condition' && trace.rule);
  expect(rules[0].rule).toMatchObject({ transitionKey: 'route3', status: 'Matched' });
  expect(rules.slice(1).map((trace) => trace.rule!.status)).toEqual(['Skipped', 'Skipped']);
});

test('numeric mock input reaches the real simulation API as a number', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createDesignerFoundation(browser, true);
  await app.open('/admin/' + fixture.path + '/designer');
  await screen.getByRole('button', 'Simulate routing', { exact: true }).click();
  const simulation = browser.locator('app-workflow-routing-simulation');
  await simulation.getByRole('button', '2. Run simulation').click();
  const select = simulation.getByLabel('Complete a waiting element');
  await expect(select.getByRole('option', /^Origin Step/)).toHaveCount(1);
  await select.selectOption({
    value: (await select.getByRole('option', /^Origin Step/).getAttribute('value'))!,
  });
  const context = await fixture.api<{ sources: { key: string; label: string }[] }>(
    fixture.path + '/routing/context',
  );
  await simulation
    .getByLabel(context.sources.find((s) => s.key === 'a.amount')!.label)
    .fill('1500');
  let submitted: unknown;
  await browser.route('**/routing/simulate', async (route) => {
    submitted = JSON.parse(route.request.postData ?? '{}');
    await route.continue();
  });
  const [response] = await Promise.all([
    browser.waitForResponse('**/routing/simulate'),
    simulation.getByRole('button', 'Submit mock result').click(),
  ]);
  expect(response.status).toBe(200);
  // Known UI follow-up #4: preserve this assertion as a failing repro until the UI lane fixes coercion.
  expect(
    (submitted as { signals: { values: { amount: unknown } }[] }).signals[0].values.amount,
  ).toBe(1500);
});

for (const scenario of [
  {
    title: 'routes 1 and 3 match: route 1 wins',
    route: 'MATCH',
    approved: 'yes',
    statuses: ['Matched', 'Skipped', 'Skipped'],
  },
  {
    title: 'only route 3 matches: all three are evaluated in order',
    route: 'OTHER',
    approved: 'yes',
    statuses: ['Checked', 'Checked', 'Matched'],
  },
  {
    title: 'early match short-circuits a later missing value',
    route: 'MATCH',
    statuses: ['Matched', 'Skipped', 'Skipped'],
  },
]) {
  test('actual simulation: ' + scenario.title, async ({ app, browser, screen }) => {
    const fixture = await createDesignerFoundation(browser, true);
    await app.open('/admin/' + fixture.path + '/designer');
    await screen.getByRole('button', 'Simulate routing', { exact: true }).click();
    const simulation = browser.locator('app-workflow-routing-simulation');
    await simulation.getByRole('button', '2. Run simulation').click();
    const waiting = simulation.getByLabel('Complete a waiting element');
    await expect(waiting.getByRole('option', /^Origin Step/)).toHaveCount(1);
    const option = await waiting.getByRole('option', /^Origin Step/).getAttribute('value');
    await waiting.selectOption({ value: option! });
    const context = await fixture.api<{ sources: { key: string; label: string }[] }>(
      fixture.path + '/routing/context',
    );
    const label = (key: string) => context.sources.find((source) => source.key === key)!.label;
    await simulation.getByLabel(label('a.route')).fill(scenario.route);
    if (scenario.approved) await simulation.getByLabel(label('a.approved')).fill(scenario.approved);
    const [response] = await Promise.all([
      browser.waitForResponse('**/routing/simulate'),
      simulation.getByRole('button', 'Submit mock result').click(),
    ]);
    expect(response.status).toBe(200);
    const result = (await response.json()) as Simulation;
    const rules = result.trace.filter((trace) => trace.nodeKey === 'condition' && trace.rule);
    expect(rules.map((trace) => trace.ruleOrder)).toEqual([1, 2, 3]);
    expect(rules.map((trace) => trace.rule!.transitionKey)).toEqual(['route1', 'route2', 'route3']);
    expect(rules.map((trace) => trace.rule!.status)).toEqual(scenario.statuses);
    expect(rules.every((trace) => !trace.rule!.problem)).toBe(true);
    expect(result.diagnostics).toEqual([]);
    // This intentionally exercises the current UI consumer of real structured backend traces.
    const rendered = simulation.getByRole('list', 'Routes in the order they were tried');
    await expect(rendered.getByRole('listitem')).toHaveCount(3);
    for (let index = 0; index < 3; index++) {
      await expect(rendered.getByRole('listitem').nth(index)).toHaveAttribute(
        'data-rule-status',
        scenario.statuses[index].toLowerCase(),
      );
    }
  });
}

test('visited missing source remains an attention error with its rule identity', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createDesignerFoundation(browser, true);
  await app.open('/admin/' + fixture.path + '/designer');
  await screen.getByRole('button', 'Simulate routing', { exact: true }).click();
  const simulation = browser.locator('app-workflow-routing-simulation');
  await simulation.getByRole('button', '2. Run simulation').click();
  const select = simulation.getByLabel('Complete a waiting element');
  await expect(select.getByRole('option', /^Origin Step/)).toHaveCount(1);
  await select.selectOption({
    value: (await select.getByRole('option', /^Origin Step/).getAttribute('value'))!,
  });
  const context = await fixture.api<{ sources: { key: string; label: string }[] }>(
    fixture.path + '/routing/context',
  );
  await simulation
    .getByLabel(context.sources.find((s) => s.key === 'a.route')!.label)
    .fill('OTHER');
  const [response] = await Promise.all([
    browser.waitForResponse('**/routing/simulate'),
    simulation.getByRole('button', 'Submit mock result').click(),
  ]);
  const result = (await response.json()) as Simulation;
  const last = result.trace.find((trace) => trace.rule?.transitionKey === 'route3');
  expect(last?.ruleOrder).toBe(3);
  expect(last?.rule?.status).toBe('Checked');
  expect(last?.rule?.problem).toBeTruthy();
  await expect(
    browser.locator('app-workflow-routing-simulation [data-rule-status="error"]'),
  ).toContainText('Route 3');
  await expect(
    browser
      .locator('app-workflow-routing-simulation [data-rule-status="error"]')
      .getByRole('alert'),
  ).toBeVisible();
});
