import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { routes } from './app.routes';
import { provideApiConfiguration } from './shared/models/api-configuration';
import { provideLocaleInitializer } from './core/i18n/locale.initializer';
import { AppContextService } from './core/context/app-context.service';
import { contextInterceptor } from './core/context/context.interceptor';
export const appConfig: ApplicationConfig = { providers: [
  provideZoneChangeDetection({ eventCoalescing: true }), provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top', anchorScrolling: 'enabled' })),
  provideHttpClient(withInterceptors([contextInterceptor])), provideApiConfiguration(''),
  provideTranslateService({ fallbackLang: 'en' }),
  provideTranslateHttpLoader({ prefix: '/assets/i18n/', suffix: '.json' }), provideLocaleInitializer(),
  provideAppInitializer(() => inject(AppContextService).load()),
] };
