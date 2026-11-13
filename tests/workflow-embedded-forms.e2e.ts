import { beforeEach, test } from '@e2e-dev/web';
import { expect } from 'e2e';
import { randomUUID } from 'node:crypto';
import { createDesignerFoundation, type DesignerVersion } from './support/designer-foundation.js';
import { designerAccounts } from './support/designer-accounts.js';
const accounts = designerAccounts('embedded-forms');

beforeEach(async ({ app, screen }) => {
  const admin = accounts.next();
  await app.open('/login');
  await screen.getByLabel('Username').fill(admin.username);
  await screen.getByLabel('Password').fill(admin.password);
  await screen.getByRole('button', 'Sign in').click();
  await expect(screen.getByRole('link', 'Workflows', { exact: true })).toBeVisible();
});

for (const mode of ['create', 'edit', 'duplicate'] as const) {
  test(`embedded ${mode} retains minimized edits and publishes only the origin Step pin`, async ({
    app,
    browser,
    screen,
  }) => {
    const fixture = await createDesignerFoundation(browser);
    const publishedPath = `workflow/definitions/${fixture.child.definitionId}/versions/${fixture.child.versionId}`;
    const publishedBefore = await fixture.api<DesignerVersion>(publishedPath);
    await app.open('/admin/' + fixture.path + '/designer');
    await screen.getByRole('button', 'Select Origin Step', { exact: true }).click();
    await screen.getByRole('button', /^Form/).click();
    const picker = browser.locator('app-workflow-activity-forms');
    const form = picker.getByRole('article').filter({ hasText: fixture.forms[0].nameEn });
    if (mode === 'create') await picker.getByRole('button', 'Create form').click();
    else
      await form
        .getByRole('button', mode === 'edit' ? 'Edit shared form' : 'Duplicate for this Step')
        .click();
    const window = screen.getByRole('dialog', /— form editor$/);
    await expect(window).toBeVisible();
    await expect(window).toContainText('For Step: Origin Step');
    const name = 'Embedded ' + mode + ' ' + randomUUID().slice(0, 8);
    if (mode !== 'edit') {
      await window.getByLabel('Name (English)', { exact: false }).fill(name);
      await window.getByLabel('Name (Arabic)', { exact: false }).fill('نموذج اختبار');
      await window
        .getByLabel('Code', { exact: false })
        .fill('EMB' + randomUUID().replaceAll('-', '').slice(0, 18));
      await window
        .getByRole(
          'button',
          mode === 'create' ? 'Create form and design' : 'Create copy and design',
        )
        .click();
    }
    const builder = browser.locator('.child-editor-window app-form-builder');
    // The existing builder's two name inputs have unassociated labels, so use its source-defined input order.
    const nameInput = browser.locator('.child-editor-window app-form-builder input').first();
    await expect(nameInput).toBeVisible();
    await nameInput.fill(name);
    if (mode === 'create') {
      await builder.getByRole('button', 'Text', { exact: true }).click();
      const fieldDialog = screen.getByRole('dialog', 'Field Properties');
      await expect(fieldDialog).toBeVisible();
      await fieldDialog.getByRole('button', 'Save', { exact: true }).click();
      await expect(fieldDialog).toBeHidden();
    }
    await window.getByRole('button', 'Minimize form editor').press('Enter');
    await expect(window).toBeHidden();
    // Minimize preserves the Step's modal inspector; the canvas is intentionally aria-hidden behind it.
    await expect(screen.getByRole('dialog', 'Origin Step', { exact: true })).toBeVisible();
    await expect(screen.getByRole('navigation', 'Minimized editors')).toBeVisible();
    await screen
      .getByRole('navigation')
      .getByRole('button', /^Restore /)
      .click();
    await expect(window).toBeVisible();
    await expect(nameInput).toHaveValue(name);
    await window.getByRole('button', 'Full screen form editor').click();
    await expect(browser.locator('.child-editor-overlay.child-editor-fullscreen')).toBeVisible();
    await window.getByRole('button', 'Publish and use in this Step').click();
    await expect(window).toBeHidden();
    const pin = async () => {
      const version = await fixture.api<DesignerVersion>(fixture.path);
      return JSON.parse(version.activities.find((node) => node.nodeKey === 'a')!.configurationJson)
        .formSteps[0] as { formId: string; versionNo: number };
    };
    await expect.poll(async () => (await pin()).versionNo).toBe(mode === 'edit' ? 2 : 1);
    if (mode !== 'edit') {
      await expect.poll(async () => (await pin()).formId).not.toBe(fixture.forms[0].id);
    }
    const origin = await pin();
    if (mode === 'edit') expect(origin.formId).toBe(fixture.forms[0].id);
    else expect(origin.formId).not.toBe(fixture.forms[0].id);
    const version = await fixture.api<DesignerVersion>(fixture.path);
    const other = JSON.parse(
      version.activities.find((node) => node.nodeKey === 'b')!.configurationJson,
    ).formSteps[0];
    expect(other.formId).toBe(fixture.forms[0].id);
    expect(other.versionNo).toBe(1);
    const publishedAfter = await fixture.api<DesignerVersion>(publishedPath);
    // The API does not order activities or their child rows; compare the published graph by content.
    const canonical = (value: unknown): unknown =>
      Array.isArray(value)
        ? value.map(canonical).sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)))
        : value && typeof value === 'object'
          ? Object.fromEntries(
              Object.entries(value)
                .sort(([a], [b]) => a.localeCompare(b))
                .map(([key, entry]) => [key, canonical(entry)]),
            )
          : value;
    expect(canonical(publishedAfter.activities)).toEqual(canonical(publishedBefore.activities));
    const detail = (
      await fixture.api<{
        value: { departmentCode: string; fieldActivityCode: string; currentVersionNo: number };
      }>(`v1/form-engine/forms/${origin.formId}`)
    ).value;
    expect(detail.currentVersionNo).toBe(origin.versionNo);
    expect(detail.departmentCode).toBe('10');
    expect(detail.fieldActivityCode).toBe('ISOLATION');
    await browser.reload();
    expect(await pin()).toEqual(origin);
  });
}

