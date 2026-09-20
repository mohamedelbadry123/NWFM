import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnDestroy,
  AfterViewInit,
  computed,
  effect,
  inject,
  input,
  model,
  signal,
  viewChild,
} from '@angular/core';
import { Clipboard } from '@angular/cdk/clipboard';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TooltipModule } from 'primeng/tooltip';
import { TranslateContextDirective } from '../../../core/i18n/translate-context.directive';
import { LocaleService } from '../../../core/i18n/locale.service';
import { GoogleMapsLoaderService } from './google-maps-loader.service';
import {
  MAP_TYPES,
  type GeoPoint,
  type GoogleGeocoder,
  type GoogleMapInstance,
  type GoogleMapsApi,
  type GoogleMarkerInstance,
  type MapType,
  type PlaceSelectEvent,
} from './google-maps.types';

/** Riyadh — the default view when the field has no coordinate yet. */
export const SAUDI_ARABIA_CENTER: GeoPoint = { lat: 24.7136, lng: 46.6753 };

/** Wide enough to show the whole Kingdom. */
export const SAUDI_ARABIA_ZOOM = 5;

/** Zoom used once an actual point is selected. */
const PICKED_ZOOM = 13;

/** Coordinates are stored/displayed at ~11 cm precision. */
const COORDINATE_PRECISION = 6;

const COPIED_FEEDBACK_MS = 1500;

/** Mirrors `SurveyGeolocation.MaxAddressLength` on the server, which truncates past this. */
const MAX_ADDRESS_LENGTH = 250;

/** Suggestions in the search box are limited to Saudi Arabia. */
const REGION_CODES = ['sa'];

/**
 * The one non-fatal entry in `error`. A failed lookup leaves a usable map and a valid point, so
 * unlike `notConfigured` / `loadFailed` this banner is cleared as soon as a lookup succeeds.
 */
const ADDRESS_LOOKUP_ERROR = 'geoMap.addressLookupFailed';

export function roundCoordinate(value: number): number {
  return Number(value.toFixed(COORDINATE_PRECISION));
}

/**
 * Reusable Google Map coordinate picker.
 *
 * Standalone and free of Formly/form-builder concepts, so it can back a Formly
 * field, a filter panel, or a detail page equally well. Click the map or drag
 * the marker to set the point; `value` is a plain `{ lat, lng }`.
 */
