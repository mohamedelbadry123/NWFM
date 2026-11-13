import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { LookupItem, LookupsService } from '@core/lookups/lookups.service';
import {
  WorkflowScopeChange,
  WorkflowScopeEditorComponent,
} from './workflow-scope-editor.component';
import { WorkflowScopeSummaryComponent } from './workflow-scope-summary.component';
import { WorkflowScopeRow, WorkflowWorkspaceService } from './workflow-workspace.service';
import { WorkflowLocationComponent } from './workflow-location.component';

const item = (code: string, parentCode?: string, nameAr = code): LookupItem => ({
  id: code,
  code,
  parentCode,
  nameEn: 'Name ' + code,
  nameAr,
  isActive: true,
});
const data: Record<string, LookupItem[]> = {
  Cluster: [item('A', undefined, 'قطاع أ'), item('B')],
  Cbu: [item('A1', 'A'), item('A2', 'A'), item('B1', 'B')],
  Branch: [item('AB1', 'A1'), item('BB1', 'B1')],
  OperationArea: [item('AA1', 'A1'), item('BA1', 'B1')],
};

describe('Organization scope entries', () => {
  const locale = signal('en');
  let changes: WorkflowScopeChange[];
  let pending: boolean[];

  beforeEach(() => {
    locale.set('en');
    changes = [];
    pending = [];
    TestBed.configureTestingModule({
      imports: [WorkflowScopeEditorComponent, WorkflowScopeSummaryComponent],
      providers: [
        { provide: LocaleService, useValue: { locale } },
        { provide: LookupsService, useValue: { listAll: (type: string) => of(data[type]) } },
      ],
    });
  });

  function render(
    scopes: WorkflowScopeChange['scopes'] = [],
    rows?: WorkflowScopeRow[],
    readonly = false,
  ) {
    const fixture = TestBed.createComponent(WorkflowScopeEditorComponent);
    fixture.componentInstance.scopeChange.subscribe((change) => changes.push(change));
    fixture.componentInstance.pendingChange.subscribe((value) => pending.push(value));
    fixture.componentRef.setInput('scopes', scopes);
    fixture.componentRef.setInput('scopeRows', rows);
    fixture.componentRef.setInput('readonly', readonly);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const select = (level: string) =>
      el.querySelector<HTMLSelectElement>(`select[data-level="${level}"]`)!;
    const choose = (level: string, value: string) => {
      select(level).value = value;
      select(level).dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    const button = (text: string) =>
      Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as
        HTMLButtonElement | undefined;
    const entries = () =>
      Array.from(el.querySelectorAll('[data-scope-entry]')).map((li) =>
        li.querySelector('span')!.textContent!.trim(),
      );
    return { fixture, el, select, choose, button, entries };
  }

  it('adds an entry from four single selects, with Cluster required and the rest optional', () => {
    const { fixture, choose, button, el, entries } = render();
    expect(el.querySelectorAll('select')).toHaveLength(4);
    button('Add')!.click();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Choose a Cluster');
    expect(changes).toEqual([]);

    choose('clusterCode', 'A');
    button('Add')!.click();
    fixture.detectChanges();
    expect(changes.at(-1)!.scopes).toEqual([{ level: 'Cluster', code: 'A', clusterCode: 'A' }]);
    expect(changes.at(-1)!.rows).toHaveLength(1);
    expect(entries()).toEqual(['Name A']);
  });

  it('keeps CBU, Branch and Operation Area unavailable until their parent is chosen and clears descendants on change', () => {
    const { choose, select } = render();
    expect(select('cbuCode').disabled).toBe(true);
    expect(select('branchCode').disabled).toBe(true);
    choose('clusterCode', 'A');
    expect(select('cbuCode').disabled).toBe(false);
    expect(Array.from(select('cbuCode').options).map((o) => o.value)).toEqual(['', 'A1', 'A2']);
    expect(select('branchCode').disabled).toBe(true);
    choose('cbuCode', 'A1');
    choose('branchCode', 'AB1');
    choose('operationAreaCode', 'AA1');
    expect(select('branchCode').disabled).toBe(false);
    expect(select('operationAreaCode').disabled).toBe(false);
    choose('cbuCode', 'A2');
    expect(select('branchCode').value).toBe('');
    expect(select('operationAreaCode').value).toBe('');
    choose('cbuCode', 'A1');
    choose('branchCode', 'AB1');
    choose('clusterCode', 'B');
    expect(select('cbuCode').value).toBe('');
    expect(select('branchCode').value).toBe('');
  });

  it('allows a repeated Cluster with different selections, rejects exact duplicates and keeps the union of Branch and Area', () => {
    const { fixture, choose, button, el } = render();
    const add = (cluster: string, cbu = '', branch = '', area = '') => {
      choose('clusterCode', cluster);
      if (cbu) choose('cbuCode', cbu);
      if (branch) choose('branchCode', branch);
      if (area) choose('operationAreaCode', area);
      button('Add')!.click();
      fixture.detectChanges();
    };
    add('A', 'A1', 'AB1', 'AA1');
    add('A', 'A2');
    expect(changes).toHaveLength(2);
    expect(changes.at(-1)!.scopes.map((s) => s.level + ':' + s.code)).toEqual([
      'Branch:AB1',
      'OperationArea:AA1',
      'Cbu:A2',
    ]);

    add('A', 'A2');
    expect(changes).toHaveLength(2);
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('already in the list');
  });

  it('shows two entries with +N more and expands to every entry with Edit and Remove', () => {
    const rows: WorkflowScopeRow[] = [
      { id: '1', clusterCode: 'A' },
      { id: '2', clusterCode: 'B' },
      { id: '3', clusterCode: 'A', cbuCode: 'A1' },
      { id: '4', clusterCode: 'A', cbuCode: 'A2' },
    ];
    const scopes = [
      { level: 'Cluster' as const, code: 'A', clusterCode: 'A' },
      { level: 'Cluster' as const, code: 'B', clusterCode: 'B' },
      { level: 'Cbu' as const, code: 'A1', clusterCode: 'A', cbuCode: 'A1' },
      { level: 'Cbu' as const, code: 'A2', clusterCode: 'A', cbuCode: 'A2' },
    ];
    const { fixture, el, entries, button } = render(scopes, rows);
    expect(entries()).toHaveLength(2);
    const more = button('+2 more')!;
    expect(more.getAttribute('aria-expanded')).toBe('false');
    more.click();
    fixture.detectChanges();
    expect(entries()).toHaveLength(4);
    expect(button('Show fewer')!.getAttribute('aria-expanded')).toBe('true');
    expect(el.querySelectorAll('[data-scope-entry] button')).toHaveLength(8);
    expect(el.querySelector('[data-scope-entry="3"]')!.textContent).toContain(
      'Already covered by Name A',
    );
  });

  it('edits an entry in place with Save changes, and Cancel leaves it untouched', () => {
    const rows: WorkflowScopeRow[] = [{ id: '1', clusterCode: 'A', cbuCode: 'A1' }];
    const { fixture, el, choose, button, select, entries } = render(
      [{ level: 'Cbu', code: 'A1', clusterCode: 'A', cbuCode: 'A1' }],
      rows,
    );
    button('Edit')!.click();
    fixture.detectChanges();
    expect(select('clusterCode').value).toBe('A');
    expect(select('cbuCode').value).toBe('A1');
    expect(button('Save changes')).toBeDefined();

    choose('cbuCode', 'A2');
    expect(pending.at(-1)).toBe(true);
    button('Cancel')!.click();
    fixture.detectChanges();
    expect(changes).toEqual([]);
    expect(pending.at(-1)).toBe(false);
    expect(entries()).toEqual(['Name A / Name A1']);

    button('Edit')!.click();
    fixture.detectChanges();
    choose('cbuCode', 'A2');
    button('Save changes')!.click();
    fixture.detectChanges();
    expect(changes.at(-1)!.rows).toEqual([{ id: '1', clusterCode: 'A', cbuCode: 'A2' }]);
    expect(changes.at(-1)!.scopes).toEqual([
      { level: 'Cbu', code: 'A2', clusterCode: 'A', cbuCode: 'A2' },
    ]);
    expect(el.querySelectorAll('[data-scope-entry]')).toHaveLength(1);
  });

  it('removes only the chosen entry', () => {
    const rows: WorkflowScopeRow[] = [
      { id: '1', clusterCode: 'A' },
      { id: '2', clusterCode: 'B' },
    ];
    const { fixture, button } = render(
      [
        { level: 'Cluster', code: 'A', clusterCode: 'A' },
        { level: 'Cluster', code: 'B', clusterCode: 'B' },
      ],
      rows,
    );
    button('Remove')!.click();
    fixture.detectChanges();
    expect(changes.at(-1)!.rows.map((r) => r.id)).toEqual(['2']);
    expect(changes.at(-1)!.scopes.map((s) => s.code)).toEqual(['B']);
  });

  it('reports an unfinished entry and blocks Edit and Remove until it is added or cancelled', () => {
    const { fixture, el, choose, button } = render(
      [{ level: 'Cluster', code: 'B', clusterCode: 'B' }],
      [{ id: '1', clusterCode: 'B' }],
    );
    expect(pending).toEqual([]);
    choose('clusterCode', 'A');
    expect(pending.at(-1)).toBe(true);
    expect(el.textContent).toContain('Add this entry or cancel it');
    expect(button('Edit')!.disabled).toBe(true);
    expect(button('Remove')!.disabled).toBe(true);
    button('Cancel')!.click();
    fixture.detectChanges();
    expect(pending.at(-1)).toBe(false);
    expect(changes).toEqual([]);
    choose('clusterCode', 'A');
    button('Add')!.click();
    fixture.detectChanges();
    expect(pending.at(-1)).toBe(false);
  });

  it('cancels an entry with Escape without leaving the keyboard flow', () => {
    const { fixture, el, choose } = render();
    choose('clusterCode', 'A');
    el.querySelector('[role="group"]')!.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
    );
    fixture.detectChanges();
    expect(pending.at(-1)).toBe(false);
  });

  it('does not write anything when older scopes are only displayed', () => {
    const scopes = [
      { level: 'Branch' as const, code: 'AB1', clusterCode: 'A', cbuCode: 'A1' },
      { level: 'Cluster' as const, code: 'B', clusterCode: 'B' },
    ];
    const { entries } = render(scopes);
    expect(entries()).toEqual(['Name A / Name A1 / Branch: Name AB1', 'Name B']);
    expect(changes).toEqual([]);
  });

  it('is read only for a published version, yet the entries still expand', () => {
    const rows: WorkflowScopeRow[] = ['A', 'B', 'A1'].map((code, i) => ({
      id: String(i),
      clusterCode: code === 'A1' ? 'A' : code,
      ...(code === 'A1' ? { cbuCode: 'A1' } : {}),
    }));
    const { fixture, el, button, entries } = render(
      [
        { level: 'Cluster', code: 'A', clusterCode: 'A' },
        { level: 'Cluster', code: 'B', clusterCode: 'B' },
        { level: 'Cbu', code: 'A1', clusterCode: 'A', cbuCode: 'A1' },
      ],
      rows,
      true,
    );
    expect(el.querySelectorAll('select')).toHaveLength(0);
    expect(button('Add')).toBeUndefined();
    expect(button('Edit')).toBeUndefined();
    button('+1 more')!.click();
    fixture.detectChanges();
    expect(entries()).toHaveLength(3);
  });

  it('keeps a saved unit that is no longer available visible instead of swapping it', () => {
    const { fixture, el, button, select } = render(
      [{ level: 'Cluster', code: 'GONE', clusterCode: 'GONE' }],
      [{ id: '1', clusterCode: 'GONE' }],
    );
    expect(el.textContent).toContain('Some saved units are no longer available');
    button('Edit')!.click();
    fixture.detectChanges();
    expect(select('clusterCode').value).toBe('GONE');
  });

  it('renders Arabic labels and names', () => {
    locale.set('ar');
    const { el, entries } = render(
      [{ level: 'Cluster', code: 'A', clusterCode: 'A' }],
      [{ id: '1', clusterCode: 'A' }],
    );
    expect(el.querySelector('legend')!.textContent).toBe('النطاق التنظيمي');
    expect(el.textContent).toContain('إضافة');
    expect(entries()).toEqual(['قطاع أ']);
  });
});

