import { FormBuilderStore } from './form-builder.store';
import { ELEMENT_TYPES } from '../../../../shared/form-schema/form-schema.types';

/**
 * The store is the builder's model, and `json()` is exactly what gets saved and published. A field
 * dropped here is a column that never exists, so the shape of that output is pinned down.
 */
describe('form builder store', () => {
  let store: FormBuilderStore;

  beforeEach(() => {
    store = new FormBuilderStore();
  });

  it('gives every new field its own data name', () => {
    const first = store.addFromType(ELEMENT_TYPES.Text);
    const second = store.addFromType(ELEMENT_TYPES.Text);

    expect(first.data_name).toBeTruthy();
    expect(second.data_name).toBeTruthy();
    expect(first.data_name).not.toBe(second.data_name);
  });

  it('adds a field inside the section it was dropped on', () => {
    const section = store.addFromType(ELEMENT_TYPES.Section);
    store.addFromType(ELEMENT_TYPES.Numeric, section.key);

    const stored = store.elements().find((el) => el.key === section.key)!;

    expect(stored.elements.length).toBe(1);
    // Flattening is what the server does too: a section is not itself an answer.
    expect(store.allElements().length).toBe(2);
  });

  it('duplicates a field under a name of its own', () => {
    const original = store.addFromType(ELEMENT_TYPES.Text);
    store.duplicate(original.key);

    const names = store.allElements().map((el) => el.data_name);

    expect(names.length).toBe(2);
    expect(new Set(names).size).toBe(2);
  });

  it('removes a field', () => {
    const element = store.addFromType(ELEMENT_TYPES.Text);
    store.remove(element.key);

    expect(store.elements().length).toBe(0);
  });

  it('serializes to the document the server parses', () => {
    store.setNameEn('Leak report');
    store.setNameAr('تقرير تسرب');

    const text = store.addFromType(ELEMENT_TYPES.Text);
    store.update(text.key, { data_name: 'note', label_en: 'Note', required: true });

    const json = JSON.parse(store.json()) as Record<string, unknown>;
    const elements = json['elements'] as Array<Record<string, unknown>>;

    expect(json['name_en']).toBe('Leak report');
    expect(elements[0]['data_name']).toBe('note');
    expect(elements[0]['required']).toBe(true);
    // The editing key is internal and must not reach the stored document.
    expect(elements[0]['key']).toBeUndefined();
  });

  it('writes a choice field with its options, and a media field with its limits', () => {
    const choice = store.addFromType(ELEMENT_TYPES.SingleChoice);
    store.update(choice.key, { data_name: 'material', allow_other: true });

    const photo = store.addFromType(ELEMENT_TYPES.Photo);
    store.update(photo.key, { data_name: 'photos', max_files: 5 });

    const elements = (JSON.parse(store.json()) as Record<string, unknown>)['elements'] as Array<Record<string, unknown>>;
    const stored = Object.fromEntries(elements.map((el) => [el['data_name'], el]));

    expect((stored['material']['choices'] as unknown[]).length).toBe(2);
    expect(stored['material']['allow_other']).toBe(true);
    expect(stored['photos']['max_files']).toBe(5);
  });

  it('reloads a stored document and writes it back unchanged in substance', () => {
    const document = {
      name_en: 'Reloaded',
      name_ar: 'معاد',
      elements: [
        { type: 'text', data_name: 'note', label_en: 'Note', required: true },
        {
          type: 'section',
          data_name: 'details',
          elements: [{ type: 'numeric', data_name: 'depth_m', format: 'integer' }],
        },
      ],
    };

    store.loadFromJson(document);

    const reserialized = JSON.parse(store.json()) as Record<string, unknown>;
    const elements = reserialized['elements'] as Array<Record<string, unknown>>;
    const section = elements[1];

    expect(reserialized['name_en']).toBe('Reloaded');
    expect(elements[0]['data_name']).toBe('note');
    expect((section['elements'] as Array<Record<string, unknown>>)[0]['data_name']).toBe('depth_m');
  });

  it('clears everything when a new form is started', () => {
    store.addFromType(ELEMENT_TYPES.Text);
    store.setNameEn('Leak report');
    store.resetForm();

    expect(store.elements().length).toBe(0);
    // A blank form still has a name, so the designer never opens on an empty title.
    expect(store.nameEn()).toBe('Untitled Form');
  });
});
