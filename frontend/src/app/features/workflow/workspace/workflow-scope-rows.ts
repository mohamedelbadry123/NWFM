import { LookupItem } from '@core/lookups/lookups.service';
import { WorkflowOrganizationScope, WorkflowScopeRow } from './workflow-workspace.service';
import { WorkflowScopeLookups } from './workflow-scope-selection';

/** What the entry form holds while an entry is being built or edited. */
export interface WorkflowScopeDraft {
  clusterCode: string;
  cbuCode: string;
  branchCode: string;
  operationAreaCode: string;
}
export type ScopeDraftLevel = keyof WorkflowScopeDraft;
export type ScopeDraftProblem = 'cluster' | 'duplicate';

export const EMPTY_SCOPE_DRAFT: WorkflowScopeDraft = {
  clusterCode: '',
  cbuCode: '',
  branchCode: '',
  operationAreaCode: '',
};

/** The lookup list each level of the entry form chooses from, and the level whose choice filters it. */
export const SCOPE_DRAFT_LEVELS: {
  key: ScopeDraftLevel;
  list: keyof WorkflowScopeLookups;
  parent?: ScopeDraftLevel;
  en: string;
  ar: string;
}[] = [
  { key: 'clusterCode', list: 'clusters', en: 'Cluster', ar: 'القطاع' },
  { key: 'cbuCode', list: 'cbus', parent: 'clusterCode', en: 'CBU', ar: 'وحدة الأعمال' },
  { key: 'branchCode', list: 'branches', parent: 'cbuCode', en: 'Branch', ar: 'الفرع' },
  {
    key: 'operationAreaCode',
    list: 'areas',
    parent: 'cbuCode',
    en: 'Operation Area',
    ar: 'منطقة العمليات',
  },
];

const same = (a?: string | null, b?: string | null) =>
  (a ?? '').toUpperCase() === (b ?? '').toUpperCase();

export function draftFromRow(row: WorkflowScopeRow): WorkflowScopeDraft {
  return {
    clusterCode: row.clusterCode,
    cbuCode: row.cbuCode ?? '',
    branchCode: row.branchCode ?? '',
    operationAreaCode: row.operationAreaCode ?? '',
  };
}

/** True once anything has been chosen: such an entry must be added or cancelled, never dropped. */
export function isDraftStarted(draft: WorkflowScopeDraft): boolean {
  return !!(draft.clusterCode || draft.cbuCode || draft.branchCode || draft.operationAreaCode);
}

/** Choosing a level clears every level beneath it that no longer belongs to the new parent. */
export function changeDraft(
  draft: WorkflowScopeDraft,
  level: ScopeDraftLevel,
  value: string,
): WorkflowScopeDraft {
  const next = { ...draft, [level]: value };
  if (level === 'clusterCode')
    return { ...next, cbuCode: '', branchCode: '', operationAreaCode: '' };
  if (level === 'cbuCode') return { ...next, branchCode: '', operationAreaCode: '' };
  return next;
}

/** Choices for one level of the entry form. Branch and Operation Area are siblings under the chosen CBU. */
export function draftOptions(
  lookups: WorkflowScopeLookups,
  draft: WorkflowScopeDraft,
  level: ScopeDraftLevel,
): LookupItem[] {
  const spec = SCOPE_DRAFT_LEVELS.find((l) => l.key === level)!;
  if (!spec.parent) return lookups[spec.list];
  const parent = draft[spec.parent];
  return parent ? lookups[spec.list].filter((item) => same(item.parentCode, parent)) : [];
}

/** The scopes one entry stands for: its deepest level, with Branch and Operation Area as a union. */
export function expandScopeRow(row: Omit<WorkflowScopeRow, 'id'>): WorkflowOrganizationScope[] {
  const { clusterCode, cbuCode, branchCode, operationAreaCode } = row;
  if (!cbuCode) return [{ level: 'Cluster', code: clusterCode, clusterCode }];
  const scopes: WorkflowOrganizationScope[] = [];
  if (branchCode) scopes.push({ level: 'Branch', code: branchCode, clusterCode, cbuCode });
  if (operationAreaCode)
    scopes.push({ level: 'OperationArea', code: operationAreaCode, clusterCode, cbuCode });
  return scopes.length ? scopes : [{ level: 'Cbu', code: cbuCode, clusterCode, cbuCode }];
}

