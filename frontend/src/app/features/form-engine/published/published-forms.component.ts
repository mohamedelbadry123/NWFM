import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TagModule } from 'primeng/tag';
import { LocaleService } from '../../../core/i18n/locale.service';
import { FormsService } from '../../../core/form-engine/forms.service';
import { PublishedForm } from '../../../core/form-engine/form-engine.models';

/**
 * The forms a person can fill right now. Separate from the management grid because filling and
 * designing are different jobs, held by different permissions.
 */
@Component({
  selector: 'app-published-forms',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule, ButtonModule, InputTextModule, TagModule],
  template: `
    <div class="flex flex-col gap-4">
      <div class="flex items-center justify-end gap-2">
        <input
          pInputText
          type="search"
          [(ngModel)]="search"
          (keyup.enter)="load()"
          [placeholder]="'common.search' | translate"
          class="w-64"
        />
        <p-button icon="pi pi-search" [text]="true" (onClick)="load()" [ariaLabel]="'common.search' | translate" />
      </div>

      @if (loading()) {
        <p class="p-6 text-center opacity-70">{{ 'common.loading' | translate }}</p>
      } @else if (forms().length === 0) {
        <p class="p-6 text-center opacity-70">{{ 'forms.published.empty' | translate }}</p>
      } @else {
        <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          @for (form of forms(); track form.id) {
            <button
              type="button"
              class="card flex flex-col gap-2 p-4 text-start transition hover:shadow-app-md"
              (click)="fill(form)"
            >
              <div class="flex items-start justify-between gap-2">
                <span class="font-medium">{{ nameOf()(form) }}</span>
                <p-tag [value]="'v' + form.currentVersionNo" severity="secondary" />
              </div>
              <span class="font-mono text-xs opacity-70">{{ form.code }}</span>
              <span class="text-xs opacity-70">{{ 'forms.categories.' + form.category | translate }}</span>
            </button>
          }
        </div>
      }
    </div>
  `,
})
export class PublishedFormsComponent implements OnInit {
  private readonly formsApi = inject(FormsService);
  private readonly locale = inject(LocaleService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly forms = signal<PublishedForm[]>([]);
  protected readonly loading = signal(false);
  protected readonly search = signal('');

  protected readonly nameOf = computed(() => (form: PublishedForm) =>
    this.locale.locale() === 'ar' ? form.nameAr : form.nameEn);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.formsApi
      .published(this.search() || null)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({
        next: (result) => this.forms.set(result.value ?? []),
        error: () => this.forms.set([]),
      });
  }

  protected fill(form: PublishedForm): void {
    void this.router.navigate(['/forms', form.id, 'fill']);
  }
}
