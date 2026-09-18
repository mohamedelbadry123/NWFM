import { ChangeDetectionStrategy, Component, ViewEncapsulation } from '@angular/core';

/**
 * prv-table — themed table surface. Wrap a standard semantic table; the component styles
 * the projected <thead>/<tbody>/<th>/<td> to the NWFM design system (adapted from
 * TailAdmin's table). Horizontal overflow scrolls inside the card so the page never does.
 *
 *   <prv-table>
 *     <table>
 *       <thead><tr><th>Name</th><th>Status</th></tr></thead>
 *       <tbody>
 *         <tr><td>…</td><td><prv-status-pill …/></td></tr>
 *       </tbody>
 *     </table>
 *   </prv-table>
 *
 * Styles are scoped by the `.prv-table` host class (ViewEncapsulation.None), so they never
 * leak to tables outside this component.
 */
@Component({
  selector: 'prv-table',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  host: { class: 'prv-table' },
  template: `
    <div class="prv-table__scroll">
      <ng-content />
    </div>
  `,
  styles: [`
    .prv-table { display: block; }
    .prv-table__scroll { width: 100%; overflow-x: auto; }
    .prv-table table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }

    .prv-table thead th {
      text-align: start;
      font-weight: 600;
      font-size: 0.75rem;
      letter-spacing: 0.02em;
      color: #5A6377;
      background: #F4F6FB;
      padding: 0.75rem 1rem;
      white-space: nowrap;
      border-bottom: 1px solid #E7EBF3;
    }
    .prv-table tbody td {
      padding: 0.875rem 1rem;
      color: #2E3550;
      border-bottom: 1px solid #E7EBF3;
      vertical-align: middle;
    }
    .prv-table tbody tr:last-child td { border-bottom: 0; }
    .prv-table tbody tr { transition: background-color 0.12s ease; }
    .prv-table tbody tr:hover { background: #F4F6FB; }

    /* Dark mode (ViewEncapsulation.None → plain global selectors, scoped by .prv-table) */
    html.dark .prv-table thead th { color: rgba(234, 236, 245, 0.6); background: #141838; border-bottom-color: #242B5C; }
    html.dark .prv-table tbody td { color: rgba(234, 236, 245, 0.85); border-bottom-color: #242B5C; }
    html.dark .prv-table tbody tr:hover { background: rgba(255, 255, 255, 0.03); }
  `],
})
export class PrvTableComponent {}
