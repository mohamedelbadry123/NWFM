import { hasLegacyLocationConflict, hasRequiredLocation, normalizeWorkspaceSettings, WorkspaceSettings } from './workflow-workspace.service';

/** Compare saved units and their ancestry, never ancestor coverage or overlap. */
export function sameWorkflowScope(parent: WorkspaceSettings | null, child: WorkspaceSettings | null): boolean {
  const keys = (settings: WorkspaceSettings | null): string[] | null => {
    if (!settings) return null;
    const s = normalizeWorkspaceSettings(settings);
    if (!s.organizationScopes?.length || !hasRequiredLocation(s) || hasLegacyLocationConflict(s)
      || s.clusterCode || s.cbuCode || s.branchCode || s.operationAreaCode) return null;
    return [...new Set(s.organizationScopes.map(scope => JSON.stringify([
      scope.level, scope.code.toUpperCase(), scope.clusterCode.toUpperCase(), scope.cbuCode?.toUpperCase() || null,
    ])))].sort();
  };
  const a = keys(parent), b = keys(child);
  return !!a && !!b && a.length === b.length && a.every((key, i) => key === b[i]);
}

export function matchingChild(parent: WorkspaceSettings | null, childJson: string, taskTypeId?: string): boolean {
  try {
    const child = JSON.parse(childJson) as WorkspaceSettings;
    return !!taskTypeId && child?.kind === 'Child' && child.taskTypeId?.toLowerCase() === taskTypeId.toLowerCase()
      && sameWorkflowScope(parent, child);
  } catch { return false; }
}
