import type { E2EConfig } from 'e2e';
import { web } from '@e2e-dev/web';
import { chatgpt } from 'e2e/oauth/chatgpt';
import { replayOnly } from './scripts/e2e/replay-only.js';

const replay =
  process.env.NWFM_E2E_REPLAY === '1' ||
  (!!process.env.CI && !['0', 'false'].includes(process.env.CI));
export default {
  projectId: 'nwfm',
  tests: 'tests/**/*.e2e.ts',
  workers: 1,
  retries: 0,
  timeout: 600_000,
  assertionTimeout: 30_000,
  trace: 'on',
  cache: { mode: replay ? 'read-only' : 'read-write', strict: replay },
  agents: {
    default: {
      ...(replay ? { executor: replayOnly } : { model: chatgpt('gpt-6-luna') }),
      context:
        'NWFM approvals and required integration events are asynchronous. Disabled action buttons immediately after a click mean the request is still processing, not failure. Observe again or use the page Refresh button until the activity has settled. Do not conclude failure solely because the previous status is briefly visible.',
    },
  },
  credentials: {
    admin: {
      username: 'administrator@localhost',
      password: () => process.env.NWFM_TEST_PASSWORD ?? '',
    },
  },
  targets: [
    {
      name: 'chromium',
      engine: web({
        viewport: { width: 1440, height: 1000 },
        locale: 'en-US',
        timezoneId: 'Asia/Riyadh',
      }),
      app: {
        url: 'http://localhost:4200',
        identity: 'nwfm-isolated-e2e',
        environment: 'test',
        command: {
          executable: 'node',
          args: ['scripts/e2e/start-app.mjs'],
          env: { NWFM_E2E_ISOLATED: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1' },
          startupTimeout: 360_000,
          shutdownTimeout: 30_000,
          log: '.e2e/logs/app.log',
        },
      },
    },
  ],
} satisfies E2EConfig;
