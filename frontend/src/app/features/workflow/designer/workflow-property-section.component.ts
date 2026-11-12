import { Component, EventEmitter, Input, Output } from '@angular/core';

/** Presentation only: the owning editor retains configuration and draft state. */
@Component({
  selector: 'app-workflow-property-section',
  standalone: true,
  template: `
    <section class="property-section" [class.is-open]="open">
      <h3>
        <button
          type="button"
          [attr.aria-expanded]="open"
          [attr.aria-controls]="sectionId"
          (click)="opened.emit()"
        >
          <span
            ><strong>{{ title }}</strong
            ><small>{{ summary }}</small></span
          >
          <span aria-hidden="true">{{ open ? '−' : '+' }}</span>
        </button>
      </h3>
      <div [id]="sectionId" [hidden]="!open" class="section-content"><ng-content /></div>
    </section>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .property-section {
        border-bottom: 1px solid var(--ds-border);
      }
      h3 {
        margin: 0;
      }
      button {
        display: flex;
        width: 100%;
        align-items: center;
        justify-content: space-between;
        gap: 12px;
        padding: 16px 20px;
        text-align: start;
        color: var(--ds-text);
        background: var(--ds-surface);
        cursor: pointer;
      }
      button:hover,
      .is-open button {
        background: var(--ds-primary-soft);
      }
      strong {
        display: block;
        font-size: 14px;
        font-weight: 600;
      }
      small {
        display: block;
        font-size: 12px;
        font-weight: 400;
        color: var(--ds-text-3);
        margin-top: 4px;
        line-height: 1.5;
      }
      button:focus-visible {
        outline: 2px solid var(--ds-primary);
        outline-offset: -3px;
      }
      .section-content {
        padding: 0 4px 12px;
      }
      [hidden] {
        display: none !important;
      }
    `,
  ],
})
export class WorkflowPropertySectionComponent {
  @Input({ required: true }) sectionId = '';
  @Input({ required: true }) title = '';
  @Input() summary = '';
  @Input() open = false;
  @Output() opened = new EventEmitter<void>();
}
