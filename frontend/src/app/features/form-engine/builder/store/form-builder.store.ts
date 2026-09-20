import { Injectable, computed, signal } from '@angular/core';
import { moveItemInArray } from '@angular/cdk/drag-drop';
import { createElement } from '../../../../shared/form-schema/form-schema-element';
import { deserializeSchema } from '../../../../shared/form-schema/form-schema-import';
import {
  DROP_IDS,
  ELEMENT_TYPES,
  isAttachmentType,
  isChoiceType,
  isDateRuleType,
  isTextLikeType,
  type ElementType,
  type FormSchema,
  type FormElement,
} from '../../../../shared/form-schema/form-schema.types';

/**
 * The exported Fulcrum-style shape. Declared as a type alias (not an interface)
 * so it stays assignable to `Record<string, unknown>` for the JSON round-trip
 * through `deserializeSchema`.
 */
export type SerializedForm = {
  name_en: string;
  name_ar: string;
  elements: Record<string, unknown>[];
};

/**
 * In-memory state for the form builder. No API/persistence — the exported
 * Fulcrum-style JSON (`json`) is the only output.
 *
 * All mutating operations are IMMUTABLE: they never touch the array/object
 * references that CDK drop-lists are bound to. This guarantees Angular signals
 * always see a genuinely new value so `json` always recomputes correctly, and
 * CDK never has its DOM references invalidated mid-drag (no overlap glitch).
 */
@Injectable()
export class FormBuilderStore {
  private readonly _elements = signal<FormElement[]>([]);
  private readonly _nameEn = signal('Untitled Form');
  private readonly _nameAr = signal('نموذج بدون عنوان');
  private readonly _selectedKey = signal<string | null>(null);

  readonly elements = this._elements.asReadonly();
  readonly nameEn = this._nameEn.asReadonly();
  readonly nameAr = this._nameAr.asReadonly();
  readonly selectedKey = this._selectedKey.asReadonly();

  /** Flat list of every element (sections + children). */
  readonly allElements = computed<FormElement[]>(() => this.flatten(this._elements()));

  /** Flat list of all choice fields, for the cascading "parent field" selector. */
  readonly choiceFields = computed<FormElement[]>(() =>
    this.flatten(this._elements()).filter((el) => isChoiceType(el.type)),
  );

  /** The exported Fulcrum-style definition. */
  readonly definition = computed<SerializedForm>(() => ({
    name_en: this._nameEn(),
    name_ar: this._nameAr(),
    elements: this._elements().map((el) => this.serialize(el)),
  }));

  readonly json = computed(() => JSON.stringify(this.definition(), null, 2));

  setNameEn(value: string): void {
    this._nameEn.set(value);
  }

  setNameAr(value: string): void {
    this._nameAr.set(value);
  }

  select(key: string | null): void {
    this._selectedKey.set(key);
  }

  find(key: string): FormElement | undefined {
    return this.flatten(this._elements()).find((el) => el.key === key);
  }

  /** Add a brand-new element (from the palette) to the top level or a section. */
  addFromType(type: ElementType, sectionKey?: string | null): FormElement {
    const element = createElement(type);
    element.data_name = this.uniqueDataName(this.slugify(type));
    if (sectionKey) {
      this._elements.set(
        this._elements().map((el) =>
          el.key === sectionKey && el.type === ELEMENT_TYPES.Section
            ? { ...el, elements: [...el.elements, element] }
            : el,
        ),
      );
    } else {
      this._elements.set([...this._elements(), element]);
    }
    return element;
  }

  /**
   * Insert a new element (dragged from the palette) into a container by ID.
   * `containerId` is either `DROP_IDS.CanvasRoot` or a section's key.
   */
  insertNewAt(type: ElementType, containerId: string, index: number): FormElement {
    const element = createElement(type);
    element.data_name = this.uniqueDataName(this.slugify(type));
    this._elements.set(this.insertInto(this._elements(), containerId, element, index));
    return element;
  }

