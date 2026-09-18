import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type PrvSpinnerSize = 'sm' | 'md' | 'lg';

/** prv-spinner — indeterminate loading indicator in the brand primary colour. */
@Component({
  selector: 'prv-spinner',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span [class]="classes()" role="status" [attr.aria-label]="label()"></span>`,
  styles: [`
    :host { display: inline-flex; }
    span {
      display: inline-block;
      border-radius: 999px;
      border-style: solid;
      border-color: currentColor;
      border-top-color: transparent;
      animation: prv-spin 0.6s linear infinite;
    }
    @keyframes prv-spin { to { transform: rotate(360deg); } }
  `],
})
export class PrvSpinnerComponent {
  readonly size = input<PrvSpinnerSize>('md');
  readonly label = input('Loading');

  private static readonly SIZES: Record<PrvSpinnerSize, string> = {
    sm: 'w-4 h-4 border-2',
    md: 'w-6 h-6 border-[3px]',
    lg: 'w-9 h-9 border-4',
  };

  protected readonly classes = computed(() => `text-primary ${PrvSpinnerComponent.SIZES[this.size()]}`);
}
