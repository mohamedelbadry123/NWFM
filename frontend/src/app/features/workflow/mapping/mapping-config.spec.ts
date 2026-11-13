import {
  availabilityInfo,
  buildSourceTree,
  callNode,
  collectSources,
  configExpressions,
  configFingerprint,
  emptyConfiguration,
  emptyEndpoint,
  exampleFor,
  isValidLiteralName,
  isValidPointer,
  literalNode,
  mapConfigSources,
  newId,
  normalizeConfiguration,
  objectNode,
  propertyName,
  repinCandidate,
  replaceAt,
  sampleSources,
  serializeConfiguration,
  skeleton,
  sourceKey,
  sourceNode,
  structuralProblems,
  unmappedRequired,
  MAX_TRANSFORM_DEPTH,
  MAX_TRANSFORM_NODES,
} from './mapping-config';
import type {
  DesignerMappingConfiguration,
  SourceDescriptor,
  SourceReference,
  TransformOp,
} from './mapping.models';

const ref = (over: Partial<SourceReference> = {}): SourceReference => ({
  workflowDefinitionId: 'def-1',
  workflowVersionId: 'v-1',
  nodeKey: 'Activity_1',
  kind: 'FormField',
  fieldKey: 'customer.id',
  jsonPointer: '',
  formStepId: 'step-1',
  formId: 'form-1',
  formVersion: 3,
  ...over,
});

const descriptor = (reference: SourceReference, breadcrumbs: string[]): SourceDescriptor => ({
  reference,
  label: breadcrumbs[breadcrumbs.length - 1],
  breadcrumbs,
  schema: { type: 'string' },
  availability: 'SubmittedForm',
});

