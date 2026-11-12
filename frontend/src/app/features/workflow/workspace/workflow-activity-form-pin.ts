import { ActivityFormStep, activityFormSteps } from './workflow-form-routing.types';

/** The Department and Field Activity Type a Step needs before the workflow can be published. */
export interface ActivityClassification {
  departmentCode?: string;
  fieldActivityCode?: string;
}

export const isClassified = (
  value?: { departmentCode?: string | null; fieldActivityCode?: string | null } | null,
): boolean => !!value?.departmentCode && !!value.fieldActivityCode;

/** A published form version to use in one Step. */
export interface PinnedForm {
  formId: string;
  versionNo: number;
  name: string;
  departmentCode?: string;
  fieldActivityCode?: string;
}

/**
 * Pins a published form version in one Step's saved settings and returns the new settings; nothing else is touched.
 *
 * - The Step already uses this form (it was edited and published again): only its version moves, the form's
 *   routing rules stay, and the Step is flagged for review so a rule that referred to a changed field is
 *   surfaced rather than deleted.
 * - A different form in a single-form Step replaces the old one and starts with no rules; in a multi-form
 *   Step it is added at the end.
 *
 * Only the Step it is called for is affected: other Steps keep the version they pinned.
 */
export function pinFormToStep(
  config: Record<string, unknown>,
  form: PinnedForm,
  singleForm: boolean,
): Record<string, unknown> {
  let steps: ActivityFormStep[] = activityFormSteps(config);
  const existing = steps.find((step) => step.formId === form.formId);
  if (existing) {
    if (existing.versionNo !== form.versionNo) {
      existing.versionNo = form.versionNo;
      existing.reviewRequired = true;
    }
    existing.name = form.name || existing.name;
  } else {
    const added: ActivityFormStep = {
      id: crypto.randomUUID(),
      formId: form.formId,
      versionNo: form.versionNo,
      name: form.name,
      rules: [],
      otherwise: { kind: 'Next' },
    };
    steps = singleForm ? [added] : [...steps, added];
  }
  const next: Record<string, unknown> = { ...config, formSteps: steps };
  delete next['formDefinitionId'];
  delete next['formVersionNo'];
  // A complete classification is the Step's own and is kept; an absent or half-set one is completed from the form,
  // so a Step whose form was just created is publishable.
  if (
    !isClassified(next as ActivityClassification) &&
    form.departmentCode &&
    form.fieldActivityCode
  ) {
    next['departmentCode'] = form.departmentCode;
    next['fieldActivityCode'] = form.fieldActivityCode;
  }
  return next;
}

/** Whether pinning this form would replace a different form that already has routing rules. */
export function replacesFormWithRules(
  config: Record<string, unknown>,
  formId: string,
  singleForm: boolean,
): boolean {
  if (!singleForm) return false;
  return activityFormSteps(config).some(
    (step) => step.formId !== formId && (step.rules.length > 0 || step.otherwise.kind !== 'Next'),
  );
}
