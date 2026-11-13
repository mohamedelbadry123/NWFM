import { Injectable, computed, inject, signal } from '@angular/core';
import { LocaleService } from '@core/i18n/locale.service';
import type {
  CallableTarget,
  EndpointDefinition,
  MappingCatalog,
  MappingDefinitionChoice,
  MappingSchema,
  RelatedInstanceSelector,
  SourceDescriptor,
  SourceReference,
  TransformNode,
} from './mapping.models';
import {
  findDescriptor,
  invocationInputReference,
  relatedPinKey,
  responseReference,
  schemaAt,
  sourceKey,
  sourceNode,
} from './mapping-config';

/** An in-flight or finished related-catalog request, identified by the exact pins it was made for. */
export interface RelatedRequest {
  pin: string;
  subscription: { unsubscribe(): void } | null;
}

/** A place a source can be mapped: an input, an output, an object property, a function argument… */
export interface MappingTarget {
  /** Unique within the editor; identifies the target for "active" state and drop handling. */
  id: string;
  /** Human label, shown in the browser's "Map to …" buttons and announced to assistive technology. */
  label: string;
  /** Expected type, for hints. */
  expected?: MappingSchema | null;
  /** Writes the node into the document. */
  apply: (node: TransformNode) => void;
  /** Whether the target already holds something other than an empty constant or one plain source. */
  occupied: () => boolean;
  /** The editor that owns this target; it releases the target when it is destroyed. */
  owner?: object;
  /** False once the owning editor is gone, so a stale target can never report a mapping it did not make. */
  alive?: () => boolean;
}

/**
 * State shared by the mapper's stages and the transform editors nested inside them: what the catalog offers,
 * which sources the author has gathered in the tray, and which target is currently active for click/keyboard
 * mapping. Provided by the mapper (or Step mapping) component, so every editor sees the same one.
 */
@Injectable()
export class MappingEditContext {
  private readonly locale = inject(LocaleService);
  t = (en: string, ar: string) => (this.locale.locale() === 'ar' ? ar : en);

  readonly readonly = signal(false);
  readonly owner = signal<CallableTarget | null>(null);
  /** The pinned MAIN tree root every mapping API call is addressed to (the owner may be in a child). */
  readonly root = signal<{ definitionId: string; versionId: string } | null>(null);
  readonly catalog = signal<MappingCatalog | null>(null);
  readonly catalogError = signal('');
  readonly loading = signal(false);
  readonly endpoint = signal<EndpointDefinition | null>(null);
  readonly invocationInputs = signal<MappingSchema | null>(null);
  readonly selectors = signal<RelatedInstanceSelector[]>([]);
  readonly choices = signal<MappingDefinitionChoice[]>([]);
  /** Related catalogs by selector id; each holds the related workflow's own pinned sources. */
  readonly related = signal<Record<string, MappingCatalog | 'loading' | 'error'>>({});
  /** The definition/version pins each related catalog (above) was requested for; a catalog only counts for those pins. */
  readonly relatedPin = signal<Record<string, string>>({});
  /** The newest request per selector id, so a superseded (retargeted or removed) request cannot write its answer. */
  readonly relatedRequests = new Map<string, RelatedRequest>();

  /** Sources gathered for mapping (multi-select). Related sources carry their `selectorId`. */
  readonly tray = signal<SourceDescriptor[]>([]);
  readonly activeTarget = signal<MappingTarget | null>(null);
  /** The source currently being dragged, so drop targets can validate before the browser hands over data. */
  readonly dragging = signal<SourceDescriptor | null>(null);
  readonly announcement = signal('');

  /** Local sources that are not in the server catalog: the event's own response and (for a callable event) its inputs. */
  readonly localSources = computed<SourceDescriptor[]>(() => {
    const owner = this.owner();
    if (!owner) return [];
    const result: SourceDescriptor[] = [];
    const response = this.endpoint()?.response;
    if (response)
      result.push({
        reference: responseReference(owner),
        label: this.t('This request’s response', 'استجابة هذا الطلب'),
        breadcrumbs: [
          this.t('This event', 'هذا الحدث'),
          this.t('Response body', 'محتوى الاستجابة'),
        ],
        schema: response,
        availability: 'CompletedOutput',
      });
    const inputs = this.invocationInputs();
    if (inputs)
      result.push({
        reference: invocationInputReference(owner),
        label: this.t('Inputs this event receives', 'المدخلات التي يستقبلها هذا الحدث'),
        breadcrumbs: [
          this.t('This event', 'هذا الحدث'),
          this.t('Invocation inputs', 'مدخلات الاستدعاء'),
        ],
        schema: inputs,
        availability: 'InvocationStart',
      });
    return result;
  });

  /** Sources of this workflow tree (never including the event's own response/inputs). */
  readonly treeSources = computed(() => this.catalog()?.sources ?? []);

  /** Related workflows' sources, each tagged with its selector and a readable "Related workflow" breadcrumb. */
  readonly relatedSources = computed<SourceDescriptor[]>(() => {
    const all = this.related();
    const pins = this.relatedPin();
    return this.selectors().flatMap((selector) => {
      const entry = all[selector.id];
      if (!entry || entry === 'loading' || entry === 'error') return [];
      // A catalog fetched for another workflow/version never stands in for this selector's pins.
      if (pins[selector.id] !== relatedPinKey(selector)) return [];
      const name =
        this.choices().find((c) => c.versionId === selector.workflowVersionId)?.name ??
        selector.workflowVersionId;
      return entry.sources.map((descriptor) => ({
        ...descriptor,
        reference: { ...descriptor.reference, selectorId: selector.id },
        breadcrumbs: [
          `${this.t('Related workflow', 'سير عمل مرتبط')}: ${name}`,
          ...descriptor.breadcrumbs.slice(1),
        ],
      }));
    });
  });

