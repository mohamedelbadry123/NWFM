import { firstValueFrom } from 'rxjs';
import { MappingEditContext, type RelatedRequest } from './mapping-edit-context';
import { relatedPinKey } from './mapping-config';
import type { WorkflowMappingHost } from './mapping-host';
import { WorkflowMappingService } from './workflow-mapping.service';

/** Syncs the shared edit context with the designer's current event (owner, root, read-only). */
export function syncMappingContext(
  ctx: MappingEditContext,
  host: WorkflowMappingHost | null,
  readonly: boolean,
) {
  ctx.readonly.set(readonly || !!host?.readonly());
  ctx.owner.set(host?.owner() ?? null);
  ctx.root.set(host?.root() ?? null);
}

export function ownerKey(host: WorkflowMappingHost | null) {
  const o = host?.owner();
  return o ? `${o.workflowDefinitionId}|${o.workflowVersionId}|${o.nodeKey}` : '';
}

/**
 * One related workflow's catalog, loaded lazily and independently so one failure does not hide the others.
 * The request is remembered by selector id AND the exact definition/version pins it was made for: a newer request
 * for the same selector supersedes (and cancels) an older one, so a late answer for the previous pin is dropped.
 */
export function loadRelatedCatalog(
  ctx: MappingEditContext,
  api: WorkflowMappingService,
  id: string,
  definitionId: string,
  versionId: string,
) {
  const pin = `${definitionId}|${versionId}`;
  ctx.relatedRequests.get(id)?.subscription?.unsubscribe();
  const request: RelatedRequest = { pin, subscription: null };
  ctx.relatedRequests.set(id, request);
  ctx.relatedPin.update((all) => ({ ...all, [id]: pin }));
  ctx.related.update((all) => ({ ...all, [id]: 'loading' }));
  const current = () => ctx.relatedRequests.get(id) === request;
  request.subscription = api.relatedCatalog(definitionId, versionId).subscribe({
    next: (catalog) => {
      if (current()) ctx.related.update((all) => ({ ...all, [id]: catalog }));
    },
    error: () => {
      if (current()) ctx.related.update((all) => ({ ...all, [id]: 'error' }));
    },
  });
}

/**
 * Brings the related catalogs in line with the current selectors: loads the ones that are new or were retargeted to
 * another workflow/version (their old catalog is gone at once, never shown under the new label), keeps the ones whose
 * pins did not change, and removes the ones whose selector no longer exists. Sources already gathered in the tray from
 * a removed or retargeted selector are dropped with it; authored expressions that use them are left untouched (they
 * show as unresolved until the author fixes them).
 */
export function syncRelatedCatalogs(ctx: MappingEditContext, api: WorkflowMappingService) {
  const selectors = ctx.selectors();
  const live = new Set(selectors.map((s) => s.id));
  const vanished = [...ctx.relatedRequests.keys()].filter((id) => !live.has(id));
  for (const id of vanished) {
    ctx.relatedRequests.get(id)?.subscription?.unsubscribe();
    ctx.relatedRequests.delete(id);
  }
  for (const d of ctx.tray()) {
    const id = d.reference.selectorId;
    if (id && !live.has(id)) ctx.dropTrayOf(id);
  }
  const without = (all: Record<string, unknown>) =>
    Object.fromEntries(Object.entries(all).filter(([id]) => live.has(id)));
  if (vanished.length || Object.keys(ctx.related()).some((id) => !live.has(id))) {
    ctx.related.update((all) => without(all) as typeof all);
    ctx.relatedPin.update((all) => without(all) as typeof all);
  }
  for (const s of selectors) {
    const previous = ctx.relatedRequests.get(s.id);
    if (previous?.pin === relatedPinKey(s)) continue;
    // Retargeted: what was gathered from the old workflow must not map onto the new one.
    if (previous) ctx.dropTrayOf(s.id);
    loadRelatedCatalog(ctx, api, s.id, s.workflowDefinitionId, s.workflowVersionId);
  }
}

/** Forgets every related catalog (and cancels what is still loading) before they are loaded again. */
export function resetRelatedCatalogs(ctx: MappingEditContext) {
  for (const request of ctx.relatedRequests.values()) request.subscription?.unsubscribe();
  ctx.relatedRequests.clear();
  ctx.relatedPin.set({});
  ctx.related.set({});
}

/**
 * Saves the draft (so the server catalog matches what the author sees), then reads the catalog and the related-workflow
 * choices. Failures stay visible in `catalogError`; nothing is guessed.
 */
export async function loadMappingCatalog(
  ctx: MappingEditContext,
  api: WorkflowMappingService,
  host: WorkflowMappingHost | null,
) {
  const root = ctx.root();
  if (!host || !root) {
    ctx.catalogError.set(
      ctx.t(
        'Typed mapping needs the workflow designer: open this event there to browse sources.',
        'يحتاج الربط المصنّف إلى مصمم سير العمل: افتح هذا الحدث هناك لتصفح المصادر.',
      ),
    );
    return;
  }
  ctx.loading.set(true);
  ctx.catalogError.set('');
  try {
    if (!ctx.readonly()) await host.ensureSaved();
    const [catalog, choices] = await Promise.all([
      firstValueFrom(api.catalog(root.definitionId, root.versionId)),
      firstValueFrom(api.relatedDefinitions(root.definitionId, root.versionId)),
    ]);
    ctx.catalog.set(catalog);
    ctx.choices.set(choices);
    resetRelatedCatalogs(ctx);
    syncRelatedCatalogs(ctx, api);
  } catch (e) {
    const message =
      (e as { error?: { message?: string } })?.error?.message ?? (e as Error)?.message;
    ctx.catalogError.set(
      message ||
        ctx.t(
          'Could not load the sources. Save the workflow and try again.',
          'تعذر تحميل المصادر. احفظ سير العمل وحاول مجدداً.',
        ),
    );
  } finally {
    ctx.loading.set(false);
  }
}
