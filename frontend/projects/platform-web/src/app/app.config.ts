import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { providePrimeNG } from 'primeng/config';
import { MessageService } from 'primeng/api';
import { LanguageService, WaterPreset } from '@platform/shared';

import { routes } from './app.routes';
import { AppConfigService } from './core/config/app-config.service';
import { platformAuthInterceptor } from './core/interceptors/platform-auth.interceptor';
import { platformErrorInterceptor } from './core/interceptors/platform-error.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([platformAuthInterceptor, platformErrorInterceptor])),
    provideAnimationsAsync(),
    MessageService,
    providePrimeNG({
      theme: {
        preset: WaterPreset,
        options: {
          darkModeSelector: '[data-theme="dark"]',
          cssLayer: {
            name: 'primeng',
            order: 'tailwind-base, primeng, tailwind-utilities',
          },
        },
      },
    }),
    provideTranslateService({ fallbackLang: 'en' }),
    // enforceLoading stamps each fetch, so a browser holding an older dictionary cannot pair it
    // with a newer build and show raw keys.
    provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json', enforceLoading: true }),
    // The dictionary must be in before the first template asks for a key.
    provideAppInitializer(async () => {
      const configService = inject(AppConfigService);
      const languageService = inject(LanguageService);
      await configService.load();
      await languageService.init(configService.snapshot.defaultLanguage);
    }),
  ],
};