  /**
   * Reorder within a single container (same-container CDK drop).
   * `containerId` is `DROP_IDS.CanvasRoot` or a section key.
   */
  moveWithin(containerId: string, from: number, to: number): void {
    if (from === to) return;
    if (containerId === DROP_IDS.CanvasRoot) {
      const arr = [...this._elements()];
      moveItemInArray(arr, from, to);
      this._elements.set(arr);
    } else {
      this._elements.set(
        this._elements().map((el) => {
          if (el.key !== containerId) return el;
          const children = [...el.elements];
          moveItemInArray(children, from, to);
          return { ...el, elements: children };
        }),
      );
    }
  }

  /**
   * Move an element between two different containers (cross-container CDK drop).
   * All IDs are `DROP_IDS.CanvasRoot` or section keys.
   */
  transfer(fromId: string, toId: string, fromIndex: number, toIndex: number): void {
    let moved: FormElement | undefined;

    // Step 1: remove from source (immutably)
    let newElements = this.removeAt(this._elements(), fromId, fromIndex, (el) => {
      moved = el;
    });

    if (!moved) return;

    // Step 2: insert into target (immutably)
    newElements = this.insertInto(newElements, toId, moved, toIndex);

    this._elements.set(newElements);
  }

  update(key: string, patch: Partial<FormElement>): void {
    this._elements.set(this.updateIn(this._elements(), key, patch));
  }

  remove(key: string): void {
    this._elements.set(this.removeFrom(this._elements(), key));
    if (this._selectedKey() === key) {
      this._selectedKey.set(null);
    }
  }

  duplicate(key: string): void {
    this._elements.set(this.duplicateIn(this._elements(), key));
  }

  clear(): void {
    this._elements.set([]);
    this._selectedKey.set(null);
  }

  /** Load a Fulcrum-style definition (e.g. from sample JSON) for editing. */
  loadDefinition(definition: FormSchema): void {
    this._nameEn.set(definition.name_en);
    this._nameAr.set(definition.name_ar);
    this._elements.set(definition.elements);
    this._selectedKey.set(null);
  }

  /** Parse raw JSON object and load into the builder. */
  loadFromJson(raw: Record<string, unknown>): void {
    this.loadDefinition(deserializeSchema(raw));
  }

  resetForm(): void {
    this._nameEn.set('Untitled Form');
    this._nameAr.set('نموذج بدون عنوان');
    this.clear();
  }

  // ── Private immutable helpers ─────────────────────────────────────────────

  /** Insert `item` at `index` inside the container identified by `containerId`. */
  private insertInto(
    elements: FormElement[],
    containerId: string,
    item: FormElement,
    index: number,
  ): FormElement[] {
    if (containerId === DROP_IDS.CanvasRoot) {
      const arr = [...elements];
      arr.splice(index, 0, item);
      return arr;
    }
    return elements.map((el) => {
      if (el.key !== containerId) return el;
      const children = [...el.elements];
      children.splice(index, 0, item);
      return { ...el, elements: children };
    });
  }

  /** Remove the element at `index` from `containerId`, passing it to `onRemoved`. */
  private removeAt(
    elements: FormElement[],
    containerId: string,
    index: number,
    onRemoved: (el: FormElement) => void,
  ): FormElement[] {
    if (containerId === DROP_IDS.CanvasRoot) {
      const arr = [...elements];
      const [removed] = arr.splice(index, 1);
      onRemoved(removed);
      return arr;
    }
    return elements.map((el) => {
      if (el.key !== containerId) return el;
      const children = [...el.elements];
      const [removed] = children.splice(index, 1);
      onRemoved(removed);
      return { ...el, elements: children };
    });
  }

  private updateIn(
    elements: FormElement[],
    key: string,
    patch: Partial<FormElement>,
  ): FormElement[] {
    return elements.map((el) => {
      if (el.key === key) return { ...el, ...patch };
      if (el.elements.length > 0) return { ...el, elements: this.updateIn(el.elements, key, patch) };
      return el;
    });
  }

