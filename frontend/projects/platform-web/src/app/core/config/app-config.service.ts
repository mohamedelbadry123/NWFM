import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AppLanguage } from '@platform/shared';

const CONFIG_URL = 'config/app-config.json';

/** Runtime configuration of the platform web app, read from `config/app-config.json` before bootstrap. */
export interface PlatformAppConfig {
  /** The control-plane API (NWFM.Platform.Api), never the tenant API. Empty means this origin. */
  readonly apiBaseUrl: string;
  readonly defaultLanguage: AppLanguage;
  readonly appName: string;
}

const DEFAULTS: Omit<PlatformAppConfig, 'apiBaseUrl'> = {
  defaultLanguage: 'ar',
  appName: 'NWFM',
};

@Injectable({ providedIn: 'root' })
export class AppConfigService {
  private readonly http = inject(HttpClient);
  private readonly config = signal<PlatformAppConfig | null>(null);

  get snapshot(): PlatformAppConfig {
    const loaded = this.config();
    if (!loaded) {
      throw new Error('config/app-config.json has not been loaded.');
    }
    return loaded;
  }

  async load(): Promise<void> {
    const loaded = await firstValueFrom(this.http.get<Partial<PlatformAppConfig>>(CONFIG_URL));
    // Empty means this origin: the dev server and nginx proxy /api to the control plane.
    const apiBaseUrl = (loaded.apiBaseUrl ?? '').trim().replace(/\/+$/, '');
    this.config.set({ ...DEFAULTS, ...loaded, apiBaseUrl });
  }
}
