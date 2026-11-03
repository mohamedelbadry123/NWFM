import { test } from '@e2e-dev/web';
import { credentials, expect } from 'e2e';
import {
  assertForeignResourcesDenied,
  assertOwnMutations,
  assertOwnResources,
  createTenantResources,
  tenantA,
  tenantApi,
  tenantB,
} from './support/tenant-security.js';

// Exact assertions deliberately need no agent calls, replay recording, or model credentials.
test(
  'tenant credentials, records, media and pooled requests stay isolated',
  {
    tags: ['tenant-security'],
  },
  async ({ app, browser, screen }) => {
    const admin = credentials.user('securityAdmin');
    const login = async (origin: string) => {
      await app.open(origin + '/login');
      await screen.getByLabel('Username').fill(admin.username);
      await screen.getByLabel('Password').fill(admin.password);
      await screen.getByRole('button', 'Sign in').click();
      await expect(screen.getByRole('link', 'New Instance', { exact: false })).toBeVisible();
    };
    await login(tenantA.origin);
    const api = tenantApi(browser);
    await api('v1/form-engine/forms', 401, { anonymous: true });
    // A token replayed on another workspace's host is refused before any handler runs.
    const replayed = await api<{ error: { code: string } }>('v1/form-engine/forms', 400, {
      origin: tenantB.origin,
    });
    expect(replayed.error.code).toBe('Tenant.Mismatch');
    const a = await createTenantResources(browser);
    await assertOwnResources(browser, a);

    for (const header of ['x-organization-id', 'x-tenant-id']) {
      await api('v1/form-engine/forms', 400, { headers: { [header]: tenantB.id } });
      await api('v1/form-engine/forms', 400, { headers: { [header]: 'invalid-tenant' } });
      await api('v1/form-engine/forms', 200, { headers: { [header]: tenantA.id } });
    }
    for (const selector of ['organizationId', 'tenantId']) {
      await api(`v1/form-engine/forms?${selector}=${tenantB.id}`, 400);
      await api(`v1/form-engine/forms?${selector}=invalid-tenant`, 400);
      await api(`v1/form-engine/forms?${selector}=${tenantA.id}`, 200);
    }
    await api('workflow/definitions', 400, {
      method: 'POST',
      body: { definitionKey: 'SEC-OVERRIDE', name: 'Intrusion', organizationId: tenantB.id },
    });
    await assertOwnResources(browser, a);

    await login(tenantB.origin);
    await api('v1/form-engine/forms', 400, { origin: tenantA.origin });
    const b = await createTenantResources(browser);
    await assertOwnResources(browser, b);
    await assertForeignResourcesDenied(browser, a);

    // Alternate real requests repeatedly: each API reuses its ordinary ADO.NET pool.
    for (let iteration = 0; iteration < 3; iteration++) {
      await app.open(tenantA.origin);
      await assertOwnResources(browser, a);
      await assertForeignResourcesDenied(browser, b);
      await app.open(tenantB.origin);
      await assertOwnResources(browser, b);
      await assertForeignResourcesDenied(browser, a);
    }
    await app.open(tenantA.origin);
    await assertOwnMutations(browser, a);
    await app.open(tenantB.origin);
    await assertOwnResources(browser, b);
    await assertOwnMutations(browser, b);
  },
);