describe('Scope summary', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [WorkflowScopeSummaryComponent],
      providers: [{ provide: LocaleService, useValue: { locale: () => 'en' } }],
    }),
  );

  it('shows two chips and expands the rest from a button that reports its state', () => {
    const fixture = TestBed.createComponent(WorkflowScopeSummaryComponent);
    fixture.componentRef.setInput('labels', ['One', 'Two', 'Three', 'Four']);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('li')).toHaveLength(2);
    const more = el.querySelector('button')!;
    expect(more.textContent!.trim()).toBe('+2 more');
    expect(more.getAttribute('aria-expanded')).toBe('false');
    more.click();
    fixture.detectChanges();
    expect(el.querySelectorAll('li')).toHaveLength(4);
    expect(el.querySelector('button')!.getAttribute('aria-expanded')).toBe('true');
  });

  it('offers no button when everything fits, and shows the empty text', () => {
    const fixture = TestBed.createComponent(WorkflowScopeSummaryComponent);
    fixture.componentRef.setInput('labels', []);
    fixture.componentRef.setInput('empty', 'Not configured');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Not configured');
    vi.restoreAllMocks();
  });
});

describe('Read-only organization scope in the workflow setup', () => {
  const locale = signal('en');

  beforeEach(() => {
    locale.set('en');
    TestBed.configureTestingModule({
      imports: [WorkflowLocationComponent],
      providers: [
        { provide: LocaleService, useValue: { locale } },
        { provide: LookupsService, useValue: { listAll: (type: string) => of(data[type]) } },
        { provide: WorkflowWorkspaceService, useValue: { references: () => of([]) } },
      ],
    });
  });

  function renderPublished(readonly: boolean) {
    const fixture = TestBed.createComponent(WorkflowLocationComponent);
    fixture.componentRef.setInput('settings', {
      kind: 'Main',
      schemaVersion: 2,
      organizationScopes: [
        { level: 'Cluster', code: 'A', clusterCode: 'A' },
        { level: 'Cluster', code: 'B', clusterCode: 'B' },
        { level: 'Cbu', code: 'A1', clusterCode: 'A' },
        { level: 'Cbu', code: 'B1', clusterCode: 'B' },
      ],
    });
    fixture.componentRef.setInput('readonly', readonly);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }
  const entries = (el: HTMLElement) => el.querySelectorAll('[data-scope-entry]');
  const more = (el: HTMLElement) =>
    Array.from(el.querySelectorAll('button')).find((b) => /more|أخرى|fewer/.test(b.textContent!));

  it('keeps “+N more” enabled and working inside the published (read-only) wrapper', () => {
    const { fixture, el } = renderPublished(true);
    // Compact: two entries, the rest behind the disclosure.
    expect(entries(el)).toHaveLength(2);
    const button = more(el)!;
    expect(button).toBeDefined();
    expect(button.textContent!.trim()).toBe('+2 more');
    // No ancestor disables it: not a disabled fieldset, and not a disabled button.
    expect(button.disabled).toBe(false);
    expect(button.matches(':disabled')).toBe(false);
    expect(button.closest('fieldset[disabled]')).toBeNull();
    expect(button.getAttribute('aria-expanded')).toBe('false');

    button.click();
    fixture.detectChanges();
    expect(entries(el)).toHaveLength(4);
    expect(more(el)!.getAttribute('aria-expanded')).toBe('true');
    expect(more(el)!.textContent!.trim()).toBe('Show fewer');
    more(el)!.click();
    fixture.detectChanges();
    expect(entries(el)).toHaveLength(2);
  });

  it('still offers no way to edit a published scope: no selects, Add, Edit or Remove', async () => {
    const { fixture, el } = renderPublished(true);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelectorAll('select[data-level]')).toHaveLength(0);
    const labels = Array.from(el.querySelectorAll('button')).map((b) => b.textContent!.trim());
    expect(labels.filter((text) => ['Add', 'Edit', 'Remove'].includes(text))).toEqual([]);
    // The workflow type control is disabled on its own, not through an ancestor.
    const controls = Array.from(el.querySelectorAll<HTMLSelectElement>('select'));
    expect(controls.length).toBeGreaterThan(0);
    expect(controls.every((control) => control.disabled)).toBe(true);
  });

  it('keeps the editable draft usable', () => {
    const { el } = renderPublished(false);
    expect(el.querySelectorAll('select[data-level]')).toHaveLength(4);
    expect(el.querySelector('select[data-level="clusterCode"]')!.hasAttribute('disabled')).toBe(
      false,
    );
  });
});
