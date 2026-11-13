import { expect } from 'e2e';
import { randomUUID } from 'node:crypto';
import {
  createDesignerFoundation,
  xmlAttribute,
  type DesignerVersion,
} from './designer-foundation.js';

export type MappingFixture = Awaited<ReturnType<typeof createDesignerFoundation>>;
export const literal = (value: unknown) => ({ op: 'literal', value });
export const emptyMapping = () => ({
  contractVersion: 1,
  inputs: [],
  outputs: [],
  selectors: [],
  actions: [],
  stepBindings: [],
  callable: false,
});

export async function configureStep(fixture: MappingFixture, mapping: object) {
  const version = await fixture.api<DesignerVersion>(fixture.path);
  const config = JSON.parse(
    version.activities.find((node) => node.nodeKey === 'a')!.configurationJson,
  );
  config.typedMapping = mapping;
  const xml = (await fixture.api<string>(fixture.path + '/xml')).replace(
    /(<Activity\b[^>]*nodeKey="a"[^>]*configurationJson=")[^"]*(")/,
    '$1' + xmlAttribute(config) + '$2',
  );
  await fixture.api(fixture.path + '/xml', 'PUT', {
    xmlContent: xml,
    expectedRevision: version.revision,
  });
}

/** Validates and publishes real owned hierarchy; never bypasses workspace publication. */
export async function publishOwnedTree(fixture: MappingFixture) {
  const current = await fixture.api<DesignerVersion>(fixture.path);
  let xml = await fixture.api<string>(fixture.path + '/xml');
  const exits: string[] = [];
  for (const node of current.activities) {
    const config = JSON.parse(node.configurationJson || '{}');
    if (config.typedMapping && 'connectionId' in config) {
      config.mappingMode = 'Typed';
      xml = xml.replace(
        new RegExp(
          '(<Activity\\b[^>]*nodeKey="' + node.nodeKey + '"[^>]*configurationJson=")[^"]*(")',
        ),
        '$1' + xmlAttribute(config) + '$2',
      );
      if (
        config.typedMapping.callable &&
        !new RegExp('<Transition\\b[^>]*from="' + node.nodeKey + '"').test(xml)
      ) {
        exits.push(
          `<Transition key="fixture-${node.nodeKey}-exit" from="${node.nodeKey}" to="end"/>`,
        );
      }
    }
  }
  xml = xml.replace('</Transitions>', exits.join('') + '</Transitions>');
  xml = xml.replace(
    /<Activity\b[^>]*type="UserTask"[^>]*(?:\/>|>[\s\S]*?<\/Activity>)/g,
    (element) => {
      const outcomes =
        '<Outcomes><Outcome key="APPROVE" name="Accept" isDefault="true"/><Outcome key="REJECT" name="Reject" requiresComment="true"/></Outcomes>';
      const assigned = element.replace(/^[^>]+>/, (opening) =>
        opening
          .replace(/\sassignmentGroupId="[^"]*"/g, '')
          .replace(/\s*\/?>$/, ` assignmentGroupId="${fixture.group.id}">`),
      );
      if (element.endsWith('/>')) return assigned + outcomes + '</Activity>';
      return assigned
        .replace(/<Outcomes(?:\s[^>]*)?\s*\/>|<Outcomes(?:\s[^>]*)?>[\s\S]*?<\/Outcomes>/g, '')
        .replace('</Activity>', outcomes + '</Activity>');
    },
  );
  await fixture.api(fixture.path + '/xml', 'PUT', {
    xmlContent: xml,
    expectedRevision: current.revision,
  });
  expect(
    (await fixture.api<{ errors: unknown[] }>(fixture.path + '/validate', 'POST', {})).errors,
  ).toEqual([]);
  await fixture.api(fixture.path + '/publish', 'POST', {});
  const parent = await fixture.api<DesignerVersion>(fixture.parentPath);
  const config = JSON.parse(
    parent.activities.find((node) => node.nodeKey === 'owner')!.configurationJson,
  );
  const child = await fixture.api<{ definitionKey: string }>(
    `workflow/definitions/${fixture.draft.definitionId}`,
  );
  const parentXml = (await fixture.api<string>(fixture.parentPath + '/xml')).replace(
    /(<Activity\b[^>]*nodeKey="owner"[^>]*configurationJson=")[^"]*(")/,
    '$1' +
      xmlAttribute({
        ...config,
        versionId: fixture.draft.versionId,
        definitionKey: child.definitionKey,
      }) +
      '$2',
  );
  await fixture.api(fixture.parentPath + '/xml', 'PUT', {
    xmlContent: parentXml,
    expectedRevision: parent.revision,
  });
  expect(
    (await fixture.api<{ errors: unknown[] }>(fixture.parentPath + '/validate', 'POST', {})).errors,
  ).toEqual([]);
  await fixture.api(fixture.parentPath + '/publish', 'POST', {});
}

export async function startOwnedTree(fixture: MappingFixture, reference = fixture.name) {
  return (
    await fixture.api<{ instanceId: string }>(
      `workflow/workspace/definitions/${fixture.parent.definitionId}/instances`,
      'POST',
      { requestId: randomUUID(), reference, isDemo: false },
    )
  ).instanceId;
}

export const noteBinding = (fixture: MappingFixture) => ({
  id: 'note',
  formStepId: 'aForm',
  formId: fixture.forms[0].id,
  formVersion: 1,
  fieldKey: 'notes',
  mode: 'ReadOnly',
  schema: { type: 'string', nullable: true },
  value: literal(null),
});

export interface Receipt {
  id: string;
  status: string;
  executionKind: string;
  targetNodeKey: string;
  prepared: boolean;
  applied: boolean;
  graphAdvanced: boolean;
  operationId?: string | null;
  error?: string | null;
  deadline?: string | null;
}
export const receipts = (fixture: MappingFixture, rootId: string) =>
  fixture.api<Receipt[]>('workflow/mapping-invocations?rootInstanceId=' + rootId);
