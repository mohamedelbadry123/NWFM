import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LocaleService } from '@core/i18n/locale.service';
import { MappingEditContext } from './mapping-edit-context';
import {
  MappingSelectorsComponent,
  durationSeconds,
  newSelector,
  splitDuration,
} from './mapping-selectors.component';
import type { MappingDefinitionChoice, RelatedInstanceSelector } from './mapping.models';

const CHOICES: MappingDefinitionChoice[] = [
  { definitionId: 'def-a', versionId: 'ver-a', name: 'Customer onboarding', versionNumber: 3 },
  { definitionId: 'def-b', versionId: 'ver-b', name: 'Credit check', versionNumber: 1 },
];

@Component({
  standalone: true,
  imports: [MappingSelectorsComponent],
  providers: [MappingEditContext],
  template: `<app-mapping-selectors
    [selectors]="selectors()"
    (selectorsChange)="selectors.set($event); changes.push($event)"
  />`,
})
class HostComponent {
  readonly selectors = signal<RelatedInstanceSelector[]>([]);
  readonly changes: RelatedInstanceSelector[][] = [];
}

describe('Related-instance duration helpers', () => {
  it('shows the largest whole unit and stores seconds', () => {
    expect(splitDuration(86400)).toEqual({ amount: 1, unit: 'days' });
    expect(splitDuration(7200)).toEqual({ amount: 2, unit: 'hours' });
    expect(splitDuration(90)).toEqual({ amount: 2, unit: 'minutes' });
    expect(durationSeconds(2, 'hours')).toBe(7200);
    expect(durationSeconds(3, 'days')).toBe(259200);
  });
});

