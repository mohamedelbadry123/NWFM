import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';

import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { OrgScopeSelectorComponent } from './org-scope-selector.component';
import { EMPTY_ORG_LOCATION, OrgLocation, isEmptyLocation } from './org-scope.model';

/**
 * The organization filter, as a popup.
 *
 * The selection is **staged**: the cascade picker reports every level as it is chosen, and reloading
 * the list on each one would cost four round trips to narrow to an operation area. Here those go
 * into a draft and only leave on Apply, so narrowing costs one.
 */
@Component({
  selector: 'app-org-filter-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslateContextDirective, ButtonModule, DialogModule, OrgScopeSelectorComponent],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        (onShow)="onShow()"
        [header]="t('org.filterByOrg')"
        [modal]="true"
        [draggable]="false"
        [resizable]="false"
        [style]="{ width: '620px' }"
        [breakpoints]="{ '720px': '95vw' }"
      >
        <p class="mb-4 text-sm text-[var(--p-text-muted-color)]">
          {{ t('org.filterHint') }}
        </p>

        @if (visible()) {
          <app-org-scope-selector
            mode="single"
            [initialLocation]="seed()"
            (locationChange)="onDraftChange($event)"
          />
        }

        <ng-template pTemplate="footer">
          <p-button
            [label]="t('common.reset')"
            icon="pi pi-filter-slash"
            severity="secondary"
            [text]="true"
            [disabled]="!canApply()"
            (onClick)="reset()"
          />
          <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="cancel()" />
          <p-button [label]="t('common.apply')" icon="pi pi-check" (onClick)="apply()" />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class OrgFilterDialogComponent {
  readonly visible = model(false);

  /** The filter currently in force, so reopening shows what is applied. */
  readonly location = input<OrgLocation>(EMPTY_ORG_LOCATION);

  readonly applied = output<OrgLocation>();

  /** What the picker reports as the operator moves through the cascade. */
  protected readonly draft = signal<OrgLocation>(EMPTY_ORG_LOCATION);

  /**
   * A new object identity on every open is what tells the picker to rebuild its cascade; reusing
   * the same reference would leave the previous one standing.
   */
  protected readonly seed = signal<OrgLocation>(EMPTY_ORG_LOCATION);

  protected readonly canApply = computed(() => !isEmptyLocation(this.draft()));

  protected onShow(): void {
    const current = this.location();
    this.draft.set({ ...current });
    this.seed.set({ ...current });
  }

  protected onDraftChange(location: OrgLocation): void {
    this.draft.set(location);
  }

  /** Clears the cascade without closing — the operator usually re-picks straight away. */
  protected reset(): void {
    this.draft.set({ ...EMPTY_ORG_LOCATION });
    this.seed.set({ ...EMPTY_ORG_LOCATION });
  }

  protected apply(): void {
    this.applied.emit(this.draft());
    this.visible.set(false);
  }

  protected cancel(): void {
    this.visible.set(false);
  }
}
