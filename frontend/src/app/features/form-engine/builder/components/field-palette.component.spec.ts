import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

import { FieldPaletteComponent } from './field-palette.component';
import { PALETTE_ITEMS } from '../data/palette';
import { DROP_IDS, ELEMENT_TYPES, type ElementType } from '../../../../shared/form-schema/form-schema.types';

/**
 * The palette is how a field gets onto the canvas. It once compiled cleanly while rendering
 * nothing — the designer opened on an empty column — so these assert the buttons are really in the
 * DOM, and draggable, rather than that the component merely constructs.
 */
describe('FieldPaletteComponent', () => {
  let fixture: ComponentFixture<FieldPaletteComponent>;

  function buttons(): NodeListOf<HTMLButtonElement> {
    return (fixture.nativeElement as HTMLElement).querySelectorAll('button[cdkDrag]');
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [FieldPaletteComponent, TranslateModule.forRoot()] });

    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      formBuilder: {
        fields: 'Fields',
        paletteHint: 'Drag a field onto the canvas',
        groups: { basic: 'Basic', choice: 'Choice', media: 'Media', location: 'Location', design: 'Design' },
        types: { text: 'Text', geolocation: 'Location', section: 'Section' },
      },
    });
    translate.use('en');

    fixture = TestBed.createComponent(FieldPaletteComponent);
    fixture.detectChanges();
  });

  it('shows one draggable button per field type', () => {
    expect(buttons().length).toBe(PALETTE_ITEMS.length);
  });

  it('labels the buttons through the translations', () => {
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Fields');
    expect(text).toContain('Basic');
    expect(text).toContain('Text');
  });

  it('is a drop list the canvas can be connected to', () => {
    const list = (fixture.nativeElement as HTMLElement).querySelector('[cdkDropList]');

    expect(list?.id).toBe(DROP_IDS.Palette);
  });

  it('adds a field when its button is clicked, for anyone not dragging', () => {
    const added: ElementType[] = [];
    fixture.componentInstance.add.subscribe((type) => added.push(type));

    const first = Array.from(buttons()).find((button) => button.textContent?.includes('Text'));
    first!.click();

    expect(added).toEqual([ELEMENT_TYPES.Text]);
  });
});
