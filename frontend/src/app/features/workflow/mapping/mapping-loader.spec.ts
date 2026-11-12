import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { LocaleService } from '@core/i18n/locale.service';
import { MappingEditContext, type MappingTarget } from './mapping-edit-context';
import { loadMappingCatalog, loadRelatedCatalog, syncRelatedCatalogs } from './mapping-loader';
import { emptyNode } from './mapping-config';
import type { WorkflowMappingService } from './workflow-mapping.service';
import type { MappingCatalog, RelatedInstanceSelector, SourceDescriptor } from './mapping.models';

const sourceOf = (versionId: string, key: string): SourceDescriptor => ({
  reference: {
    workflowDefinitionId: 'def',
    workflowVersionId: versionId,
    nodeKey: 'Task_1',
    kind: 'FormField',
    fieldKey: key,
    jsonPointer: '',
  },
  label: key,
  breadcrumbs: [key],
  schema: { type: 'string' },
  availability: 'SubmittedForm',
});
const catalogOf = (versionId: string, key: string): MappingCatalog => ({
  sources: [sourceOf(versionId, key)],
  targets: [],
  diagnostics: [],
});
const selector = (id: string, definition: string, version: string): RelatedInstanceSelector => ({
  id,
  workflowDefinitionId: definition,
  workflowVersionId: version,
  keyKind: 'BusinessKey',
  keyValue: emptyNode(),
  multipleMatches: 'Attention',
  missing: { mode: 'Attention' },
});

/** A request that ignores unsubscription (an HTTP call that cannot be aborted) and is answered by hand. */
function unabortable() {
  const observers: Array<{ next: (c: MappingCatalog) => void; error: (e: unknown) => void }> = [];
  const source = {
    subscribe: (observer: (typeof observers)[number]) => {
      observers.push(observer);
      return { unsubscribe: () => undefined };
    },
  } as unknown as Observable<MappingCatalog>;
  return { source, observers };
}

