import { deserializeSchema } from './form-schema-import';
import { ACTION_TAKEN_DATA_NAME, C2M_FA_STATUSES } from './form-schema.types';

/**
 * The C2M metadata a closing form carries survives opening the form in the builder: the parameter a
 * field is sent under, and the outcome each Action Taken option closes the field activity with.
 */
describe('form schema C2M metadata', () => {
  const schema = deserializeSchema({
    elements: [
      {
        type: 'single_choice',
        data_name: ACTION_TAKEN_DATA_NAME,
        choices: [
          { value: 'OCUL01', label_en: 'Done', label_ar: 'تم', c2m_fa_status: ' c ', c2m_reason: ' OK ' },
          { value: 'MMFCNR1', label_en: 'No contract', label_ar: 'لا عقد' },
        ],
      },
      { type: 'numeric', data_name: 'wfm_building_units', c2m_parameter_name: ' CM_BUNIT ' },
      { type: 'text', data_name: 'note', c2m_parameter_name: '   ' },
    ],
  });

  it('reads each option’s C2M outcome, normalised', () => {
    const [done, noContract] = schema.elements[0].choices;

    expect(done.c2m_fa_status).toBe(C2M_FA_STATUSES.Completed);
    expect(done.c2m_reason).toBe('OK');
    expect(noContract.c2m_fa_status).toBeNull();
    expect(noContract.c2m_reason).toBeNull();
  });

  it('reads a field’s C2M parameter name, and treats a blank one as none', () => {
    expect(schema.elements[1].c2m_parameter_name).toBe('CM_BUNIT');
    expect(schema.elements[2].c2m_parameter_name).toBeNull();
  });
});
