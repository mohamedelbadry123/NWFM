import type { Browser } from '@e2e-dev/web';
import { expect } from 'e2e';
import { randomUUID } from 'node:crypto';

export const tenantA = {
  id: '10000000-0000-0000-0000-000000000001',
  origin: 'http://localhost:4200',
};
export const tenantB = {
  id: '20000000-0000-0000-0000-000000000002',
  origin: 'http://localhost:4201',
};

interface RequestOptions {
  method?: string;
  body?: unknown;
  headers?: Record<string, string>;
  origin?: string;
  anonymous?: boolean;
  tokenStorageKey?: string;
}

/** Runs against the real API, keeping bearer credentials inside the browser. */
export function tenantApi(browser: Browser) {
  return async <T>(
    path: string,
    expectedStatus = 200,
    options: RequestOptions = {},
  ): Promise<T> => {
    const response = JSON.parse(
      String(
        await browser.evaluate(async (input) => {
          const { path, options } = JSON.parse(String(input));
          const response = await fetch((options.origin ?? '') + '/api/' + path, {
            method: options.method ?? 'GET',
            headers: {
              ...(options.anonymous
                ? {}
                : {
                    Authorization:
                      'Bearer ' +
                      (options.tokenStorageKey
                        ? sessionStorage.getItem(options.tokenStorageKey)
                        : localStorage.getItem('nwfm_access_token')),
                  }),
              'Content-Type': 'application/json',
              ...options.headers,
            },
            body: options.body === undefined ? undefined : JSON.stringify(options.body),
          });
          const text = await response.text();
          return JSON.stringify({
            status: response.status,
            body: text
              ? response.headers.get('content-type')?.includes('json')
                ? JSON.parse(text)
                : text
              : null,
          });
        }, JSON.stringify({ path, options })),
      ),
    ) as { status: number; body: T };
    expect(
      response.status,
      `${options.method ?? 'GET'} ${path}: ${JSON.stringify(response.body).slice(0, 1000)}`,
    ).toBe(expectedStatus);
    return response.body;
  };
}

export interface TenantResources {
  name: string;
  formId: string;
  typeId: string;
  definitionId: string;
  fileId: string;
  fileContents: string;
}

/** All writes go through normal authorization and the least-privileged request connection. */
export async function createTenantResources(browser: Browser): Promise<TenantResources> {
  const api = tenantApi(browser);
  const suffix = randomUUID().replaceAll('-', '');
  const name = 'Tenant security ' + suffix;
  const form = await api<{ value: { id: string } }>('v1/form-engine/forms', 201, {
    method: 'POST',
    body: {
      code: 'SEC' + suffix,
      nameEn: name,
      nameAr: name,
      category: 'GENERAL',
      departmentCode: '10',
      fieldActivityCode: 'ISOLATION',
    },
  });
  const formId = form.value.id;
  await api(`v1/form-engine/forms/${formId}/schema`, 200, {
    method: 'PUT',
    body: {
      schemaJson: JSON.stringify({
        name_en: name,
        name_ar: name,
        elements: [
          { type: 'text', data_name: 'reference', label_en: 'Reference' },
          {
            type: 'file',
            data_name: 'evidence',
            label_en: 'Evidence',
            allowed_extensions: ['txt'],
          },
        ],
      }),
    },
  });
  await api(`v1/form-engine/forms/${formId}/publish`, 200, { method: 'POST' });
  const type = await api<{ value: { id: string } }>('v1/task-types', 201, {
    method: 'POST',
    body: { code: 'SEC' + suffix, nameEn: name, nameAr: name, formDefinitionIds: [formId] },
  });
  const definition = await api<{ id: string }>('workflow/definitions', 201, {
    method: 'POST',
    body: { definitionKey: 'SEC-' + suffix, name },
  });
  const fileContents = 'Private tenant evidence ' + suffix;
  const upload = JSON.parse(
    String(
      await browser.evaluate(async (input) => {
        const { formId, fileContents } = JSON.parse(String(input));
        const body = new FormData();
        body.set('formDefinitionId', formId);
        body.set('versionNo', '1');
        body.set('dataName', 'evidence');
        body.set('file', new Blob([fileContents], { type: 'text/plain' }), 'evidence.txt');
        const response = await fetch('/api/v1/form-engine/uploads', {
          method: 'POST',
          headers: { Authorization: 'Bearer ' + localStorage.getItem('nwfm_access_token') },
          body,
        });
        return JSON.stringify({ status: response.status, body: await response.json() });
      }, JSON.stringify({ formId, fileContents })),
    ),
  ) as { status: number; body: { value: { fileId: string } } };
  expect(upload.status).toBe(200);
  return {
    name,
    formId,
    typeId: type.value.id,
    definitionId: definition.id,
    fileId: upload.body.value.fileId,
    fileContents,
  };
}

