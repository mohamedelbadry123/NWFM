import type { Browser } from '@e2e-dev/web';
import { expect } from 'e2e';
import type { App, Screen } from 'e2e';
import {
  createDesignerFoundation,
  xmlAttribute,
  type DesignerVersion,
} from './designer-foundation.js';

export async function createMappingFixture(browser: Browser) {
  const fixture = await createDesignerFoundation(browser);
  const connections = await fixture.api<{ id: string; name: string; baseAddress: string }[]>(
    'workflow/integrations/connections',
  );
  const connection = connections.find((c) => c.name === 'Demo — Local HTTP');
  expect(connection).toBeDefined();
  const http = {
    connectionId: connection!.id,
    protocol: 'Rest',
    method: 'POST',
    path: '/echo',
    required: true,
  };
  const typed = (extra: object = {}) => ({
    contractVersion: 1,
    endpoint: {
      method: 'POST',
      path: '/echo',
      parameters: [],
      body: { type: 'object', properties: {}, additionalProperties: true },
    },
    inputs: [{ location: 'body', name: '$body', value: { op: 'literal', value: {} } }],
    selectors: [],
    outputs: [],
    actions: [],
    stepBindings: [],
    callable: false,
    treeRoot: {
      workflowDefinitionId: fixture.parent.definitionId,
      workflowVersionId: fixture.parent.versionId,
    },
    ...extra,
  });
  const upstream = {
    ...http,
    typedMapping: typed({
      outputs: [
        {
          id: 'numbers',
          name: 'Numbers',
          schema: { type: 'array', items: { type: 'number' } },
          value: { op: 'literal', value: [1, 2, 3] },
        },
      ],
    }),
  };
  const receiver = {
    ...http,
    required: false,
    typedMapping: typed({
      callable: true,
      invocationInputs: { type: 'object', properties: {}, additionalProperties: true },
    }),
  };
  const node = (key: string, name: string, config: object, x: number) =>
    `<Activity nodeKey="${key}" type="ServiceTask" name="${name}" actionKey="http.request" positionX="${x}" positionY="700" configurationJson="${xmlAttribute(config)}"/>`;
  const current = await fixture.api<DesignerVersion>(fixture.path);
  let xml = await fixture.api<string>(fixture.path + '/xml');
  xml = xml.replace(
    '</Activities>',
    node('upstream', 'Earlier event', upstream, 100) +
      node('mapping', 'Mapping event', http, 400) +
      node('receiver', 'Callable receiver', receiver, 700) +
      '</Activities>',
  );
  // Workflows are serialized by the backend, so replace the transition by its stable key rather than formatting.
  xml = xml.replace(
    /<Transition\b[^>]*key="finish"[^>]*\/?>/g,
    '<Transition key="finish" from="b" to="upstream"/><Transition key="after-source" from="upstream" to="mapping"/><Transition key="after-mapping" from="mapping" to="end"/>',
  );
  await fixture.api(fixture.path + '/xml', 'PUT', {
    xmlContent: xml,
    expectedRevision: current.revision,
    designerJson: '{}',
  });
  const configuration = async () =>
    JSON.parse(
      (await fixture.api<DesignerVersion>(fixture.path)).activities.find(
        (n) => n.nodeKey === 'mapping',
      )!.configurationJson,
    );
  return { ...fixture, connection: connection!, configuration };
}

export const mappingOpenApi = {
  openapi: '3.0.3',
  info: { title: 'Owned test request', version: '1' },
  paths: {
    '/echo': {
      post: {
        requestBody: {
          required: true,
          content: {
            'application/json': {
              schema: {
                type: 'object',
                required: ['customer', 'amount'],
                properties: {
                  // Form-field sources are nullable by contract, so the destination that receives one must accept an empty value.
                  customer: { type: 'string', nullable: true },
                  amount: { type: 'number', nullable: true },
                  details: { type: 'object', properties: { enabled: { type: 'boolean' } } },
                  items: { type: 'array', items: { type: 'number' } },
                },
              },
            },
          },
        },
        responses: {
          '200': {
            description: 'Local response',
            content: {
              'application/json': {
                schema: { type: 'object', properties: { accepted: { type: 'boolean' } } },
              },
            },
          },
        },
      },
    },
  },
};

export async function openMappingEditor(
  fixture: Awaited<ReturnType<typeof createMappingFixture>>,
  app: App,
  browser: Browser,
  screen: Screen,
) {
  await app.open('/admin/' + fixture.parentPath + '/designer');
  await screen.getByRole('button', 'Select Owner Task Type', { exact: true }).press('Enter');
  if (!(await screen.getByRole('button', 'Open child workflow').count())) {
    await screen.getByRole('button', /^Child workflow/).click();
  }
  // The API creates an owned draft; the designer's Create action also pins that draft on the parent node.
  // OpenChild is idempotent for the owning node, so this does not create a second child.
  const create = screen.getByRole('button', 'Create child workflow', { exact: true });
  if (await create.count()) await create.click();
  else await screen.getByRole('button', 'Open child workflow').click();
  const child = screen.getByRole('dialog', 'Owner Task Type — child workflow');
  await expect(child).toBeVisible();
  await child.getByRole('button', 'Select Mapping event', { exact: true }).press('Enter');
  await child.getByRole('button', /^Action and inputs/).click();
  const editor = browser.locator('app-workflow-integration-editor');
  await expect(editor.getByRole('button', 'Use typed REST/JSON mapping')).toBeVisible();
  await editor.getByRole('button', 'Use typed REST/JSON mapping').click();
  const mapper = screen.getByRole('region', 'Typed request mapping');
  await expect(mapper.getByRole('tablist', 'Mapping stages')).toBeVisible();
  return { child, editor, mapper };
}