describe('mapping-config helpers', () => {
  describe('literal names and pointers', () => {
    it('treats dots, hyphens and Arabic as literal characters of a name', () => {
      for (const name of ['customer.id', 'order-no', 'اسم العميل', 'a/b'])
        expect(isValidLiteralName(name)).toBe(true);
    });
    it('rejects empty, control-character and over-long names', () => {
      expect(isValidLiteralName('')).toBe(false);
      expect(isValidLiteralName('bad\u0007name')).toBe(false);
      expect(isValidLiteralName('x'.repeat(201))).toBe(false);
      expect(isValidLiteralName('x'.repeat(200))).toBe(true);
    });
    it('makes property names unique without rewriting their characters', () => {
      expect(propertyName('customer.id', [])).toBe('customer.id');
      expect(propertyName('customer.id', ['customer.id'])).toBe('customer.id_2');
      expect(propertyName('  ', [])).toBe('value');
    });
    it('accepts only RFC 6901 pointers', () => {
      expect(isValidPointer('')).toBe(true);
      expect(isValidPointer('/a/b~1c~0d')).toBe(true);
      expect(isValidPointer('a/b')).toBe(false);
      expect(isValidPointer('/a~2')).toBe(false);
    });
  });

  describe('identity', () => {
    it('keeps identity independent of the pointer but dependent on version pins', () => {
      expect(sourceKey(ref({ jsonPointer: '/x' }))).toBe(sourceKey(ref({ jsonPointer: '/y' })));
      expect(sourceKey(ref({ formVersion: 4 }))).not.toBe(sourceKey(ref()));
      expect(sourceKey(ref({ workflowVersionId: 'v-2' }))).not.toBe(sourceKey(ref()));
    });
    it('does not treat a dotted field key as nesting', () => {
      expect(sourceKey(ref({ fieldKey: 'a.b' }))).not.toBe(sourceKey(ref({ fieldKey: 'a' })));
    });
    it('creates unique prefixed ids', () => {
      const first = newId('out');
      expect(first.startsWith('out_')).toBe(true);
      expect(newId('out', [first])).not.toBe(first);
    });
  });

  describe('configuration documents', () => {
    it('fills missing collections without dropping stored entries', () => {
      const stored = { inputs: [{ location: 'query', name: 'q', value: literalNode('1') }] };
      const normalized = normalizeConfiguration(stored);
      expect(normalized.inputs).toHaveLength(1);
      expect(normalized.outputs).toEqual([]);
      expect(normalized.callable).toBe(false);
      expect(normalizeConfiguration(null).contractVersion).toBe(1);
    });
    it('serializes without undefined members', () => {
      const config = { ...emptyConfiguration(), invocationInputs: undefined };
      expect('invocationInputs' in serializeConfiguration(config)).toBe(false);
    });
    it('keeps a null literal and a default null distinct from an absent one', () => {
      const withNull = serializeConfiguration({
        ...emptyConfiguration(),
        outputs: [
          {
            id: 'o1',
            name: 'n',
            schema: { type: 'string', default: null },
            value: literalNode(null),
          },
        ],
      });
      expect(withNull.outputs[0].value.value).toBeNull();
      expect('value' in withNull.outputs[0].value).toBe(true);
      expect(withNull.outputs[0].schema.default).toBeNull();
    });
    it('fingerprints content, not identity', () => {
      expect(configFingerprint(emptyConfiguration())).toBe(configFingerprint(emptyConfiguration()));
    });
  });

  describe('transform trees', () => {
    it('builds a valid skeleton for every op that does not need a reference', () => {
      const ops: TransformOp[] = [
        'literal',
        'item',
        'object',
        'concat',
        'add',
        'subtract',
        'multiply',
        'divide',
        'text',
        'number',
        'integer',
        'boolean',
        'coalesce',
        'map',
        'filter',
        'count',
        'sum',
        'equal',
        'greater',
      ];
      for (const op of ops) {
        const node = skeleton(op);
        // `item` is only valid inside a map/filter; wrap so structural checks see it in scope.
        const root = op === 'item' ? callNode('map', literalNode([]), node) : node;
        expect(structuralProblems(root), op).toEqual([]);
      }
      expect(() => skeleton('source')).toThrow();
    });
    it('reports "each item" used outside a map or filter', () => {
      expect(structuralProblems(skeleton('item')).join(' ')).toContain('Each item');
    });
    it('reports arity, missing source and depth/size limits', () => {
      expect(structuralProblems(callNode('add', literalNode(1)))).not.toEqual([]);
      expect(structuralProblems({ op: 'source' })).not.toEqual([]);
      let deep = literalNode(1);
      for (let i = 0; i < MAX_TRANSFORM_DEPTH + 1; i++) deep = callNode('text', deep);
      expect(structuralProblems(deep).join(' ')).toContain('levels');
      const wide = callNode(
        'concat',
        ...Array.from({ length: MAX_TRANSFORM_NODES + 1 }, () => literalNode('a')),
      );
      expect(structuralProblems(wide).join(' ')).toContain(`${MAX_TRANSFORM_NODES}`);
      expect(structuralProblems(null)).not.toEqual([]);
    });
    it('replaces a nested node by path without mutating the original', () => {
      const original = callNode('add', literalNode(1), objectNode({ 'a.b': literalNode(2) }));
      const next = replaceAt(original, [1, 'a.b'], literalNode(9));
      expect(next.args![1].properties!['a.b'].value).toBe(9);
      expect(original.args![1].properties!['a.b'].value).toBe(2);
    });
    it('collects sources from every position and remaps them', () => {
      const tree = objectNode({
        x: sourceNode(ref(), '/p'),
        y: callNode('concat', sourceNode(ref({ fieldKey: 'k' }))),
      });
      expect(collectSources(tree).map((s) => s.fieldKey)).toEqual(['customer.id', 'k']);
    });
  });

  describe('configuration-wide helpers', () => {
    const config = (): DesignerMappingConfiguration => ({
      ...emptyConfiguration(),
      endpoint: {
        ...emptyEndpoint(),
        parameters: [
          { location: 'query', name: 'id', schema: { type: 'string', required: true } },
          { location: 'query', name: 'opt', schema: { type: 'string' } },
          {
            location: 'header',
            name: 'x-d',
            schema: { type: 'string', required: true, default: 'd' },
          },
        ],
        body: { type: 'object', required: true },
      },
      inputs: [{ location: 'query', name: 'other', value: sourceNode(ref(), '/deep') }],
      outputs: [
        {
          id: 'o1',
          name: 'out',
          schema: { type: 'string' },
          value: sourceNode(ref({ nodeKey: 'Other' })),
        },
      ],
    });

    it('finds required parameters and body without a binding, ignoring defaults', () => {
      expect(unmappedRequired(config())).toEqual(['query:id', 'body']);
      const bound = config();
      bound.inputs.push(
        { location: 'query', name: 'id', value: literalNode('1') },
        { location: 'body', name: '', value: literalNode({}) },
      );
      expect(unmappedRequired(bound)).toEqual([]);
      expect(unmappedRequired(emptyConfiguration())).toEqual([]);
    });
    it('lists every expression with where it lives', () => {
      expect(configExpressions(config()).map((e) => e.where)).toEqual([
        'input query other',
        'output out',
      ]);
    });
    it('samples each whole source once, without the pointer', () => {
      const sources = sampleSources(config());
      expect(sources).toHaveLength(2);
      expect(sources.every((s) => s.jsonPointer === '')).toBe(true);
    });
    it('maps sources everywhere but changes nothing else', () => {
      const next = mapConfigSources(config(), (r) => ({ ...r, workflowVersionId: 'v-2' }));
      expect(collectSources(next.inputs[0].value)[0].workflowVersionId).toBe('v-2');
      expect(collectSources(next.outputs[0].value)[0].workflowVersionId).toBe('v-2');
      expect(next.inputs[0].value.source!.jsonPointer).toBe('/deep');
      expect(next.endpoint).toEqual(config().endpoint);
    });
    it('builds example values for the test panel from schemas only', () => {
      expect(
        exampleFor({
          type: 'object',
          properties: { a: { type: 'integer' }, b: { type: 'array', items: { type: 'string' } } },
        }),
      ).toEqual({
        a: 1,
        b: ['sample'],
      });
      expect(exampleFor({ type: 'string', default: null })).toBeNull();
    });
  });

  describe('source catalog', () => {
    it('groups sources by their breadcrumbs, last crumb being the source itself', () => {
      const tree = buildSourceTree([
        descriptor(ref(), ['Wf', 'Task Type', 'Step', 'Form', 'Amount']),
        descriptor(ref({ fieldKey: 'b' }), ['Wf', 'Task Type', 'Step', 'Form', 'Other']),
        descriptor(ref({ nodeKey: 'E', kind: 'EventOutput' }), ['Wf', 'Event', 'result']),
      ]);
      expect(tree.count).toBe(3);
      expect(tree.groups.map((g) => g.label)).toEqual(['Wf']);
      expect(tree.groups[0].groups.map((g) => g.label)).toEqual(['Task Type', 'Event']);
      const form = tree.groups[0].groups[0].groups[0].groups[0];
      expect(form.label).toBe('Form');
      expect(form.sources).toHaveLength(2);
    });
    it('offers a repin only for the same field under a different version pin', () => {
      const current = descriptor(ref({ workflowVersionId: 'v-2', formVersion: 4 }), ['a', 'b']);
      expect(repinCandidate([current], ref())).toBe(current);
      expect(repinCandidate([descriptor(ref(), ['a'])], ref())).toBeUndefined();
      expect(
        repinCandidate([descriptor(ref({ fieldKey: 'other', formVersion: 4 }), ['a'])], ref()),
      ).toBeUndefined();
    });
    it('describes availability and flags waits', () => {
      expect(availabilityInfo('InvocationStart').waits).toBe(false);
      expect(availabilityInfo('SubmittedForm').waits).toBe(true);
      expect(availabilityInfo('CompletedOutput').waits).toBe(true);
      expect(availabilityInfo('SchemaRequired').known).toBe(false);
    });
  });
});
