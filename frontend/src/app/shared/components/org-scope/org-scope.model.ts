export const ORG_SCOPE_LEVELS = {
  cluster: 'Cluster',
  cbu: 'Cbu',
  branch: 'Branch',
  operationArea: 'OperationArea',
} as const;

export type OrgScopeLevel = (typeof ORG_SCOPE_LEVELS)[keyof typeof ORG_SCOPE_LEVELS];

export const LEVEL_ORDER: readonly OrgScopeLevel[] = [
  ORG_SCOPE_LEVELS.cluster,
  ORG_SCOPE_LEVELS.cbu,
  ORG_SCOPE_LEVELS.branch,
  ORG_SCOPE_LEVELS.operationArea,
];

const LEVEL_PARENT: Readonly<Partial<Record<OrgScopeLevel, OrgScopeLevel>>> = {
  [ORG_SCOPE_LEVELS.cbu]: ORG_SCOPE_LEVELS.cluster,
  [ORG_SCOPE_LEVELS.branch]: ORG_SCOPE_LEVELS.cbu,
  [ORG_SCOPE_LEVELS.operationArea]: ORG_SCOPE_LEVELS.cbu,
};

export function isLevelUnder(level: OrgScopeLevel, ancestor: OrgScopeLevel): boolean {
  for (let current = LEVEL_PARENT[level]; current; current = LEVEL_PARENT[current]) {
    if (current === ancestor) {
      return true;
    }
  }
  return false;
}

export const LEVEL_LABEL_KEYS: Readonly<Record<OrgScopeLevel, string>> = {
  [ORG_SCOPE_LEVELS.cluster]: 'org.cluster',
  [ORG_SCOPE_LEVELS.cbu]: 'org.cbu',
  [ORG_SCOPE_LEVELS.branch]: 'org.branch',
  [ORG_SCOPE_LEVELS.operationArea]: 'org.operationArea',
};

export interface OrgLocation {
  clusterCode: string | null;
  cbuCode: string | null;
  branchCode: string | null;
  operationAreaCode: string | null;
}

export const EMPTY_ORG_LOCATION: OrgLocation = {
  clusterCode: null,
  cbuCode: null,
  branchCode: null,
  operationAreaCode: null,
};

export function isEmptyLocation(location: OrgLocation | null | undefined): boolean {
  return !location?.clusterCode && !location?.cbuCode && !location?.branchCode && !location?.operationAreaCode;
}

/** One coverage row: a territory, a department code, or both. */
export interface OrgScopeAssignment {
  level?: string | null;
  code?: string | null;
  departmentId?: string | null;
}