  private duplicateIn(elements: FormElement[], key: string): FormElement[] {
    const result: FormElement[] = [];
    for (const el of elements) {
      const withChildren = { ...el, elements: this.duplicateIn(el.elements, key) };
      result.push(withChildren);
      if (el.key === key) {
        const clone = this.cloneWithNewKeys(el);
        clone.data_name = this.uniqueDataName(el.data_name);
        result.push(clone);
      }
    }
    return result;
  }

  private uniqueDataName(base: string): string {
    const existing = new Set(this.flatten(this._elements()).map((el) => el.data_name));
    const slug = this.slugify(base) || 'field';
    if (!existing.has(slug)) {
      return slug;
    }
    let counter = 2;
    while (existing.has(`${slug}_${counter}`)) {
      counter += 1;
    }
    return `${slug}_${counter}`;
  }

  private slugify(value: string): string {
    return value
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '_')
      .replace(/^_+|_+$/g, '');
  }

  private flatten(elements: FormElement[]): FormElement[] {
    const result: FormElement[] = [];
    for (const el of elements) {
      result.push(el);
      if (el.elements.length > 0) {
        result.push(...this.flatten(el.elements));
      }
    }
    return result;
  }

  private removeFrom(elements: FormElement[], key: string): FormElement[] {
    return elements
      .filter((el) => el.key !== key)
      .map((el) => ({ ...el, elements: this.removeFrom(el.elements, key) }));
  }

  private cloneWithNewKeys(element: FormElement): FormElement {
    return {
      ...structuredClone(element),
      key: `el_${Math.random().toString(36).slice(2, 8)}`,
      elements: element.elements.map((child) => this.cloneWithNewKeys(child)),
    };
  }

  /** Strip internal fields and type-irrelevant props for the exported JSON. */
  private serialize(element: FormElement): Record<string, unknown> {
    const base: Record<string, unknown> = {
      type: element.type,
      label_en: element.label_en,
      label_ar: element.label_ar,
      data_name: element.data_name,
      description_en: element.description_en,
      description_ar: element.description_ar,
      hidden: element.hidden,
    };

    if (element.type === ELEMENT_TYPES.Section) {
      base['display'] = element.display;
      base['visible_conditions'] = element.visible_conditions;
      base['elements'] = element.elements.map((child) => this.serialize(child));
      return base;
    }

    base['default_value'] = element.default_value;
    base['default_value_mode'] = element.default_value_mode;
    base['required'] = element.required;
    base['disabled'] = element.disabled;

    if (isDateRuleType(element.type)) {
      base['date_rule'] = element.date_rule;
      base['min_date'] = element.min_date;
      base['max_date'] = element.max_date;
    }

    if (isTextLikeType(element.type)) {
      base['min_length'] = element.min_length;
      base['max_length'] = element.max_length;
      base['pattern'] = element.pattern;
    }

    if (element.type === ELEMENT_TYPES.Numeric) {
      base['format'] = element.format;
      base['min'] = element.min;
      base['max'] = element.max;
    }

    if (isChoiceType(element.type)) {
      base['allow_other'] = element.allow_other;
      base['multiple'] = element.type === ELEMENT_TYPES.MultipleChoice;
      base['parent_field'] = element.parent_field;
      base['choices'] = element.choices.map((choice) => ({
        value: choice.value,
        label_en: choice.label_en,
        label_ar: choice.label_ar,
        dependency_value: choice.dependency_value,
      }));
    }

    if (isAttachmentType(element.type)) {
      base['max_files'] = element.max_files;
      base['max_file_size_mb'] = element.max_file_size_mb;
      base['allowed_extensions'] = element.allowed_extensions;
    }

    if (element.type === ELEMENT_TYPES.Barcode) {
      base['barcode_formats'] = element.barcode_formats;
    }

    if (element.type === ELEMENT_TYPES.Geolocation) {
      base['map_zoom'] = element.map_zoom;
    }

    base['visible_conditions'] = element.visible_conditions;
    base['required_conditions'] = element.required_conditions;
    return base;
  }
}
