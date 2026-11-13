import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { AuthStore } from '@core/auth/auth.store';
import { AppContextService } from '@core/context/app-context.service';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowSelectedFormComponent } from './workflow-selected-form.component';
import { TranslateModule } from '@ngx-translate/core';
import { provideDynamicForms } from '@shared/components/dynamic-form/provide-dynamic-forms';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

describe('Selected workflow form', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [WorkflowSelectedFormComponent, TranslateModule.forRoot()],
      providers: [
        provideDynamicForms(),
        provideNoopAnimations(),
        { provide: LocaleService, useValue: { locale: () => 'en', isRtl: () => false } },
      ],
    }),
  );
  it('starts fresh answers when returning to the same form with a new visit', () => {
    const c = TestBed.runInInjectionContext(() => new WorkflowSelectedFormComponent());
    c.workItemId = 'work';
    c.selectedForm = { formId: 'f', versionNo: 1, visitId: 'first', schemaJson: '{"elements":[]}' };
    c.answers = { answer: 'previous' };
    c.ngOnChanges();
    const old = c.initialAnswers;
    c.selectedForm = { ...c.selectedForm, visitId: 'second' };
    c.answers = {};
    c.ngOnChanges();
    expect(c.initialAnswers).toEqual({});
    expect(c.initialAnswers).not.toBe(old);
  });
  it('renders a real required form, retains typed answers on polling and emits a submission payload', async () => {
    const fixture = TestBed.createComponent(WorkflowSelectedFormComponent);
    const selected = {
      formId: 'form',
      versionNo: 2,
      schemaJson: JSON.stringify({
        name_en: 'Activity form',
        elements: [{ type: 'text', data_name: 'notes', label_en: 'Notes', required: true }],
      }),
    };
    fixture.componentRef.setInput('workItemId', 'work');
    fixture.componentRef.setInput('selectedForm', selected);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.componentInstance.validate()).toBe(false);
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(input).not.toBeNull();
    const changed = vi.fn();
    fixture.componentInstance.answersChange.subscribe(changed);
    input.value = 'Completed inspection';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.componentInstance.validate()).toBe(true);
    expect(fixture.componentInstance.payload()['notes']).toBe('Completed inspection');
    expect(changed).toHaveBeenCalled();
    fixture.componentRef.setInput('selectedForm', { ...selected });
    fixture.componentRef.setInput('answers', { notes: 'Completed inspection' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input')).toBe(input);
    expect(input.value).toBe('Completed inspection');
  });
  it('keeps renderer inputs stable while task data polls and answers change', () => {
    const c = TestBed.runInInjectionContext(() => new WorkflowSelectedFormComponent());
    c.workItemId = 'work';
    c.selectedForm = { formId: 'f', versionNo: 1, schemaJson: '{"elements":[]}' };
    c.answers = { a: 'original' };
    c.ngOnChanges();
    const schema = c.definition,
      answers = c.initialAnswers;
    c.answers = { a: 'typed' };
    c.selectedForm = { ...c.selectedForm };
    c.ngOnChanges();
    expect(c.definition).toBe(schema);
    expect(c.initialAnswers).toBe(answers);
    c.readonly = true;
    c.ngOnChanges();
    expect(c.initialAnswers).toEqual({ a: 'typed' });
  });
  it('does not allow completion when the pinned schema is missing or malformed', () => {
    const c = TestBed.runInInjectionContext(() => new WorkflowSelectedFormComponent());
    c.selectedForm = { formId: 'f', versionNo: 1, schemaJson: 'broken' };
    c.ngOnChanges();
    expect(c.definition).toBeNull();
    expect(c.validate()).toBe(false);
  });
});

