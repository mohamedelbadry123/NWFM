import { Component, input } from '@angular/core';
import { TooltipModule } from 'primeng/tooltip';

/**
 * A small info icon that explains a console setting or action on hover or keyboard focus — what it
 * is for and what changing it affects. Pass text that is already translated.
 */
@Component({
  selector: 'app-help-tip',
  imports: [TooltipModule],
  template: `
    <i
      class="pi pi-info-circle help-tip"
      tabindex="0"
      role="img"
      [attr.aria-label]="text()"
      [pTooltip]="text()"
      tooltipPosition="top"
    ></i>
  `,
  styles: `
    :host {
      display: inline-flex;
      vertical-align: middle;
    }

    .help-tip {
      font-size: 0.85rem;
      color: var(--p-text-muted-color);
      cursor: help;
      margin-inline-start: 0.3rem;

      &:hover,
      &:focus-visible {
        color: var(--p-primary-color);
        outline: none;
      }
    }
  `,
})
export class HelpTipComponent {
  readonly text = input.required<string>();
}
