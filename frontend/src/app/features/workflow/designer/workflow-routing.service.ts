import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ApiConfiguration } from '@shared/models/api-configuration';
import type { RoutingGroup } from '../workspace/workflow-form-routing.types';

export interface RoutingSource {
  key: string;
  nodeKey: string;
  field: string;
  type: string;
  label: string;
  formId?: string;
  formVersion?: number;
  sensitive?: boolean;
  choices?: { value: string; labelEn: string; labelAr: string }[];
}
export interface RoutingRule {
  transitionKey: string;
  always: boolean;
  when: RoutingGroup;
}
export interface RoutingConfiguration {
  contractVersion: number;
  matchMode: 'Unique' | 'First' | 'All';
  routes: RoutingRule[];
  sources: RoutingSource[];
  fallbackTransitionKey?: string | null;
  joinNodeKey?: string;
  waitFor: 'All' | 'Any';
  remainingBranches: 'Continue' | 'Cancel';
  deadlineSeconds?: number | null;
  failureTransitionKey?: string | null;
}
export interface RoutingDiagnostic {
  code: string;
  message: string;
  nodeKey?: string;
  field?: string;
}
/** One route's part in a Condition's decision, from the backend's RoutingDecision.rules. */
export type RoutingRuleStatus = 'Checked' | 'Matched' | 'Skipped';
export interface RoutingRuleTrace {
  transitionKey: string;
  status: RoutingRuleStatus;
  problem?: string | null;
}
export interface RoutingTrace {
  nodeKey: string;
  status: string;
  explanation: string;
  branch?: string;
  /** Set when this entry is one route's line of a Condition: the route itself, never parsed out of `explanation`. */
  rule?: RoutingRuleTrace | null;
  /** The route's 1-based position among the Condition's routes (the order they were tried). */
  ruleOrder?: number | null;
}
export interface RoutingContext {
  sources: RoutingSource[];
  nodes: { key: string; name: string; type: string }[];
  connections: { key: string; from: string; to: string }[];
  enabled: boolean;
  revision: string;
}
export interface RoutingSimulation {
  trace: RoutingTrace[];
  waiting: RoutingTrace[];
  diagnostics: RoutingDiagnostic[];
}
export interface RoutingProgress {
  nodeKey: string;
  name: string;
  status: string;
  explanation: string;
  activationId: string;
  waitFor: string;
  met: number;
  total: number;
  branches: { tokenId: string; name: string; status: string }[];
  skipped: string[];
  deadlineAt?: string;
  releasedAt?: string;
}
/**
 * What a saved configuration without these settings means: the legacy contract (version 1), whose Conditions
 * check every route. Use it to READ stored or missing configuration so that opening a workflow never changes
 * how it behaves; never use it to create new work — see {@link newConditionRouting}.
 */
export const routingDefaults = (): RoutingConfiguration => ({
  contractVersion: 1,
  matchMode: 'Unique',
  routes: [],
  sources: [],
  waitFor: 'All',
  remainingBranches: 'Continue',
});
/** A new Condition: ordered routes on contract version 2 — routes are tried top to bottom and the first match wins. */
export const newConditionRouting = (): RoutingConfiguration => ({
  ...routingDefaults(),
  contractVersion: 2,
  matchMode: 'First',
});
/** Ordered routing: the contract the backend evaluates one route at a time and stops at the first match. */
export const isOrderedRouting = (
  config: Pick<RoutingConfiguration, 'contractVersion' | 'matchMode'>,
) => config.contractVersion === 2 && config.matchMode === 'First';
export const isRoutingGate = (type: string) =>
  ['ExclusiveGateway', 'ParallelGateway', 'InclusiveGateway', 'JoinGateway'].includes(type);

@Injectable({ providedIn: 'root' })
export class WorkflowRoutingService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiConfiguration);
  private url(definition: string, version: string) {
    return `${this.api.rootUrl}/api/workflow/definitions/${definition}/versions/${version}/routing`;
  }
  context(definition: string, version: string) {
    return this.http.get<RoutingContext>(`${this.url(definition, version)}/context`);
  }
  prepare(definition: string, version: string, expectedRevision: string) {
    return this.http.post<string>(`${this.url(definition, version)}/prepare`, { expectedRevision });
  }
  validateDesign(
    definition: string,
    version: string,
    xmlContent: string,
    designerJson: string,
    editId: number,
  ) {
    return this.http.post<{
      editId: number;
      revision: string;
      result: { isValid: boolean; errors: RoutingDiagnostic[]; warnings: RoutingDiagnostic[] };
    }>(`${this.url(definition, version)}/validate-design`, { xmlContent, designerJson, editId });
  }
  preview(definition: string, version: string) {
    return this.http.get<{
      canUpgrade: boolean;
      diagnostics: RoutingDiagnostic[];
      conditionsAdded: number;
      revision: string;
    }>(`${this.url(definition, version)}/upgrade-preview`);
  }
  upgrade(definition: string, version: string, expectedRevision: string) {
    return this.http.post<string>(`${this.url(definition, version)}/upgrade`, { expectedRevision });
  }
  simulate(
    definition: string,
    version: string,
    signals: {
      nodeKey: string;
      values?: Record<string, unknown>;
      outcome: string;
      advanceSeconds?: number;
      branch?: string;
    }[],
    inputs: Record<string, unknown> = {},
  ) {
    return this.http.post<RoutingSimulation>(`${this.url(definition, version)}/simulate`, {
      signals,
      inputs,
    });
  }
  progress(instanceId: string) {
    return this.http.get<RoutingProgress[]>(
      `${this.api.rootUrl}/api/workflow/workspace/instances/${instanceId}/routing`,
    );
  }
}
