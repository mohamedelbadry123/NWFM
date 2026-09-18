import { computed, inject, Injectable, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

export type Locale = 'en' | 'ar';

const LOCALE_KEY = 'nwfm_locale';

@Injectable({ providedIn: 'root' })
export class LocaleService {
  private readonly translate = inject(TranslateService);

  private readonly _locale = signal<Locale>(
    (localStorage.getItem(LOCALE_KEY) as Locale | null) ?? 'en'
  );

  readonly locale = this._locale.asReadonly();
  readonly isRtl = computed(() => this._locale() === 'ar');

  setLocale(lang: Locale): void {
    this._locale.set(lang);
    this.translate.use(lang);
    localStorage.setItem(LOCALE_KEY, lang);
    document.documentElement.setAttribute('lang', lang);
    document.documentElement.setAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
  }
}
