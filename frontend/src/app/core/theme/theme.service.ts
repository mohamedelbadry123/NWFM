import { Injectable, computed, signal } from '@angular/core';

const THEME_STORAGE_KEY = 'nwfm.theme';
const LEGACY_THEME_KEY = 'privora_theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly _isDark = signal(false);

  /** Workflow designer and other existing pages call `theme.isDark()`. */
  readonly isDark = this._isDark.asReadonly();
  readonly mode = computed(() => this._isDark() ? 'dark' : 'light');

  constructor() {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    const fromLegacy = localStorage.getItem(LEGACY_THEME_KEY);
    const dark = stored === 'dark' || (stored !== 'light' && fromLegacy === 'dark');
    this.apply(dark);
  }

  toggle(): void {
    this.apply(!this._isDark());
  }

  private apply(dark: boolean): void {
    this._isDark.set(dark);
    localStorage.setItem(THEME_STORAGE_KEY, dark ? 'dark' : 'light');
    document.documentElement.classList.toggle('dark', dark);
    if (dark) {
      document.documentElement.setAttribute('data-theme', 'dark');
    } else {
      document.documentElement.removeAttribute('data-theme');
    }
  }
}
