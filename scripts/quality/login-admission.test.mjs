import test from 'node:test';
import assert from 'node:assert/strict';
import { submitLoginWithRetryAfter } from '../../tests/support/login.ts';

test('successful sign-in submits once without a cooldown', async () => {
  let calls = 0;
  const response = await submitLoginWithRetryAfter(
    async () => {
      calls++;
      return { status: 200 };
    },
    async () => assert.fail('No wait for success'),
  );
  assert.equal(response.status, 200);
  assert.equal(calls, 1);
});

test('a real admission response controls the one retry deadline', async () => {
  const events = [];
  let calls = 0;
  const response = await submitLoginWithRetryAfter(
    async () => {
      events.push('submit');
      return ++calls === 1
        ? { status: 429, code: 'Tenant.RateLimit', retryAfter: '17' }
        : { status: 200 };
    },
    async (milliseconds) => {
      events.push(milliseconds);
    },
  );
  assert.equal(response.status, 200);
  assert.deepEqual(events, ['submit', 17000, 'submit']);
});

test('authentication and server errors are never retried or delayed', async () => {
  for (const status of [400, 401, 403, 500]) {
    let calls = 0;
    const response = await submitLoginWithRetryAfter(
      async () => {
        calls++;
        return { status };
      },
      async () => assert.fail('No wait for non-admission failure'),
    );
    assert.equal(response.status, status);
    assert.equal(calls, 1);
  }
});

test('unexpected 429s and invalid or excessive cooldowns fail without replay', async () => {
  for (const response of [
    { status: 429, code: 'Other', retryAfter: '1' },
    ...[undefined, '', '0', '-1', '1.5', '61', 'invalid'].map((retryAfter) => ({
      status: 429,
      code: 'Tenant.RateLimit',
      retryAfter,
    })),
  ]) {
    let calls = 0;
    await assert.rejects(
      submitLoginWithRetryAfter(
        async () => {
          calls++;
          return response;
        },
        async () => assert.fail('Invalid cooldown must not wait'),
      ),
      /expected bounded/,
    );
    assert.equal(calls, 1);
  }
});

test('a second rate limit fails rather than creating a retry loop', async () => {
  let calls = 0;
  let waits = 0;
  await assert.rejects(
    submitLoginWithRetryAfter(
      async () => {
        calls++;
        return { status: 429, code: 'Tenant.RateLimit', retryAfter: '1' };
      },
      async () => {
        waits++;
      },
    ),
    /single server-directed retry/,
  );
  assert.equal(calls, 2);
  assert.equal(waits, 1);
});
