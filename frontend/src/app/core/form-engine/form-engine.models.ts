/** Everything the FormEngine API serves, and where it lives. */
export const FORM_ENGINE_BASE_PATH = '/api/v1/form-engine';
export const FORM_ENGINE_FORMS_PATH = `${FORM_ENGINE_BASE_PATH}/forms`;
export const FORM_ENGINE_UPLOADS_PATH = `${FORM_ENGINE_BASE_PATH}/uploads`;
export const FORM_ENGINE_FIELD_CATALOG_PATH = `${FORM_ENGINE_BASE_PATH}/field-catalog`;

/** Lifecycle of a form. Mirrors `FormStatuses` on the server. */
export const FORM_STATUSES = {
  draft: 'DRAFT',
  published: 'PUBLISHED',
  deprecated: 'DEPRECATED',
  archived: 'ARCHIVED',
} as const;

export type FormStatus = (typeof FORM_STATUSES)[keyof typeof FORM_STATUSES];

/** Mirrors `FormCategories` on the server; the label for each is an i18n key. */
export const FORM_CATEGORIES = ['GENERAL', 'INSPECTION', 'SURVEY', 'REQUEST', 'CHECKLIST', 'OTHER'] as const;

export type FormCategory = (typeof FORM_CATEGORIES)[number];

export interface FormListItem {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  category: string;
  status: FormStatus;
  departmentCode: string | null;
  currentVersionNo: number | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface FormDetail extends FormListItem {
  createdBy: string | null;
  updatedBy: string | null;
  /** The working form-builder document. */
  schemaJson: string;
}

export interface FormVersionSummary {
  id: string;
  versionNo: number;
  targetClient: string;
  publishedBy: string | null;
  publishedAt: string;
}

/** A published version with the schema frozen into it — what a fill or a review renders. */
export interface FormVersionDetail extends FormVersionSummary {
  formDefinitionId: string;
  code: string;
  nameEn: string;
  nameAr: string;
  formStatus: FormStatus;
  currentVersionNo: number | null;
  schemaJson: string;
  snapshotJson: string;
  acceptsSubmissions: boolean;
}

/** A form that can be filled right now. */
export interface PublishedForm {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  category: string;
  status: FormStatus;
  departmentCode: string | null;
  currentVersionNo: number;
  versionNos: number[];
}

export interface FieldCatalogItem {
  id: string;
  dataName: string;
  fieldType: string;
  labelEn: string | null;
  labelAr: string | null;
  description: string | null;
}

export interface FormSubmissionCreated {
  submissionId: string;
  versionNo: number;
  /** True when the server recognised this as a retry and returned the original submission. */
  isReplay: boolean;
}

/** A submission row: base columns plus whatever answer columns the form declares. */
export type FormSubmissionRow = Record<string, unknown>;

export interface CreateFormPayload {
  code: string;
  nameEn: string;
  nameAr: string;
  category: string;
  departmentCode: string | null;
}

export type UpdateFormPayload = Omit<CreateFormPayload, 'code'>;

export interface CloneFormPayload {
  newCode: string;
  newNameEn: string;
  newNameAr: string;
}

export interface SubmitFormPayload {
  versionNo?: number | null;
  contextType?: string | null;
  contextId?: string | null;
  clientSubmissionId?: string | null;
  clientFilledAt?: string | null;
  answers: Record<string, unknown>;
}

export interface FormListQuery {
  pageNumber: number;
  pageSize: number;
  searchTerm?: string | null;
  category?: string | null;
  status?: string | null;
  departmentCode?: string | null;
  excludeArchived?: boolean;
}
