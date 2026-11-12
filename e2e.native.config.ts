import type { E2EConfig } from 'e2e';
import { web } from '@e2e-dev/web';
import { replayOnly } from './scripts/e2e/replay-only.js';

if (process.env.NWFM_E2E_REPLAY !== '1' || !process.env.NWFM_E2E_RUN_ID)
  throw new Error('Use the owned native launcher with NWFM_E2E_REPLAY=1.');
export default {
  projectId: 'nwfm',
  tests: 'tests/**/*.e2e.ts',
  workers: 1,
  retries: 0,
  timeout: 180_000,
  assertionTimeout: 15_000,
  trace: 'on',
  cache: { mode: 'read-only', strict: true },
  agents: { default: { executor: replayOnly } },
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
        url: process.env.NWFM_E2E_UI_URL!,
        identity: 'nwfm-owned-native',
        environment: 'test',
      },
    },
  ],
} satisfies E2EConfig;
