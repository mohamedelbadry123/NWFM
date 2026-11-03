import { Injectable, signal } from '@angular/core';

const THEME_STORAGE_KEY = 'dk.theme';
type ThemeMode = 'light' | 'dark';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly _mode = signal<ThemeMode>('light');
  readonly mode = this._mode.asReadonly();

  constructor() {
    const stored = localStorage.getItem(THEME_STORAGE_KEY) as ThemeMode | null;
    this.setMode(stored ?? 'light');
  }

  toggle(): void {
    this.setMode(this._mode() === 'dark' ? 'light' : 'dark');
  }

  private setMode(mode: ThemeMode): void {
    this._mode.set(mode);
    localStorage.setItem(THEME_STORAGE_KEY, mode);
    if (mode === 'dark') {
      document.documentElement.setAttribute('data-theme', 'dark');
    } else {
      document.documentElement.removeAttribute('data-theme');
    }
  }
}
