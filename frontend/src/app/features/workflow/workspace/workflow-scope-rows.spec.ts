import { describe, expect, it } from 'vitest';
import { LookupItem } from '@core/lookups/lookups.service';
import { WorkflowScopeLookups, buildWorkflowScopes } from './workflow-scope-selection';
import {
  EMPTY_SCOPE_DRAFT,
  changeDraft,
  coveringRow,
  draftOptions,
  draftProblem,
  effectiveScopeRows,
  expandScopeRow,
  expandScopeRows,
  isDraftStarted,
  rowFromDraft,
  scopeRowsFromScopes,
} from './workflow-scope-rows';
import { WorkflowScopeRow } from './workflow-workspace.service';

const item = (code: string, parentCode?: string): LookupItem => ({
  id: code,
  code,
  parentCode,
  nameEn: code,
  nameAr: code,
  isActive: true,
});
const lookups: WorkflowScopeLookups = {
  clusters: [item('A'), item('B')],
  cbus: [item('A1', 'A'), item('A2', 'A'), item('B1', 'B')],
  branches: [item('AB1', 'A1'), item('BB1', 'B1')],
  areas: [item('AA1', 'A1'), item('BA1', 'B1')],
};
const row = (
  id: string,
  clusterCode: string,
  extra: Partial<WorkflowScopeRow> = {},
): WorkflowScopeRow => ({
  id,
  clusterCode,
  ...extra,
});

