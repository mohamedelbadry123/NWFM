import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FieldType, FieldTypeConfig } from '@ngx-formly/core';
import { GeoMapComponent, SAUDI_ARABIA_ZOOM } from '../geo-map/geo-map.component';
import type { GeoPoint } from '../geo-map/google-maps.types';

/**
 * Formly type `geolocation` — thin adapter between the reusable
 * {@link GeoMapComponent} and a Formly field. The control value is a plain
 * `{ lat, lng, address? }` object, or `null` when nothing is picked yet.
 *
 * The address is optional: a form default supplies only coordinates, and a point the geocoder
 * cannot name keeps them alone. {@link isGeoPoint} therefore keys off the coordinates only.
 */
@Component({
  selector: 'formly-field-geolocation',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [GeoMapComponent],
  template: `
    <app-geo-map
      [value]="point"
      (valueChange)="onPointChange($event)"
      [readonly]="!!props.disabled"
      [zoom]="mapZoom"
      [height]="mapHeight"
    />
  `,
})
export class FormlyGeolocationComponent extends FieldType<FieldTypeConfig> {
  protected get point(): GeoPoint | null {
    const value: unknown = this.formControl.value;
    return isGeoPoint(value) ? value : null;
  }

  protected get mapZoom(): number {
    return (this.props['mapZoom'] as number | undefined) ?? SAUDI_ARABIA_ZOOM;
  }

  protected get mapHeight(): string {
    return (this.props['mapHeight'] as string | undefined) ?? '360px';
  }

  protected onPointChange(point: GeoPoint | null): void {
    this.formControl.setValue(point);
    this.formControl.markAsTouched();
  }
}

function isGeoPoint(value: unknown): value is GeoPoint {
  return (
    value !== null &&
    typeof value === 'object' &&
    typeof (value as GeoPoint).lat === 'number' &&
    typeof (value as GeoPoint).lng === 'number'
  );
}
