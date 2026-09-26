import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { DialogModule } from 'primeng/dialog';

import { TranslateContextDirective } from '../../../../core/i18n/translate-context.directive';
import { GeoMapComponent } from '../../../../shared/components/geo-map/geo-map.component';
import type { GeoPoint } from '../../../../shared/components/geo-map/google-maps.types';

/**
 * One geolocation answer on a read-only map, with the address it was filed under. The answer list
 * gives the same point as text; this is the "where is that, actually" view behind it, so a reviewer
 * never has to paste a pair of numbers into another tab.
 *
 * The map is only built while the dialog is open, so a fill with several points does not load a
 * map per row.
 */
@Component({
  selector: 'app-task-answer-map-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslateContextDirective, DialogModule, GeoMapComponent],
  template: `
    <ng-container *translateContext="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [draggable]="false"
        [dismissableMask]="true"
        [style]="{ width: '46rem', maxWidth: '95vw' }"
        [header]="label() || t('tasks.detail.viewOnMap')"
      >
        @if (visible() && point(); as picked) {
          <app-geo-map [value]="picked" [readonly]="true" height="380px" />
        }
      </p-dialog>
    </ng-container>
  `,
})
export class TaskAnswerMapDialogComponent {
  readonly visible = model.required<boolean>();
  readonly point = input<GeoPoint | null>(null);

  /** The question's own label, so the map is titled the way the answer row reads. */
  readonly label = input('');
}
