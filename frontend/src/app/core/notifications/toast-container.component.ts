import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from './toast.service';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toast-stack">
      @for (toast of toastSvc.toasts(); track toast.id) {
        <div class="toast" [class]="'toast--' + toast.type" role="alert" aria-live="polite">
          <span class="toast-icon">
            @if (toast.type === 'success') {
              <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd"/>
              </svg>
            }
            @if (toast.type === 'error') {
              <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd"/>
              </svg>
            }
            @if (toast.type === 'warning') {
              <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd"/>
              </svg>
            }
          </span>
          <span class="toast-message">{{ toast.message }}</span>
          <button
            class="toast-close"
            type="button"
            (click)="toastSvc.dismiss(toast.id)"
            aria-label="Dismiss"
          >
            <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
              <path fill-rule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clip-rule="evenodd"/>
            </svg>
          </button>
        </div>
      }
    </div>
  `,
  styles: [`
    .toast-stack {
      position: fixed;
      inset-block-start: 1.25rem;
      inset-inline-end: 1.25rem;
      z-index: 9999;
      display: flex;
      flex-direction: column;
      gap: 0.625rem;
      width: 22rem;
      max-width: calc(100vw - 2.5rem);
      pointer-events: none;
    }

    .toast {
      display: flex;
      align-items: flex-start;
      gap: 0.625rem;
      padding: 0.75rem 1rem;
      border-radius: 0.625rem;
      border: 1px solid transparent;
      font-size: 0.875rem;
      font-weight: 500;
      line-height: 1.45;
      box-shadow: 0 4px 20px rgba(0, 0, 0, 0.1), 0 0 0 1px rgba(0, 0, 0, 0.04);
      pointer-events: auto;
      animation: toastIn 0.22s cubic-bezier(0.2, 0.8, 0.3, 1) both;
    }

    @keyframes toastIn {
      from { opacity: 0; transform: translateY(-8px) scale(0.97); }
      to   { opacity: 1; transform: translateY(0)  scale(1);    }
    }

    .toast--success { background: #f0fdf4; border-color: #bbf7d0; color: #15803d; }
    .toast--error   { background: #fef2f2; border-color: #fecaca; color: #b91c1c; }
    .toast--warning { background: #fffbeb; border-color: #fde68a; color: #92400e; }

    .toast-icon {
      flex-shrink: 0;
      width: 1.125rem;
      height: 1.125rem;
      margin-block-start: 0.0625rem;
    }
    .toast-icon svg { width: 100%; height: 100%; }

    .toast-message { flex: 1; min-width: 0; word-break: break-word; }

    .toast-close {
      flex-shrink: 0;
      width: 1rem;
      height: 1rem;
      margin-inline-start: 0.25rem;
      margin-block-start: 0.125rem;
      opacity: 0.5;
      cursor: pointer;
      background: none;
      border: none;
      padding: 0;
      color: inherit;
      transition: opacity 0.15s;
    }
    .toast-close:hover { opacity: 1; }
    .toast-close svg { width: 100%; height: 100%; }
  `]
})
export class ToastContainerComponent {
  protected readonly toastSvc = inject(ToastService);
}