@Component({
  selector: 'app-geo-map',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ButtonModule, InputTextModule, MessageModule, TooltipModule, TranslateContextDirective],
  template: `
    <ng-container *translateContext="let t">
      <div
        class="rounded-xl border border-[var(--app-border)] bg-[var(--app-surface)] overflow-hidden shadow-sm"
      >
        @if (error()) {
          <p-message severity="warn" [text]="t(error()!)" styleClass="w-full rounded-none border-0" />
        }

        <!-- Address search. The element renders its own input; it is only mounted
             once the Places library has resolved, so the container stays empty
             (and hidden) when the API was loaded without it. -->
        @if (!readonly()) {
          <div
            #search
            class="geo-search border-b border-[var(--app-border)] px-3 py-2"
            [class.hidden]="!searchReady()"
            [attr.aria-label]="t('geoMap.search')"
          ></div>
        }

        @if (manualEntry()) {
          <!-- No map to click, so the coordinate is typed. A location is still a complete answer
               without one, and a dead grey panel would not be. -->
          <div class="px-3 py-3 grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label class="block text-xs font-semibold mb-1">{{ t('geoMap.latitude') }}</label>
              <input
                pInputText
                type="number"
                step="any"
                inputmode="decimal"
                class="w-full"
                [disabled]="readonly()"
                [value]="manualLat()"
                [attr.aria-label]="t('geoMap.latitude')"
                (change)="onManualCoordinate('lat', $event)"
              />
            </div>
            <div>
              <label class="block text-xs font-semibold mb-1">{{ t('geoMap.longitude') }}</label>
              <input
                pInputText
                type="number"
                step="any"
                inputmode="decimal"
                class="w-full"
                [disabled]="readonly()"
                [value]="manualLng()"
                [attr.aria-label]="t('geoMap.longitude')"
                (change)="onManualCoordinate('lng', $event)"
              />
            </div>
            @if (!readonly()) {
              <p class="sm:col-span-2 m-0 text-[11px] text-[var(--p-text-muted-color)]">
                {{ t('geoMap.manualHint') }}
              </p>
            }
          </div>
        } @else {
          <!-- Map surface + floating controls -->
          <div class="relative">
            <div #canvas class="w-full bg-[var(--app-surface-alt)]" [style.height]="height()"></div>

            <!-- View switch, top-start so it clears Google's own controls -->
            <div
              class="absolute top-2 start-2 flex rounded-lg overflow-hidden shadow-md bg-white/95 backdrop-blur-sm"
            >
              <button
                type="button"
                class="px-2.5 py-1.5 text-xs font-semibold transition"
                [class]="mapType() === roadmap ? activeViewClass : idleViewClass"
                (click)="setMapType(roadmap)"
              >
                <i class="pi pi-map text-[11px] me-1"></i>{{ t('geoMap.viewMap') }}
              </button>
              <button
                type="button"
                class="px-2.5 py-1.5 text-xs font-semibold transition"
                [class]="mapType() === satellite ? activeViewClass : idleViewClass"
                (click)="setMapType(satellite)"
              >
                <i class="pi pi-globe text-[11px] me-1"></i>{{ t('geoMap.viewSatellite') }}
              </button>
            </div>

            @if (!value() && !error()) {
              <div
                class="absolute inset-x-0 bottom-0 px-3 py-2 text-[11px] font-medium text-white bg-gradient-to-t from-black/60 to-transparent pointer-events-none"
              >
                <i class="pi pi-map-marker me-1"></i>{{ t('geoMap.pickHint') }}
              </div>
            }
          </div>
        }

        <!-- Readout + actions -->
        <div class="px-3 py-2.5 border-t border-[var(--app-border)] flex flex-wrap items-center gap-2">
          @if (value(); as point) {
            <span class="flex items-center gap-1.5 flex-1 min-w-0">
              <span class="coordinate-chip">
                <span class="opacity-60">{{ t('geoMap.latitude') }}</span>{{ point.lat }}
              </span>
              <span class="coordinate-chip">
                <span class="opacity-60">{{ t('geoMap.longitude') }}</span>{{ point.lng }}
              </span>
            </span>
          } @else {
            <span class="flex-1 min-w-0 text-xs text-[var(--p-text-muted-color)]">
              {{ manualEntry() ? t('geoMap.noPointManual') : t('geoMap.noPoint') }}
            </span>
          }

          <span class="flex items-center gap-0.5 shrink-0">
            @if (value()) {
              <p-button
                [icon]="copied() ? 'pi pi-check' : 'pi pi-copy'"
                size="small"
                [text]="true"
                [severity]="copied() ? 'success' : 'secondary'"
                [pTooltip]="t('geoMap.copy')"
                [ariaLabel]="t('geoMap.copy')"
                (onClick)="copyCoordinates()"
              />
            }
            @if (!manualEntry()) {
              <p-button
                icon="pi pi-home"
                size="small"
                [text]="true"
                severity="secondary"
                [pTooltip]="t('geoMap.recenter')"
                [ariaLabel]="t('geoMap.recenter')"
                (onClick)="recenter()"
              />
            }
            @if (!readonly()) {
              <p-button
                icon="pi pi-compass"
                size="small"
                [text]="true"
                [loading]="locating()"
                [pTooltip]="t('geoMap.myLocation')"
                [ariaLabel]="t('geoMap.myLocation')"
                (onClick)="useMyLocation()"
              />
              <p-button
                icon="pi pi-times"
                size="small"
                [text]="true"
                severity="danger"
                [disabled]="!value()"
                [pTooltip]="t('geoMap.clear')"
                [ariaLabel]="t('geoMap.clear')"
                (onClick)="clear()"
              />
            }
          </span>
        </div>

        <!-- Address. Auto-resolved from the pin, and editable — a well site or a
             pump station has no name the geocoder knows. -->
        @if (showAddress()) {
          @if (value(); as point) {
            <div class="px-3 pb-2.5 flex items-center gap-2">
              <i class="pi pi-map-marker text-xs text-[var(--p-text-muted-color)] shrink-0"></i>

              @if (readonly()) {
                <span class="text-xs text-[var(--p-text-color)] flex-1 min-w-0 break-words">
                  {{ point.address || t('geoMap.noAddress') }}
                </span>
              } @else {
                <input
                  pInputText
                  type="text"
                  class="flex-1 min-w-0 !text-xs"
                  [attr.maxlength]="maxAddressLength"
                  [attr.aria-label]="t('geoMap.address')"
                  [placeholder]="
                    geocoding() ? t('geoMap.addressLoading') : t('geoMap.addressPlaceholder')
                  "
                  [value]="point.address ?? ''"
                  (input)="onAddressInput($event)"
                />
                <p-button
                  icon="pi pi-refresh"
                  size="small"
                  [text]="true"
                  severity="secondary"
                  [loading]="geocoding()"
                  [disabled]="geocoding()"
                  [pTooltip]="t('geoMap.refreshAddress')"
                  [ariaLabel]="t('geoMap.refreshAddress')"
                  (onClick)="refreshAddress()"
                />
              }
            </div>
          }
        }
      </div>
    </ng-container>
  `,
  styles: [
    `
      .coordinate-chip {
        display: inline-flex;
        align-items: center;
        gap: 0.3rem;
        padding: 0.15rem 0.5rem;
        border-radius: 9999px;
        background: var(--p-primary-50, #ecfeff);
        color: var(--p-primary-700, #0e7490);
        font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
        font-size: 0.7rem;
        font-weight: 600;
        white-space: nowrap;
      }
      :host-context([data-theme='dark']) .coordinate-chip {
        background: color-mix(in srgb, var(--p-primary-color) 18%, transparent);
        color: var(--p-primary-300, #67e8f9);
      }

      /* The autocomplete element brings its own input; stretch it to the row. */
      .geo-search gmp-place-autocomplete {
        display: block;
        width: 100%;
      }
    `,
  ],
})
export class GeoMapComponent implements AfterViewInit, OnDestroy {
  private readonly loader = inject(GoogleMapsLoaderService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly clipboard = inject(Clipboard);
  private readonly locale = inject(LocaleService);

  readonly value = model<GeoPoint | null>(null);
  readonly readonly = input(false);
  readonly zoom = input(SAUDI_ARABIA_ZOOM);
  readonly height = input('340px');

  /**
   * Whether the address row is shown and resolved. Turn it **off** where the consumer only stores a
   * coordinate — a planned location may have latitude/longitude columns and nothing to put an
   * address in, so offering the field would invite the user to type something that is thrown away.
   * The search box stays either way; finding a place by name is useful regardless.
   */
  readonly showAddress = input(true);

  protected readonly error = signal<string | null>(null);
  protected readonly locating = signal(false);
  protected readonly copied = signal(false);
  protected readonly geocoding = signal(false);
  protected readonly searchReady = signal(false);
  protected readonly mapType = signal<MapType>(MAP_TYPES.Roadmap);

  /**
   * True when there is no map to click: no key configured, or the script never arrived. The field
   * then takes the coordinate by hand — a typed location is still a complete answer, and a dead
   * grey panel is not.
   */
  protected readonly manualEntry = computed(
    () => this.error() === 'geoMap.notConfigured' || this.error() === 'geoMap.loadFailed',
  );

  /**
   * What the manual boxes show. Held as text, and read only when the box is left, so a coordinate
   * is never parsed halfway through being typed.
   */
  protected readonly manualLat = signal('');
  protected readonly manualLng = signal('');

  protected readonly maxAddressLength = MAX_ADDRESS_LENGTH;
  protected readonly roadmap = MAP_TYPES.Roadmap;
  protected readonly satellite = MAP_TYPES.Satellite;
  protected readonly activeViewClass = 'bg-[var(--p-primary-color)] text-white';
  // Token-based so the idle state follows the colour scheme; the previous
  // slate classes only worked in light mode.
  protected readonly idleViewClass = 'text-[var(--p-text-color)] hover:bg-app-hover';

  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');
  private readonly search = viewChild<ElementRef<HTMLDivElement>>('search');
  private map: GoogleMapInstance | null = null;
  private marker: GoogleMarkerInstance | null = null;
  private geocoder: GoogleGeocoder | null = null;
  private destroyed = false;

  /**
   * Sequence number for reverse-geocode requests. Two pins dropped in quick succession resolve out
   * of order often enough to matter, and the older reply must not overwrite the newer address.
   */
  private geocodeRequest = 0;

  /**
   * Set once the surveyor types over the resolved address, so a lookup already in flight cannot
   * clobber what they wrote. Cleared whenever the pin itself moves — that is a new place, and its
   * address should be resolved afresh.
   */
  private addressEdited = false;

  /** Coordinates the marker was last drawn at — see {@link syncMarker}. */
  private syncedAt: string | null = null;

  constructor() {
    // Keep the marker in step when the value is set from outside (default
    // value, "my location", a parent patching the form, …).
    effect(() => {
      const point = this.value();
      if (this.map) {
        this.syncMarker(point);
      }
    });

    // Same for the manual boxes: they follow the value, including one set by "my location" or by a
    // parent. This runs on a committed value, never mid-keystroke, so it cannot fight the typist.
    effect(() => {
      const point = this.value();
      this.manualLat.set(point ? String(point.lat) : '');
      this.manualLng.set(point ? String(point.lng) : '');
    });
  }

  async ngAfterViewInit(): Promise<void> {
    if (!this.loader.isConfigured) {
      this.error.set('geoMap.notConfigured');
      return;
    }

    try {
      const api = await this.loader.load();
      if (this.destroyed) {
        return;
      }

      const point = this.value();
      this.map = new api.Map(this.canvas().nativeElement, {
        center: point ?? SAUDI_ARABIA_CENTER,
        zoom: point ? PICKED_ZOOM : this.zoom(),
        mapTypeId: this.mapType(),
        // Replaced by our own compact view switch.
        mapTypeControl: false,
        streetViewControl: false,
        fullscreenControl: true,
        clickableIcons: false,
      });

      if (!this.readonly()) {
        this.map.addListener('click', (event) => {
          const latLng = event.latLng;
          if (latLng) {
            this.commit({ lat: latLng.lat(), lng: latLng.lng() });
          }
        });
      }

      this.markerFactory = (position) =>
        new api.Marker({ position, map: this.map!, draggable: !this.readonly() });

      this.geocoder = new api.Geocoder();
      this.mountSearch(api);

      this.syncMarker(point);
    } catch {
      this.error.set('geoMap.loadFailed');
    } finally {
      this.changeDetector.markForCheck();
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.marker?.setMap(null);
    this.marker = null;
    this.map = null;
    this.geocoder = null;
  }

  /**
   * Mounts the Places autocomplete into the search row. Selecting a suggestion gives us both the
   * coordinate and the address in one call, so the reverse-geocode round trip is skipped. Silently
   * left unmounted — the row stays hidden — when the API was loaded without the Places library.
   */
  private mountSearch(api: GoogleMapsApi): void {
    const host = this.search()?.nativeElement;
    const places = api.places;
    if (!host || !places || this.readonly() || !this.loader.hasPlaces(api)) {
      return;
    }

    const element = new places.PlaceAutocompleteElement({ includedRegionCodes: REGION_CODES });
    element.addEventListener('gmp-select', (event: Event) => {
      void this.applyPrediction(event as Event & PlaceSelectEvent);
    });

    host.appendChild(element);
    this.searchReady.set(true);
  }

  private async applyPrediction(event: Event & PlaceSelectEvent): Promise<void> {
    const prediction = event.placePrediction;
    if (!prediction) {
      return;
    }

    try {
      const { place } = await prediction
        .toPlace()
        .fetchFields({ fields: ['location', 'formattedAddress'] });

      const location = place.location;
      if (!location || this.destroyed) {
        return;
      }

      // The search already carries the address, so the pin is set and named in
      // one step rather than resolved a second time.
      this.setPoint(
        { lat: location.lat(), lng: location.lng(), address: place.formattedAddress ?? null },
        { resolveAddress: false },
      );
      this.map?.setZoom(PICKED_ZOOM);
      this.clearLookupError();
    } catch {
      this.error.set(ADDRESS_LOOKUP_ERROR);
    } finally {
      this.changeDetector.markForCheck();
    }
  }

  protected setMapType(type: MapType): void {
    this.mapType.set(type);
    this.map?.setMapTypeId(type);
  }

  /** Back to the country-wide view without clearing the selection. */
  protected recenter(): void {
    const point = this.value();
    this.map?.setCenter(point ?? SAUDI_ARABIA_CENTER);
    this.map?.setZoom(point ? PICKED_ZOOM : this.zoom());
  }

  protected copyCoordinates(): void {
    const point = this.value();
    if (!point) {
      return;
    }
    const coordinates = `${point.lat}, ${point.lng}`;
    this.clipboard.copy(point.address ? `${coordinates} — ${point.address}` : coordinates);
    this.copied.set(true);
    setTimeout(() => {
      this.copied.set(false);
      this.changeDetector.markForCheck();
    }, COPIED_FEEDBACK_MS);
  }

  protected clear(): void {
    // Cancels anything in flight — its reply must not resurrect an address on a cleared field.
    this.geocodeRequest++;
    this.geocoding.set(false);
    this.addressEdited = false;
    this.value.set(null);
  }

  /** A hand-typed address wins over whatever the geocoder would return for this pin. */
  protected onAddressInput(event: Event): void {
    const point = this.value();
    if (!point) {
      return;
    }

    this.addressEdited = true;
    const typed = (event.target as HTMLInputElement).value.trim();
    this.value.set({ ...point, address: typed || null });
  }

  /** Re-runs the lookup, discarding a hand-typed address on purpose — that is what the button is for. */
  protected refreshAddress(): void {
    const point = this.value();
    if (!point || this.geocoding()) {
      return;
    }

    this.addressEdited = false;
    void this.resolveAddress(point);
  }

  /**
   * Takes a coordinate typed into the manual boxes, once the box is left. Both halves are needed:
   * a latitude on its own is not a location, and committing one would silently invent a longitude
   * of zero.
   */
  protected onManualCoordinate(part: 'lat' | 'lng', event: Event): void {
    const text = (event.target as HTMLInputElement).value.trim();
    (part === 'lat' ? this.manualLat : this.manualLng).set(text);

    const latText = this.manualLat();
    const lngText = this.manualLng();

    if (latText === '' && lngText === '') {
      this.clear();
      return;
    }

    const lat = Number(latText);
    const lng = Number(lngText);

    if (
      latText === '' ||
      lngText === '' ||
      !Number.isFinite(lat) ||
      !Number.isFinite(lng) ||
      Math.abs(lat) > 90 ||
      Math.abs(lng) > 180
    ) {
      return;
    }

    // There is no geocoder to ask without the API, so the address stays whatever was typed into
    // its own box.
    this.setPoint({ lat, lng, address: this.value()?.address ?? null }, { resolveAddress: false });
  }

  protected useMyLocation(): void {
    if (!navigator.geolocation) {
      this.error.set('geoMap.geolocationUnavailable');
      return;
    }
    this.locating.set(true);
    navigator.geolocation.getCurrentPosition(
      (position) => {
        this.commit({ lat: position.coords.latitude, lng: position.coords.longitude });
        this.map?.setZoom(PICKED_ZOOM);
        this.locating.set(false);
        this.changeDetector.markForCheck();
      },
      () => {
        this.error.set('geoMap.geolocationDenied');
        this.locating.set(false);
        this.changeDetector.markForCheck();
      },
    );
  }

  /** Builds a marker bound to the live map; set once the API has loaded. */
  private markerFactory: ((position: GeoPoint) => GoogleMarkerInstance) | null = null;

  /** A pin the user just placed: new coordinates, so the address is resolved for them. */
  private commit(point: GeoPoint): void {
    this.setPoint(point, { resolveAddress: true });
  }

  private setPoint(point: GeoPoint, options: { resolveAddress: boolean }): void {
    const next: GeoPoint = {
      lat: roundCoordinate(point.lat),
      lng: roundCoordinate(point.lng),
      address: point.address ?? null,
    };

    // The pin moved, so whatever address was on the old one no longer describes it.
    this.addressEdited = false;
    this.value.set(next);

    if (options.resolveAddress) {
      void this.resolveAddress(next);
    }
  }

  /**
   * Names the point via the geocoder, in the language the form is being filled in. A failure is a
   * warning, never a block: the coordinate is a complete answer on its own, and the surveyor can
   * always type the address instead.
   */
  private async resolveAddress(point: GeoPoint): Promise<void> {
    // No lookup where the address is not shown — it is a billed call for a value nobody stores.
    if (!this.geocoder || !this.showAddress()) {
      return;
    }

    const request = ++this.geocodeRequest;
    this.geocoding.set(true);

    try {
      const response = await this.geocoder.geocode({
        location: { lat: point.lat, lng: point.lng },
        language: this.locale.locale(),
      });

      // A newer pin, a clear, or a hand-typed address landed while this was in flight.
      if (this.destroyed || request !== this.geocodeRequest || this.addressEdited) {
        return;
      }

      const address = response.results[0]?.formatted_address?.trim();
      const current = this.value();
      if (address && current) {
        this.value.set({ ...current, address: address.slice(0, MAX_ADDRESS_LENGTH) });
      }

      this.clearLookupError();
    } catch {
      if (request === this.geocodeRequest) {
        this.error.set(ADDRESS_LOOKUP_ERROR);
      }
    } finally {
      if (request === this.geocodeRequest) {
        this.geocoding.set(false);
      }
      this.changeDetector.markForCheck();
    }
  }

  /**
   * Drops the "could not look up the address" banner once a lookup works again. Only that one —
   * `notConfigured` and `loadFailed` describe a map that is still broken.
   */
  private clearLookupError(): void {
    if (this.error() === ADDRESS_LOOKUP_ERROR) {
      this.error.set(null);
    }
  }

  private syncMarker(point: GeoPoint | null): void {
    if (!point) {
      this.syncedAt = null;
      this.marker?.setMap(null);
      this.marker = null;
      return;
    }

    // The value also changes on every keystroke in the address box. Redrawing and re-panning the
    // map for those would fight the user, so only a change of coordinates moves the marker.
    const key = `${point.lat},${point.lng}`;
    if (key === this.syncedAt && this.marker) {
      return;
    }
    this.syncedAt = key;

    if (!this.marker) {
      this.marker = this.markerFactory?.(point) ?? null;
      if (this.marker && !this.readonly()) {
        this.marker.addListener('dragend', (event) => {
          const latLng = event.latLng;
          if (latLng) {
            this.commit({ lat: latLng.lat(), lng: latLng.lng() });
          }
        });
      }
    } else {
      this.marker.setPosition(point);
    }

    this.map?.panTo(point);
  }
}
