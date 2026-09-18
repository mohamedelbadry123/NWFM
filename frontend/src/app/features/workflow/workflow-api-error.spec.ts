import { workflowApiErrorKey, workflowApiErrorMessage, workflowApiErrorStatus } from './workflow-api-error';

describe('workflowApiErrorKey', () => {
  it('maps ProblemDetails title that is a Workflow code', () => {
    expect(workflowApiErrorKey({
      status: 409,
      title: 'Workflow.Version.DraftAlreadyExists',
      detail: 'A Draft version already exists',
    })).toBe('error.Workflow.Version.DraftAlreadyExists');
  });

  it('maps camelCase { code, message } bodies after interceptor unwrap', () => {
    expect(workflowApiErrorKey({
      status: 400,
      title: 'Workflow.Definition.InactiveCannotPublish',
      detail: 'Inactive definitions cannot receive new versions.',
    })).toBe('error.Workflow.Definition.InactiveCannotPublish');
  });

  it('maps validation failures', () => {
    expect(workflowApiErrorKey({
      status: 400,
      title: 'VALIDATION_ERROR',
      detail: 'One or more validation errors occurred.',
    })).toBe('error.VALIDATION_ERROR');
  });

  it('maps a bare 409 to the work-item concurrency key', () => {
    expect(workflowApiErrorKey({ status: 409 }, 'error.generic'))
      .toBe('error.Workflow.WorkItem.ConcurrencyConflict');
  });

  it('uses the fallback when no code is present', () => {
    expect(workflowApiErrorKey({ status: 500 }, 'workflow.versions.error_create_draft'))
      .toBe('workflow.versions.error_create_draft');
  });
});

describe('workflowApiErrorStatus', () => {
  it('reads status from ProblemDetails', () => {
    expect(workflowApiErrorStatus({ status: 403, title: 'Forbidden' })).toBe(403);
  });
});

describe('workflowApiErrorMessage', () => {
  const translate = {
    instant: (key: string) => key === 'error.Workflow.Version.DraftAlreadyExists'
      ? 'A draft already exists.'
      : key,
  };

  it('returns the translated code when the key exists', () => {
    expect(workflowApiErrorMessage({
      status: 409,
      title: 'Workflow.Version.DraftAlreadyExists',
    }, translate, 'workflow.versions.error_create_draft')).toBe('A draft already exists.');
  });

  it('falls back to detail when the key is untranslated', () => {
    expect(workflowApiErrorMessage({
      status: 400,
      title: 'Some.Unknown.Code',
      detail: 'Server said this.',
    }, translate, 'workflow.versions.error_create_draft')).toBe('Server said this.');
  });

  it('prefers ASP.NET model-state errors over generic toast text', () => {
    const t = { instant: (key: string) => key === 'error.generic' ? 'An unexpected error occurred. Please try again.' : key };
    expect(workflowApiErrorMessage({
      status: 400,
      title: 'One or more validation errors occurred.',
      errors: { Code: ['The Code field is required.'] },
    }, t, 'error.generic')).toBe('The Code field is required.');
  });

  it('prefers FluentValidation error array messages', () => {
    const t = { instant: (key: string) => key === 'error.VALIDATION_ERROR' ? 'Please check the form and try again.' : key };
    expect(workflowApiErrorMessage({
      status: 400,
      title: 'VALIDATION_ERROR',
      detail: 'One or more validation errors occurred.',
      errors: [{ field: 'Code', message: 'Code must contain only uppercase letters.' }],
    }, t, 'error.generic')).toBe('Code must contain only uppercase letters.');
  });
});
