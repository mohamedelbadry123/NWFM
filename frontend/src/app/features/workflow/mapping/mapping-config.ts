import type {
  CallableTarget,
  DesignerMappingConfiguration,
  EndpointDefinition,
  MappingSchema,
  MappingSchemaType,
  MissingDataPolicy,
  SourceDescriptor,
  SourceReference,
  TransformNode,
  TransformOp,
} from './mapping.models';

/**
 * Pure helpers for authoring a typed mapping. Nothing here evaluates a mapping: values are only ever computed by
 * the server (preview, send test, runtime). These helpers build the documents the server accepts, mirror its
 * STRUCTURAL limits so errors show next to the control, and describe things for people.
 */
export const MAX_TRANSFORM_DEPTH = 16;
export const MAX_TRANSFORM_NODES = 200;
/** The server's schema limits (`MappingTypes.ValidateSchema`): depth 16, 500 nodes. */
export const MAX_SCHEMA_DEPTH = 16;
export const MAX_SCHEMA_NODES = 500;
export const SCHEMA_TYPES: MappingSchemaType[] = [
  'string',
  'number',
  'integer',
  'boolean',
  'object',
  'array',
];
/** The only HTTP methods the endpoint stage offers (matches the integration validator). */
export const HTTP_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'];

// ── identifiers ───────────────────────────────────────────────────────────

/** Stable identifiers: output, action, selector and binding IDs survive renames and reordering. */
export function newId(prefix: string, taken: Iterable<string> = []): string {
  const used = new Set(taken);
  let id: string;
  do {
    const random =
      typeof crypto !== 'undefined' && 'randomUUID' in crypto
        ? crypto.randomUUID().replace(/-/g, '').slice(0, 10)
        : Math.random().toString(36).slice(2, 12);
    id = `${prefix}_${random}`;
  } while (used.has(id));
  return id;
}

/** The exact workflow definition and version a related-instance selector points at. */
export function relatedPinKey(selector: {
  workflowDefinitionId: string;
  workflowVersionId: string;
}): string {
  return `${selector.workflowDefinitionId}|${selector.workflowVersionId}`;
}

/** Identity of the value a reference reads, ignoring the JSON pointer inside it. */
export function sourceKey(ref: SourceReference): string {
  return [
    ref.workflowDefinitionId,
    ref.workflowVersionId,
    ref.nodeKey,
    ref.kind,
    ref.fieldKey,
    ref.formStepId ?? '',
    ref.formId ?? '',
    ref.formVersion ?? '',
    ref.selectorId ?? '',
  ].join('|');
}

// ── configuration documents ────────────────────────────────────────────────

export const emptyConfiguration = (): DesignerMappingConfiguration => ({
  contractVersion: 1,
  endpoint: null,
  inputs: [],
  selectors: [],
  outputs: [],
  stepBindings: [],
  actions: [],
  callable: false,
});

export const emptyEndpoint = (): EndpointDefinition => ({
  method: 'POST',
  path: '/',
  parameters: [],
  body: null,
  response: null,
});

/**
 * Reads a stored `typedMapping` into the editable shape. It only fills in missing collections; it never drops or
 * repairs an entry, so a mapping the catalog now calls broken stays visible until the author corrects it.
 */
export function normalizeConfiguration(raw: unknown): DesignerMappingConfiguration {
  const value = (
    raw && typeof raw === 'object' ? raw : {}
  ) as Partial<DesignerMappingConfiguration>;
  const base = emptyConfiguration();
  return {
    ...base,
    ...value,
    contractVersion: value.contractVersion ?? 1,
    endpoint: value.endpoint ?? null,
    inputs: value.inputs ?? [],
    selectors: value.selectors ?? [],
    outputs: value.outputs ?? [],
    stepBindings: value.stepBindings ?? [],
    actions: value.actions ?? [],
    callable: value.callable ?? false,
  };
}

/** Removes editor-only gaps (undefined members) so the server's strict contract accepts the document. */
export function serializeConfiguration(
  config: DesignerMappingConfiguration,
): DesignerMappingConfiguration {
  return JSON.parse(JSON.stringify(config)) as DesignerMappingConfiguration;
}

