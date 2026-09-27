/**
 * FormEngine's name for the shared API error helper. The helper reads any module's `{ error: { code } }`
 * envelope, so it lives in `core/api`; this name stays so the form-engine screens keep their import.
 */
export { apiErrorMessage as formEngineErrorMessage } from '../api/api-error-message';
