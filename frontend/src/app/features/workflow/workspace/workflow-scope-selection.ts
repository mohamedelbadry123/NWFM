import { LookupItem } from '@core/lookups/lookups.service';
import { WorkflowOrganizationScope } from './workflow-workspace.service';

export interface WorkflowScopeSelection { clusters: string[]; cbus: string[]; branches: string[]; areas: string[] }
export interface WorkflowScopeLookups { clusters: LookupItem[]; cbus: LookupItem[]; branches: LookupItem[]; areas: LookupItem[] }

/** Each parent's selected descendants narrow only that parent. Sibling leaf levels form a union. */
export function buildWorkflowScopes(selection: WorkflowScopeSelection, lookups: WorkflowScopeLookups): WorkflowOrganizationScope[] {
  const result: WorkflowOrganizationScope[] = [];
  for (const clusterCode of new Set(selection.clusters)) {
    const cbus = lookups.cbus.filter(c => c.parentCode === clusterCode && selection.cbus.includes(c.code));
    if (!cbus.length) result.push({ level: 'Cluster', code: clusterCode, clusterCode });
    for (const cbu of cbus) {
      const branches = lookups.branches.filter(b => b.parentCode === cbu.code && selection.branches.includes(b.code));
      const areas = lookups.areas.filter(a => a.parentCode === cbu.code && selection.areas.includes(a.code));
      if (!branches.length && !areas.length) result.push({ level: 'Cbu', code: cbu.code, clusterCode, cbuCode: cbu.code });
      for (const branch of branches) result.push({ level: 'Branch', code: branch.code, clusterCode, cbuCode: cbu.code });
      for (const area of areas) result.push({ level: 'OperationArea', code: area.code, clusterCode, cbuCode: cbu.code });
    }
  }
  return result;
}

export function selectionFromScopes(scopes: WorkflowOrganizationScope[]): WorkflowScopeSelection {
  return {
    clusters: [...new Set(scopes.map(s => s.clusterCode))],
    cbus: [...new Set(scopes.map(s => s.cbuCode).filter((c): c is string => !!c))],
    branches: scopes.filter(s => s.level === 'Branch').map(s => s.code),
    areas: scopes.filter(s => s.level === 'OperationArea').map(s => s.code),
  };
}

export function pruneWorkflowSelection(selection: WorkflowScopeSelection, lookups: WorkflowScopeLookups): WorkflowScopeSelection {
  const cbus = selection.cbus.filter(code => lookups.cbus.some(c => c.code === code && selection.clusters.includes(c.parentCode || '')));
  return { ...selection, cbus,
    branches: selection.branches.filter(code => lookups.branches.some(b => b.code === code && cbus.includes(b.parentCode || ''))),
    areas: selection.areas.filter(code => lookups.areas.some(a => a.code === code && cbus.includes(a.parentCode || ''))),
  };
}
