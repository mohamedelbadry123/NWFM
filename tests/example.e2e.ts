import { test } from '@e2e-dev/web';
import { expect } from 'e2e';

test('app opens', async ({ app, screen }) => {
  await app.open('/login');
  await expect(screen.getByLabel('Username')).toBeVisible();
  await expect(screen.getByLabel('Password')).toBeVisible();
  await expect(screen.getByRole('button', 'Sign in')).toBeVisible();
});
