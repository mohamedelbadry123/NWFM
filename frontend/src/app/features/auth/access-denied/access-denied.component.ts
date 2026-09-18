import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-access-denied',
  standalone: true,
  imports: [TranslateModule, ButtonModule, RouterLink],
  template: `
    <div class="flex min-h-screen items-center justify-center bg-ink-50 px-4">
      <div class="w-full max-w-md rounded-2xl border border-ink-200 bg-white p-8 text-center shadow-lg">
        <div class="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-danger-50">
          <svg class="h-8 w-8 text-danger" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M18.364 18.364A9 9 0 005.636 5.636m12.728 12.728A9 9 0 015.636 5.636m12.728 12.728L5.636 5.636" />
          </svg>
        </div>
        <h1 class="text-xl font-bold text-ink-900">{{ 'auth.access_denied.title' | translate }}</h1>
        <p class="mt-2 text-sm text-ink-500">
          @if (reason) {
            {{ reason }}
          } @else {
            {{ 'auth.access_denied.message' | translate }}
          }
        </p>
        <div class="mt-6">
          <p-button [label]="'auth.access_denied.back_to_login' | translate"
            routerLink="/login" severity="secondary" />
        </div>
      </div>
    </div>
  `
})
export class AccessDeniedComponent {
  private readonly route = inject(ActivatedRoute);
  readonly reason = this.route.snapshot.queryParamMap.get('reason');
}
