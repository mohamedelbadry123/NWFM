import { APP_INITIALIZER, EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { LocaleService } from './locale.service';

function localeInitFactory(translate: TranslateService, locale: LocaleService) {
  return (): Promise<void> => {
    const lang = locale.locale();

    translate.setDefaultLang('en');

    // Apply dir/lang to <html> immediately — before any component renders —
    // so RTL users never see a LTR flash even during the HTTP load.
    document.documentElement.setAttribute('lang', lang);
    document.documentElement.setAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');

    // Block app bootstrap until the active locale JSON is fully fetched.
    return firstValueFrom(translate.use(lang)).then(() => undefined);
  };
}

export function provideLocaleInitializer(): EnvironmentProviders {
  return makeEnvironmentProviders([
    {
      provide: APP_INITIALIZER,
      useFactory: localeInitFactory,
      deps: [TranslateService, LocaleService],
      multi: true,
    },
  ]);
}