export const ownerOf = (
  definitionId: string,
  versionId: string,
  nodeKey: string,
): CallableTarget => ({
  workflowDefinitionId: definitionId,
  workflowVersionId: versionId,
  nodeKey,
});

/** The event's own HTTP response BODY as a source (never the legacy `{status,headers,body}` envelope). */
export const responseReference = (owner: CallableTarget): SourceReference => ({
  ...owner,
  kind: 'Response',
  fieldKey: '$response',
  jsonPointer: '',
});

export const invocationInputReference = (owner: CallableTarget): SourceReference => ({
  ...owner,
  kind: 'InvocationInput',
  fieldKey: '$inputs',
  jsonPointer: '',
});

// ── schemas ────────────────────────────────────────────────────────────────

export function newSchema(type: MappingSchemaType): MappingSchema {
  return type === 'object'
    ? { type, properties: {} }
    : type === 'array'
      ? { type, items: { type: 'string' } }
      : { type };
}

/** Changes the type, keeping only what still makes sense (required/nullable stay; children are rebuilt). */
export function changeSchemaType(schema: MappingSchema, type: MappingSchemaType): MappingSchema {
  if (schema.type === type) return schema;
  const next = newSchema(type);
  if (schema.required) next.required = true;
  if (schema.nullable) next.nullable = true;
  if (schema.inferred) next.inferred = true;
  return next;
}

export const isUnknownSchema = (schema: MappingSchema | null | undefined) =>
  !schema || (schema.type as string) === 'unknown';

/** "text", "number (optional)", "list of text", "object { a, b }" — for people, not for the wire. */
export function describeSchema(
  schema: MappingSchema | null | undefined,
  t = (en: string) => en,
): string {
  if (isUnknownSchema(schema)) return t('unknown type');
  const s = schema as MappingSchema;
  const names: Record<string, string> = {
    string: t('text'),
    number: t('number'),
    integer: t('whole number'),
    boolean: t('yes/no'),
  };
  let text: string;
  if (s.type === 'array') text = `${t('list of')} ${describeSchema(s.items ?? undefined, t)}`;
  else if (s.type === 'object') {
    const keys = Object.keys(s.properties ?? {});
    const shown = keys.slice(0, 4).join(', ') + (keys.length > 4 ? ', …' : '');
    text = `${t('object')}${keys.length ? ` { ${shown} }` : ''}${s.additionalProperties ? ` + ${t('other fields')}` : ''}`;
  } else text = names[s.type] ?? s.type;
  return s.nullable ? `${text} ${t('or empty')}` : text;
}

