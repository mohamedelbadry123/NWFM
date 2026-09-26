import { Injectable, signal } from '@angular/core';
import { AppConfig, DEFAULT_APP_CONFIG, PLACEHOLDER_PREFIX } from './app-config.model';

const CONFIG_URL = '/config/app-config.json';

/**
 * Loads `public/config/app-config.json` once, before the app starts.
 *
 * Deliberately `fetch` rather than `HttpClient`: this runs before the app's interceptors are worth
 * anything, and the file is a static asset that needs no token. A missing or malformed file is not
 * fatal — the app starts on defaults and the features that need a key degrade instead of failing.
 */
@Injectable({ providedIn: 'root' })
export class AppConfigService {
  private readonly _config = signal<AppConfig>(DEFAULT_APP_CONFIG);

  readonly config = this._config.asReadonly();

  get snapshot(): AppConfig {
    return this._config();
  }

  /** True when a usable Maps key is configured — the placeholder does not count. */
  get hasGoogleMapsKey(): boolean {
    const key = this._config().googleMapsApiKey?.trim() ?? '';

    return key.length > 0 && !key.startsWith(PLACEHOLDER_PREFIX);
  }

  async load(): Promise<void> {
    try {
      const response = await fetch(CONFIG_URL, { cache: 'no-cache' });

      if (!response.ok) {
        return;
      }

      const config = (await response.json()) as Partial<AppConfig>;
      this._config.set({ ...DEFAULT_APP_CONFIG, ...config });
    } catch {
      // Keep the defaults: a deployment without the file still runs.
    }
  }
}
