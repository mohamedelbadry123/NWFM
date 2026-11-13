export const designerSuites = [
  'create-scope',
  'scope-rows',
  'designer-autosave',
  'draft-preservation',
  'embedded-forms',
  'event-mapping',
  'guided-designer',
  'hierarchy',
  'library',
  'mapping-acceptance',
  'ordered-routing',
  'role-assignment',
  'routing',
  'routing-runtime',
  'unrc',
] as const;
export type DesignerSuite = (typeof designerSuites)[number];
export function designerCredentialName(suite: DesignerSuite, index: number): string {
  if (!Number.isInteger(index) || index < 1 || index > 7)
    throw new Error('Designer fixture account pool exhausted.');
  return `designer-${suite}-${index}`;
}
export const designerFixtureUsers = designerSuites.flatMap((suite) =>
  Array.from({ length: 7 }, (_, index) => designerCredentialName(suite, index + 1)),
);
