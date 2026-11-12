import type { Browser } from '@e2e-dev/web';
import { createActivityForms } from './activity-forms.js';

export interface DesignerVersion {
  workspaceJson: string;
  revision: string;
  activities: { nodeKey: string; configurationJson: string }[];
}

export const xmlAttribute = (value: unknown) =>
  (typeof value === 'string' ? value : JSON.stringify(value))
    .replaceAll('&', '&amp;')
    .replaceAll('"', '&quot;')
    .replaceAll('<', '&lt;');

/** Uses only the already isolated API and the standard main/child/forms fixture. */
export async function createDesignerFoundation(browser: Browser, ordered = false) {
  const fixture = await createActivityForms(browser);
  const original = await fixture.api<DesignerVersion>(fixture.mainPath);
  const mainSettings = { ...JSON.parse(original.workspaceJson), designerVersion: 4 };
  const parent = await fixture.api<{ definitionId: string; versionId: string }>(
    'workflow/workspace/definitions',
    'POST',
    { name: fixture.name + ' designer foundation', settings: mainSettings },
  );
  const parentPath = `workflow/definitions/${parent.definitionId}/versions/${parent.versionId}`;
  const parentVersion = await fixture.api<DesignerVersion>(parentPath);
  await fixture.api(parentPath + '/xml', 'PUT', {
    expectedRevision: parentVersion.revision,
    xmlContent: `<Workflow xmlns="https://privora.io/workflow/v1" workspaceJson="${xmlAttribute(mainSettings)}"><Activities><Activity nodeKey="start" type="Start" name="Start"/><Activity nodeKey="owner" type="MainActivity" name="Owner Task Type" configurationJson="${xmlAttribute({ taskTypeId: fixture.type.id, departmentCode: '10', fieldActivityCode: 'ISOLATION' })}"/><Activity nodeKey="end" type="End" name="End"/></Activities><Transitions><Transition key="entry" from="start" to="owner"/><Transition key="finish" from="owner" to="end"/></Transitions></Workflow>`,
  });
  // v4 child definitions must be created through their owning MainActivity.
  const child = await fixture.api<{ id: string; versionId: string }>(
    'workflow/workspace/children',
    'POST',
    { parentVersionId: parent.versionId, nodeKey: 'owner' },
  );
  const draft = { definitionId: child.id, versionId: child.versionId };
  const path = `workflow/definitions/${draft.definitionId}/versions/${draft.versionId}`;
  const childVersion = await fixture.api<DesignerVersion>(path);
  const settings = JSON.parse(childVersion.workspaceJson);
  const step = (key: string) => ({
    taskTypeId: fixture.type.id,
    departmentCode: '10',
    fieldActivityCode: 'ISOLATION',
    formSteps: [
      {
        id: key + 'Form',
        formId: fixture.forms[0].id,
        versionNo: 1,
        name: 'Shared pinned form',
        rules: [],
        otherwise: { kind: 'Next' },
      },
    ],
  });
  const route = (key: string, field: string, value: string) => ({
    transitionKey: key,
    always: false,
    when: {
      match: 'All',
      conditions: [{ field: 'a.' + field, operator: 'equal', value }],
      groups: [],
    },
  });
  const routing = {
    contractVersion: 2,
    matchMode: 'First',
    routes: [
      route('route1', 'route', 'MATCH'),
      route('route2', 'route', 'NEVER'),
      route('route3', 'approved', 'yes'),
    ],
    sources: ['route', 'approved'].map((field) => ({
      key: 'a.' + field,
      nodeKey: 'a',
      field,
      type: 'text',
      label: field === 'route' ? 'Routing choice' : 'Approved',
      formId: fixture.forms[0].id,
      formVersion: 1,
    })),
    waitFor: 'All',
    remainingBranches: 'Continue',
  };
  const node = (key: string, type: string, name: string, config: object, x: number, y: number) =>
    `<Activity nodeKey="${key}" type="${type}" name="${name}" positionX="${x}" positionY="${y}" configurationJson="${xmlAttribute(config)}"/>`;
  const nodes = [
    node('start', 'Start', 'Start', {}, 100, 40),
    node('a', 'UserTask', 'Origin Step', step('a'), 100, 180),
    node('b', 'UserTask', 'Other Step', step('b'), 600, 480),
    node('end', 'End', 'End', {}, 600, 650),
    ...(ordered
      ? [node('condition', 'ExclusiveGateway', 'Ordered choice', { routing }, 100, 380)]
      : []),
  ];
  const edges = ordered
    ? [
        ['entry', 'start', 'a'],
        ['check', 'a', 'condition'],
        ['route1', 'condition', 'b'],
        ['route2', 'condition', 'end'],
        ['route3', 'condition', 'b'],
        ['finish', 'b', 'end'],
      ]
    : [
        ['entry', 'start', 'a'],
        ['next', 'a', 'b'],
        ['finish', 'b', 'end'],
      ];
  const xml = `<Workflow xmlns="https://privora.io/workflow/v1" workspaceJson="${xmlAttribute(settings)}"><Activities>${nodes.join('')}</Activities><Transitions>${edges.map(([key, from, to]) => `<Transition key="${key}" from="${from}" to="${to}"/>`).join('')}</Transitions></Workflow>`;
  await fixture.api(path + '/xml', 'PUT', {
    xmlContent: xml,
    designerJson: '{}',
    expectedRevision: childVersion.revision,
  });
  return { ...fixture, draft, path, parent, parentPath };
}
