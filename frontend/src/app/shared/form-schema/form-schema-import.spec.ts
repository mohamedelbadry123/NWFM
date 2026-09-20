import { deserializeSchema } from './form-schema-import';
import { ELEMENT_TYPES } from './form-schema.types';

/**
 * Importing is how a stored schema becomes an editable design, and how an older or foreign document
 * is brought up to the shape the builder expects.
 */
describe('form schema import', () => {
  it('fills in the defaults a document leaves out', () => {
    const schema = deserializeSchema({
      name_en: 'Leak',
      name_ar: 'تسرب',
      elements: [{ type: 'text', data_name: 'note' }],
    });

    const field = schema.elements[0];

    expect(schema.name_en).toBe('Leak');
    expect(field.data_name).toBe('note');
    expect(field.required).toBe(false);
    expect(field.visible_conditions.conditions).toEqual([]);
    // Every element gets an editing key, which never leaves the builder.
    expect(field.key).toBeTruthy();
  });

  it('trims a data name so it matches the column it becomes', () => {
    const schema = deserializeSchema({ elements: [{ type: 'text', data_name: ' leak_type ' }] });

    expect(schema.elements[0].data_name).toBe('leak_type');
  });

  it('keeps a section and its children', () => {
    const schema = deserializeSchema({
      elements: [
        {
          type: 'section',
          data_name: 'details',
          elements: [{ type: 'numeric', data_name: 'depth_m' }],
        },
      ],
    });

    const section = schema.elements[0];

    expect(section.type).toBe(ELEMENT_TYPES.Section);
    expect(section.elements.length).toBe(1);
    expect(section.elements[0].data_name).toBe('depth_m');
  });

  it('reads choices, cascading and the "Other" option', () => {
    const schema = deserializeSchema({
      elements: [
        {
          type: 'single_choice',
          data_name: 'material',
          allow_other: true,
          parent_field: 'category',
          choices: [
            { value: 'steel', label_en: 'Steel', label_ar: 'صلب', dependency_value: 'metal' },
          ],
        },
      ],
    });

    const field = schema.elements[0];

    expect(field.allow_other).toBe(true);
    expect(field.parent_field).toBe('category');
    expect(field.choices[0].dependency_value).toBe('metal');
  });

  it('accepts extensions as a list or as a comma-separated string', () => {
    const fromList = deserializeSchema({
      elements: [{ type: 'file', data_name: 'docs', allowed_extensions: ['.PDF', ' docx '] }],
    });

    const fromText = deserializeSchema({
      elements: [{ type: 'file', data_name: 'docs', allowed_extensions: 'pdf, docx' }],
    });

    expect(fromList.elements[0].allowed_extensions).toEqual(['pdf', 'docx']);
    expect(fromText.elements[0].allowed_extensions).toEqual(['pdf', 'docx']);
  });

  it('ignores properties written by another product', () => {
    const schema = deserializeSchema({
      elements: [{ type: 'text', data_name: 'note', c2m_parameter_name: 'CM_NOTE', unknown: { a: 1 } }],
    });

    expect(schema.elements[0].data_name).toBe('note');
    expect((schema.elements[0] as unknown as Record<string, unknown>)['c2m_parameter_name']).toBeUndefined();
  });
});
