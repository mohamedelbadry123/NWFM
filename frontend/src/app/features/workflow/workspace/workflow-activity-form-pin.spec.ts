import { describe, expect, it } from 'vitest';
import { pinFormToStep, replacesFormWithRules } from './workflow-activity-form-pin';

const step = (formId: string, versionNo: number, extra: object = {}) => ({
  id: 'step-' + formId,
  formId,
  versionNo,
  name: formId,
  rules: [],
  otherwise: { kind: 'Next' },
  ...extra,
});
const rule = {
  id: 'r1',
  name: 'Rule',
  when: { match: 'All', conditions: [], groups: [] },
  destination: { kind: 'Next' },
};

describe('Pinning a form version in one Step', () => {
  it('moves only the version when a shared form is edited and keeps its rules, flagged for review', () => {
    const config = {
      taskTypeId: 'T',
      formSteps: [step('F', 1, { rules: [rule] })],
      rejectTargetNodeKey: 'prev',
    };
    const next = pinFormToStep(config, { formId: 'F', versionNo: 2, name: 'Form F' }, true);
    const [pinned] = next['formSteps'] as {
      versionNo: number;
      rules: unknown[];
      reviewRequired?: boolean;
      name: string;
    }[];
    expect(pinned).toMatchObject({ versionNo: 2, reviewRequired: true, name: 'Form F' });
    expect(pinned.rules).toHaveLength(1);
    expect(next['taskTypeId']).toBe('T');
    expect(next['rejectTargetNodeKey']).toBe('prev');
    // The input settings, which another Step may share by reference, are never mutated.
    expect((config.formSteps[0] as { versionNo: number }).versionNo).toBe(1);
  });

  it('does not flag a Step for review when its version did not change', () => {
    const next = pinFormToStep(
      { formSteps: [step('F', 3)] },
      { formId: 'F', versionNo: 3, name: 'F' },
      true,
    );
    expect((next['formSteps'] as { reviewRequired?: boolean }[])[0].reviewRequired).toBeUndefined();
  });

  it('replaces the form of a single-form Step with a new one that starts without rules', () => {
    const next = pinFormToStep(
      { formSteps: [step('OLD', 1, { rules: [rule] })] },
      { formId: 'NEW', versionNo: 1, name: 'New' },
      true,
    );
    const steps = next['formSteps'] as { formId: string; rules: unknown[] }[];
    expect(steps.map((s) => s.formId)).toEqual(['NEW']);
    expect(steps[0].rules).toEqual([]);
  });

  it('adds a different form after the others in a multi-form Step', () => {
    const next = pinFormToStep(
      { formSteps: [step('A', 1)] },
      { formId: 'B', versionNo: 1, name: 'B' },
      false,
    );
    expect((next['formSteps'] as { formId: string }[]).map((s) => s.formId)).toEqual(['A', 'B']);
  });

  it('converts a legacy single-form pin and supplies SLA context only when it is missing', () => {
    const legacy = { formDefinitionId: 'F', formVersionNo: 1 };
    const next = pinFormToStep(
      legacy,
      { formId: 'F', versionNo: 2, name: 'F', departmentCode: '10', fieldActivityCode: 'LEAK' },
      true,
    );
    expect(next['formDefinitionId']).toBeUndefined();
    expect(next['formVersionNo']).toBeUndefined();
    expect(next).toMatchObject({ departmentCode: '10', fieldActivityCode: 'LEAK' });
    const kept = pinFormToStep(
      { departmentCode: 'old', fieldActivityCode: 'OLD' },
      { formId: 'F', versionNo: 1, name: 'F', departmentCode: '10', fieldActivityCode: 'LEAK' },
      true,
    );
    expect(kept).toMatchObject({ departmentCode: 'old', fieldActivityCode: 'OLD' });
  });

  it('warns about replacing a form that has rules, but only for single-form Steps and different forms', () => {
    const config = { formSteps: [step('OLD', 1, { rules: [rule] })] };
    expect(replacesFormWithRules(config, 'NEW', true)).toBe(true);
    expect(replacesFormWithRules(config, 'OLD', true)).toBe(false);
    expect(replacesFormWithRules(config, 'NEW', false)).toBe(false);
    expect(replacesFormWithRules({ formSteps: [step('OLD', 1)] }, 'NEW', true)).toBe(false);
  });
});