  /**
   * Step-field bindings write into this Step's own form, so its fields are destinations there, not sources
   * (a value cannot be derived from the field it fills). Set only by the Step mapping editor.
   */
  readonly hideOwnForm = signal(false);

  readonly allSources = computed(() => {
    const owner = this.owner();
    const tree =
      this.hideOwnForm() && owner
        ? this.treeSources().filter(
            (s) =>
              !(
                s.reference.kind === 'FormField' &&
                s.reference.nodeKey === owner.nodeKey &&
                s.reference.workflowDefinitionId === owner.workflowDefinitionId
              ),
          )
        : this.treeSources();
    return [...tree, ...this.localSources(), ...this.relatedSources()];
  });

  descriptorFor(ref: SourceReference): SourceDescriptor | undefined {
    if (ref.selectorId) {
      const key = sourceKey(ref);
      return this.relatedSources().find((s) => sourceKey(s.reference) === key);
    }
    return findDescriptor([...this.treeSources(), ...this.localSources()], ref);
  }
  schemaFor = (ref: SourceReference): MappingSchema | null =>
    this.descriptorFor(ref)?.schema ?? null;
  labelOf = (ref: SourceReference): string => this.descriptorFor(ref)?.label ?? ref.fieldKey;

  /**
   * Whether a source can still be mapped right now. A related source only counts for the exact definition/version
   * its selector currently points at and whose catalog is loaded; one gathered before the selector was retargeted
   * or removed (or while its catalog is reloading) is stale and is never offered or mapped.
   */
  isCurrent(descriptor: SourceDescriptor): boolean {
    if (!descriptor.reference.selectorId) return true;
    const key = sourceKey(descriptor.reference);
    return this.relatedSources().some((s) => sourceKey(s.reference) === key);
  }
  /** The tray as the mapping entry points see it: only sources that are still current. */
  readonly liveTray = computed(() => this.tray().filter((d) => this.isCurrent(d)));
  /** Drops the gathered sources of one related selector (it was retargeted or removed). */
  dropTrayOf(selectorId: string) {
    if (this.tray().some((d) => d.reference.selectorId === selectorId))
      this.tray.update((all) => all.filter((d) => d.reference.selectorId !== selectorId));
  }

  // ── tray ──────────────────────────────────────────────────────────────
  inTray(descriptor: SourceDescriptor) {
    const key = sourceKey(descriptor.reference);
    return this.liveTray().some((d) => sourceKey(d.reference) === key);
  }
  toggleTray(descriptor: SourceDescriptor) {
    if (this.inTray(descriptor)) this.removeFromTray(descriptor);
    else {
      this.tray.update((all) => [...all, descriptor]);
      this.announce(
        this.t(`${descriptor.label} added to the tray.`, `أضيف ${descriptor.label} إلى الصينية.`),
      );
    }
  }
  removeFromTray(descriptor: SourceDescriptor) {
    const key = sourceKey(descriptor.reference);
    this.tray.update((all) => all.filter((d) => sourceKey(d.reference) !== key));
  }
  clearTray() {
    this.tray.set([]);
  }

  // ── mapping a source to a target ─────────────────────────────────────────
  setActive(target: MappingTarget | null) {
    this.activeTarget.set(target);
    if (target)
      this.announce(this.t(`Mapping target: ${target.label}.`, `هدف الربط: ${target.label}.`));
  }
  /** Drops the active target when its owning editor is destroyed; a newer target from another editor stays. */
  releaseTarget(owner: object) {
    if (this.activeTarget()?.owner === owner) this.activeTarget.set(null);
  }
  /** Maps a source onto `target`; refuses (and says why) when it would silently replace a real expression. */
  mapTo(target: MappingTarget, descriptor: SourceDescriptor): boolean {
    if (this.readonly()) return false;
    if (target.alive && !target.alive()) {
      if (this.activeTarget() === target) this.activeTarget.set(null);
      this.announce(
        this.t(
          `${target.label} is no longer in the mapping, so nothing was mapped. Choose another target.`,
          `${target.label} لم يعد في الربط، لذلك لم يتم ربط شيء. اختر هدفاً آخر.`,
        ),
      );
      return false;
    }
    if (!this.isCurrent(descriptor)) {
      this.announce(
        this.t(
          `${descriptor.label} is no longer available from its related workflow, so nothing was mapped. Choose it again from the current sources.`,
          `${descriptor.label} لم يعد متاحاً من سير العمل المرتبط، لذلك لم يتم ربط شيء. اختره مجدداً من المصادر الحالية.`,
        ),
      );
      return false;
    }
    if (target.occupied()) {
      this.announce(
        this.t(
          `${target.label} already has an expression. Open it and choose where this value goes.`,
          `${target.label} يحتوي بالفعل على تعبير. افتحه واختر مكان هذه القيمة.`,
        ),
      );
      return false;
    }
    target.apply(sourceNode(descriptor.reference));
    this.announce(
      this.t(
        `${descriptor.label} mapped to ${target.label}.`,
        `تم ربط ${descriptor.label} بـ ${target.label}.`,
      ),
    );
    return true;
  }
  mapToActive(descriptor: SourceDescriptor): boolean {
    const target = this.activeTarget();
    return target ? this.mapTo(target, descriptor) : false;
  }
  announce(message: string) {
    this.announcement.set(message);
  }

  /** The declared schema of a response/inputs/outputs pointer, convenient for hints. */
  schemaAtSource(ref: SourceReference): MappingSchema | null {
    return schemaAt(this.schemaFor(ref), ref.jsonPointer);
  }
}