describe('Related-instance selectors', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [{ provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } }],
    }),
  );

  function mount(selectors: RelatedInstanceSelector[] = [], readonly = false) {
    const fixture = TestBed.createComponent(HostComponent);
    const ctx = fixture.debugElement.injector.get(MappingEditContext);
    ctx.choices.set(CHOICES);
    ctx.readonly.set(readonly);
    fixture.componentInstance.selectors.set(selectors);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const change = (select: HTMLSelectElement | HTMLInputElement, value: string) => {
      select.value = value;
      select.dispatchEvent(
        new Event(select instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }),
      );
      fixture.detectChanges();
    };
    const labelled = <T extends HTMLElement>(text: string, within: ParentNode = el) =>
      Array.from(within.querySelectorAll('label'))
        .find((l) => l.textContent!.includes(text))!
        .querySelector<T>('select, input')!;
    return { fixture, el, host: fixture.componentInstance, change, labelled };
  }

  it('adds a related workflow with the safe defaults and a stable id', () => {
    const { el, host, change } = mount();
    expect(el.querySelector('[data-selector]')).toBeNull();
    change(el.querySelector<HTMLSelectElement>('select.wf-input')!, 'ver-a');
    const [selector] = host.selectors();
    expect(selector).toEqual({
      id: expect.stringMatching(/^related/),
      workflowDefinitionId: 'def-a',
      workflowVersionId: 'ver-a',
      keyKind: 'BusinessKey',
      keyValue: expect.objectContaining({ op: 'literal' }),
      multipleMatches: 'Attention',
      missing: { mode: 'Attention' },
    });
    expect(el.querySelector(`[data-selector="${selector.id}"]`)).not.toBeNull();
    expect(selector).not.toHaveProperty('startedByUserId');
    expect(selector).not.toHaveProperty('status');
    expect(selector).not.toHaveProperty('windowSeconds');
  });

  it('never reuses an id, even after another selector was removed', () => {
    const first = newSelector(CHOICES[0], []);
    const second = newSelector(CHOICES[1], [first.id]);
    expect(second.id).not.toBe(first.id);
  });

  it('a selector Fallback is { mode: "Fallback", fallback: null } and never an invented instance', () => {
    const { host, change, labelled, el } = mount([newSelector(CHOICES[0], [])]);
    change(labelled('If no instance matches'), 'Fallback');
    expect(host.selectors()[0].missing).toEqual({ mode: 'Fallback', fallback: null });
    expect(el.textContent).toContain('No instance is invented');
  });

  it('Wait carries a deadline and a timeout outcome, editable in whole units', () => {
    const { host, change, labelled, el } = mount([newSelector(CHOICES[0], [])]);
    change(labelled('If no instance matches'), 'Wait');
    expect(host.selectors()[0].missing).toEqual({
      mode: 'Wait',
      deadlineSeconds: 86400,
      timeoutOutcome: 'timeout',
    });
    change(labelled('Wait up to'), '2');
    expect(host.selectors()[0].missing?.deadlineSeconds).toBe(2 * 86400);
    const unit = Array.from(el.querySelectorAll<HTMLSelectElement>('select.wf-input')).find((s) =>
      Array.from(s.options).some((o) => o.value === 'hours'),
    )!;
    change(unit, 'hours');
    expect(host.selectors()[0].missing?.deadlineSeconds).toBe(2 * 3600);
    change(labelled('Outcome when the wait ends'), 'escalate');
    expect(host.selectors()[0].missing).toEqual({
      mode: 'Wait',
      deadlineSeconds: 7200,
      timeoutOutcome: 'escalate',
    });
    change(labelled('If no instance matches'), 'Attention');
    expect(host.selectors()[0].missing).toEqual({ mode: 'Attention' });
  });

  it('keeps optional criteria absent until chosen, then writes them as exact members', () => {
    const { fixture, host, change, labelled, el } = mount([newSelector(CHOICES[0], [])]);
    change(labelled('If several instances match'), 'Latest');
    expect(host.selectors()[0].multipleMatches).toBe('Latest');

    change(labelled('Only instances with status'), 'Completed');
    expect(host.selectors()[0].status).toBe('Completed');
    change(labelled('Only instances with status'), '');
    expect(host.selectors()[0]).not.toHaveProperty('status');

    const starter = labelled<HTMLInputElement>('started by a specific user');
    starter.checked = true;
    starter.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect(host.selectors()[0].startedByUserId).toEqual(expect.objectContaining({ op: 'literal' }));
    expect(el.querySelector('[data-slot$=".starter"]')).not.toBeNull();
    starter.checked = false;
    starter.dispatchEvent(new Event('change', { bubbles: true }));
    expect(host.selectors()[0]).not.toHaveProperty('startedByUserId');

    const window = labelled<HTMLInputElement>('within a time window');
    window.checked = true;
    window.dispatchEvent(new Event('change', { bubbles: true }));
    expect(host.selectors()[0].windowSeconds).toBe(86400);
  });

  it('renders the key expression as a mapping slot of its own', () => {
    const selector = newSelector(CHOICES[0], []);
    const { el } = mount([selector]);
    expect(el.querySelector(`[data-slot="selector.${selector.id}.key"]`)).not.toBeNull();
  });

  it('keeps a selector whose workflow is no longer offered and says so', () => {
    const gone = { ...newSelector(CHOICES[0], []), workflowVersionId: 'ver-gone' };
    const { el, host } = mount([gone]);
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('no longer offered');
    expect(host.selectors()[0].workflowVersionId).toBe('ver-gone');
  });

  it('removes a selector on request, and is fully read-only for a published version', () => {
    const { el, host } = mount([newSelector(CHOICES[0], [])]);
    el.querySelector<HTMLButtonElement>('button.mx-danger')!.click();
    expect(host.selectors()).toEqual([]);

    const published = mount([newSelector(CHOICES[0], [])], true);
    expect(published.el.querySelector('fieldset')!.disabled).toBe(true);
    expect(published.el.querySelector('button.mx-danger')).toBeNull();
    expect(published.host.changes).toEqual([]);
  });
});
