import { test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { randomUUID } from 'node:crypto';
import { apiFor, type Task } from './support/activity-forms.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('unrc');

interface UnrcDetail {
  tree: {
    id: string;
    status: string;
    activities: { nodeKey: string; status: string; tasks: { task: Task }[] }[];
  }[];
  operations: {
    id: string;
    name: string;
    status: string;
    attempts: number;
    required: boolean;
    responseJson: string;
  }[];
}

// Each independently seeded scenario keeps the existing per-test timeout and all assertions.
for (const scenario of ['FAIL-', 'COMPLETE-']) {
  test(`UNRC gates contractor release, retries safely, and routes between task types (${scenario})`, async ({
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
    const api = apiFor(browser);
    const catalog = await api<{ id: string; definitionKey: string; workspaceJson: string }[]>(
      'workflow/workspace/catalog',
    );
    const definition = catalog.find((d) => d.definitionKey === 'UNRC-LOCAL');
    expect(definition).toBeDefined();
    const scope = JSON.parse(definition!.workspaceJson).organizationScopes[0];

    const reference = scenario + randomUUID();
    const started = await api<{ instanceId: string }>(
      `workflow/workspace/definitions/${definition!.id}/instances`,
      'POST',
      {
        requestId: randomUUID(),
        reference,
        location: { clusterCode: scope.clusterCode, cbuCode: scope.cbuCode },
      },
    );
    const detail = () => api<UnrcDetail>('workflow/workspace/instances/' + started.instanceId);
    const active = (d: UnrcDetail, key: string) =>
      d.tree
        .flatMap((r) => r.activities)
        .filter((a) => a.nodeKey === key)
        .flatMap((a) => a.tasks.map((t) => t.task))
        .filter((t) => ['Pending', 'Claimed'].includes(t.status));
    const submit = async (key: string, values: Record<string, unknown>) => {
      try {
        await expect.poll(async () => active(await detail(), key).length).toBe(1);
      } catch (error) {
        const state = await detail();
        throw new Error(
          `Expected active UNRC step ${key}: ${JSON.stringify({
            runs: state.tree.map((run) => ({
              status: run.status,
              nodes: run.activities.map((a) => ({ key: a.nodeKey, status: a.status })),
            })),
            operations: state.operations,
          })}`,
          { cause: error },
        );
      }
      const task = active(await detail(), key)[0];
      await api(
        `workflow/workspace/tasks/${task.id}/actions`,
        'POST',
        {
          requestId: randomUUID(),
          action: 'APPROVE',
          formVisitId: task.formVisitId,
          formValues: values,
        },
        204,
      );
      return task;
    };
    await submit('registration', {
      request_reference: reference,
      customer_name: 'Local example customer',
    });
    // Async notification delivery does not prevent the location step from opening.
    expect(active(await detail(), 'location')).toHaveLength(1);
    await submit('location', { site_address: 'Local demonstration site' });
    await submit('feedback', { release_ready: 'no', feedback: 'Site not ready' });
    expect(
      (await detail()).operations.filter((o) => o.name === 'Release with contractor'),
    ).toHaveLength(0);
    await submit('feedback', { release_ready: 'yes', feedback: 'Site ready for installation' });
    if (scenario === 'FAIL-') {
      await expect
        .poll(
          async () =>
            (await detail()).operations.find((o) => o.name === 'Release with contractor')?.status,
          { timeout: 30000 },
        )
        .toBe('Failed');
      const blocked = await detail();
      const release = blocked.operations.find((o) => o.name === 'Release with contractor')!;
      expect(release.attempts).toBe(3);
      expect(active(blocked, 'contractor')).toHaveLength(0);
      expect(blocked.tree.every((r) => r.status !== 'Completed')).toBe(true);
      const recovery = await fetch('http://127.0.0.1:5091/unrc/recover', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reference }),
      });
      expect(recovery.ok).toBe(true);
      await api(`workflow/integrations/operations/${release.id}/replay`, 'POST', {}, 204);
    }
    await expect
      .poll(async () => active(await detail(), 'contractor').length, { timeout: 30000 })
      .toBe(1);
    const delivered = (await detail()).operations.find(
      (o) => o.name === 'Release with contractor',
    )!;
    expect(delivered.status).toBe('Completed');
    expect(delivered.required).toBe(true);
    if (scenario === 'COMPLETE-') expect(delivered.attempts).toBe(2);
    expect(JSON.parse(JSON.parse(delivered.responseJson).body).id).toBeTruthy();
    await api(`workflow/integrations/operations/${delivered.id}/replay`, 'POST', {}, 400);
    expect(active(await detail(), 'contractor')).toHaveLength(1);
    await submit('contractor', { meter_serial: 'METER-' + scenario });
    await submit('consultant', { consultant_notes: 'Installation inspected' });
    for (const key of ['approve1', 'approve2', 'approve3'])
      await submit(key, { approval_notes: key + ' accepted' });
    if (scenario === 'FAIL-') {
      await submit('activation-step', { activation_reference: 'ACTIVATED' });
    }
    await expect
      .poll(async () => (await detail()).tree[0].status, { timeout: 30000 })
      .toBe('Completed');
    const result = await detail();
    await expect
      .poll(
        async () =>
          (await detail()).operations.find((o) => o.name === 'Registration notification')?.status,
        { timeout: 30000 },
      )
      .toBe('Completed');
    const notification = (await detail()).operations.find(
      (o) => o.name === 'Registration notification',
    )!;
    expect(notification.required).toBe(false);
    expect(notification.attempts).toBe(2);
    expect(result.tree).toHaveLength(scenario === 'FAIL-' ? 3 : 2);
    expect(result.tree.every((r) => r.status === 'Completed')).toBe(true);
    expect(result.operations.find((o) => o.name === 'Close in other system')?.status).toBe(
      'Completed',
    );
    expect(result.operations.find((o) => o.name === 'Determine next Task Type')?.status).toBe(
      'Completed',
    );
    expect(result.tree[0].activities.every((a) => a.tasks.length === 0)).toBe(true);
    await app.open('/admin/workflow/instances/' + started.instanceId);
    await expect(browser.locator('app-workflow-instance-workspace header > div > p')).toContainText(
      'Completed',
    );
    await browser.reload();
    await expect(browser.locator('app-workflow-instance-workspace header > div > p')).toContainText(
      'Completed',
    );
  });
}