test('embedded failed draft save keeps dirty editor and keyboard close decision open', async ({
  app,
  browser,
  screen,
}) => {
  const fixture = await createDesignerFoundation(browser);
  await app.open('/admin/' + fixture.path + '/designer');
  await screen.getByRole('button', 'Select Origin Step', { exact: true }).click();
  await screen.getByRole('button', /^Form/).click();
  await browser
    .locator('app-workflow-activity-forms')
    .getByRole('article')
    .filter({ hasText: fixture.forms[0].nameEn })
    .getByRole('button', 'Edit shared form')
    .click();
  const window = screen.getByRole('dialog', /— form editor$/);
  const nameInput = browser.locator('.child-editor-window app-form-builder input').first();
  await expect(nameInput).toBeVisible();
  const name = 'Unsaved ' + randomUUID();
  await nameInput.fill(name);
  await browser.route('**/form-engine/forms/*/schema', async (route) => {
    if (route.request.method === 'PUT')
      await route.fulfill({ status: 503, json: { message: 'Deterministic local save failure' } });
    else await route.continue();
  });
  await window.getByRole('button', 'Close form editor', { exact: true }).press('Enter');
  const decision = screen.getByRole('alertdialog', 'Save your changes to this form?');
  await expect(decision).toBeVisible();
  await expect(decision.getByRole('button', 'Save draft')).toBeFocused();
  await decision.getByRole('button', 'Save draft').press('Enter');
  await expect(window).toBeVisible();
  await expect(window.getByRole('alert')).toBeVisible();
  await expect(nameInput).toHaveValue(name);
  await window.getByRole('button', 'Close form editor', { exact: true }).click();
  await expect(decision).toBeVisible();
  await decision.press('Escape');
  await expect(decision).toBeHidden();
  await expect(window).toBeVisible();
  await expect(nameInput).toHaveValue(name);
});
