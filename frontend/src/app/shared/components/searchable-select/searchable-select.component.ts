import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';

export interface SearchableSelectOption {
  value: string;
  label: string;
  sublabel?: string;
}

// Lightweight, dependency-free searchable dropdown — this codebase has no third-party
// UI component library (no Angular Material, no ng-select, no PrimeNG), so this fills the
// same role as the search-and-select pattern already used ad hoc in the ConsentRequest
// wizard (Beneficiary/Term pickers), but as one reusable component instead of duplicated
// per-feature logic. Not a ControlValueAccessor — callers bind `[value]`/`(valueChange)`
// directly and update their own FormControl, matching this codebase's existing convention
// of reading/writing FormGroup state explicitly rather than through custom CVA controls.
@Component({
  selector: 'app-searchable-select',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './searchable-select.component.html',
  styleUrl: './searchable-select.component.css',
})
export class SearchableSelectComponent {
  private readonly elementRef = inject(ElementRef<HTMLElement>);

  readonly options = input.required<SearchableSelectOption[]>();
  readonly value = input<string | null>(null);
  readonly placeholder = input('');
  readonly searchPlaceholder = input('');
  readonly noResultsText = input('No results found.');
  readonly clearAriaLabel = input('Clear selection');
  readonly disabled = input(false);
  readonly invalid = input(false);

  /** Emits the newly selected value, or null when cleared. */
  readonly valueChange = output<string | null>();
  /** Emitted whenever the dropdown closes (selection or blur) — callers use this to
   *  mark their FormControl as touched, mirroring native input blur behaviour. */
  readonly touched = output<void>();

  protected readonly isOpen = signal(false);
  protected readonly searchTerm = signal('');

  protected readonly selectedOption = computed(
    () => this.options().find(o => o.value === this.value()) ?? null
  );

  protected readonly filteredOptions = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const opts = this.options();
    if (!term) return opts;
    return opts.filter(
      o =>
        o.label.toLowerCase().includes(term) ||
        (o.sublabel?.toLowerCase().includes(term) ?? false)
    );
  });

  protected toggleOpen(): void {
    if (this.disabled()) return;
    const next = !this.isOpen();
    this.isOpen.set(next);
    if (!next) {
      this.searchTerm.set('');
      this.touched.emit();
    }
  }

  protected onSearchInput(term: string): void {
    this.searchTerm.set(term);
  }

  protected select(option: SearchableSelectOption): void {
    this.valueChange.emit(option.value);
    this.isOpen.set(false);
    this.searchTerm.set('');
    this.touched.emit();
  }

  protected clear(event: Event): void {
    event.stopPropagation();
    this.valueChange.emit(null);
    this.touched.emit();
  }

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (this.isOpen() && !this.elementRef.nativeElement.contains(event.target as Node)) {
      this.isOpen.set(false);
      this.searchTerm.set('');
      this.touched.emit();
    }
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.isOpen.set(false);
  }
}
