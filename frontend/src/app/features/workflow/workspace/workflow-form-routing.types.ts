export interface FormRouteDestination { kind: 'Next' | 'Form' | 'Activity'; target?: string | null }
export interface RoutingCondition { field: string; operator: string; value: unknown; to?: unknown }
export interface RoutingGroup { match: 'All' | 'Any'; conditions: RoutingCondition[]; groups: RoutingGroup[] }
export interface FormRoutingRule { id: string; name: string; when: RoutingGroup; destination: FormRouteDestination }
export interface ActivityFormStep { id: string; formId: string; versionNo: number; name: string; reviewRequired?: boolean; rules: FormRoutingRule[]; otherwise: FormRouteDestination }
export interface FormRoutingField { dataName: string; type: string; labelEn: string; labelAr: string; choices: {value: string; labelEn: string; labelAr: string}[] }
export interface ActivityFormVisit { visitId: string; stepId: string; formId: string; versionNo: number; submissionId: string; submittedAt: string; ruleId?: string; destination: FormRouteDestination; answers: Record<string, unknown>; schemaJson?: string | null }

export function activityFormSteps(config: Record<string, unknown>): ActivityFormStep[] {
  if (Array.isArray(config['formSteps'])) return structuredClone(config['formSteps']);
  return config['formDefinitionId'] && config['formVersionNo'] ? [{id: 'legacy-form', formId: String(config['formDefinitionId']), versionNo: Number(config['formVersionNo']), name: '', rules: [], otherwise: {kind: 'Next'}}] : [];
}
export function routingOperators(type: string): string[] {
  const empty = ['is_empty', 'is_not_empty'];
  if (['numeric','date','date_time','time'].includes(type)) return ['equal','not_equal','greater_than','greater_or_equal','less_than','less_or_equal','between',...empty];
  if (type === 'single_choice') return ['equal','not_equal','in','not_in',...empty];
  if (type === 'multiple_choice') return ['contains_any','contains_all','contains_none',...empty];
  if (type === 'yes_no') return ['equal','not_equal',...empty];
  if (['text','memo','barcode'].includes(type)) return ['equal','not_equal','contains','starts_with','ends_with',...empty];
  return empty;
}

export function routingFields(schema: Record<string, unknown>): FormRoutingField[] {
  const result: FormRoutingField[] = [];
  const walk = (elements: any[]) => {
    for (const e of elements) {
      if (e.type === 'section') walk(e.elements ?? []);
      else if (e.data_name) result.push({dataName: e.data_name, type: e.type, labelEn: e.label_en || e.data_name, labelAr: e.label_ar || e.label_en || e.data_name,
        choices: (e.choices ?? []).map((c: any) => ({value: String(c.value), labelEn: c.label_en || c.value, labelAr: c.label_ar || c.label_en || c.value}))});
    }
  };
  walk(Array.isArray(schema['elements']) ? schema['elements'] : []); return result;
}
