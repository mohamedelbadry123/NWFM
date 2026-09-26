/** Static sample schema served from `public/form-builder/`. */
export const FORM_BUILDER_SAMPLE_FILES = {
  /** The same document the server seeds as the "All Input Types" demo form — keep the two in step. */
  AllInputTypes: 'all-input-types',
} as const;

export type FormBuilderSampleFile =
  (typeof FORM_BUILDER_SAMPLE_FILES)[keyof typeof FORM_BUILDER_SAMPLE_FILES];

export function formBuilderSampleUrl(fileName: FormBuilderSampleFile): string {
  return `/form-builder/${fileName}.json`;
}