describe('related catalog loading', () => {
  let ctx: MappingEditContext;
  const relatedCatalog = vi.fn();
  const api = { relatedCatalog } as unknown as WorkflowMappingService;

  beforeEach(() => {
    relatedCatalog.mockReset();
    TestBed.configureTestingModule({
      providers: [
        MappingEditContext,
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
      ],
    });
    ctx = TestBed.inject(MappingEditContext);
  });

  it('requests a catalog per selector once, and not again while its pins are unchanged', () => {
    relatedCatalog.mockImplementation((_d: string, v: string) => of(catalogOf(v, `f_${v}`)));
    ctx.selectors.set([selector('s1', 'def-a', 'ver-a'), selector('s2', 'def-b', 'ver-b')]);
    syncRelatedCatalogs(ctx, api);
    syncRelatedCatalogs(ctx, api);
    expect(relatedCatalog).toHaveBeenCalledTimes(2);
    expect(ctx.relatedSources().map((s) => s.reference.selectorId)).toEqual(['s1', 's2']);
  });

  it('reloads when the same selector id is retargeted to another definition or version', () => {
    relatedCatalog.mockImplementation((_d: string, v: string) => of(catalogOf(v, `f_${v}`)));
    ctx.selectors.set([selector('s1', 'def-a', 'ver-a')]);
    syncRelatedCatalogs(ctx, api);
    ctx.selectors.set([selector('s1', 'def-a', 'ver-a2')]);
    syncRelatedCatalogs(ctx, api);
    ctx.selectors.set([selector('s1', 'def-b', 'ver-a2')]);
    syncRelatedCatalogs(ctx, api);
    expect(relatedCatalog.mock.calls).toEqual([
      ['def-a', 'ver-a'],
      ['def-a', 'ver-a2'],
      ['def-b', 'ver-a2'],
    ]);
    expect(ctx.relatedSources().map((s) => s.reference.workflowVersionId)).toEqual(['ver-a2']);
  });

  it('rejects an out-of-order answer for a superseded pin, even from a request that cannot be aborted', () => {
    const first = unabortable();
    const second = unabortable();
    relatedCatalog.mockReturnValueOnce(first.source).mockReturnValueOnce(second.source);
    ctx.selectors.set([selector('s1', 'def', 'ver-a')]);
    syncRelatedCatalogs(ctx, api);
    ctx.selectors.set([selector('s1', 'def', 'ver-b')]);
    syncRelatedCatalogs(ctx, api);

    second.observers[0].next(catalogOf('ver-b', 'beta'));
    first.observers[0].next(catalogOf('ver-a', 'alpha'));
    expect(ctx.relatedSources().map((s) => s.reference.fieldKey)).toEqual(['beta']);

    // The old request failing late must not turn the newer, healthy catalog into an error either.
    first.observers[0].error(new Error('late'));
    expect(ctx.related()['s1']).not.toBe('error');
    expect(ctx.relatedSources().map((s) => s.reference.fieldKey)).toEqual(['beta']);
  });

  it('does not show the old catalog while the retargeted one is still loading', () => {
    const slow = unabortable();
    relatedCatalog
      .mockReturnValueOnce(of(catalogOf('ver-a', 'alpha')))
      .mockReturnValueOnce(slow.source);
    ctx.selectors.set([selector('s1', 'def', 'ver-a')]);
    syncRelatedCatalogs(ctx, api);
    expect(ctx.relatedSources()).toHaveLength(1);
    ctx.selectors.set([selector('s1', 'def', 'ver-b')]);
    syncRelatedCatalogs(ctx, api);
    expect(ctx.related()['s1']).toBe('loading');
    expect(ctx.relatedSources()).toEqual([]);
  });

  describe('sources already gathered in the tray', () => {
    const target = () => ({
      id: 't',
      label: 'target',
      apply: vi.fn(),
      occupied: () => false,
    });
    const gather = () => {
      relatedCatalog.mockImplementation((_d: string, v: string) => of(catalogOf(v, `f_${v}`)));
      ctx.selectors.set([selector('s1', 'def', 'ver-a'), selector('s2', 'def', 'ver-x')]);
      syncRelatedCatalogs(ctx, api);
      const [a, x] = ctx.relatedSources();
      ctx.toggleTray(a);
      ctx.toggleTray(x);
      ctx.toggleTray(sourceOf('tree', 'own'));
      return { a, x };
    };

    it('are dropped when their selector is retargeted; the others stay', () => {
      const { a } = gather();
      ctx.selectors.set([selector('s1', 'def', 'ver-b'), selector('s2', 'def', 'ver-x')]);
      syncRelatedCatalogs(ctx, api);
      expect(ctx.tray().map((d) => d.reference.fieldKey)).toEqual(['f_ver-x', 'own']);
      const t = target();
      expect(ctx.mapTo(t, a)).toBe(false);
      expect(t.apply).not.toHaveBeenCalled();
    });

    it('are dropped when their selector is removed', () => {
      const { a } = gather();
      ctx.selectors.set([selector('s2', 'def', 'ver-x')]);
      syncRelatedCatalogs(ctx, api);
      expect(ctx.tray().map((d) => d.reference.fieldKey)).toEqual(['f_ver-x', 'own']);
      const t = target();
      expect(ctx.mapTo(t, a)).toBe(false);
      expect(t.apply).not.toHaveBeenCalled();
    });

    it('cannot be mapped or picked in the moment before the reload starts', () => {
      // The selector already points elsewhere but nothing has synced yet (the effect has not run).
      const { a } = gather();
      ctx.selectors.set([selector('s1', 'def', 'ver-b'), selector('s2', 'def', 'ver-x')]);
      expect(ctx.liveTray().map((d) => d.reference.fieldKey)).toEqual(['f_ver-x', 'own']);
      expect(ctx.inTray(a)).toBe(false);
      const t = target();
      expect(ctx.mapTo(t, a)).toBe(false);
      expect(t.apply).not.toHaveBeenCalled();
    });

    it('refuses a related source that was never gathered but is no longer offered', () => {
      const { a } = gather();
      ctx.tray.set([]);
      ctx.selectors.set([]);
      syncRelatedCatalogs(ctx, api);
      expect(ctx.isCurrent(a)).toBe(false);
      const t = target();
      expect(ctx.mapTo(t, a)).toBe(false);
      expect(ctx.announcement()).toContain('nothing was mapped');
    });

    it('survive a full reload of the same pins and map as before', async () => {
      const { a } = gather();
      const catalog = { sources: [], targets: [], diagnostics: [] } as MappingCatalog;
      const full = {
        catalog: vi.fn().mockReturnValue(of(catalog)),
        relatedDefinitions: vi.fn().mockReturnValue(of([])),
        relatedCatalog,
      } as unknown as WorkflowMappingService;
      ctx.root.set({ definitionId: 'root', versionId: 'root-v' });
      const host = { ensureSaved: vi.fn() } as never;
      await loadMappingCatalog(ctx, full, host);
      expect(ctx.liveTray().map((d) => d.reference.fieldKey)).toEqual([
        'f_ver-a',
        'f_ver-x',
        'own',
      ]);
      const t = target();
      expect(ctx.mapTo(t, a)).toBe(true);
      expect(t.apply).toHaveBeenCalledTimes(1);
    });
  });

  it('never counts a catalog fetched for other pins, in the moment before the reload starts', () => {
    relatedCatalog.mockReturnValue(of(catalogOf('ver-a', 'alpha')));
    ctx.selectors.set([selector('s1', 'def', 'ver-a')]);
    syncRelatedCatalogs(ctx, api);
    expect(ctx.relatedSources()).toHaveLength(1);
    // The selector changes; the catalog in hand is still Alpha's until the sync runs.
    ctx.selectors.set([selector('s1', 'def', 'ver-b')]);
    expect(ctx.relatedSources()).toEqual([]);
    expect(ctx.descriptorFor({ ...sourceOf('ver-a', 'alpha').reference, selectorId: 's1' })).toBe(
      undefined,
    );
  });

  it('forgets a vanished selector’s catalog, cancels its request and loads afresh when it returns', () => {
    const unsubscribed = vi.fn();
    relatedCatalog.mockReturnValueOnce(new Observable<MappingCatalog>(() => unsubscribed));
    ctx.selectors.set([selector('s1', 'def', 'ver-a')]);
    syncRelatedCatalogs(ctx, api);
    ctx.selectors.set([]);
    syncRelatedCatalogs(ctx, api);
    expect(unsubscribed).toHaveBeenCalled();
    expect(ctx.related()).toEqual({});
    expect(ctx.relatedPin()).toEqual({});
    expect(ctx.relatedRequests.size).toBe(0);

    relatedCatalog.mockReturnValueOnce(of(catalogOf('ver-a', 'alpha')));
    ctx.selectors.set([selector('s1', 'def', 'ver-a')]);
    syncRelatedCatalogs(ctx, api);
    expect(relatedCatalog).toHaveBeenCalledTimes(2);
    expect(ctx.relatedSources()).toHaveLength(1);
  });

  it('keeps one selector’s failure from hiding another’s sources', () => {
    relatedCatalog.mockImplementation((_d: string, v: string) =>
      v === 'ver-a' ? throwError(() => new Error('nope')) : of(catalogOf(v, 'beta')),
    );
    ctx.selectors.set([selector('s1', 'def', 'ver-a'), selector('s2', 'def', 'ver-b')]);
    syncRelatedCatalogs(ctx, api);
    expect(ctx.related()['s1']).toBe('error');
    expect(ctx.relatedSources().map((s) => s.reference.selectorId)).toEqual(['s2']);
  });

  it('answers for the exact selector even when two selectors read the same workflow', () => {
    const calls: Subject<MappingCatalog>[] = [];
    relatedCatalog.mockImplementation(() => {
      const subject = new Subject<MappingCatalog>();
      calls.push(subject);
      return subject;
    });
    ctx.selectors.set([selector('s1', 'def', 'ver-a'), selector('s2', 'def', 'ver-a')]);
    syncRelatedCatalogs(ctx, api);
    expect(calls).toHaveLength(2);
    calls[1].next(catalogOf('ver-a', 'alpha'));
    expect(ctx.relatedSources().map((s) => s.reference.selectorId)).toEqual(['s2']);
  });

  it('loadRelatedCatalog on its own supersedes the earlier request for the same selector', () => {
    const first = unabortable();
    relatedCatalog
      .mockReturnValueOnce(first.source)
      .mockReturnValueOnce(of(catalogOf('ver-b', 'beta')));
    ctx.selectors.set([selector('s1', 'def', 'ver-b')]);
    loadRelatedCatalog(ctx, api, 's1', 'def', 'ver-a');
    loadRelatedCatalog(ctx, api, 's1', 'def', 'ver-b');
    first.observers[0].next(catalogOf('ver-a', 'alpha'));
    expect(ctx.relatedSources().map((s) => s.reference.fieldKey)).toEqual(['beta']);
  });

  it('a full catalog reload drops what was loading and asks again for every selector', async () => {
    const stale = unabortable();
    const catalog = {
      catalog: () => of(catalogOf('root', 'x')),
      relatedDefinitions: () => of([]),
      relatedCatalog,
    };
    relatedCatalog
      .mockReturnValueOnce(stale.source)
      .mockReturnValueOnce(of(catalogOf('ver-a', 'fresh')));
    ctx.root.set({ definitionId: 'r', versionId: 'rv' });
    ctx.selectors.set([selector('s1', 'def', 'ver-a')]);
    syncRelatedCatalogs(ctx, api);
    const host = { ensureSaved: async () => undefined } as never;
    await loadMappingCatalog(ctx, catalog as unknown as WorkflowMappingService, host);
    stale.observers[0].next(catalogOf('ver-a', 'stale'));
    expect(ctx.relatedSources().map((s) => s.reference.fieldKey)).toEqual(['fresh']);
  });
});