test(
  'refresh rotation and logout revoke access without stale authorization',
  {
    tags: ['tenant-security'],
  },
  async ({ app, browser, screen }) => {
    const admin = credentials.user('securityAdmin');
    await app.open('/login');
    await screen.getByLabel('Username').fill(admin.username);
    await screen.getByLabel('Password').fill(admin.password);
    await screen.getByRole('button', 'Sign in').click();
    await expect(screen.getByRole('link', 'New Instance', { exact: false })).toBeVisible();
    await tenantApi(browser)('v1/auth/me');

    // Tokens never leave the browser or enter the test report. Return only HTTP status codes.
    const statuses = JSON.parse(
      String(
        await browser.evaluate(async () => {
          const oldAccess = localStorage.getItem('nwfm_access_token');
          const oldRefresh = localStorage.getItem('nwfm_refresh_token');
          const refresh = await fetch('/api/v1/auth/refresh', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ refreshToken: oldRefresh }),
          });
          const rotated = await refresh.json();
          const newAccess = rotated.value?.accessToken;
          const newRefresh = rotated.value?.refreshToken;
          const profile = (token: string | null) =>
            fetch('/api/v1/auth/me', { headers: { Authorization: 'Bearer ' + token } });
          const oldAccessStatus = (await profile(oldAccess)).status;
          const newAccessStatus = (await profile(newAccess)).status;
          const oldRefreshStatus = (
            await fetch('/api/v1/auth/refresh', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ refreshToken: oldRefresh }),
            })
          ).status;
          const logout = await fetch('/api/v1/auth/logout', {
            method: 'POST',
            headers: {
              Authorization: 'Bearer ' + newAccess,
              'Content-Type': 'application/json',
            },
            body: JSON.stringify({ refreshToken: newRefresh }),
          });
          const afterLogoutStatus = (await profile(newAccess)).status;
          const afterLogoutRefreshStatus = (
            await fetch('/api/v1/auth/refresh', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ refreshToken: newRefresh }),
            })
          ).status;
          return JSON.stringify({
            refresh: refresh.status,
            oldAccess: oldAccessStatus,
            newAccess: newAccessStatus,
            oldRefresh: oldRefreshStatus,
            logout: logout.status,
            afterLogout: afterLogoutStatus,
            afterLogoutRefresh: afterLogoutRefreshStatus,
          });
        }),
      ),
    );
    expect(statuses).toEqual({
      refresh: 200,
      oldAccess: 401,
      newAccess: 200,
      oldRefresh: 401,
      logout: 200,
      afterLogout: 401,
      afterLogoutRefresh: 401,
    });
  },
);

for (const change of ['roles', 'disabled'] as const) {
  test(
    `current ${change} immediately constrain an existing authenticated session`,
    {
      tags: ['tenant-security'],
    },
    async ({ app, browser, screen }) => {
      const login = async (account: 'securityAdmin' | 'securityReviewer' | 'securityDisabled') => {
        const user = credentials.user(account);
        await app.open('/login');
        await screen.getByLabel('Username').fill(user.username);
        await screen.getByLabel('Password').fill(user.password);
        await screen.getByRole('button', 'Sign in').click();
        await expect(screen.getByRole('link', 'New Instance', { exact: false })).toBeVisible();
      };
      await login(change === 'roles' ? 'securityReviewer' : 'securityDisabled');
      const api = tenantApi(browser);
      const subject = await api<{ value: { userId: string } }>('v1/auth/me');
      await api('v1/users');
      await browser.evaluate(() => {
        sessionStorage.setItem(
          'nwfm_security_subject',
          localStorage.getItem('nwfm_access_token') ?? '',
        );
        localStorage.clear();
        return true;
      });
      await login('securityAdmin');
      if (change === 'roles') {
        await api(`v1/users/${subject.value.userId}`, 200, {
          method: 'PUT',
          body: { roles: ['Monitor'], scopes: [{ departmentCodes: ['10'] }] },
        });
        await api('v1/users', 403, { tokenStorageKey: 'nwfm_security_subject' });
        const current = await api<{ value: { roles: string[] } }>('v1/auth/me', 200, {
          tokenStorageKey: 'nwfm_security_subject',
        });
        expect(current.value.roles).toEqual(['Monitor']);
      } else {
        await api(`v1/users/${subject.value.userId}/status`, 200, {
          method: 'PUT',
          body: { isEnabled: false },
        });
        await api('v1/auth/me', 401, { tokenStorageKey: 'nwfm_security_subject' });
        await api('v1/users', 401, { tokenStorageKey: 'nwfm_security_subject' });
      }
      // A subject's restriction does not invalidate a different administrator's session.
      await api('v1/auth/me');
      await api('v1/users');
    },
  );
}