describe('Selected workflow form with a Step mapping snapshot', () => {
  const schema = JSON.stringify({
    name_en: 'Mapped form',
    elements: [
      { type: 'text', data_name: 'notes', label_en: 'Notes' },
      { type: 'text', data_name: 'customer.id', label_en: 'Customer' },
      { type: 'text', data_name: 'city', label_en: 'City', default_value: 'Cairo' },
      { type: 'numeric', data_name: 'amount', label_en: 'Amount' },
      { type: 'yes_no', data_name: 'approved', label_en: 'Approved' },
    ],
  });
  const selected = {
    formId: 'form-1',
    versionNo: 2,
    schemaJson: schema,
    visitId: 'visit-1',
    stepId: 'step-1',
  };
  const value = (
    fieldKey: string,
    mode: 'ReadOnly' | 'Prefill',
    v: unknown,
    over: Record<string, unknown> = {},
  ) => ({
    bindingId: `b-${fieldKey}`,
    formStepId: 'step-1',
    formId: 'form-1',
    formVersion: 2,
    fieldKey,
    mode,
    value: v,
    ...over,
  });
  const snapshot = (
    values: ReturnType<typeof value>[],
    status = 'Completed',
    invocationId = 'inv-1',
  ) => ({
    invocationId,
    visitId: 'visit-1',
    status,
    values,
  });

  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [WorkflowSelectedFormComponent, TranslateModule.forRoot()],
      providers: [
        provideDynamicForms(),
        provideNoopAnimations(),
        { provide: LocaleService, useValue: { locale: () => 'en', isRtl: () => false } },
      ],
    }),
  );

  async function open(
    mapping: unknown,
    answers: Record<string, unknown> = {},
    form: typeof selected = selected,
  ) {
    const fixture = TestBed.createComponent(WorkflowSelectedFormComponent);
    fixture.componentRef.setInput('workItemId', 'work-' + Math.random());
    fixture.componentRef.setInput('selectedForm', form);
    fixture.componentRef.setInput('answers', answers);
    fixture.componentRef.setInput('mappingSnapshot', mapping);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }
  it('locks a ReadOnly field and submits the exact typed value, not the control display form', async () => {
    const fixture = await open(
      snapshot([
        value('amount', 'ReadOnly', 12.5),
        value('approved', 'ReadOnly', false),
        value('notes', 'ReadOnly', null),
      ]),
    );
    const payload = fixture.componentInstance.payload();
    expect(payload['amount']).toBe(12.5);
    expect(payload['approved']).toBe(false);
    expect(payload['notes']).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="locked-note"]')).not.toBeNull();
    expect(
      fixture.nativeElement.querySelectorAll('.dynamic-field--locked').length,
    ).toBeGreaterThanOrEqual(3);
  });

  it('keeps a dotted data_name as one flat key in the model, the lock and the payload', async () => {
    const fixture = await open(snapshot([value('customer.id', 'ReadOnly', 'C-17')]));
    const payload = fixture.componentInstance.payload();
    expect(payload['customer.id']).toBe('C-17');
    expect('customer' in payload).toBe(false);
  });

  it('applies a Prefill once and never replaces what the person typed when the task polls again', async () => {
    const fixture = await open(snapshot([value('notes', 'Prefill', 'from workflow')]));
    expect(fixture.componentInstance.initialAnswers['notes']).toBe('from workflow');
    expect(fixture.componentInstance.payload()['notes']).toBe('from workflow');

    // The person edits; the host polls with an equal snapshot (new object) and the server's older answers.
    const changed = Array.from(fixture.nativeElement.querySelectorAll('input')).find(
      (i) => (i as HTMLInputElement).value === 'from workflow',
    ) as HTMLInputElement;
    changed.value = 'edited by user';
    changed.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    fixture.componentRef.setInput('mappingSnapshot', {
      ...snapshot([value('notes', 'Prefill', 'from workflow')]),
    });
    fixture.componentRef.setInput('answers', {});
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.componentInstance.payload()['notes']).toBe('edited by user');
  });

  it('a Prefill of null opens the field empty even when the form gives a default', async () => {
    const withNull = await open(snapshot([value('city', 'Prefill', null)]));
    expect(withNull.componentInstance.nullKeys).toEqual(['city']);
    expect(withNull.componentInstance.payload()['city']).toBeNull();
    const without = await open(null);
    expect(without.componentInstance.payload()['city']).toBe('Cairo');
  });

  describe('a ReadOnly null for a field that has a default', () => {
    // `extra` shows only while `city` is empty, so it tells the displayed model apart from the submitted one.
    const withDefault = {
      ...selected,
      schemaJson: JSON.stringify({
        name_en: 'Defaulted form',
        elements: [
          { type: 'text', data_name: 'city', label_en: 'City', default_value: 'Cairo' },
          {
            type: 'text',
            data_name: 'extra',
            label_en: 'Extra',
            visible_conditions: {
              match: 'all',
              conditions: [{ field: 'city', operator: 'is_empty', value: '' }],
              preserve_data: false,
            },
          },
        ],
      }),
    };
    const textInputs = (fixture: { nativeElement: HTMLElement }) =>
      Array.from(fixture.nativeElement.querySelectorAll('input[type="text"], input:not([type])'));

    it('shows the field empty, never the default, and keeps the form logic and the payload on null', async () => {
      const fixture = await open(
        snapshot([value('city', 'ReadOnly', null)]),
        {},
        withDefault as typeof selected,
      );
      // The displayed model: empty, not "Cairo", so the condition on `city` is satisfied and `extra` is on screen.
      expect(fixture.componentInstance.payload()['city']).toBeNull();
      const inputs = textInputs(fixture) as HTMLInputElement[];
      expect(inputs.map((i) => i.value)).not.toContain('Cairo');
      expect(inputs.length).toBe(2);
      expect(inputs.every((i) => i.value === '')).toBe(true);
      expect(inputs[0].disabled).toBe(true);
      expect(fixture.nativeElement.querySelector('.dynamic-field--locked')).not.toBeNull();
    });

    it('still shows the default, and hides what depends on it, when nothing is mandated', async () => {
      const fixture = await open(null, {}, withDefault as typeof selected);
      const inputs = textInputs(fixture) as HTMLInputElement[];
      expect(inputs.map((i) => i.value)).toEqual(['Cairo']);
      expect(fixture.componentInstance.payload()['city']).toBe('Cairo');
    });

    it('locks a non-null mandated value to that value, not the default', async () => {
      const fixture = await open(
        snapshot([value('city', 'ReadOnly', 'Giza')]),
        {},
        withDefault as typeof selected,
      );
      const inputs = textInputs(fixture) as HTMLInputElement[];
      expect(inputs.map((i) => i.value)).toEqual(['Giza']);
      expect(fixture.componentInstance.payload()['city']).toBe('Giza');
    });
  });

  describe('a browser hard reload (the component and every service are rebuilt, the tab storage stays)', () => {
    let who = { user: 'user-1', tenant: 'tenant-1' };
    const configure = () => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [WorkflowSelectedFormComponent, TranslateModule.forRoot()],
        providers: [
          provideDynamicForms(),
          provideNoopAnimations(),
          { provide: LocaleService, useValue: { locale: () => 'en', isRtl: () => false } },
          {
            provide: AuthStore,
            useValue: { user: () => ({ userId: who.user }), isAuthenticated: () => true },
          },
          {
            provide: AppContextService,
            useValue: { tenant: () => ({ id: who.tenant }), selectedParticipantId: () => '' },
          },
        ],
      });
    };
    // Both fields are text so the test can read and edit them as the person does.
    const textForm = {
      ...selected,
      schemaJson: JSON.stringify({
        name_en: 'Text form',
        elements: [
          { type: 'text', data_name: 'notes', label_en: 'Notes' },
          { type: 'text', data_name: 'approved', label_en: 'Approved' },
        ],
      }),
    };
    const mapped = () =>
      snapshot([value('notes', 'ReadOnly', null), value('approved', 'Prefill', 'initial-prefill')]);
    /** Opens the form the way a fresh page load does: server answers never saw the edit. */
    async function load(draftKey: string | null = 'attempt-1', visit = textForm, actor = '') {
      configure();
      const fixture = TestBed.createComponent(WorkflowSelectedFormComponent);
      fixture.componentRef.setInput('workItemId', 'work-reload');
      fixture.componentRef.setInput('selectedForm', visit);
      fixture.componentRef.setInput('answers', { approved: 'initial-prefill' });
      fixture.componentRef.setInput('serverAnswers', { approved: 'initial-prefill' });
      fixture.componentRef.setInput('mappingSnapshot', mapped());
      fixture.componentRef.setInput('draftKey', draftKey);
      fixture.componentRef.setInput('actorKey', actor);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      return fixture;
    }
    const field = (fixture: { nativeElement: HTMLElement }, value: string) =>
      Array.from(fixture.nativeElement.querySelectorAll('input')).find(
        (i) => (i as HTMLInputElement).value === value,
      ) as HTMLInputElement | undefined;
    const edit = async (fixture: Awaited<ReturnType<typeof load>>, from: string, to: string) => {
      const input = field(fixture, from)!;
      input.value = to;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
      await fixture.whenStable();
    };

    beforeEach(() => {
      sessionStorage.clear();
      who = { user: 'user-1', tenant: 'tenant-1' };
    });
    afterEach(() => sessionStorage.clear());

    it('keeps the edited Prefill and still shows the server-held null for the locked field', async () => {
      const first = await load();
      expect(field(first, 'initial-prefill')).toBeDefined();
      await edit(first, 'initial-prefill', 'edited-prefill');
      first.destroy();

      const reloaded = await load();
      expect(field(reloaded, 'edited-prefill')).toBeDefined();
      expect(field(reloaded, 'initial-prefill')).toBeUndefined();
      expect(reloaded.componentInstance.payload()['approved']).toBe('edited-prefill');
      expect(reloaded.componentInstance.payload()['notes']).toBeNull();
      expect(reloaded.componentInstance.lockedValues).toEqual({ notes: null });
    });

    it('does not restore without an edit: a reload then shows the frozen initial mapping', async () => {
      (await load()).destroy();
      expect(field(await load(), 'initial-prefill')).toBeDefined();
    });

    it('never submits a draft: restoring emits answers to the host but completes nothing', async () => {
      const first = await load();
      await edit(first, 'initial-prefill', 'edited-prefill');
      first.destroy();
      configure();
      const second = TestBed.createComponent(WorkflowSelectedFormComponent);
      const emitted = vi.fn();
      second.componentInstance.answersChange.subscribe(emitted);
      second.componentRef.setInput('workItemId', 'work-reload');
      second.componentRef.setInput('selectedForm', textForm);
      second.componentRef.setInput('answers', { approved: 'initial-prefill' });
      second.componentRef.setInput('mappingSnapshot', mapped());
      second.componentRef.setInput('draftKey', 'attempt-1');
      second.detectChanges();
      await second.whenStable();
      expect(emitted).toHaveBeenCalled();
      expect(emitted.mock.calls.at(-1)![0]).toMatchObject({
        approved: 'edited-prefill',
        notes: null,
      });
    });

    it('opens a new claim attempt, another user and another tenant with the server values', async () => {
      const first = await load();
      await edit(first, 'initial-prefill', 'edited-prefill');
      first.destroy();

      expect(field(await load('attempt-2'), 'initial-prefill')).toBeDefined();

      const again = await load();
      await edit(again, 'initial-prefill', 'edited-prefill');
      again.destroy();
      who = { user: 'user-2', tenant: 'tenant-1' };
      expect(field(await load(), 'initial-prefill')).toBeDefined();
      who = { user: 'user-1', tenant: 'tenant-2' };
      expect(field(await load(), 'initial-prefill')).toBeDefined();
    });

    it('keeps a draft typed while pending once the work item is claimed, because the attempt is the same', async () => {
      const first = await load('attempt-1');
      await edit(first, 'initial-prefill', 'edited-prefill');
      first.destroy();
      expect(field(await load('attempt-1'), 'edited-prefill')).toBeDefined();
    });

    it('does not restore a pending edit after a claim and release elsewhere rotated the attempt', async () => {
      const first = await load('attempt-1');
      await edit(first, 'initial-prefill', 'edited-prefill');
      first.destroy();
      // Returning to the still-pending item: same visit, same activation, a new attempt key.
      const returned = await load('attempt-2');
      expect(field(returned, 'initial-prefill')).toBeDefined();
      expect(field(returned, 'edited-prefill')).toBeUndefined();
      returned.destroy();
      expect(field(await load('attempt-1'), 'initial-prefill')).toBeDefined();
    });

    it('neither persists nor restores when the response carries no draft key', async () => {
      for (const missing of [null, '']) {
        const first = await load(missing);
        await edit(first, 'initial-prefill', 'edited-prefill');
        first.destroy();
        expect(Object.keys(sessionStorage).filter((k) => k.startsWith('nwfm.stepDraft'))).toEqual(
          [],
        );
        expect(field(await load(missing), 'initial-prefill')).toBeDefined();
      }
    });

    describe('when the actor or ownership attempt changes on the same rendered component', () => {
      /** Plays the host: it feeds every emitted answer set back in, as the workspace does with formValues. */
      const hosted = async (draftKey: string | null, actor: string) => {
        const fixture = await load(draftKey, textForm, actor);
        const emitted: Record<string, unknown>[] = [];
        fixture.componentInstance.answersChange.subscribe((v) => {
          emitted.push(v);
          fixture.componentRef.setInput('answers', v);
        });
        return { fixture, emitted };
      };
      /** Lets the microtask emission and the renderer's ngModel writes settle. */
      const settle = async (fixture: Awaited<ReturnType<typeof load>>) => {
        fixture.detectChanges();
        await fixture.whenStable();
        await new Promise((resolve) => setTimeout(resolve));
        fixture.detectChanges();
        await fixture.whenStable();
      };
      const change = async (
        fixture: Awaited<ReturnType<typeof load>>,
        inputs: Record<string, unknown>,
      ) => {
        for (const [name, value] of Object.entries(inputs))
          fixture.componentRef.setInput(name, value);
        await settle(fixture);
      };

      it('shows the next actor the server values, not the previous actor draft, and restores each own draft', async () => {
        const { fixture, emitted } = await hosted('attempt-1', 'actor-a');
        await edit(fixture, 'initial-prefill', 'edited-by-a');
        expect(fixture.componentInstance.payload()['approved']).toBe('edited-by-a');

        await change(fixture, { actorKey: 'actor-b' });
        expect(field(fixture, 'edited-by-a')).toBeUndefined();
        expect(field(fixture, 'initial-prefill')).toBeDefined();
        expect(fixture.componentInstance.payload()['approved']).toBe('initial-prefill');
        expect(emitted.at(-1)).toEqual({ approved: 'initial-prefill' });
        expect(fixture.componentInstance.payload()['notes']).toBeNull();

        await change(fixture, { actorKey: 'actor-a' });
        expect(field(fixture, 'edited-by-a')).toBeDefined();
        expect(fixture.componentInstance.payload()['approved']).toBe('edited-by-a');
        expect(fixture.componentInstance.payload()['notes']).toBeNull();
      });

      it('does not write the previous actor draft into the next actor scope', async () => {
        const { fixture } = await hosted('attempt-1', 'actor-a');
        await edit(fixture, 'initial-prefill', 'edited-by-a');
        await change(fixture, { actorKey: 'actor-b' });
        fixture.destroy();
        // A hard reload as the second actor finds nothing of the first actor's typing.
        expect(
          field(await load('attempt-1', textForm, 'actor-b'), 'initial-prefill'),
        ).toBeDefined();
      });

      it('discards the draft when the ownership attempt is replaced', async () => {
        const { fixture, emitted } = await hosted('attempt-1', '');
        await edit(fixture, 'initial-prefill', 'edited-prefill');
        await change(fixture, { draftKey: 'attempt-2' });
        expect(field(fixture, 'edited-prefill')).toBeUndefined();
        expect(fixture.componentInstance.payload()['approved']).toBe('initial-prefill');
        expect(emitted.at(-1)).toEqual({ approved: 'initial-prefill' });
      });

      it('discards the draft when the key is lost: null is not continuity', async () => {
        const { fixture } = await hosted('attempt-1', '');
        await edit(fixture, 'initial-prefill', 'edited-prefill');
        await change(fixture, { draftKey: null });
        expect(field(fixture, 'edited-prefill')).toBeUndefined();
        expect(fixture.componentInstance.payload()['approved']).toBe('initial-prefill');
      });

      it('does not let a later attempt inherit an edit typed under an earlier one', async () => {
        const { fixture } = await hosted('attempt-1', '');
        await edit(fixture, 'initial-prefill', 'edited-prefill');
        await change(fixture, { draftKey: 'attempt-2' });
        await change(fixture, { draftKey: 'attempt-1' });
        expect(field(fixture, 'edited-prefill')).toBeUndefined();
        expect(fixture.componentInstance.payload()['approved']).toBe('initial-prefill');
      });

      it('keeps an edit typed while pending when the same actor claims the work item', async () => {
        const { fixture, emitted } = await hosted('attempt-1', '');
        await edit(fixture, 'initial-prefill', 'edited-prefill');
        const before = emitted.length;
        // Claiming keeps the attempt key: the edit survives, and nothing is cleared or re-seeded.
        await change(fixture, { draftKey: 'attempt-1', readonly: false });
        expect(emitted.slice(before)).toEqual([]);
        expect(field(fixture, 'edited-prefill')).toBeDefined();
        expect(fixture.componentInstance.payload()['approved']).toBe('edited-prefill');
        fixture.destroy();
        expect(field(await load('attempt-1'), 'edited-prefill')).toBeDefined();
      });

      it('keeps the claimed draft through polling that re-sends the same attempt and actor', async () => {
        const { fixture } = await hosted('attempt-1', 'actor-a');
        await edit(fixture, 'initial-prefill', 'edited-prefill');
        await change(fixture, { draftKey: 'attempt-1', actorKey: 'actor-a' });
        await change(fixture, { mappingSnapshot: mapped() });
        expect(field(fixture, 'edited-prefill')).toBeDefined();
      });

      it('shows the authoritative server answers, not the local draft, when a reassignment makes it read-only', async () => {
        const { fixture, emitted } = await hosted('attempt-1', '');
        await edit(fixture, 'initial-prefill', 'edited-prefill');
        expect(fixture.componentInstance.payload()['approved']).toBe('edited-prefill');
        // Polling returns the same visit under another attempt and canAct=false.
        await change(fixture, {
          draftKey: 'attempt-2',
          readonly: true,
          serverAnswers: { approved: 'server-value' },
        });
        expect(field(fixture, 'edited-prefill')).toBeUndefined();
        expect(field(fixture, 'server-value')).toBeDefined();
        expect(
          field(fixture, 'server-value')!.disabled || field(fixture, 'server-value')!.readOnly,
        ).toBe(true);
        // The host's obsolete answers are replaced by the baseline, so nothing stale can be submitted.
        expect(emitted.at(-1)).toEqual({ approved: 'server-value' });
      });

      it('shows server answers to a read-only viewer even without an attempt change', async () => {
        const { fixture } = await hosted('attempt-1', '');
        await edit(fixture, 'initial-prefill', 'edited-prefill');
        await change(fixture, { readonly: true, serverAnswers: { approved: 'server-value' } });
        expect(field(fixture, 'server-value')).toBeDefined();
        expect(field(fixture, 'edited-prefill')).toBeUndefined();
      });
    });
  });

  it('applies nothing and blocks completion when a value targets another form version', async () => {
    const fixture = await open(snapshot([value('notes', 'Prefill', 'x', { formVersion: 1 })]));
    expect(fixture.componentInstance.mapping.state).toBe('invalid');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(fixture.componentInstance.initialAnswers['notes']).toBeUndefined();
    expect(fixture.componentInstance.validate()).toBe(false);
  });

  it('waits, without applying anything, while the Step values are still being prepared', async () => {
    const fixture = await open(snapshot([value('notes', 'Prefill', 'x')], 'Waiting'));
    expect(fixture.componentInstance.mapping.state).toBe('waiting');
    expect(fixture.componentInstance.initialAnswers['notes']).toBeUndefined();
    expect(fixture.nativeElement.querySelector('[role="status"]')).not.toBeNull();
    expect(fixture.componentInstance.validate()).toBe(false);
  });
});