const scopeKey = (s: WorkflowOrganizationScope) =>
  [s.level, s.code, s.clusterCode, s.cbuCode ?? ''].map((part) => part.toUpperCase()).join('|');

/** The canonical scope list for a set of entries: every entry's scopes in order, repeats collapsed, nothing else. */
export function expandScopeRows(rows: WorkflowScopeRow[]): WorkflowOrganizationScope[] {
  const seen = new Set<string>();
  return rows.flatMap(expandScopeRow).filter((scope) => {
    const key = scopeKey(scope);
    return seen.has(key) ? false : !!seen.add(key);
  });
}

export function scopeRowKey(row: Omit<WorkflowScopeRow, 'id'>): string {
  return [row.clusterCode, row.cbuCode, row.branchCode, row.operationAreaCode]
    .map((c) => (c ?? '').toUpperCase())
    .join('|');
}

export function isDuplicateScopeRow(
  rows: WorkflowScopeRow[],
  draft: WorkflowScopeDraft,
  ignoreId?: string,
): boolean {
  const key = scopeRowKey(draft);
  return rows.some((row) => row.id !== ignoreId && scopeRowKey(row) === key);
}

export function draftProblem(
  rows: WorkflowScopeRow[],
  draft: WorkflowScopeDraft,
  ignoreId?: string,
): ScopeDraftProblem | null {
  if (!draft.clusterCode) return 'cluster';
  return isDuplicateScopeRow(rows, draft, ignoreId) ? 'duplicate' : null;
}

export function rowFromDraft(draft: WorkflowScopeDraft, id: string): WorkflowScopeRow {
  const row: WorkflowScopeRow = { id, clusterCode: draft.clusterCode };
  if (draft.cbuCode) {
    row.cbuCode = draft.cbuCode;
    if (draft.branchCode) row.branchCode = draft.branchCode;
    if (draft.operationAreaCode) row.operationAreaCode = draft.operationAreaCode;
  }
  return row;
}

/**
 * Entries for a canonical scope list that carries no (or stale) grouping: one entry per scope. Coverage is
 * identical to the saved scopes, and nothing is written back until the author changes something.
 */
export function scopeRowsFromScopes(scopes: WorkflowOrganizationScope[]): WorkflowScopeRow[] {
  const seen = new Set<string>();
  const rows: WorkflowScopeRow[] = [];
  for (const scope of scopes) {
    const key = scopeKey(scope);
    if (seen.has(key)) continue;
    seen.add(key);
    const row: WorkflowScopeRow = {
      id: 'saved-' + (rows.length + 1),
      clusterCode: scope.clusterCode,
    };
    if (scope.level !== 'Cluster' && scope.cbuCode) row.cbuCode = scope.cbuCode;
    if (scope.level === 'Branch') row.branchCode = scope.code;
    if (scope.level === 'OperationArea') row.operationAreaCode = scope.code;
    rows.push(row);
  }
  return rows;
}

/** Saved entries are used only while they still expand to exactly the saved scopes; otherwise the scopes win. */
export function effectiveScopeRows(
  scopes: WorkflowOrganizationScope[],
  rows?: WorkflowScopeRow[],
): WorkflowScopeRow[] {
  if (rows?.length) {
    const authored = new Set(expandScopeRows(rows).map(scopeKey));
    const saved = new Set(scopes.map(scopeKey));
    if (authored.size === saved.size && [...authored].every((key) => saved.has(key))) return rows;
  }
  return scopeRowsFromScopes(scopes);
}

const scopeCovers = (outer: WorkflowOrganizationScope, inner: WorkflowOrganizationScope) =>
  same(outer.clusterCode, inner.clusterCode) &&
  (outer.level === 'Cluster' ||
    (same(outer.cbuCode, inner.cbuCode) &&
      (outer.level === 'Cbu' || (outer.level === inner.level && same(outer.code, inner.code)))));

/**
 * Another entry that already covers everything this one does — a whole Cluster over one of its CBUs, a CBU over
 * its Branches. Both stay saved and listed; this only lets the editor say the narrower one adds no coverage.
 */
export function coveringRow(
  rows: WorkflowScopeRow[],
  row: WorkflowScopeRow,
): WorkflowScopeRow | undefined {
  const inner = expandScopeRow(row);
  return rows.find(
    (other) =>
      other.id !== row.id &&
      scopeRowKey(other) !== scopeRowKey(row) &&
      inner.every((scope) => expandScopeRow(other).some((outer) => scopeCovers(outer, scope))),
  );
}
