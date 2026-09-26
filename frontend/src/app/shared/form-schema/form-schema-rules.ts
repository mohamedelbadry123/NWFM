import {
  RULE_MATCH,
  RULE_OPERATORS,
  type RuleCondition,
  type RuleGroup,
} from './form-schema.types';

/** The flat preview model, keyed by `data_name`. */
export type RuleModel = Record<string, unknown>;

export type RulePredicate = (model: RuleModel) => boolean;

/**
 * Compile a builder `RuleGroup` into a predicate over the flat form model.
 *
 * Returns `null` when the group has nothing to evaluate — callers use that to
 * skip registering a Formly expression at all (an always-true expression would
 * still cost a check on every model change).
 */
export function buildRulePredicate(group: RuleGroup | null | undefined): RulePredicate | null {
  const conditions = (group?.conditions ?? []).filter((condition) => !!condition.field);
  if (conditions.length === 0) {
    return null;
  }

  const predicates = conditions.map((condition) => conditionPredicate(condition));

  return group?.match === RULE_MATCH.Any
    ? (model) => predicates.some((predicate) => predicate(model))
    : (model) => predicates.every((predicate) => predicate(model));
}

function conditionPredicate(condition: RuleCondition): RulePredicate {
  const { field, value } = condition;

  switch (condition.operator) {
    case RULE_OPERATORS.Equal:
      return (model) => equals(model[field], value);
    case RULE_OPERATORS.NotEqual:
      return (model) => !equals(model[field], value);
    case RULE_OPERATORS.Contains:
      return (model) => contains(model[field], value);
    case RULE_OPERATORS.StartsWith:
      return (model) => asText(model[field]).toLowerCase().startsWith(value.toLowerCase());
    case RULE_OPERATORS.GreaterThan:
      return (model) => compare(model[field], value, (left, right) => left > right);
    case RULE_OPERATORS.LessThan:
      return (model) => compare(model[field], value, (left, right) => left < right);
    case RULE_OPERATORS.IsEmpty:
      return (model) => isEmpty(model[field]);
    case RULE_OPERATORS.IsNotEmpty:
      return (model) => !isEmpty(model[field]);
    default:
      return () => true;
  }
}

function isEmpty(value: unknown): boolean {
  if (value === null || value === undefined || value === '') {
    return true;
  }
  return Array.isArray(value) && value.length === 0;
}

function asText(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }
  if (Array.isArray(value)) {
    return value.map((item) => String(item)).join(',');
  }
  return String(value);
}

/** Multi-choice values are arrays — "equal" means the array holds the value. */
function equals(value: unknown, expected: string): boolean {
  if (Array.isArray(value)) {
    return value.map((item) => String(item)).includes(expected);
  }
  return asText(value) === expected;
}

function contains(value: unknown, expected: string): boolean {
  if (Array.isArray(value)) {
    return value.map((item) => String(item)).includes(expected);
  }
  return asText(value).toLowerCase().includes(expected.toLowerCase());
}

function compare(value: unknown, expected: string, matches: (left: number, right: number) => boolean): boolean {
  const left = Number(value);
  const right = Number(expected);
  if (Number.isNaN(left) || Number.isNaN(right)) {
    return false;
  }
  return matches(left, right);
}
