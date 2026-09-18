import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

// Reusable pagination bar shared by every paginated list screen in the app
// (Beneficiaries, Terms and Conditions, Data Processing Organizations, Consent Requests).
// Owns all the numeric pagination logic AND the "X–Y of Z <items>" range text/controls
// as a single self-contained unit — previously ~30 lines of computed signals plus ~20
// lines of near-identical markup were duplicated across every list component. The parent
// only supplies page/pageSize/totalCount/itemsLabel (the already-translated noun, e.g.
// "beneficiaries" — this varies per feature so it can't be baked in here) and reacts to
// (pageChange) by updating its own page signal and re-fetching.
@Component({
  selector: 'app-pagination',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './pagination.component.html',
  styleUrl: './pagination.component.css',
})
export class PaginationComponent {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly totalCount = input.required<number>();
  /** Already-translated noun for the range text, e.g. "beneficiaries" / "policies". */
  readonly itemsLabel = input('');

  /** Emits the newly selected page number. Parent is responsible for setting its own
   *  page signal and re-fetching — this component holds no fetch/loading state itself. */
  readonly pageChange = output<number>();

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize()))
  );

  protected readonly rangeStart = computed(() =>
    this.totalCount() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1
  );

  protected readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize(), this.totalCount())
  );

  protected readonly pageNumbers = computed(() => {
    const total = this.totalPages();
    const current = this.page();
    const pages: (number | '...')[] = [];
    for (let i = 1; i <= total; i++) {
      if (i === 1 || i === total || (i >= current - 2 && i <= current + 2)) {
        pages.push(i);
      } else if (pages[pages.length - 1] !== '...') {
        pages.push('...');
      }
    }
    return pages;
  });

  protected prevPage(): void {
    if (this.page() > 1) this.pageChange.emit(this.page() - 1);
  }

  protected nextPage(): void {
    if (this.page() < this.totalPages()) this.pageChange.emit(this.page() + 1);
  }

  protected goToPage(p: number | '...'): void {
    if (p === '...' || p === this.page()) return;
    this.pageChange.emit(p as number);
  }

  protected isNumber(v: number | '...'): v is number {
    return typeof v === 'number';
  }
}
