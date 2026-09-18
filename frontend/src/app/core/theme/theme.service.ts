import { Injectable, signal } from '@angular/core';

const THEME_KEY = 'privora_theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly _isDark = signal<boolean>(
    localStorage.getItem(THEME_KEY) === 'dark'
  );

  readonly isDark = this._isDark.asReadonly();

  constructor() {
    document.documentElement.classList.toggle('dark', this._isDark());
  }

  toggle(): void {
    const dark = !this._isDark();
    this._isDark.set(dark);
    document.documentElement.classList.toggle('dark', dark);
    localStorage.setItem(THEME_KEY, dark ? 'dark' : 'light');
  }
}
