import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AppLanguage } from './app-language';

const LANG_STORAGE_KEY = 'nwfm_platform_locale';
const RTL_LANGS: readonly AppLanguage[] = ['ar'];

/** Owns the active language and keeps the document direction and lang in sync. */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly translate = inject(TranslateService);
  private readonly _lang = signal<AppLanguage>('ar');

  readonly current = this._lang.asReadonly();
  readonly isRtl = computed(() => RTL_LANGS.includes(this._lang()));
  readonly direction = computed<'rtl' | 'ltr'>(() => (this.isRtl() ? 'rtl' : 'ltr'));

  /** Called once during bootstrap after config is loaded; resolves when the dictionary is in. */
  async init(defaultLang: AppLanguage): Promise<void> {
    const stored = localStorage.getItem(LANG_STORAGE_KEY);
    await this.setLanguage(stored === 'en' || stored === 'ar' ? stored : defaultLang);
  }

  async setLanguage(lang: AppLanguage): Promise<void> {
    this._lang.set(lang);
    localStorage.setItem(LANG_STORAGE_KEY, lang);
    this.applyToDocument();
    await firstValueFrom(this.translate.use(lang));
  }

  toggle(): void {
    void this.setLanguage(this._lang() === 'ar' ? 'en' : 'ar');
  }

  private applyToDocument(): void {
    const el = document.documentElement;
    el.setAttribute('lang', this._lang());
    el.setAttribute('dir', this.direction());
  }
}
