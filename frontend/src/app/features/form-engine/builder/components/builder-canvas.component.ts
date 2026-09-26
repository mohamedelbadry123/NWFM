import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDragPlaceholder, CdkDropList } from '@angular/cdk/drag-drop';
import { CdkScrollable } from '@angular/cdk/scrolling';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';
import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../../core/i18n/locale.service';
import { FormBuilderStore } from '../store/form-builder.store';
import { PALETTE_ITEMS } from '../data/palette';
import {
  DATE_RULES,
  DEFAULT_VALUE_MODES,
  DROP_IDS,
  ELEMENT_TYPES,
  isChoiceType,
  localizedDisplay,
  type ElementType,
  type FormElement,
} from '../../../../shared/form-schema/form-schema.types';

export interface FieldBadge {
  /** Translation key under formBuilder.badges.* */
  key: string;
  /** `.app-badge` tone classes — see BADGE_TONE */
  classes: string;
  /** Optional interpolation params for translate */
  params?: Record<string, unknown>;
}

/**
 * Badge tones, grouped by what the badge *means* rather than by field property.
 * A field row can show up to five badges at once, so each extra hue is visual
 * noise; three meanings cover every case and stay readable side by side.
 */
const BADGE_TONE = {
  /** Author-set constraint on the value (required). */
  constraint: 'app-badge app-badge--brand',
  /** Behaviour driven by a rule/condition (visibility, requirement, cascade). */
  rule: 'app-badge app-badge--rule',
  /** Non-default field state the author should notice (disabled). */
  state: 'app-badge app-badge--warn',
  /** Descriptive facts (hidden, default value, choice count). */
  metadata: 'app-badge app-badge--neutral',
} as const;

const ICON_BY_TYPE = new Map<ElementType, string>(
  PALETTE_ITEMS.map((item) => [item.type, item.icon]),
);
const LABEL_KEY_BY_TYPE = new Map<ElementType, string>(
  PALETTE_ITEMS.map((item) => [item.type, item.labelKey]),
);

@Component({
  selector: 'app-builder-canvas',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
    CdkDragPlaceholder,
    CdkScrollable,
    ButtonModule,
    TooltipModule,
    TranslateContextDirective,
  ],
  templateUrl: './builder-canvas.component.html',
})
export class BuilderCanvasComponent {
  protected readonly store = inject(FormBuilderStore);

  readonly edit = output<string>();

  protected readonly rootId = DROP_IDS.CanvasRoot;
  protected readonly paletteId = DROP_IDS.Palette;
  protected readonly sectionType = ELEMENT_TYPES.Section;

  /** Top-level section keys — used to wire connected drop lists. */
  protected readonly sectionIds = computed(() =>
    this.store.elements().filter((el) => el.type === ELEMENT_TYPES.Section).map((el) => el.key),
  );

  /** Root list can receive from every section. */
  protected readonly rootConnectedTo = computed(() => this.sectionIds());

  protected connectedForSection(key: string): string[] {
    return [this.rootId, ...this.sectionIds().filter((id) => id !== key)];
  }

  protected iconFor(type: ElementType): string {
    return ICON_BY_TYPE.get(type) ?? 'pi pi-question';
  }

  protected labelKeyFor(type: ElementType): string {
    return LABEL_KEY_BY_TYPE.get(type) ?? 'formBuilder.types.text';
  }

  private readonly locale = inject(LocaleService);
  private readonly activeLang = this.locale.locale;

  protected displayLabel(element: FormElement): string {
    return localizedDisplay(
      element.label_en,
      element.label_ar,
      this.activeLang(),
      element.data_name,
    );
  }

  /**
   * Returns the list of active configuration badges for a field element.
   * Each badge has a translation key, colour classes, and optional params.
   */
  protected badgesFor(element: FormElement): FieldBadge[] {
    const badges: FieldBadge[] = [];

    if (element.required) {
      badges.push({ key: 'required', classes: BADGE_TONE.constraint });
    }
    if (element.hidden) {
      badges.push({ key: 'hidden', classes: BADGE_TONE.metadata });
    }
    if (element.disabled) {
      badges.push({ key: 'disabled', classes: BADGE_TONE.state });
    }
    if ((element.visible_conditions?.conditions?.length ?? 0) > 0) {
      badges.push({ key: 'hasVisibility', classes: BADGE_TONE.rule });
    }
    if ((element.required_conditions?.conditions?.length ?? 0) > 0) {
      badges.push({ key: 'hasRequirement', classes: BADGE_TONE.rule });
    }
    // A `now` default carries no stored value — the fill-time clock supplies it — so the badge
    // cannot be driven by `default_value` alone.
    if (
      element.default_value_mode === DEFAULT_VALUE_MODES.Now ||
      (element.default_value != null && element.default_value !== '')
    ) {
      badges.push({ key: 'hasDefault', classes: BADGE_TONE.metadata });
    }
    if (
      (element.date_rule != null && element.date_rule !== DATE_RULES.None) ||
      element.min_date != null ||
      element.max_date != null
    ) {
      badges.push({ key: 'hasDateRule', classes: BADGE_TONE.constraint });
    }
    if (element.parent_field) {
      badges.push({ key: 'cascading', classes: BADGE_TONE.rule });
    }
    if (isChoiceType(element.type) && element.choices?.length > 0) {
      badges.push({
        key: 'choices',
        classes: BADGE_TONE.metadata,
        params: { count: element.choices.length },
      });
    }
    return badges;
  }

  /** Badge label for section header rows — only hidden applies. */
  protected sectionBadgesFor(element: FormElement): FieldBadge[] {
    const badges: FieldBadge[] = [];
    if (element.hidden) {
      badges.push({ key: 'hidden', classes: BADGE_TONE.metadata });
    }
    if ((element.visible_conditions?.conditions?.length ?? 0) > 0) {
      badges.push({ key: 'hasVisibility', classes: BADGE_TONE.rule });
    }
    return badges;
  }

  protected onDrop(event: CdkDragDrop<FormElement[]>): void {
    const fromId = event.previousContainer.id;
    const toId = event.container.id;

    if (fromId === this.paletteId) {
      // New element dragged from the palette
      this.store.insertNewAt(event.item.data as ElementType, toId, event.currentIndex);
      return;
    }
    if (fromId === toId) {
      // Reorder within same container
      this.store.moveWithin(fromId, event.previousIndex, event.currentIndex);
      return;
    }
    // Cross-container: move between root and a section (or section to section)
    this.store.transfer(fromId, toId, event.previousIndex, event.currentIndex);
  }

  protected onEdit(key: string): void {
    this.store.select(key);
    this.edit.emit(key);
  }

  protected onDuplicate(key: string): void {
    this.store.duplicate(key);
  }

  protected onRemove(key: string): void {
    this.store.remove(key);
  }
}
