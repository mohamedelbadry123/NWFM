import { defineConfig } from 'vitest/config';
import { playwright } from '@vitest/browser-playwright';

export default defineConfig({
  test: {
    restoreMocks: true,
    browser: {
      provider: playwright({
        launchOptions: { executablePath: process.env.CHROME_BIN },
      }),
    },
  },
});