describe('Scope entries', () => {
  it('expands each entry to its deepest level, with Branch and Operation Area as a union', () => {
    expect(expandScopeRow(row('1', 'A'))).toEqual([
      { level: 'Cluster', code: 'A', clusterCode: 'A' },
    ]);
    expect(expandScopeRow(row('2', 'A', { cbuCode: 'A1' }))).toEqual([
      { level: 'Cbu', code: 'A1', clusterCode: 'A', cbuCode: 'A1' },
    ]);
    expect(
      expandScopeRow(
        row('3', 'A', { cbuCode: 'A1', branchCode: 'AB1', operationAreaCode: 'AA1' }),
      ).map((s) => s.level),
    ).toEqual(['Branch', 'OperationArea']);
  });

  it('allows the same Cluster in several entries with different selections, and keeps every scope', () => {
    const rows = [
      row('1', 'A', { cbuCode: 'A1' }),
      row('2', 'A', { cbuCode: 'A2' }),
      row('3', 'B'),
    ];
    expect(expandScopeRows(rows).map((s) => s.code)).toEqual(['A1', 'A2', 'B']);
  });

  it('keeps overlapping broader and narrower entries without dropping either, and flags the covered one', () => {
    const rows = [
      row('whole', 'A'),
      row('part', 'A', { cbuCode: 'A1' }),
      row('leaf', 'A', { cbuCode: 'A1', branchCode: 'AB1' }),
    ];
    expect(expandScopeRows(rows).map((s) => s.level)).toEqual(['Cluster', 'Cbu', 'Branch']);
    expect(coveringRow(rows, rows[1])?.id).toBe('whole');
    expect(coveringRow(rows, rows[2])?.id).toBe('whole');
    expect(coveringRow(rows, rows[0])).toBeUndefined();
    expect(coveringRow([rows[1], row('x', 'A', { cbuCode: 'A2' })], rows[1])).toBeUndefined();
  });

  it('collapses an exact repeat in the canonical list but never in the entries', () => {
    const rows = [
      row('1', 'A', { cbuCode: 'A1', branchCode: 'AB1' }),
      row('2', 'A', { cbuCode: 'A1', branchCode: 'AB1', operationAreaCode: 'AA1' }),
    ];
    expect(expandScopeRows(rows).map((s) => s.code)).toEqual(['AB1', 'AA1']);
    expect(rows).toHaveLength(2);
  });

  it('produces the same canonical scopes as the previous selection builder for a single entry', () => {
    const entry = row('1', 'A', { cbuCode: 'A1', branchCode: 'AB1', operationAreaCode: 'AA1' });
    expect(expandScopeRows([entry])).toEqual(
      buildWorkflowScopes(
        { clusters: ['A'], cbus: ['A1'], branches: ['AB1'], areas: ['AA1'] },
        lookups,
      ),
    );
  });

  it('rejects exact duplicates (ignoring case) but not the entry being edited', () => {
    const rows = [row('1', 'A', { cbuCode: 'A1' })];
    expect(draftProblem(rows, { ...EMPTY_SCOPE_DRAFT, clusterCode: 'a', cbuCode: 'a1' })).toBe(
      'duplicate',
    );
    expect(
      draftProblem(rows, { ...EMPTY_SCOPE_DRAFT, clusterCode: 'A', cbuCode: 'A1' }, '1'),
    ).toBeNull();
    expect(
      draftProblem(rows, { ...EMPTY_SCOPE_DRAFT, clusterCode: 'A', cbuCode: 'A2' }),
    ).toBeNull();
    expect(draftProblem(rows, { ...EMPTY_SCOPE_DRAFT })).toBe('cluster');
  });

  it('only offers choices beneath the chosen parent and clears descendants that no longer fit', () => {
    let draft = changeDraft(EMPTY_SCOPE_DRAFT, 'clusterCode', 'A');
    expect(draftOptions(lookups, draft, 'cbuCode').map((c) => c.code)).toEqual(['A1', 'A2']);
    expect(draftOptions(lookups, draft, 'branchCode')).toEqual([]);
    draft = changeDraft(changeDraft(draft, 'cbuCode', 'A1'), 'branchCode', 'AB1');
    draft = changeDraft(draft, 'operationAreaCode', 'AA1');
    expect(draftOptions(lookups, draft, 'branchCode').map((c) => c.code)).toEqual(['AB1']);
    expect(draftOptions(lookups, draft, 'operationAreaCode').map((c) => c.code)).toEqual(['AA1']);
    expect(changeDraft(draft, 'cbuCode', 'A2')).toMatchObject({
      clusterCode: 'A',
      cbuCode: 'A2',
      branchCode: '',
      operationAreaCode: '',
    });
    expect(changeDraft(draft, 'clusterCode', 'B')).toMatchObject({
      clusterCode: 'B',
      cbuCode: '',
      branchCode: '',
      operationAreaCode: '',
    });
  });

  it('treats any started entry as unfinished', () => {
    expect(isDraftStarted(EMPTY_SCOPE_DRAFT)).toBe(false);
    expect(isDraftStarted({ ...EMPTY_SCOPE_DRAFT, clusterCode: 'A' })).toBe(true);
  });

  it('builds a row without levels that are not under a CBU', () => {
    expect(
      rowFromDraft(
        { clusterCode: 'A', cbuCode: '', branchCode: 'AB1', operationAreaCode: '' },
        'x',
      ),
    ).toEqual({ id: 'x', clusterCode: 'A' });
  });
});

describe('Older saves and reloads', () => {
  const scopes = buildWorkflowScopes(
    { clusters: ['A', 'B'], cbus: ['A1'], branches: ['AB1'], areas: ['AA1'] },
    lookups,
  );

  it('reads scopes saved without entries as one entry per scope, covering exactly the saved scopes', () => {
    const rows = effectiveScopeRows(scopes);
    expect(expandScopeRows(rows)).toEqual(scopes);
    expect(rows.map((r) => r.id)).toEqual(['saved-1', 'saved-2', 'saved-3']);
    expect(scopeRowsFromScopes([...scopes, ...scopes])).toHaveLength(3);
  });

  it('shows saved entries as authored while they still describe the saved scopes', () => {
    const authored = [
      row('r1', 'A', { cbuCode: 'A1', branchCode: 'AB1', operationAreaCode: 'AA1' }),
      row('r2', 'B'),
    ];
    expect(effectiveScopeRows(expandScopeRows(authored), authored)).toBe(authored);
  });

  it('ignores stale entries: the canonical scopes are authoritative', () => {
    const stale = [row('r1', 'A')];
    const rows = effectiveScopeRows(scopes, stale);
    expect(rows).not.toBe(stale);
    expect(expandScopeRows(rows)).toEqual(scopes);
  });
});
