import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { of, Subject, throwError } from 'rxjs';
import { page as browserPage } from 'vitest/browser';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowLibraryComponent } from './workflow-library.component';
import { LibraryPage, LibraryWorkflow, WorkflowLibraryService } from './workflow-library.service';

const workflow = (id: string, changes: Partial<LibraryWorkflow> = {}): LibraryWorkflow => ({
  id,
  definitionKey: id,
  name: id,
  isActive: true,
  createdAt: '2026-10-01T00:00:00Z',
  updatedAt: '2026-10-25T00:00:00Z',
  versionCount: 2,
  versionId: id + '-v2',
  versionNumber: 2,
  versionStatus: 'Draft',
  kind: 'Main',
  activityCount: 3,
  scopes: [],
  inheritsScope: false,
  needsAttention: false,
  children: [],
  ...changes,
});
const child = workflow('Meter steps', {
  kind: 'Child',
  parentDefinitionId: 'Connection',
  inheritsScope: true,
});
const parent = workflow('Connection', {
  children: [child],
  scopes: [{ level: 'Cbu', code: 'R1', clusterCode: 'CC', nameEn: 'Riyadh', nameAr: 'الرياض' }],
});
const page = (changes: Partial<LibraryPage> = {}): LibraryPage => ({
  items: [parent],
  totalGroups: 1,
  totalWorkflows: 2,
  page: 1,
  pageSize: 10,
  scopes: parent.scopes,
  ...changes,
});

describe('Workflow library', () => {
  const list = vi.fn();
  const locale = signal('en');
  beforeEach(() => {
    list.mockReset().mockReturnValue(of(page()));
    locale.set('en');
    TestBed.configureTestingModule({
      imports: [WorkflowLibraryComponent],
      providers: [
        provideRouter([]),
        { provide: WorkflowLibraryService, useValue: { list } },
        { provide: LocaleService, useValue: { locale } },
      ],
    });
  });
  function render() {
    const fixture = TestBed.createComponent(WorkflowLibraryComponent);
    fixture.detectChanges();
    return fixture;
  }
  it('keeps nested rows within desktop and mobile layouts in both directions', async () => {
    await browserPage.viewport(1280, 900);
    const fixture = render();
    const host: HTMLElement = fixture.nativeElement;
    host.style.cssText = 'display:block;max-width:1200px;margin:24px auto;padding:16px';
    try {
      await browserPage.screenshot({ path: '__screenshots__/library-preview-desktop.png' });
      for (const direction of ['ltr', 'rtl']) {
        await browserPage.viewport(390, 844);
        host.dir = direction;
        locale.set(direction === 'rtl' ? 'ar' : 'en');
        fixture.detectChanges();
        await fixture.whenStable();
        expect(host.scrollWidth).toBeLessThanOrEqual(host.clientWidth);
        const rows = host.querySelectorAll<HTMLElement>('.workflow-row');
        for (const row of rows) expect(row.scrollWidth).toBeLessThanOrEqual(row.clientWidth);
        await browserPage.screenshot({
          path: `__screenshots__/library-preview-mobile-${direction}.png`,
        });
      }
    } finally {
      await browserPage.viewport(1280, 900);
      host.removeAttribute('dir');
    }
  });
  it('renders nested workflows, named scopes, dates and direct designer links', () => {
    const fixture = render();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.textContent).toContain('Riyadh');
    expect(root.textContent).toContain('Oct 25, 2026');
    expect(root.textContent).toContain('Inherited from parent');
    expect(root.querySelector('.children h3')?.textContent).toContain('Meter steps');
    expect(root.querySelector('.open-link')?.getAttribute('href')).toContain(
      '/Connection/versions/Connection-v2/designer',
    );
    const toggle = root.querySelector<HTMLButtonElement>('.expand-button')!;
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(root.querySelector<HTMLElement>('.children')?.hidden).toBe(true);
  });
  it('resets pagination on filter changes and uses the returned page for boundaries', () => {
    list.mockReturnValue(of(page({ totalGroups: 21 })));
    const fixture = render();
    list.mockReturnValue(of(page({ page: 2, totalGroups: 21 })));
    fixture.componentInstance.go(2);
    expect(list.mock.lastCall?.[0].page).toBe(2);
    expect(fixture.componentInstance.first()).toBe(11);
    fixture.componentInstance.query.search = 'Meter';
    list.mockReturnValue(of(page()));
    fixture.componentInstance.load();
    expect(list.mock.lastCall?.[0].page).toBe(1);
    expect(list.mock.lastCall?.[0].search).toBe('Meter');
  });

  it('restores filters, page and collapsed children after navigating back from the designer', () => {
    const first = render();
    first.componentInstance.toggle(parent.id);
    first.componentInstance.query.search = 'Meter';
    list.mockReturnValue(of(page({ page: 2, totalGroups: 21 })));
    first.componentInstance.go(2);
    first.destroy();
    const restored = render();
    expect(restored.componentInstance.query.search).toBe('Meter');
    expect(list.mock.lastCall?.[0].page).toBe(2);
    expect(restored.componentInstance.expanded().has(parent.id)).toBe(false);
  });
  it('cancels obsolete requests so a slow search cannot overwrite the latest result', () => {
    const old = new Subject<LibraryPage>();
    list.mockReturnValueOnce(old);
    const fixture = render();
    list.mockReturnValueOnce(of(page({ items: [workflow('Newest result')] })));
    fixture.componentInstance.load();
    old.next(page({ items: [workflow('Stale result')] }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Newest result');
    expect(fixture.nativeElement.textContent).not.toContain('Stale result');
  });
  it('distinguishes failure from an empty result and retries', () => {
    list.mockReturnValueOnce(throwError(() => new Error('offline')));
    const fixture = render();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('No workflows found');
    fixture.nativeElement.querySelector('.empty-state button').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Connection');
  });
  it('shows an actionable empty state and preserves Arabic names', () => {
    list.mockReturnValueOnce(of(page({ items: [], totalGroups: 0, totalWorkflows: 0 })));
    const fixture = render();
    expect(fixture.nativeElement.textContent).toContain('Reset filters');
    locale.set('ar');
    list.mockReturnValueOnce(
      of(page({ items: [workflow('Connection', { nameAr: 'توصيل المياه' })] })),
    );
    fixture.componentInstance.load();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('توصيل المياه');
    expect(fixture.nativeElement.textContent).toContain('متابعة التعديل');
  });
});
