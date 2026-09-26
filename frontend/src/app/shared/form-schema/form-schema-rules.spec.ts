import { buildRulePredicate } from './form-schema-rules';
import { RULE_MATCH, RULE_OPERATORS, type RuleGroup, type RuleOperator } from './form-schema.types';

/**
 * The rules engine decides whether a field is on screen and whether it is required. The server runs
 * a port of it, so a disagreement between the two rejects fills the form accepted — which is why the
 * operators are pinned down here.
 */
describe('form schema rules', () => {
  function group(match: RuleGroup['match'], conditions: RuleGroup['conditions']): RuleGroup {
    return { match, conditions, preserve_data: false };
  }

  it('treats a group with nothing to evaluate as no rule at all', () => {
    expect(buildRulePredicate(null)).toBeNull();
    expect(buildRulePredicate(group(RULE_MATCH.All, []))).toBeNull();

    // A blank row in the builder names no field, so it is not a condition.
    expect(buildRulePredicate(group(RULE_MATCH.All, [
      { field: '', operator: RULE_OPERATORS.Equal, value: 'x' },
    ]))).toBeNull();
  });

  it('applies the text operators', () => {
    const cases: Array<[RuleOperator, string, boolean]> = [
      [RULE_OPERATORS.Equal, 'steel', true],
      [RULE_OPERATORS.Equal, 'pvc', false],
      [RULE_OPERATORS.NotEqual, 'pvc', true],
      [RULE_OPERATORS.Contains, 'tee', true],
      [RULE_OPERATORS.StartsWith, 'ste', true],
      [RULE_OPERATORS.IsNotEmpty, '', true],
      [RULE_OPERATORS.IsEmpty, '', false],
    ];

    for (const [operator, value, expected] of cases) {
      const predicate = buildRulePredicate(group(RULE_MATCH.All, [{ field: 'material', operator, value }]));

      expect(predicate!({ material: 'steel' })).withContext(operator + ' ' + value).toBe(expected);
    }
  });

  it('compares numbers, and treats an unanswered field as no match', () => {
    const greater = buildRulePredicate(group(RULE_MATCH.All, [
      { field: 'depth', operator: RULE_OPERATORS.GreaterThan, value: '3' },
    ]))!;

    expect(greater({ depth: 5 })).toBe(true);
    expect(greater({ depth: 1 })).toBe(false);
    expect(greater({})).toBe(false);
  });

  it('asks whether a multi-choice answer holds the value', () => {
    const predicate = buildRulePredicate(group(RULE_MATCH.All, [
      { field: 'impacts', operator: RULE_OPERATORS.Equal, value: 'soil' },
    ]))!;

    expect(predicate({ impacts: ['water', 'soil'] })).toBe(true);
    expect(predicate({ impacts: ['water'] })).toBe(false);
  });

  it('matches any or all, as the group says', () => {
    const conditions = [
      { field: 'a', operator: RULE_OPERATORS.Equal, value: '1' },
      { field: 'b', operator: RULE_OPERATORS.Equal, value: '2' },
    ];

    const any = buildRulePredicate(group(RULE_MATCH.Any, conditions))!;
    const all = buildRulePredicate(group(RULE_MATCH.All, conditions))!;

    expect(any({ a: 'x', b: '2' })).toBe(true);
    expect(all({ a: 'x', b: '2' })).toBe(false);
    expect(all({ a: '1', b: '2' })).toBe(true);
  });

  it('is satisfied by an operator it does not know, rather than hiding the field', () => {
    const predicate = buildRulePredicate(group(RULE_MATCH.All, [
      { field: 'a', operator: 'sounds_like' as never, value: '1' },
    ]))!;

    expect(predicate({ a: 'zzz' })).toBe(true);
  });
});
