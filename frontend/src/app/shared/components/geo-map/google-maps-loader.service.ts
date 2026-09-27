import { Injectable, inject } from '@angular/core';
import { AppConfigService } from '../../../core/config/app-config.service';
import type { GoogleMapsApi } from './google-maps.types';

const SCRIPT_ID = 'google-maps-js-api';
const SCRIPT_BASE = 'https://maps.googleapis.com/maps/api/js';

/** Bias geocoding/behaviour towards Saudi Arabia. */
const REGION = 'SA';

/** Requested for the address search box; see `hasPlaces`. */
const PLACES_LIBRARY = 'places';

/**
 * How long one map waits before showing geoMap.loadFailed. Only that map gives up — the load
 * itself carries on (see `inflight`), so the next map to mount picks it up once it lands.
 */
const LOAD_TIMEOUT_MS = 15_000;

/**
 * Stop watching a load that never completed, so a later map re-examines the page rather than
 * joining a dead promise. Long enough for a slow link; a blocked host fails well before this.
 */
const WATCH_LIMIT_MS = 120_000;

/** Re-check interval, used only where Google's callback is not ours to receive. */
const READY_POLL_MS = 100;

/**
 * Google invokes this when the JS API is actually usable. `script.onload` is not
 * enough: the old index.html tag was a classic (non-async) bootstrap, so
 * `window.google.maps` existed before Angular started. The first runtime loader
 * used `loading=async` and resolved on `onload`, which often fired while the
 * namespace was still missing and showed geoMap.loadFailed.
 */
const CALLBACK_NAME = '__nwcGoogleMapsReady';

interface GoogleGlobal {
  maps?: Partial<GoogleMapsApi>;
}

/**
 * Loads the Google Maps JS API on demand from `app-config.json` → `googleMapsApiKey`.
 *
 * The script is not in `index.html`: a blocking tag would stall login and the rest of the
 * SPA until Google answered (or timed out), which is what happens on networks that throttle
 * or block `maps.googleapis.com`. Map components call `load()` when they actually mount.
 */
@Injectable({ providedIn: 'root' })
export class GoogleMapsLoaderService {
  private readonly config = inject(AppConfigService);

  /**
   * The one load in progress, shared by every map that mounts while it runs. A map that stops
   * waiting does not cancel it. It is cleared only when the load has really failed, so a slow
   * network costs one banner instead of breaking every map for the rest of the session.
   */
  private inflight: Promise<GoogleMapsApi> | null = null;

  /** False only when the API is nowhere to be found — callers show a hint then. */
  get isConfigured(): boolean {
    return !!this.resolveReady() || !!this.findScriptTag() || this.config.hasGoogleMapsKey;
  }

  /**
   * Whether the loaded API carries the Places library. A script tag that predates the address
   * search box does not request it, so the box is hidden rather than allowed to throw — the map
   * itself still works and the pin can still be dropped by hand.
   */
  hasPlaces(api: GoogleMapsApi): boolean {
    return !!api.places?.PlaceAutocompleteElement;
  }

  load(): Promise<GoogleMapsApi> {
    const ready = this.resolveReady();
    if (ready) {
      return Promise.resolve(ready);
    }

    if (!this.inflight) {
      // A tag is present but not yet usable: wait for it rather than requesting a
      // second copy of the API, which Google rejects.
      const tag = this.findScriptTag();
      if (tag) {
        this.inflight = this.watch(tag, { ownCallback: false });
      } else if (this.config.hasGoogleMapsKey) {
        this.inflight = this.watch(this.injectScript(), { ownCallback: true });
      } else {
        return Promise.reject(new Error('Google Maps API key is not configured.'));
      }
    }

    return this.withTimeout(this.inflight);
  }

