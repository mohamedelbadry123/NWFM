import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { MessageService } from 'primeng/api';
import { of, throwError } from 'rxjs';
import { FormsService } from '../../../core/form-engine/forms.service';
import { FormBuilderComponent } from './form-builder.component';
import { FormBuilderStore } from './store/form-builder.store';

const detail = (over: object = {}) => ({
  id: 'F1',
  code: 'F',
  nameEn: 'Loaded form',
  nameAr: 'نموذج',
  category: 'GENERAL',
  status: 'PUBLISHED',
  departmentCode: null,
  fieldActivityCode: null,
  currentVersionNo: 2,
  schemaJson: JSON.stringify({ name_en: 'Loaded form', name_ar: 'نموذج', elements: [] }),
  ...over,
});

describe('FormBuilderComponent inside a host window', () => {
  let api: {
    get: ReturnType<typeof vi.fn>;
    saveSchema: ReturnType<typeof vi.fn>;
    publish: ReturnType<typeof vi.fn>;
  };
  let router: { navigate: ReturnType<typeof vi.fn> };
  let messages: { add: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    api = {
      get: vi.fn().mockReturnValue(of({ isSuccess: true, value: detail() })),
      saveSchema: vi
        .fn()
        .mockReturnValue(of({ isSuccess: true, value: detail({ status: 'DRAFT' }) })),
      publish: vi
        .fn()
        .mockReturnValue(of({ isSuccess: true, value: detail({ currentVersionNo: 3 }) })),
    };
    router = { navigate: vi.fn() };
    messages = { add: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        FormBuilderStore,
        { provide: FormsService, useValue: api },
        { provide: Router, useValue: router },
        { provide: MessageService, useValue: messages },
        { provide: HttpClient, useValue: {} },
        {
          provide: TranslateService,
          useValue: { instant: (key: string) => key, currentLang: 'en' },
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'ROUTE-ID' } } },
        },
      ],
    });
  });

  async function embedded(formId: string | null = 'F1') {
    const component = TestBed.runInInjectionContext(() => new FormBuilderComponent());
    component.embeddedFormId = formId;
    const dirty: boolean[] = [];
    component.dirtyChange.subscribe((value) => dirty.push(value));
    await component.ngOnInit();
    TestBed.tick();
    return { component, store: TestBed.inject(FormBuilderStore), dirty };
  }

  it('edits the form it was given instead of the one in the route, and reports its server state', async () => {
    const { component } = await embedded();
    expect(api.get).toHaveBeenCalledWith('F1');
    expect(component.formStatus()).toBe('PUBLISHED');
    expect(component.currentVersionNo()).toBe(2);
    expect(component.dirty()).toBe(false);
  });

  it('still reads the route when standalone, and never navigates when embedded', async () => {
    const standalone = await embedded(null);
    expect(api.get).toHaveBeenCalledWith('ROUTE-ID');
    (standalone.component as unknown as { backToForms(): void }).backToForms();
    expect(router.navigate).toHaveBeenCalledWith(['/forms']);

    router.navigate.mockClear();
    const inside = await embedded('F1');
    (inside.component as unknown as { backToForms(): void }).backToForms();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('is dirty after an edit and clean again once the draft is saved', async () => {
    const { component, store, dirty } = await embedded();
    store.setNameEn('Renamed');
    TestBed.tick();
    expect(component.dirty()).toBe(true);
    expect(dirty.at(-1)).toBe(true);

    await expect(component.saveDraft()).resolves.toBe(true);
    TestBed.tick();
    expect(api.saveSchema).toHaveBeenCalledWith('F1', store.json());
    expect(component.dirty()).toBe(false);
    expect(component.formStatus()).toBe('DRAFT');
    expect(dirty.at(-1)).toBe(false);
  });

  it('stays dirty and resolves false when saving fails', async () => {
    const { component, store } = await embedded();
    store.setNameEn('Renamed');
    api.saveSchema.mockReturnValue(throwError(() => new Error('offline')));
    await expect(component.saveDraft()).resolves.toBe(false);
    expect(component.dirty()).toBe(true);
    expect(messages.add).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }));
  });

  it('keeps edits made while a save is in flight dirty', async () => {
    const { component, store } = await embedded();
    store.setNameEn('First');
    api.saveSchema.mockImplementation(() => {
      store.setNameEn('Second');
      return of({ isSuccess: true, value: detail({ status: 'DRAFT' }) });
    });
    await component.saveDraft();
    expect(component.dirty()).toBe(true);
  });

  it('saves the design on screen first and then publishes it, returning the published form', async () => {
    const { component, store } = await embedded();
    store.setNameEn('Renamed');
    const published = await component.publishForm();
    expect(api.saveSchema.mock.invocationCallOrder[0]).toBeLessThan(
      api.publish.mock.invocationCallOrder[0],
    );
    expect(published?.currentVersionNo).toBe(3);
    expect(component.currentVersionNo()).toBe(3);
    expect(component.dirty()).toBe(false);
  });

  it('returns null and leaves the design dirty when publishing is refused', async () => {
    const { component, store } = await embedded();
    store.setNameEn('Renamed');
    api.publish.mockReturnValue(throwError(() => new Error('A data name is reused')));
    await expect(component.publishForm()).resolves.toBeNull();
    expect(component.dirty()).toBe(true);
  });
});
