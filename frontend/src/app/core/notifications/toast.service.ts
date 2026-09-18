import { Injectable, signal } from '@angular/core';

export type ToastType = 'success' | 'error' | 'warning';

export interface Toast {
  id: string;
  type: ToastType;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly _toasts = signal<Toast[]>([]);
  readonly toasts = this._toasts.asReadonly();

  success(message: string, duration = 4000): void {
    this.push('success', message, duration);
  }

  error(message: string, duration = 8000): void {
    this.push('error', message, duration);
  }

  warning(message: string, duration = 6000): void {
    this.push('warning', message, duration);
  }

  dismiss(id: string): void {
    this._toasts.update(ts => ts.filter(t => t.id !== id));
  }

  private push(type: ToastType, message: string, duration: number): void {
    const id = crypto.randomUUID();
    this._toasts.update(ts => [...ts, { id, type, message }]);
    setTimeout(() => this.dismiss(id), duration);
  }
}
