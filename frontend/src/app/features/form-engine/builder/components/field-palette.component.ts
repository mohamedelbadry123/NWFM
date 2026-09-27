import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDrag, CdkDragDrop, CdkDropList } from '@angular/cdk/drag-drop';
import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { PALETTE_GROUP_ORDER, PALETTE_ITEMS, type PaletteItem } from '../data/palette';
import { DROP_IDS, type ElementType, type PaletteGroup } from '../../../../shared/form-schema/form-schema.types';

interface PaletteGroupView {
  readonly group: PaletteGroup;
  readonly items: readonly PaletteItem[];
}

@Component({
  selector: 'app-field-palette',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, CdkDropList, CdkDrag, TranslateContextDirective],
  template: `
    <ng-container *translateContext="let t">
      <div class="flex flex-col h-full">
        <div class="px-3 py-3 border-b border-[var(--app-border)]">
          <h2 class="text-xs font-bold uppercase tracking-wide text-[var(--p-text-muted-color)]">
            {{ t('formBuilder.fields') }}
          </h2>
          <p class="text-[11px] text-[var(--p-text-muted-color)] mt-1">
            {{ t('formBuilder.paletteHint') }}
          </p>
        </div>

        <div
          cdkDropList
          [id]="paletteId"
          [cdkDropListData]="paletteTypes"
          [cdkDropListConnectedTo]="connectedTo()"
          [cdkDropListSortingDisabled]="true"
          (cdkDropListDropped)="onPaletteDrop($event)"
          class="flex-1 overflow-y-auto p-3 space-y-4"
        >
          @for (groupView of groups; track groupView.group) {
            <div>
              <h3 class="text-[11px] font-semibold text-[var(--p-text-muted-color)] mb-2">
                {{ t('formBuilder.groups.' + groupView.group) }}
              </h3>
              <div class="space-y-2">
                @for (item of groupView.items; track item.type) {
                  <button
                    type="button"
                    cdkDrag
                    [cdkDragData]="item.type"
                    (click)="add.emit(item.type)"
                    class="w-full flex items-center gap-3 px-3 py-2.5 rounded-lg border border-[var(--app-border)] bg-[var(--app-surface)] hover:border-[var(--p-primary-color)] hover:shadow-sm transition cursor-grab active:cursor-grabbing text-start"
                  >
                    <i [class]="item.icon + ' text-[var(--p-primary-color)]'"></i>
                    <span class="text-sm font-medium">{{ t(item.labelKey) }}</span>
                  </button>
                }
              </div>
            </div>
          }
        </div>
      </div>
    </ng-container>
  `,
})
export class FieldPaletteComponent {
  /** CDK drop-list ids the palette items can be dropped onto. */
  readonly connectedTo = input<string[]>([]);
  readonly add = output<ElementType>();

  protected readonly paletteId = DROP_IDS.Palette;
  /** Placeholder data array so CDK treats the palette as a valid drop list. */
  protected readonly paletteTypes: ElementType[] = PALETTE_ITEMS.map((item) => item.type);

  protected readonly groups: readonly PaletteGroupView[] = PALETTE_GROUP_ORDER.map((group) => ({
    group,
    items: PALETTE_ITEMS.filter((item) => item.group === group),
  }));

  /** Palette is a source only; ignore anything dropped back onto it. */
  protected onPaletteDrop(_event: CdkDragDrop<ElementType[]>): void {
    // no-op
  }
}