  /**
   * The API counts as usable only once `Map` exists. The classic bootstrap creates `google.maps`
   * immediately and fills it in when `main.js` arrives. Checking the namespace alone gave any map
   * that mounted in between an object with no constructors, so `new api.Map` threw loadFailed.
   */
  private resolveReady(): GoogleMapsApi | null {
    const maps = (window as unknown as { google?: GoogleGlobal }).google?.maps;
    return typeof maps?.Map === 'function' ? (maps as GoogleMapsApi) : null;
  }

  /** Any Maps API script already on the page. */
  private findScriptTag(): HTMLScriptElement | null {
    return document.querySelector<HTMLScriptElement>(`script[src^="${SCRIPT_BASE}"]`);
  }

  /** Bounds how long this caller waits, without touching the shared load. */
  private withTimeout(promise: Promise<GoogleMapsApi>): Promise<GoogleMapsApi> {
    return new Promise<GoogleMapsApi>((resolve, reject) => {
      const timer = window.setTimeout(
        () => reject(new Error('Google Maps script timed out.')),
        LOAD_TIMEOUT_MS,
      );

      promise.then(
        (api) => {
          window.clearTimeout(timer);
          resolve(api);
        },
        (error: unknown) => {
          window.clearTimeout(timer);
          reject(error);
        },
      );
    });
  }

  /**
   * Same query string as the old index.html tag (`key`, `region=SA`, `libraries=places`),
   * plus Google's `callback` so we do not treat the bootstrap file as "ready".
   */
  private injectScript(): HTMLScriptElement {
    const params = new URLSearchParams({
      key: this.config.snapshot.googleMapsApiKey ?? '',
      region: REGION,
      language: document.documentElement.lang || 'en',
      libraries: PLACES_LIBRARY,
      callback: CALLBACK_NAME,
    });

    const script = document.createElement('script');
    script.id = SCRIPT_ID;
    script.src = `${SCRIPT_BASE}?${params.toString()}`;
    document.head.appendChild(script);
    return script;
  }

  /**
   * Resolves once the API is usable. For a script this service injected, that is Google's
   * callback. Polling alone would fire as soon as `Map` exists, before Places arrives, and hide
   * the search box. A tag we did not inject calls some other callback, or none, so that case polls.
   *
   * Called synchronously after the tag is appended, so the callback and error listener are in place
   * before the script can run or fail.
   */
  private watch(script: HTMLScriptElement, options: { ownCallback: boolean }): Promise<GoogleMapsApi> {
    return new Promise<GoogleMapsApi>((resolve, reject) => {
      let poll = 0;

      const settle = (): void => {
        window.clearTimeout(limit);
        window.clearInterval(poll);
        script.removeEventListener('error', onError);
        // A no-op rather than deleted: Google calls it by name and throws if it has gone.
        this.setCallback(() => undefined);
      };

      const fail = (error: Error): void => {
        settle();
        this.inflight = null;
        reject(error);
      };

      const check = (): boolean => {
        const api = this.resolveReady();
        if (api) {
          settle();
          resolve(api);
        }
        return !!api;
      };

      const startPolling = (): void => {
        poll ||= window.setInterval(check, READY_POLL_MS);
      };

      const onError = (): void => {
        // Nothing of the API ran, so a fresh tag on the next mount is safe — and the only way to
        // retry. Leaving this one would have every later map wait on a download that is over.
        script.remove();
        fail(new Error('Failed to load the Google Maps script.'));
      };

      const limit = window.setTimeout(
        () => fail(new Error('Google Maps never became ready.')),
        WATCH_LIMIT_MS,
      );

      script.addEventListener('error', onError);

      // Defensive: if the callback ever arrives before `Map` is in place, wait for it.
      this.setCallback(() => {
        if (!check()) {
          startPolling();
        }
      });

      if (!options.ownCallback && !check()) {
        startPolling();
      }
    });
  }

  private setCallback(handler: () => void): void {
    (window as unknown as Record<string, unknown>)[CALLBACK_NAME] = handler;
  }
}