const decodePointerToken = (token: string) => token.replace(/~1/g, '/').replace(/~0/g, '~');
export const encodePointerToken = (token: string) => token.replace(/~/g, '~0').replace(/\//g, '~1');

/** The schema found at an RFC 6901 pointer inside `schema` (null when the pointer leaves the declared shape). */
export function schemaAt(
  schema: MappingSchema | null | undefined,
  pointer: string,
): MappingSchema | null {
  if (!schema) return null;
  if (pointer === '') return schema;
  if (!pointer.startsWith('/')) return null;
  let current: MappingSchema | null | undefined = schema;
  for (const raw of pointer.slice(1).split('/')) {
    const token = decodePointerToken(raw);
    if (!current) return null;
    if (current.type === 'object')
      current = current.properties?.[token] ?? current.additionalPropertySchema ?? null;
    else if (current.type === 'array' && /^\d+$/.test(token)) current = current.items ?? null;
    else return null;
  }
  return current ?? null;
}

/** Every selectable pointer under a schema (for "pick a value inside this") — bounded in depth and count. */
export function schemaPaths(
  schema: MappingSchema | null | undefined,
  limit = 500,
): { pointer: string; label: string; schema: MappingSchema }[] {
  const result: { pointer: string; label: string; schema: MappingSchema }[] = [];
  const walk = (s: MappingSchema, pointer: string, label: string, depth: number) => {
    if (result.length >= limit || depth > MAX_SCHEMA_DEPTH) return;
    result.push({ pointer, label: label || '(whole value)', schema: s });
    if (s.type === 'object')
      for (const [name, child] of Object.entries(s.properties ?? {}))
        walk(
          child,
          `${pointer}/${encodePointerToken(name)}`,
          label ? `${label} › ${name}` : name,
          depth + 1,
        );
    // Array items are chosen with Each item, not by index.
  };
  if (schema) walk(schema, '', '', 0);
  return result;
}

/** Whether `actual` can feed `expected`, for inline hints only — the server decides at validate/preview. */
export function looksCompatible(
  expected: MappingSchema | null | undefined,
  actual: MappingSchema | null | undefined,
): boolean {
  if (isUnknownSchema(expected) || isUnknownSchema(actual)) return true;
  const e = expected as MappingSchema;
  const a = actual as MappingSchema;
  if (e.type === 'number' && a.type === 'integer') return true;
  if (e.type !== a.type) return false;
  if (e.type === 'array') return looksCompatible(e.items, a.items);
  return true;
}

// ── transform nodes ────────────────────────────────────────────────────────

/** Number of arguments an op takes; `[min, max]`. */
export function arityOf(op: TransformOp): [number, number] {
  switch (op) {
    case 'literal':
    case 'source':
    case 'item':
    case 'object':
      return [0, 0];
    case 'text':
    case 'number':
    case 'integer':
    case 'boolean':
    case 'count':
    case 'sum':
      return [1, 1];
    case 'concat':
    case 'coalesce':
      return [1, 100];
    default:
      return [2, 2];
  }
}

export const literalNode = (value: unknown, literalSchema?: MappingSchema): TransformNode => ({
  op: 'literal',
  value,
  ...(literalSchema ? { literalSchema } : {}),
});
export const sourceNode = (
  source: SourceReference,
  jsonPointer = '',
  missing?: MissingDataPolicy | null,
): TransformNode => ({
  op: 'source',
  source: { ...source, jsonPointer },
  ...(missing ? { missing } : {}),
});
export const itemNode = (jsonPointer = ''): TransformNode => ({ op: 'item', jsonPointer });
export const objectNode = (properties: Record<string, TransformNode> = {}): TransformNode => ({
  op: 'object',
  properties,
});
export const callNode = (op: TransformOp, ...args: TransformNode[]): TransformNode => ({
  op,
  args,
});

/** An empty text constant: the neutral starting point for a slot the author has not filled yet. */
export const emptyNode = (): TransformNode => literalNode('');

/** A valid skeleton for `op`; every slot starts as an empty constant the author then replaces. */
export function skeleton(op: TransformOp, insideItem = false): TransformNode {
  switch (op) {
    case 'literal':
      return emptyNode();
    case 'item':
      return itemNode('');
    case 'object':
      return objectNode({});
    case 'source':
      throw new Error('A source node needs a reference; use sourceNode().');
    case 'map':
      return callNode('map', emptyNode(), insideItem ? itemNode('') : itemNode(''));
    case 'filter':
      return callNode('filter', emptyNode(), callNode('equal', itemNode(''), emptyNode()));
    default: {
      const [min] = arityOf(op);
      return callNode(op, ...Array.from({ length: min }, () => emptyNode()));
    }
  }
}

/** Nodes that live directly under `node`, with the in-item flag for map/filter predicates. */
export function childNodes(node: TransformNode): TransformNode[] {
  return [...(node.args ?? []), ...Object.values(node.properties ?? {})];
}

export function nodeCount(node: TransformNode): number {
  return 1 + childNodes(node).reduce((sum, child) => sum + nodeCount(child), 0);
}
export function nodeDepth(node: TransformNode): number {
  return 1 + Math.max(0, ...childNodes(node).map(nodeDepth));
}

/** Every source reference used anywhere in an expression, with its own pointer. */
export function collectSources(node: TransformNode | null | undefined): SourceReference[] {
  if (!node) return [];
  const own = node.op === 'source' && node.source ? [node.source] : [];
  return [...own, ...childNodes(node).flatMap(collectSources)];
}

/** Replaces every source reference through `map`, returning a new tree (the original is untouched). */
export function mapSources(
  node: TransformNode,
  map: (ref: SourceReference) => SourceReference,
): TransformNode {
  const next: TransformNode = { ...node };
  if (node.op === 'source' && node.source) next.source = map(node.source);
  if (node.args) next.args = node.args.map((a) => mapSources(a, map));
  if (node.properties)
    next.properties = Object.fromEntries(
      Object.entries(node.properties).map(([k, v]) => [k, mapSources(v, map)]),
    );
  return next;
}

/**
 * Structural problems the server would reject, found locally so they show beside the control. This mirrors the
 * server's tree limits (arity, depth 16, 200 nodes, item scope); it does not and cannot evaluate anything.
 */
export function structuralProblems(root: TransformNode | null | undefined): string[] {
  if (!root) return ['Choose how this value is produced.'];
  const problems: string[] = [];
  const total = nodeCount(root);
  if (total > MAX_TRANSFORM_NODES)
    problems.push(
      `A mapping may have at most ${MAX_TRANSFORM_NODES} steps; this one has ${total}.`,
    );
  const walk = (node: TransformNode, depth: number, inItem: boolean) => {
    if (depth > MAX_TRANSFORM_DEPTH) {
      problems.push(`Nesting is limited to ${MAX_TRANSFORM_DEPTH} levels.`);
      return;
    }
    const [min, max] = arityOf(node.op);
    const count = node.args?.length ?? 0;
    if (count < min || count > max)
      problems.push(
        `"${node.op}" needs ${min === max ? min : `${min}–${max}`} value(s) but has ${count}.`,
      );
    if (node.op === 'source' && !node.source)
      problems.push('A source value has no source selected.');
    if (node.op === 'item' && !inItem)
      problems.push('"Each item" can only be used inside Each item / Keep matching.');
    if (node.op === 'object' && !node.properties) problems.push('An object needs its properties.');
    (node.args ?? []).forEach((arg, i) =>
      walk(arg, depth + 1, inItem || ((node.op === 'map' || node.op === 'filter') && i === 1)),
    );
    for (const child of Object.values(node.properties ?? {})) walk(child, depth + 1, inItem);
  };
  walk(root, 1, false);
  return [...new Set(problems)];
}

/** The type a node produces, for hints only. `lookup` resolves a source reference to its declared schema. */
export function typeHint(
  node: TransformNode,
  lookup: (ref: SourceReference) => MappingSchema | null,
  item: MappingSchema | null = null,
): MappingSchema | null {
  const arg = (i: number, itemSchema = item) =>
    node.args?.[i] ? typeHint(node.args[i], lookup, itemSchema) : null;
  switch (node.op) {
    case 'literal':
      return node.literalSchema ?? literalSchemaOf(node.value);
    case 'source': {
      const base = node.source ? lookup(node.source) : null;
      return base && node.source ? schemaAt(base, node.source.jsonPointer) : null;
    }
    case 'item':
      return item ? schemaAt(item, node.jsonPointer ?? '') : null;
    case 'object':
      return {
        type: 'object',
        properties: Object.fromEntries(
          Object.entries(node.properties ?? {}).map(([k, v]) => [
            k,
            typeHint(v, lookup, item) ?? { type: 'string' },
          ]),
        ),
      };
    case 'concat':
    case 'text':
      return { type: 'string' };
    case 'add':
    case 'subtract':
    case 'multiply':
    case 'divide':
    case 'sum':
    case 'number':
      return { type: 'number' };
    case 'integer':
    case 'count':
      return { type: 'integer' };
    case 'boolean':
    case 'equal':
    case 'greater':
      return { type: 'boolean' };
    case 'coalesce':
      return arg(0);
    case 'filter':
      return arg(0);
    case 'map': {
      const source = arg(0);
      const element = source?.type === 'array' ? (source.items ?? null) : null;
      const mapped = node.args?.[1] ? typeHint(node.args[1], lookup, element) : null;
      return { type: 'array', items: mapped ?? { type: 'string' } };
    }
    default:
      return null;
  }
}

function literalSchemaOf(value: unknown): MappingSchema | null {
  if (value === null || value === undefined) return null;
  if (typeof value === 'string') return { type: 'string' };
  if (typeof value === 'boolean') return { type: 'boolean' };
  if (typeof value === 'number') return { type: Number.isInteger(value) ? 'integer' : 'number' };
  return Array.isArray(value)
    ? { type: 'array', items: { type: 'string' } }
    : { type: 'object', properties: {} };
}

const OP_LABELS: Record<TransformOp, string> = {
  literal: 'Fixed value',
  source: 'Value from a source',
  item: 'Each item',
  object: 'Build an object',
  concat: 'Join text',
  add: 'Add',
  subtract: 'Subtract',
  multiply: 'Multiply',
  divide: 'Divide',
  text: 'Convert to text',
  number: 'Convert to number',
  integer: 'Convert to whole number',
  boolean: 'Convert to yes/no',
  coalesce: 'First available value',
  map: 'Transform each item',
  filter: 'Keep matching items',
  count: 'Count items',
  sum: 'Sum of items',
  equal: 'Equals',
  greater: 'Is greater than',
};
export const opLabel = (op: TransformOp) => OP_LABELS[op] ?? op;

/** A readable one-line summary of an expression; `labelOf` turns a source reference into its human label. */
export function summarize(
  node: TransformNode | null | undefined,
  labelOf: (ref: SourceReference) => string,
  depth = 0,
): string {
  if (!node) return '—';
  if (depth > 4) return '…';
  const s = (n: TransformNode) => summarize(n, labelOf, depth + 1);
  const a = node.args ?? [];
  switch (node.op) {
    case 'literal':
      return node.value === undefined ? '—' : JSON.stringify(node.value);
    case 'source':
      return node.source
        ? labelOf(node.source) + (node.source.jsonPointer ? ` ${node.source.jsonPointer}` : '')
        : '—';
    case 'item':
      return node.jsonPointer ? `item${node.jsonPointer}` : 'item';
    case 'object':
      return `{ ${Object.entries(node.properties ?? {})
        .map(([k, v]) => `${k}: ${s(v)}`)
        .join(', ')} }`;
    case 'concat':
      return a.map(s).join(' + ');
    case 'add':
      return `${s(a[0])} + ${s(a[1])}`;
    case 'subtract':
      return `${s(a[0])} − ${s(a[1])}`;
    case 'multiply':
      return `${s(a[0])} × ${s(a[1])}`;
    case 'divide':
      return `${s(a[0])} ÷ ${s(a[1])}`;
    case 'equal':
      return `${s(a[0])} = ${s(a[1])}`;
    case 'greater':
      return `${s(a[0])} > ${s(a[1])}`;
    case 'coalesce':
      return a.map(s).join(' ?? ');
    case 'map':
      return `each of ${s(a[0])} → ${s(a[1])}`;
    case 'filter':
      return `${s(a[0])} where ${s(a[1])}`;
    default:
      return `${node.op}(${a.map(s).join(', ')})`;
  }
}

/** Replaces the node at `path` (indexes into args, or property names) and returns a new root. */
export type NodePath = (number | string)[];
export function replaceAt(root: TransformNode, path: NodePath, next: TransformNode): TransformNode {
  if (path.length === 0) return next;
  const [head, ...rest] = path;
  if (typeof head === 'number') {
    const args = [...(root.args ?? [])];
    args[head] = replaceAt(args[head], rest, next);
    return { ...root, args };
  }
  return {
    ...root,
    properties: { ...root.properties, [head]: replaceAt(root.properties![head], rest, next) },
  };
}

/** The server's literal-name rule: 1–200 characters, no control characters. Dots, hyphens and Arabic are all literal. */
export const MAX_NAME_LENGTH = 200;
export function isValidLiteralName(name: string): boolean {
  return (
    name.length > 0 && name.length <= MAX_NAME_LENGTH && !/[\u0000-\u001f\u007f-\u009f]/.test(name)
  );
}

/** A property name for an object node, taken as-is from the label (literal characters) and made unique. */
export function propertyName(label: string, taken: Iterable<string>): string {
  const base =
    label
      .replace(/[\u0000-\u001f\u007f-\u009f]/g, '')
      .trim()
      .slice(0, MAX_NAME_LENGTH - 4) || 'value';
  const used = new Set(taken);
  let name = base;
  for (let n = 2; used.has(name); n++) name = `${base}_${n}`;
  return name;
}

/** An RFC 6901 pointer: empty (whole value) or `/token/...` with only `~0`/`~1` escapes. */
export function isValidPointer(pointer: string): boolean {
  if (pointer === '') return true;
  return pointer.startsWith('/') && !/~(?![01])/.test(pointer) && pointer.length <= 1000;
}

// ── source catalog ─────────────────────────────────────────────────────────

export interface SourceGroup {
  /** Breadcrumb segment shown for this level. */
  label: string;
  /** Unique path key (segments joined) for expansion state and trackBy. */
  path: string;
  groups: SourceGroup[];
  sources: SourceDescriptor[];
  /** Descendant source count. */
  count: number;
}

/**
 * Groups sources into the readable hierarchy the server describes in `breadcrumbs`
 * (workflow › Task Type › Step › Form › field). The last breadcrumb is the source itself.
 */
export function buildSourceTree(sources: SourceDescriptor[]): SourceGroup {
  const root: SourceGroup = { label: '', path: '', groups: [], sources: [], count: 0 };
  for (const source of sources) {
    let group = root;
    group.count++;
    const trail = source.breadcrumbs.slice(0, -1);
    for (const label of trail) {
      const path = `${group.path}\u0001${label}`;
      let child = group.groups.find((g) => g.path === path);
      if (!child) {
        child = { label, path, groups: [], sources: [], count: 0 };
        group.groups.push(child);
      }
      child.count++;
      group = child;
    }
    group.sources.push(source);
  }
  return root;
}

export function filterSources(sources: SourceDescriptor[], query: string): SourceDescriptor[] {
  const q = query.trim().toLowerCase();
  if (!q) return sources;
  return sources.filter(
    (s) =>
      s.label.toLowerCase().includes(q) || s.breadcrumbs.some((b) => b.toLowerCase().includes(q)),
  );
}

export const breadcrumbText = (descriptor: SourceDescriptor) => descriptor.breadcrumbs.join(' › ');

/** Whether data is there when the invocation starts, or has to be waited for. */
export function availabilityInfo(
  availability: string,
  t: (en: string) => string = (en) => en,
): { text: string; waits: boolean; known: boolean } {
  switch (availability) {
    case 'InvocationStart':
      return { text: t('Available when the event starts'), waits: false, known: true };
    case 'SubmittedForm':
      return {
        text: t('Available once the form is submitted (may need to wait)'),
        waits: true,
        known: true,
      };
    case 'CompletedOutput':
      return {
        text: t('Available once that event completes (may need to wait)'),
        waits: true,
        known: true,
      };
    case 'SchemaRequired':
      return {
        text: t('Needs a declared type before it can be mapped'),
        waits: false,
        known: false,
      };
    default:
      return { text: availability, waits: false, known: true };
  }
}

/** A catalog descriptor for a stored reference, ignoring the pointer (and the selector when a related catalog is used). */
export function findDescriptor(
  sources: SourceDescriptor[],
  ref: SourceReference,
): SourceDescriptor | undefined {
  const key = sourceKey({ ...ref, selectorId: null });
  return sources.find((s) => sourceKey(s.reference) === key);
}

/** A source that matches everything but the version pins — the current replacement for an out-of-date pin. */
export function repinCandidate(
  sources: SourceDescriptor[],
  ref: SourceReference,
): SourceDescriptor | undefined {
  return sources.find((s) => {
    const r = s.reference;
    return (
      r.workflowDefinitionId === ref.workflowDefinitionId &&
      r.nodeKey === ref.nodeKey &&
      r.kind === ref.kind &&
      r.fieldKey === ref.fieldKey &&
      (r.formStepId ?? '') === (ref.formStepId ?? '') &&
      (r.formId ?? '') === (ref.formId ?? '') &&
      (r.workflowVersionId !== ref.workflowVersionId ||
        (r.formVersion ?? 0) !== (ref.formVersion ?? 0))
    );
  });
}

export interface BrokenReference {
  reference: SourceReference;
  /** Where it is used, e.g. "Input: query customerId". */
  where: string;
  /** Replacement with identical identity but the current pins, when the catalog has one. */
  candidate?: SourceDescriptor;
}

/** Fingerprint of a configuration's content, to tell "same content, new object" from a real change. */
export const configFingerprint = (value: unknown) => JSON.stringify(value ?? null);

// ── whole-configuration helpers ───────────────────────────────────────────

/** Every expression in a configuration, tagged with where it lives (for sampling, repinning and diagnostics). */
export function configExpressions(
  config: DesignerMappingConfiguration,
): { where: string; node: TransformNode }[] {
  return [
    ...config.selectors.map((s) => ({ where: `selector ${s.id}`, node: s.keyValue })),
    ...config.selectors.flatMap((s) =>
      s.startedByUserId ? [{ where: `selector ${s.id}`, node: s.startedByUserId }] : [],
    ),
    ...config.inputs.map((b) => ({ where: `input ${b.location} ${b.name}`, node: b.value })),
    ...config.outputs.map((o) => ({ where: `output ${o.name}`, node: o.value })),
    ...config.stepBindings.map((b) => ({ where: `step field ${b.fieldKey}`, node: b.value })),
    ...config.actions.map((a) => ({ where: `action ${a.id}`, node: a.inputs })),
  ];
}

/** Unique whole-field sources (pointer-free) used by a configuration, excluding values the event produces itself. */
export function sampleSources(config: DesignerMappingConfiguration): SourceReference[] {
  const seen = new Map<string, SourceReference>();
  for (const { node } of configExpressions(config))
    for (const ref of collectSources(node)) {
      const whole = { ...ref, jsonPointer: '' };
      seen.set(sourceKey(whole), whole);
    }
  return [...seen.values()];
}

/** A plausible value for a schema, used to pre-fill the Test stage. Test-only: it is never written to the configuration. */
export function exampleFor(schema: MappingSchema | null | undefined, depth = 0): unknown {
  if (!schema || depth > 6) return null;
  if (schema.default !== undefined) return schema.default;
  switch (schema.type) {
    case 'string':
      return 'sample';
    case 'number':
      return 1.5;
    case 'integer':
      return 1;
    case 'boolean':
      return true;
    case 'array':
      return [exampleFor(schema.items, depth + 1)];
    case 'object':
      return Object.fromEntries(
        Object.entries(schema.properties ?? {}).map(([k, v]) => [k, exampleFor(v, depth + 1)]),
      );
    default:
      return null;
  }
}

/** Required parameters/body without a binding — the same rule the server enforces at validation. */
export function unmappedRequired(config: DesignerMappingConfiguration): string[] {
  const endpoint = config.endpoint;
  if (!endpoint) return [];
  const missing: string[] = [];
  for (const p of endpoint.parameters)
    if (
      p.schema.required &&
      p.schema.default === undefined &&
      !config.inputs.some((b) => b.location === p.location && b.name === p.name)
    )
      missing.push(`${p.location}:${p.name}`);
  if (
    endpoint.body?.required &&
    endpoint.body.default === undefined &&
    !config.inputs.some((b) => b.location === 'body')
  )
    missing.push('body');
  return missing;
}

/** Applies `map` to every source reference of every expression; nothing else in the configuration changes. */
export function mapConfigSources(
  config: DesignerMappingConfiguration,
  map: (ref: SourceReference) => SourceReference,
): DesignerMappingConfiguration {
  const node = (n: TransformNode) => mapSources(n, map);
  return {
    ...config,
    selectors: config.selectors.map((s) => ({
      ...s,
      keyValue: node(s.keyValue),
      ...(s.startedByUserId ? { startedByUserId: node(s.startedByUserId) } : {}),
    })),
    inputs: config.inputs.map((b) => ({ ...b, value: node(b.value) })),
    outputs: config.outputs.map((o) => ({ ...o, value: node(o.value) })),
    stepBindings: config.stepBindings.map((b) => ({ ...b, value: node(b.value) })),
    actions: config.actions.map((a) => ({ ...a, inputs: node(a.inputs) })),
  };
}