test(
  'signed asynchronous work is bound to tenant and receiver with a bounded queue',
  {
    tags: ['tenant-security'],
  },
  async ({ app, browser, screen }) => {
    const admin = credentials.user('securityAdmin');
    await app.open(tenantB.origin + '/login');
    await screen.getByLabel('Username').fill(admin.username);
    await screen.getByLabel('Password').fill(admin.password);
    await screen.getByRole('button', 'Sign in').click();
    await expect(screen.getByRole('link', 'New Instance', { exact: false })).toBeVisible();
    const evidence = JSON.parse(
      String(
        await browser.evaluate(async () => {
          const secret = crypto.randomUUID() + crypto.randomUUID();
          const key = await crypto.subtle.importKey(
            'raw',
            new TextEncoder().encode(secret),
            { name: 'HMAC', hash: 'SHA-256' },
            false,
            ['sign'],
          );
          const hex = (buffer: ArrayBuffer) =>
            Array.from(new Uint8Array(buffer), (value) => value.toString(16).padStart(2, '0')).join(
              '',
            );
          const create = async () => {
            const response = await fetch('/api/workflow/integrations/connections', {
              method: 'POST',
              headers: {
                Authorization: 'Bearer ' + localStorage.getItem('nwfm_access_token'),
                'Content-Type': 'application/json',
              },
              body: JSON.stringify({
                name: 'Security webhook ' + crypto.randomUUID(),
                kind: 'Webhook',
                address: '',
                authentication: 'Hmac',
                credentials: { secret },
              }),
            });
            if (response.status !== 200) throw new Error('Fixture webhook creation failed.');
            return (await response.json()).id as string;
          };
          const connection = await create();
          const otherConnection = await create();
          const eventId = crypto.randomUUID();
          const envelope = (id: string) =>
            JSON.stringify({
              eventId: id,
              eventKey: 'tenant-security',
              correlationId: 'unmatched',
              payload: { evidence: 'local deterministic fixture' },
            });
          const send = async (
            body: string,
            options: {
              tenant?: string;
              signedConnection?: string;
              receiver?: string;
              age?: number;
              actualBody?: string;
            } = {},
          ) => {
            const timestamp = String(Math.floor(Date.now() / 1000) - (options.age ?? 0));
            const signedConnection = options.signedConnection ?? connection;
            const digest = hex(
              await crypto.subtle.digest('SHA-256', new TextEncoder().encode(body)),
            );
            const canonical = [
              'NWFM-WEBHOOK-V2',
              options.tenant ?? '20000000-0000-0000-0000-000000000002',
              signedConnection,
              'POST',
              '/api/workflow/integrations/webhooks/' + signedConnection,
              timestamp,
              digest,
            ].join('\n');
            const signature = hex(
              await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(canonical)),
            );
            const response = await fetch(
              '/api/workflow/integrations/webhooks/' + (options.receiver ?? connection),
              {
                method: 'POST',
                headers: {
                  'Content-Type': 'application/json',
                  'X-Workflow-Timestamp': timestamp,
                  'X-Workflow-Signature': signature,
                },
                body: options.actualBody ?? body,
              },
            );
            const result = await response.json();
            return { status: response.status, receiptId: result.receiptId, code: result.code };
          };
          const body = envelope(eventId);
          const denied = [
            await send(body, { tenant: '10000000-0000-0000-0000-000000000001' }),
            await send(body, { receiver: otherConnection }),
            await send(body, { age: 600 }),
            await send(body, { actualBody: envelope(crypto.randomUUID()) }),
          ];
          const accepted = await send(body);
          const duplicate = await send(body);
          const concurrent = await Promise.all(
            Array.from({ length: 12 }, () => send(envelope(crypto.randomUUID()))),
          );
          const eventsResponse = await fetch('/api/workflow/integrations/events', {
            headers: { Authorization: 'Bearer ' + localStorage.getItem('nwfm_access_token') },
          });
          const events = (await eventsResponse.json()) as { eventKey: string; status: string }[];
          return JSON.stringify({
            denied: denied.map((result) => result.status),
            accepted: accepted.status,
            duplicate: duplicate.status,
            sameReceipt: accepted.receiptId === duplicate.receiptId,
            admitted: concurrent.filter((result) => result.status === 202).length,
            exhausted: concurrent.filter(
              (result) => result.status === 429 && result.code === 'Tenant.QueueCapacity',
            ).length,
            pending: events.filter(
              (event) => event.eventKey === 'tenant-security' && event.status === 'Received',
            ).length,
          });
        }),
      ),
    );
    expect(evidence).toEqual({
      denied: [401, 401, 401, 401],
      accepted: 202,
      duplicate: 202,
      sameReceipt: true,
      admitted: 7,
      exhausted: 5,
      pending: 8,
    });
    await app.open(tenantA.origin + '/login');
    await screen.getByLabel('Username').fill(admin.username);
    await screen.getByLabel('Password').fill(admin.password);
    await screen.getByRole('button', 'Sign in').click();
    await expect(screen.getByRole('link', 'New Instance', { exact: false })).toBeVisible();
    const events = await tenantApi(browser)<{ eventKey: string }[]>('workflow/integrations/events');
    expect(events.filter((event) => event.eventKey === 'tenant-security')).toEqual([]);
    await createTenantResources(browser);
  },
);