describe('mapping target ownership', () => {
  let ctx: MappingEditContext;
  const apply = vi.fn();
  const owner = {};
  const other = {};
  const target = (id: string, who: object, alive: () => boolean): MappingTarget => ({
    id,
    label: id,
    expected: null,
    owner: who,
    alive,
    occupied: () => false,
    apply,
  });

  beforeEach(() => {
    apply.mockReset();
    TestBed.configureTestingModule({
      providers: [
        MappingEditContext,
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
      ],
    });
    ctx = TestBed.inject(MappingEditContext);
  });

  it('releases the target of the editor that is destroyed', () => {
    ctx.setActive(target('a', owner, () => true));
    ctx.releaseTarget(owner);
    expect(ctx.activeTarget()).toBeNull();
  });

  it('does not clear a newer target owned by another editor', () => {
    ctx.setActive(target('a', owner, () => true));
    ctx.setActive(target('b', other, () => true));
    ctx.releaseTarget(owner);
    expect(ctx.activeTarget()?.id).toBe('b');
  });

  it('maps onto a live target and says so', () => {
    expect(
      ctx.mapTo(
        target('a', owner, () => true),
        sourceOf('v', 'f'),
      ),
    ).toBe(true);
    expect(apply).toHaveBeenCalledTimes(1);
    expect(ctx.announcement()).toContain('mapped to a');
  });

  it('refuses a dead target without applying anything or reporting a mapping', () => {
    const dead = target('a', owner, () => false);
    ctx.setActive(dead);
    expect(ctx.mapTo(dead, sourceOf('v', 'f'))).toBe(false);
    expect(apply).not.toHaveBeenCalled();
    expect(ctx.announcement()).toContain('nothing was mapped');
    expect(ctx.announcement()).not.toContain('mapped to');
    expect(ctx.activeTarget()).toBeNull();
  });

  it('keeps a newer target when a dead one is refused', () => {
    const dead = target('a', owner, () => false);
    ctx.setActive(target('b', other, () => true));
    expect(ctx.mapTo(dead, sourceOf('v', 'f'))).toBe(false);
    expect(ctx.activeTarget()?.id).toBe('b');
  });
});
