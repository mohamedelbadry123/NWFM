import { beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, Output, forwardRef, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { FormsService } from '@core/form-engine/forms.service';
import { TaskTypesService } from '@core/tasks/task-types.service';
import { ActivityForm } from './workflow-activity-forms.service';
import { PinnedForm, pinFormToStep } from './workflow-activity-form-pin';
import { FormBuilderComponent } from '../../form-engine/builder/form-builder.component';
import { WorkflowWorkspaceService } from './workflow-workspace.service';
import {
  FormEditorContext,
  WorkflowEmbeddedFormEditorComponent,
} from './workflow-embedded-form-editor.component';

const shared: ActivityForm = {
  id: 'F1',
  code: 'LEAK-INSPECTION',
  nameEn: 'Leak inspection',
  nameAr: 'فحص التسرب',
  category: 'INSPECTION',
  status: 'PUBLISHED',
  departmentCode: '10',
  fieldActivityCode: 'LEAK',
  currentVersionNo: 2,
  versionNos: [2, 1],
  isUsable: true,
  updatedAt: '2026-10-01T00:00:00Z',
};
const detail = (over: object = {}) => ({
  id: 'NEW',
  code: 'X',
  nameEn: 'New form',
  nameAr: 'نموذج جديد',
  category: 'GENERAL',
  status: 'DRAFT',
  departmentCode: null,
  fieldActivityCode: null,
  currentVersionNo: null,
  ...over,
});

/** The parts of the builder the window drives. */
interface BuilderStub {
  dirty: ReturnType<typeof signal<boolean>>;
  formStatus: ReturnType<typeof signal<string | null>>;
  currentVersionNo: ReturnType<typeof signal<number | null>>;
  saveDraft: ReturnType<typeof vi.fn>;
  publishForm: ReturnType<typeof vi.fn>;
}

describe('Form editor window', () => {
  let forms: {
    create: ReturnType<typeof vi.fn>;
    clone: ReturnType<typeof vi.fn>;
    update: ReturnType<typeof vi.fn>;
  };
  let workspace: { references: ReturnType<typeof vi.fn> };
  let taskTypes: { attachForm: ReturnType<typeof vi.fn> };
  const locale = signal('en');
  let used: Mock<(form: PinnedForm) => void>;
  let close: Mock<() => void>;

  beforeEach(() => {
    locale.set('en');
    forms = { create: vi.fn(), clone: vi.fn(), update: vi.fn() };
    workspace = {
      references: vi.fn((kind: string) =>
        of(
          kind === 'departments'
            ? [{ id: 'D10', code: '10', nameEn: 'Water', nameAr: 'المياه' }]
            : [
                {
                  id: 'T1',
                  code: 'LEAK',
                  nameEn: 'Leak repair',
                  nameAr: 'إصلاح التسرب',
                  parentCode: '10',
                },
              ],
        ),
      ),
    };
    taskTypes = { attachForm: vi.fn().mockReturnValue(of({ isSuccess: true })) };
    used = vi.fn<(form: PinnedForm) => void>();
    close = vi.fn<() => void>();
    TestBed.configureTestingModule({
      providers: [
        { provide: LocaleService, useValue: { locale } },
        { provide: FormsService, useValue: forms },
        { provide: WorkflowWorkspaceService, useValue: workspace },
        { provide: TaskTypesService, useValue: taskTypes },
        { provide: TranslateService, useValue: { instant: (key: string) => key } },
      ],
    });
  });

  /** The Step's own classification, as a Step that already has a form carries it. */
  const classified = { departmentCode: '10', fieldActivityCode: 'LEAK' };
  function create(
    mode: FormEditorContext['mode'],
    form?: ActivityForm,
    classification: FormEditorContext['classification'] = classified,
  ) {
    const component = TestBed.runInInjectionContext(
      () => new WorkflowEmbeddedFormEditorComponent(),
    );
    component.context = {
      mode,
      taskTypeId: 'TT',
      stepName: 'Inspect',
      form,
      classification,
      use: used,
      close,
    };
    component.ngOnInit();
    const builder: BuilderStub = {
      dirty: signal(false),
      formStatus: signal<string | null>('DRAFT'),
      currentVersionNo: signal<number | null>(null),
      saveDraft: vi.fn().mockResolvedValue(true),
      publishForm: vi.fn(),
    };
    (component as unknown as { builder: BuilderStub }).builder = builder;
    return { component, builder };
  }

  describe('create and duplicate', () => {
    it('asks for the form details first and suggests a code from the English name', () => {
      const { component } = create('create');
      expect(component.stage()).toBe('details');
      component.changeNameEn('Meter reading 2026!');
      expect(component.code()).toBe('METER-READING-2026');
      component.changeCode('MY-CODE');
      component.changeNameEn('Something else');
      expect(component.code()).toBe('MY-CODE');
    });

    it('creates the form, takes its identity from the server and only then starts designing', async () => {
      forms.create.mockReturnValue(of({ isSuccess: true, value: detail() }));
      const { component } = create('create');
      component.changeNameEn('New form');
      component.nameAr.set('نموذج جديد');
      await component.createForm();
      expect(forms.create).toHaveBeenCalledWith({
        code: 'NEW-FORM',
        nameEn: 'New form',
        nameAr: 'نموذج جديد',
        category: 'GENERAL',
        // The form carries the Step's classification, so using it keeps the Step publishable.
        departmentCode: '10',
        fieldActivityCode: 'LEAK',
      });
      expect(component.formId()).toBe('NEW');
      expect(component.stage()).toBe('design');
    });

    it('does not call the server until both names and a valid code are given', async () => {
      const { component } = create('create');
      component.changeNameEn('Only English');
      await component.createForm();
      expect(forms.create).not.toHaveBeenCalled();
      expect(component.submitted()).toBe(true);
      expect(component.stage()).toBe('details');
    });

    it('stays on the details with the reason when the code is taken', async () => {
      forms.create.mockReturnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 409,
              error: {
                isSuccess: false,
                error: { code: 'Dup', message: 'This code is already used.' },
              },
            }),
        ),
      );
      const { component } = create('create');
      component.changeNameEn('Taken');
      component.nameAr.set('مستخدم');
      await component.createForm();
      expect(component.stage()).toBe('details');
      expect(component.error()).toBe('This code is already used.');
      expect(component.busy()).toBe(false);
    });

    it('duplicates into an independent form under a new code and names', async () => {
      forms.clone.mockReturnValue(
        of({
          isSuccess: true,
          value: detail({ id: 'COPY', departmentCode: '10', fieldActivityCode: 'LEAK' }),
        }),
      );
      const { component } = create('duplicate', shared);
      expect(component.nameEn()).toBe('Leak inspection (copy)');
      expect(component.code()).toBe('LEAK-INSPECTION-COPY');
      await component.createForm();
      expect(forms.clone).toHaveBeenCalledWith('F1', {
        newCode: 'LEAK-INSPECTION-COPY',
        newNameEn: 'Leak inspection (copy)',
        newNameAr: 'فحص التسرب (نسخة)',
      });
      expect(component.formId()).toBe('COPY');
      expect(forms.create).not.toHaveBeenCalled();
    });
  });

  describe('Publish and use in this Step', () => {
    it('publishes, files the form under the Task Type, then hands the version to the Step and closes', async () => {
      const { component, builder } = create('edit', shared);
      const order: string[] = [];
      builder.dirty.set(true);
      builder.publishForm.mockImplementation(async () => {
        order.push('publish');
        // Like the real builder, publishing leaves nothing unsaved.
        builder.dirty.set(false);
        return detail({
          id: 'F1',
          status: 'PUBLISHED',
          currentVersionNo: 3,
          nameEn: 'Leak inspection',
          nameAr: 'فحص التسرب',
          departmentCode: '10',
          fieldActivityCode: 'LEAK',
        });
      });
      taskTypes.attachForm.mockImplementation(() => {
        order.push('attach');
        return of({ isSuccess: true });
      });
      used.mockImplementation(() => order.push('use'));
      close.mockImplementation(() => order.push('close'));

      await component.publishAndUse();

      expect(order).toEqual(['publish', 'attach', 'use', 'close']);
      expect(taskTypes.attachForm).toHaveBeenCalledWith('TT', 'F1');
      expect(used).toHaveBeenCalledWith({
        formId: 'F1',
        versionNo: 3,
        name: 'Leak inspection',
        departmentCode: '10',
        fieldActivityCode: 'LEAK',
      });
    });

    it('uses the Arabic name in an Arabic session', async () => {
      locale.set('ar');
      const { component, builder } = create('edit', shared);
      builder.dirty.set(true);
      builder.publishForm.mockResolvedValue(
        detail({
          id: 'F1',
          status: 'PUBLISHED',
          currentVersionNo: 3,
          nameEn: 'Leak inspection',
          nameAr: 'فحص التسرب',
        }),
      );
      await component.publishAndUse();
      expect(used.mock.lastCall![0].name).toBe('فحص التسرب');
    });

    it('does not publish again when the design is already the published version', async () => {
      const { component, builder } = create('edit', shared);
      builder.formStatus.set('PUBLISHED');
      builder.currentVersionNo.set(2);
      await component.publishAndUse();
      expect(builder.publishForm).not.toHaveBeenCalled();
      expect(used).toHaveBeenCalledWith(expect.objectContaining({ formId: 'F1', versionNo: 2 }));
    });

    it('stays open with the design intact and uses nothing when publishing fails', async () => {
      const { component, builder } = create('edit', shared);
      builder.dirty.set(true);
      builder.publishForm.mockResolvedValue(null);
      await component.publishAndUse();
      expect(taskTypes.attachForm).not.toHaveBeenCalled();
      expect(used).not.toHaveBeenCalled();
      expect(close).not.toHaveBeenCalled();
      expect(component.busy()).toBe(false);
    });

    it('stays open when filing fails, and a retry files again without publishing another version', async () => {
      const { component, builder } = create('edit', shared);
      builder.dirty.set(true);
      builder.publishForm.mockImplementation(async () => {
        builder.dirty.set(false);
        builder.formStatus.set('PUBLISHED');
        builder.currentVersionNo.set(3);
        return detail({
          id: 'F1',
          status: 'PUBLISHED',
          currentVersionNo: 3,
          departmentCode: '10',
          fieldActivityCode: 'LEAK',
        });
      });
      taskTypes.attachForm
        .mockReturnValueOnce(
          throwError(
            () =>
              new HttpErrorResponse({
                status: 403,
                error: { code: 'Forbidden', message: 'You may not change Task Types.' },
              }),
          ),
        )
        .mockReturnValueOnce(of({ isSuccess: true }));

      await component.publishAndUse();
      expect(component.error()).toBe('You may not change Task Types.');
      expect(used).not.toHaveBeenCalled();
      expect(close).not.toHaveBeenCalled();

      await component.publishAndUse();
      expect(builder.publishForm).toHaveBeenCalledTimes(1);
      expect(taskTypes.attachForm).toHaveBeenCalledTimes(2);
      expect(used).toHaveBeenCalledWith(expect.objectContaining({ versionNo: 3 }));
      expect(close).toHaveBeenCalledOnce();
    });
  });

  describe('closing with unsaved changes', () => {
    it('closes at once when nothing changed, or while still on the details', async () => {
      const { component } = create('edit', shared);
      await expect(component.requestClose()).resolves.toBe(true);
      expect(component.closePrompt()).toBe(false);
      const creating = create('create').component;
      await expect(creating.requestClose()).resolves.toBe(true);
    });

    it('offers Save draft, Discard changes and Keep editing', async () => {
      const { component, builder } = create('edit', shared);
      component.dirty.set(true);

      const saved = component.requestClose();
      expect(component.closePrompt()).toBe(true);
      await component.decide('save');
      await expect(saved).resolves.toBe(true);
      expect(builder.saveDraft).toHaveBeenCalledOnce();
      expect(component.closePrompt()).toBe(false);

      const discarded = component.requestClose();
      await component.decide('discard');
      await expect(discarded).resolves.toBe(true);
      expect(builder.saveDraft).toHaveBeenCalledOnce();

      const kept = component.requestClose();
      await component.decide('keep');
      await expect(kept).resolves.toBe(false);
    });

    it('keeps the window open when the draft cannot be saved', async () => {
      const { component, builder } = create('edit', shared);
      component.dirty.set(true);
      builder.saveDraft.mockResolvedValue(false);
      const closing = component.requestClose();
      await component.decide('save');
      await expect(closing).resolves.toBe(false);
      expect(component.closePrompt()).toBe(false);
    });

    it('refuses to close while a save or publish is still running', async () => {
      const { component } = create('edit', shared);
      component.busy.set(true);
      await expect(component.requestClose()).resolves.toBe(false);
    });

    it('registers itself with the host so the window can ask before closing', () => {
      const { component } = create('edit', shared);
      expect(component.context.requestClose).toBeTypeOf('function');
    });
  });

  describe('classification for the Step', () => {
    const unclassifiedShared: ActivityForm = {
      ...shared,
      departmentCode: '',
      fieldActivityCode: '',
    };
    const published = (over: object = {}) =>
      detail({
        id: 'NEW',
        status: 'PUBLISHED',
        currentVersionNo: 1,
        departmentCode: '10',
        fieldActivityCode: 'LEAK',
        ...over,
      });

    it('needs no choice when the Step, its parent or the form already has a classification', () => {
      expect(create('create').component.needsPicker()).toBe(false);
      expect(create('edit', shared, {}).component.needsPicker()).toBe(false);
      expect(create('duplicate', shared, {}).component.needsPicker()).toBe(false);
    });

    it('asks for the Field Activity Type when nothing supplies one, and will not create without it', async () => {
      forms.create.mockReturnValue(of({ isSuccess: true, value: detail() }));
      const { component } = create('create', undefined, {});
      expect(component.needsPicker()).toBe(true);
      await vi.waitFor(() => expect(component.activityTypes()).toHaveLength(1));
      expect(component.activityTypeLabel(component.activityTypes()[0])).toBe('Leak repair · Water');
      component.changeNameEn('Valve check');
      component.nameAr.set('فحص الصمام');
      await component.createForm();
      expect(forms.create).not.toHaveBeenCalled();
      expect(component.detailsValid()).toBe(false);

      component.activityTypeId.set('T1');
      await component.createForm();
      expect(forms.create).toHaveBeenCalledWith(
        expect.objectContaining({ departmentCode: '10', fieldActivityCode: 'LEAK' }),
      );
    });

    it('create → publish → use leaves the Step with a classification the workflow can publish', async () => {
      forms.create.mockReturnValue(
        of({
          isSuccess: true,
          value: detail({ departmentCode: '10', fieldActivityCode: 'LEAK' }),
        }),
      );
      const { component, builder } = create('create', undefined, {});
      await vi.waitFor(() => expect(component.activityTypes()).toHaveLength(1));
      component.changeNameEn('Valve check');
      component.nameAr.set('فحص الصمام');
      component.activityTypeId.set('T1');
      await component.createForm();
      builder.publishForm.mockImplementation(async () => {
        builder.dirty.set(false);
        return published();
      });
      await component.publishAndUse();

      expect(used).toHaveBeenCalledWith(
        expect.objectContaining({ departmentCode: '10', fieldActivityCode: 'LEAK' }),
      );
      // What the designer then saves in the Step: the publish check needs both codes.
      const unclassifiedStep = { taskTypeId: 'TT' };
      expect(pinFormToStep(unclassifiedStep, used.mock.lastCall![0], true)).toMatchObject({
        departmentCode: '10',
        fieldActivityCode: 'LEAK',
      });
      // A half-set classification is completed, a complete one is the Step's own and is kept.
      expect(pinFormToStep({ departmentCode: '10' }, used.mock.lastCall![0], true)).toMatchObject({
        departmentCode: '10',
        fieldActivityCode: 'LEAK',
      });
      expect(
        pinFormToStep(
          { departmentCode: '20', fieldActivityCode: 'OTHER' },
          used.mock.lastCall![0],
          true,
        ),
      ).toMatchObject({ departmentCode: '20', fieldActivityCode: 'OTHER' });
    });

    it('gives a copy of an unclassified form the Step’s classification and never copies twice on a retry', async () => {
      forms.clone.mockReturnValue(
        of({ isSuccess: true, value: detail({ id: 'COPY', category: 'INSPECTION' }) }),
      );
      forms.update
        .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })))
        .mockReturnValueOnce(
          of({
            isSuccess: true,
            value: detail({ id: 'COPY', departmentCode: '10', fieldActivityCode: 'LEAK' }),
          }),
        );
      const { component } = create('duplicate', unclassifiedShared);
      await component.createForm();
      expect(component.stage()).toBe('details');
      await component.createForm();
      expect(forms.clone).toHaveBeenCalledTimes(1);
      expect(forms.update).toHaveBeenLastCalledWith('COPY', {
        nameEn: 'New form',
        nameAr: 'نموذج جديد',
        category: 'INSPECTION',
        departmentCode: '10',
        fieldActivityCode: 'LEAK',
      });
      expect(component.stage()).toBe('design');
    });

    it('keeps a copy’s own classification when the source already has the Step’s', async () => {
      forms.clone.mockReturnValue(
        of({
          isSuccess: true,
          value: detail({ id: 'COPY', departmentCode: '10', fieldActivityCode: 'LEAK' }),
        }),
      );
      const { component } = create('duplicate', shared);
      await component.createForm();
      expect(forms.update).not.toHaveBeenCalled();
    });

    it('classifies an unclassified shared form in place, before it is published', async () => {
      const order: string[] = [];
      forms.update.mockImplementation(() => {
        order.push('classify');
        return of({
          isSuccess: true,
          value: detail({ id: 'F1', departmentCode: '10', fieldActivityCode: 'LEAK' }),
        });
      });
      const { component, builder } = create('edit', unclassifiedShared, {});
      expect(component.needsPicker()).toBe(true);
      builder.publishForm.mockImplementation(async () => {
        order.push('publish');
        builder.dirty.set(false);
        return published({ id: 'F1' });
      });

      await component.publishAndUse();
      expect(component.error()).toContain('Choose the Department');
      expect(builder.publishForm).not.toHaveBeenCalled();

      await vi.waitFor(() => expect(component.activityTypes()).toHaveLength(1));
      component.activityTypeId.set('T1');
      await component.publishAndUse();
      expect(order).toEqual(['classify', 'publish']);
      expect(forms.update).toHaveBeenCalledWith(
        'F1',
        expect.objectContaining({ departmentCode: '10', fieldActivityCode: 'LEAK' }),
      );
      expect(close).toHaveBeenCalledOnce();
    });

    describe('a shared form edited from a Step that already has a classification (inherited, no picker)', () => {
      const updatedF1 = () =>
        of({
          isSuccess: true,
          value: detail({
            id: 'F1',
            nameEn: 'Leak inspection',
            nameAr: 'فحص التسرب',
            category: 'INSPECTION',
            departmentCode: '10',
            fieldActivityCode: 'LEAK',
          }),
        });

      it('gives the unclassified shared form the Step’s classification in place, before publishing, with no choice asked', async () => {
        const order: string[] = [];
        forms.update.mockImplementation(() => {
          order.push('classify');
          return updatedF1();
        });
        const { component, builder } = create('edit', unclassifiedShared);
        expect(component.needsPicker()).toBe(false);
        expect(component.inheritedClassification()).toEqual(classified);
        builder.publishForm.mockImplementation(async () => {
          order.push('publish');
          builder.dirty.set(false);
          return published({ id: 'F1', currentVersionNo: 3 });
        });

        await component.publishAndUse();

        expect(component.error()).toBe('');
        expect(order).toEqual(['classify', 'publish']);
        // The form keeps its names and category; only the classification is filled in, from the Step.
        expect(forms.update).toHaveBeenCalledExactlyOnceWith('F1', {
          nameEn: 'Leak inspection',
          nameAr: 'فحص التسرب',
          category: 'INSPECTION',
          departmentCode: '10',
          fieldActivityCode: 'LEAK',
        });
        expect(taskTypes.attachForm).toHaveBeenCalledWith('TT', 'F1');
        expect(used).toHaveBeenCalledWith(
          expect.objectContaining({
            formId: 'F1',
            versionNo: 3,
            departmentCode: '10',
            fieldActivityCode: 'LEAK',
          }),
        );
        expect(close).toHaveBeenCalledOnce();
      });

      it('stops before publishing when the classification cannot be saved, and a retry classifies once then publishes once', async () => {
        forms.update
          .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })))
          .mockReturnValueOnce(updatedF1());
        const { component, builder } = create('edit', unclassifiedShared);
        builder.publishForm.mockImplementation(async () => {
          builder.dirty.set(false);
          return published({ id: 'F1' });
        });

        await component.publishAndUse();
        expect(component.error()).not.toBe('');
        expect(builder.publishForm).not.toHaveBeenCalled();
        expect(taskTypes.attachForm).not.toHaveBeenCalled();
        expect(used).not.toHaveBeenCalled();
        expect(close).not.toHaveBeenCalled();
        expect(component.busy()).toBe(false);

        await component.publishAndUse();
        expect(forms.update).toHaveBeenCalledTimes(2);
        expect(builder.publishForm).toHaveBeenCalledOnce();
        expect(used).toHaveBeenCalledOnce();
        expect(close).toHaveBeenCalledOnce();

        // Once classified, publishing again never asks the server to classify again.
        forms.update.mockClear();
        await component.publishAndUse();
        expect(forms.update).not.toHaveBeenCalled();
      });

      it('leaves a shared form that already has its own classification alone, even when the Step’s differs', async () => {
        const own: ActivityForm = { ...shared, departmentCode: '20', fieldActivityCode: 'OTHER' };
        const { component, builder } = create('edit', own);
        expect(component.needsPicker()).toBe(false);
        builder.publishForm.mockImplementation(async () => {
          builder.dirty.set(false);
          return published({ id: 'F1', departmentCode: '20', fieldActivityCode: 'OTHER' });
        });

        await component.publishAndUse();

        expect(forms.update).not.toHaveBeenCalled();
        expect(builder.publishForm).toHaveBeenCalledOnce();
        expect(used).toHaveBeenCalledWith(
          expect.objectContaining({ departmentCode: '20', fieldActivityCode: 'OTHER' }),
        );
      });

      it('keeps the Step’s classification out of the way of a form the author is only saving as a draft', async () => {
        const { component, builder } = create('edit', unclassifiedShared);
        builder.dirty.set(true);
        await expect(component.context.saveDraft!()).resolves.toBe(true);
        expect(builder.saveDraft).toHaveBeenCalledOnce();
        expect(forms.update).not.toHaveBeenCalled();
        expect(builder.publishForm).not.toHaveBeenCalled();
      });
    });
  });

  describe('closing after Publish and use, through the host’s handshake', () => {
    const publishedF1 = () =>
      detail({
        id: 'F1',
        status: 'PUBLISHED',
        currentVersionNo: 3,
        departmentCode: '10',
        fieldActivityCode: 'LEAK',
      });

    it('asks the host to close only once it is idle, and the host then agrees without a prompt', async () => {
      const { component, builder } = create('edit', shared);
      builder.dirty.set(true);
      component.dirty.set(true);
      builder.publishForm.mockImplementation(async () => {
        builder.dirty.set(false);
        return publishedF1();
      });
      let busyWhenClosing: boolean | undefined;
      let hostAnswer: Promise<boolean> | undefined;
      close.mockImplementation(() => {
        busyWhenClosing = component.busy();
        // What the window host does for the close request.
        hostAnswer = component.context.requestClose!();
      });

      await component.publishAndUse();

      expect(close).toHaveBeenCalledOnce();
      expect(busyWhenClosing).toBe(false);
      await expect(hostAnswer).resolves.toBe(true);
      expect(component.closePrompt()).toBe(false);
    });

    it('does not close, and keeps the newer edits, when the design changed while it was being published', async () => {
      const { component, builder } = create('edit', shared);
      builder.dirty.set(true);
      builder.publishForm.mockImplementation(async () => {
        // The author kept typing while the request was in flight.
        builder.dirty.set(true);
        return publishedF1();
      });

      await component.publishAndUse();

      expect(used).toHaveBeenCalledWith(expect.objectContaining({ formId: 'F1', versionNo: 3 }));
      expect(close).not.toHaveBeenCalled();
      expect(component.isDirty()).toBe(true);
      expect(component.error()).toContain('changed the form meanwhile');
      // Leaving afterwards still asks about those changes instead of discarding them.
      const closing = component.requestClose();
      expect(component.closePrompt()).toBe(true);
      await component.decide('keep');
      await expect(closing).resolves.toBe(false);
    });

    it('follows the builder’s own revision, not the last event it sent', () => {
      const { component, builder } = create('edit', shared);
      expect(component.isDirty()).toBe(false);
      builder.dirty.set(true);
      expect(component.isDirty()).toBe(true);
    });

    it('lets the host settle it: reports dirtiness, saves the draft and refuses while busy', async () => {
      const { component, builder } = create('edit', shared);
      expect(component.context.isDirty!()).toBe(false);
      builder.dirty.set(true);
      expect(component.context.isDirty!()).toBe(true);
      await expect(component.context.saveDraft!()).resolves.toBe(true);
      expect(builder.saveDraft).toHaveBeenCalledOnce();
      component.busy.set(true);
      await expect(component.context.saveDraft!()).resolves.toBe(false);
    });
  });

  describe('the rendered window', () => {
    @Component({
      selector: 'app-form-builder',
      standalone: true,
      template: '<input class="builder-input" aria-label="Field label" />',
      providers: [{ provide: FormBuilderComponent, useExisting: forwardRef(() => BuilderStub) }],
    })
    class BuilderStub {
      @Input() embeddedFormId: string | null = null;
      @Output() dirtyChange = new EventEmitter<boolean>();
      dirty = signal(false);
      formStatus = signal<string | null>('DRAFT');
      currentVersionNo = signal<number | null>(null);
      saveDraft = vi.fn().mockResolvedValue(true);
      publishForm = vi.fn();
    }

    function render(
      mode: FormEditorContext['mode'],
      form?: ActivityForm,
      classification: FormEditorContext['classification'] = classified,
    ) {
      TestBed.overrideComponent(WorkflowEmbeddedFormEditorComponent, {
        remove: { imports: [FormBuilderComponent] },
        add: { imports: [BuilderStub] },
      });
      const fixture = TestBed.createComponent(WorkflowEmbeddedFormEditorComponent);
      fixture.componentRef.setInput('context', {
        mode,
        taskTypeId: 'TT',
        stepName: 'Inspect',
        form,
        classification,
        use: used,
        close,
      } satisfies FormEditorContext);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      return { fixture, root, component: fixture.componentInstance };
    }
    const dialog = (root: HTMLElement) => root.querySelector<HTMLElement>('[role="alertdialog"]');

    it('shows the Field Activity Type choice, with the department, when nothing supplies one', async () => {
      const withChoice = render('create', undefined, {});
      await vi.waitFor(() => {
        withChoice.fixture.detectChanges();
        expect(
          withChoice.root.querySelectorAll('#form-activity-type option').length,
        ).toBeGreaterThan(1);
      });
      expect(withChoice.root.querySelector('#form-activity-type')!.textContent).toContain(
        'Leak repair · Water',
      );
    });

    it('does not ask for a classification when the Step already has one, and says so', () => {
      const classifiedForm = render('create');
      expect(classifiedForm.root.querySelector('#form-activity-type')).toBeNull();
      expect(classifiedForm.root.textContent).toContain('classified like this Step');
    });

    it('shows no classification choice when editing an unclassified shared form for a classified Step, and does when nothing supplies one', () => {
      const unclassified: ActivityForm = { ...shared, departmentCode: '', fieldActivityCode: '' };
      const inherited = render('edit', unclassified);
      expect(inherited.root.querySelector('app-form-builder')).not.toBeNull();
      expect(inherited.root.querySelector('#form-activity-type')).toBeNull();
    });

    it('shows the classification choice above the builder when an unclassified shared form meets an unclassified Step', () => {
      const unclassified: ActivityForm = { ...shared, departmentCode: '', fieldActivityCode: '' };
      const needed = render('edit', unclassified, {});
      expect(needed.root.querySelector('app-form-builder')).not.toBeNull();
      expect(needed.root.querySelector('#form-activity-type')).not.toBeNull();
    });

    it('is a real dialog of its own: focus is trapped, the background is inert and Escape means Keep editing', async () => {
      const { fixture, root, component } = render('edit', shared);
      const host = document.createElement('button');
      host.textContent = 'Close form editor';
      document.body.append(host);
      host.focus();
      const outside = vi.fn();
      document.addEventListener('keydown', outside);
      try {
        (component as unknown as { builder: BuilderStub }).builder.dirty.set(true);
        const closing = component.requestClose();
        fixture.detectChanges();
        const prompt = dialog(root)!;
        expect(prompt).not.toBeNull();
        expect(prompt.getAttribute('aria-modal')).toBe('true');
        // Everything behind the question cannot be reached or read.
        const background = root.querySelector('[inert]') as HTMLElement;
        expect(background).not.toBeNull();
        expect(background.getAttribute('aria-hidden')).toBe('true');
        expect(background.contains(prompt)).toBe(false);
        expect(background.querySelector('.builder-input')).not.toBeNull();
        expect(prompt.querySelectorAll('.cdk-focus-trap-anchor').length).toBe(0);
        expect(root.querySelectorAll('.cdk-focus-trap-anchor').length).toBe(2);
        await vi.waitFor(() => expect(prompt.contains(document.activeElement)).toBe(true));
        expect(document.activeElement!.textContent).toContain('Save draft');

        const escape = new KeyboardEvent('keydown', {
          key: 'Escape',
          bubbles: true,
          cancelable: true,
        });
        prompt.dispatchEvent(escape);
        await expect(closing).resolves.toBe(false);
        expect(escape.defaultPrevented).toBe(true);
        // The windows behind it never saw the key.
        expect(outside).not.toHaveBeenCalled();
        fixture.detectChanges();
        expect(dialog(root)).toBeNull();
        expect(root.querySelector('[inert]')).toBeNull();
        // Focus goes back to what had it before the question.
        await vi.waitFor(() => expect(document.activeElement).toBe(host));
      } finally {
        document.removeEventListener('keydown', outside);
        host.remove();
      }
    });

    it('returns focus after Keep editing and speaks Arabic in an Arabic session', async () => {
      locale.set('ar');
      const { fixture, root, component } = render('edit', shared);
      const host = document.createElement('button');
      document.body.append(host);
      host.focus();
      try {
        (component as unknown as { builder: BuilderStub }).builder.dirty.set(true);
        const closing = component.requestClose();
        fixture.detectChanges();
        expect(dialog(root)!.textContent).toContain('هل تريد حفظ تغييراتك');
        const keep = Array.from(dialog(root)!.querySelectorAll('button')).find((button) =>
          button.textContent!.includes('متابعة التحرير'),
        )!;
        keep.click();
        await expect(closing).resolves.toBe(false);
        await vi.waitFor(() => expect(document.activeElement).toBe(host));
      } finally {
        host.remove();
      }
    });

    it('keeps the editor inert for the whole of a publish, so nothing typed meanwhile can be marked saved', async () => {
      const { fixture, root, component } = render('edit', shared);
      const builder = (component as unknown as { builder: BuilderStub }).builder;
      const wrapper = () => root.querySelector('.builder-input')!.parentElement!.parentElement!;
      expect(wrapper().hasAttribute('inert')).toBe(false);
      let release!: (value: unknown) => void;
      builder.dirty.set(true);
      builder.publishForm.mockReturnValue(new Promise((resolve) => (release = resolve)));

      const publishing = component.publishAndUse();
      fixture.detectChanges();
      expect(component.busy()).toBe(true);
      expect(wrapper().hasAttribute('inert')).toBe(true);
      expect(wrapper().getAttribute('aria-busy')).toBe('true');

      release(null);
      await publishing;
      fixture.detectChanges();
      expect(wrapper().hasAttribute('inert')).toBe(false);
    });
  });
  it('labels the window by what it does, in both languages', () => {
    expect(create('edit', shared).component.title()).toBe('Edit shared form');
    expect(create('create').component.title()).toBe('Create form for this Step');
    expect(create('duplicate', shared).component.title()).toBe('Duplicate form for this Step');
    locale.set('ar');
    expect(create('edit', shared).component.title()).toBe('تعديل النموذج المشترك');
  });
});