export async function assertOwnResources(browser: Browser, resources: TenantResources) {
  const api = tenantApi(browser);
  expect(
    (await api<{ value: { nameEn: string } }>(`v1/form-engine/forms/${resources.formId}`)).value
      .nameEn,
  ).toBe(resources.name);
  expect(
    (await api<{ value: { nameEn: string } }>(`v1/task-types/${resources.typeId}`)).value.nameEn,
  ).toBe(resources.name);
  expect((await api<{ name: string }>(`workflow/definitions/${resources.definitionId}`)).name).toBe(
    resources.name,
  );
  expect(await api<string>(`v1/form-engine/uploads/${resources.fileId}`)).toBe(
    resources.fileContents,
  );
}

export async function assertForeignResourcesDenied(browser: Browser, resources: TenantResources) {
  const api = tenantApi(browser);
  for (const path of [
    `v1/form-engine/forms/${resources.formId}`,
    `v1/task-types/${resources.typeId}`,
    `workflow/definitions/${resources.definitionId}`,
    `v1/form-engine/uploads/${resources.fileId}`,
  ]) {
    const denied = await api(path, 404);
    const absent = await api(path.replace(/[^/]+$/, randomUUID()), 404);
    // Missing and foreign IDs disclose the same public error.
    expect(denied).toEqual(absent);
  }
  await api(`v1/form-engine/forms/${resources.formId}`, 404, {
    method: 'PUT',
    body: {
      nameEn: 'Intrusion',
      nameAr: 'Intrusion',
      category: 'GENERAL',
      departmentCode: '10',
      fieldActivityCode: 'ISOLATION',
    },
  });
  await api(`v1/task-types/${resources.typeId}/status`, 404, {
    method: 'PUT',
    body: { isActive: false },
  });
  await api(`workflow/definitions/${resources.definitionId}`, 404, {
    method: 'PUT',
    body: { name: 'Intrusion' },
  });
  await api(`v1/form-engine/uploads/${resources.fileId}`, 404, { method: 'DELETE' });
  for (const path of [
    `v1/form-engine/forms?search=${encodeURIComponent(resources.name)}`,
    `v1/task-types?search=${encodeURIComponent(resources.name)}`,
    `workflow/definitions?search=${encodeURIComponent(resources.name)}`,
  ]) {
    expect(JSON.stringify(await api(path))).not.toContain(resources.name);
  }
}

export async function assertOwnMutations(browser: Browser, resources: TenantResources) {
  const api = tenantApi(browser);
  const name = resources.name + ' edited';
  await api(`v1/form-engine/forms/${resources.formId}`, 200, {
    method: 'PUT',
    body: {
      nameEn: name,
      nameAr: name,
      category: 'GENERAL',
      departmentCode: '10',
      fieldActivityCode: 'ISOLATION',
    },
  });
  await api(`v1/task-types/${resources.typeId}`, 200, {
    method: 'PUT',
    body: { nameEn: name, nameAr: name, formDefinitionIds: [resources.formId] },
  });
  await api(`workflow/definitions/${resources.definitionId}`, 200, {
    method: 'PUT',
    body: { name },
  });
  await assertOwnResources(browser, { ...resources, name });
  await api(`v1/form-engine/uploads/${resources.fileId}`, 200, { method: 'DELETE' });
  await api(`v1/form-engine/uploads/${resources.fileId}`, 404);
}
