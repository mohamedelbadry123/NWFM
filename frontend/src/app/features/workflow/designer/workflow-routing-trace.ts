import type { RoutingRuleStatus, RoutingRuleTrace, RoutingTrace } from './workflow-routing.service';

const STATUSES: readonly RoutingRuleStatus[] = ['Checked', 'Matched', 'Skipped'];

const asStatus = (value: unknown): RoutingRuleStatus | null =>
  STATUSES.find((status) => status === value) ?? null;

/**
 * Reads the per-route trace the backend records for a Condition (RoutingDecision.rules). Live audit payloads use
 * the server's default casing for these entries (`TransitionKey`), the HTTP API uses camelCase — both are read.
 * Anything that is not a well-formed entry is dropped rather than guessed at.
 */
export function normalizeRuleTrace(raw: unknown): RoutingRuleTrace[] {
  if (!Array.isArray(raw)) return [];
  const entries: RoutingRuleTrace[] = [];
  for (const item of raw) {
    if (!item || typeof item !== 'object') continue;
    const record = item as Record<string, unknown>;
    const key = record['transitionKey'] ?? record['TransitionKey'];
    const status = asStatus(record['status'] ?? record['Status']);
    const problem = record['problem'] ?? record['Problem'];
    if (typeof key !== 'string' || !status) continue;
    entries.push({
      transitionKey: key,
      status,
      problem: typeof problem === 'string' ? problem : null,
    });
  }
  return entries;
}

/** The rule trace inside a history event's payload, when it has one. */
export function ruleTraceFromPayload(payloadJson?: string | null): RoutingRuleTrace[] {
  if (!payloadJson || !payloadJson.includes('"rules"')) return [];
  try {
    const payload = JSON.parse(payloadJson) as Record<string, unknown> | null;
    return normalizeRuleTrace(payload?.['rules']);
  } catch {
    return [];
  }
}

/**
 * Whether a simulation trace entry is one route's line rather than the Condition's own summary. The server marks a
 * route's line with its `rule` ({transitionKey, status, problem}); an errored visited route is `Checked` with a
 * non-null `problem`. Nothing is inferred from positions or from the English explanation.
 */
export function isRuleLine(item: RoutingTrace): boolean {
  return !!item.rule && !!asStatus(item.rule.status);
}

export type SimulationRow =
  | { kind: 'step'; item: RoutingTrace; number: number }
  | { kind: 'rules'; nodeKey: string; branch?: string; rules: RoutingRuleTrace[]; number: number };

/**
 * Folds the simulation trace into display rows: consecutive route lines of one Condition become one block of
 * ordered rules; every other entry stays a step. Numbering counts rows, as the flat trace used to.
 */
export function simulationRows(trace: RoutingTrace[]): SimulationRow[] {
  const rows: SimulationRow[] = [];
  for (const item of trace) {
    if (isRuleLine(item)) {
      const last = rows[rows.length - 1];
      const rule = ruleOfLine(item);
      // A new Condition visit starts at route 1 even when it follows another visit of the same Condition.
      const continues =
        last?.kind === 'rules' &&
        last.nodeKey === item.nodeKey &&
        last.branch === item.branch &&
        (item.ruleOrder ?? last.rules.length + 1) > last.rules.length;
      if (last?.kind === 'rules' && continues) {
        last.rules.push(rule);
        continue;
      }
      rows.push({
        kind: 'rules',
        nodeKey: item.nodeKey,
        branch: item.branch,
        rules: [rule],
        number: rows.length + 1,
      });
      continue;
    }
    rows.push({ kind: 'step', item, number: rows.length + 1 });
  }
  return rows;
}

function ruleOfLine(item: RoutingTrace): RoutingRuleTrace {
  const rule = item.rule!;
  return { transitionKey: rule.transitionKey, status: rule.status, problem: rule.problem ?? null };
}
