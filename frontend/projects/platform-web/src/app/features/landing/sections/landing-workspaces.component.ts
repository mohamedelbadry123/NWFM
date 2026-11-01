import { Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { I18nDirective } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { SkeletonModule } from 'primeng/skeleton';
import { PublicTenantCard } from '../../../core/api/public-api.service';
import { tenantInitials } from '../tenant-initials';

const SKELETON_CARDS = 3;

/**
 * "Already a customer?" — a sign-in card per serving workspace with a search box. The page loads
 * the list (the hero uses it too) and hands it in; this section only filters and links.
 */
@Component({
  selector: 'app-landing-workspaces',
  imports: [FormsModule, RouterLink, I18nDirective, ButtonModule, InputText, SkeletonModule],
  templateUrl: './landing-workspaces.component.html',
  styleUrl: './landing-workspaces.component.scss',
})
export class LandingWorkspacesComponent {
  readonly tenants = input.required<readonly PublicTenantCard[]>();
  readonly loading = input(false);
  readonly failed = input(false);
  readonly retry = output<void>();

  protected readonly skeletons = Array.from({ length: SKELETON_CARDS }, (_, i) => i);
  protected readonly initials = tenantInitials;
  protected readonly query = signal('');
  protected readonly notFound = signal(false);

  protected readonly filtered = computed(() => {
    const term = this.query().trim().toLowerCase();
    const all = this.tenants();
    return term
      ? all.filter((t) => t.name.toLowerCase().includes(term) || t.key.includes(term))
      : all;
  });

  protected onQueryChange(value: string): void {
    this.query.set(value);
    this.notFound.set(false);
  }

  /** Enter goes straight to the workspace when the search narrows to exactly one. */
  protected find(): void {
    const matches = this.filtered();
    if (this.query().trim() && matches.length === 1) {
      window.location.href = matches[0].loginUrl;
      return;
    }
    this.notFound.set(matches.length === 0);
  }

  protected host(url: string): string {
    try {
      return new URL(url).host;
    } catch {
      return url;
    }
  }
}
