import type { Browser } from '@e2e-dev/web';
import { setTimeout as delay } from 'node:timers/promises';

interface LoginResponse {
  status: number;
  retryAfter?: string;
  code?: string;
}

/** One admission retry, only when the server specifies its fixed-window cooldown.
 * Authentication failures and all scenario/security requests are left untouched.
 */
export async function submitLoginWithRetryAfter(
  submit: () => Promise<LoginResponse>,
  wait: (milliseconds: number) => Promise<void> = delay,
): Promise<LoginResponse> {
  const response = await submit();
  if (response.status !== 429) return response;
  const seconds = Number(response.retryAfter);
  if (
    response.code !== 'Tenant.RateLimit' ||
    !/^[1-9]\d*$/.test(response.retryAfter ?? '') ||
    !Number.isInteger(seconds) ||
    seconds > 60
  ) {
    throw new Error('Login HTTP 429 lacks the expected bounded Tenant.RateLimit Retry-After.');
  }
  console.info(
    `Login admission HTTP 429; Retry-After: ${response.retryAfter}; one directed retry.`,
  );
  await wait(seconds * 1000);
  const retried = await submit();
  if (retried.status === 429)
    throw new Error('Login admission still HTTP 429 after its single server-directed retry.');
  return retried;
}

/** Both submissions remain real DOM clicks; successful token bodies are never read. */
export async function signIn(browser: Browser, click: () => Promise<unknown>): Promise<void> {
  await submitLoginWithRetryAfter(async () => {
    const [response] = await Promise.all([
      browser.waitForResponse('**/api/v1/auth/login'),
      click(),
    ]);
    return {
      status: response.status,
      retryAfter: response.headers['retry-after'],
      code: response.status === 429 ? (await response.json<{ code?: string }>()).code : undefined,
    };
  });
}
