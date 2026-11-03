import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';

/** Feature-tile drawing: the workflow designer's node palette beside a canvas with a conditional route. */
@Component({
  selector: 'app-designer-preview',
  imports: [I18nDirective],
  template: `
    <ng-container *appI18n="let t">
      <div class="palette">
        @for (node of palette; track node.key) {
          <span class="chip"
            ><i [class]="node.icon"></i
            ><span class="chip-label">{{
              t('landing.features.designer.palette.' + node.key)
            }}</span></span
          >
        }
      </div>
      <div class="canvas">
        @for (node of canvas; track node.key) {
          @if (node.condition) {
            <span class="rule"
              ><i class="pi pi-bolt"></i>{{ t('landing.features.designer.condition') }}</span
            >
          }
          <div class="node" [class.lifted]="node.lifted">
            <span class="node-icon"><i [class]="node.icon"></i></span>
            <span class="node-text">
              <strong>{{ t('landing.features.designer.nodes.' + node.key + '.title') }}</strong>
              <small>{{ t('landing.features.designer.nodes.' + node.key + '.meta') }}</small>
            </span>
          </div>
        }
      </div>
    </ng-container>
  `,
  styles: `
    :host {
      display: grid;
      grid-template-columns: 12.67rem minmax(0, 1fr);
      gap: 1.13rem;

      @media (max-width: 520px) {
        grid-template-columns: 1fr;
      }
    }

    .palette {
      display: flex;
      flex-direction: column;
      gap: 0.53rem;

      @media (max-width: 520px) {
        flex-direction: row;
        flex-wrap: wrap;
      }
    }

    .chip {
      display: inline-flex;
      align-items: center;
      gap: 0.6rem;
      min-width: 0;
      max-width: 100%;
      padding: 0.53rem 0.8rem;
      background: var(--app-surface);
      border: 1px solid var(--app-border);
      border-radius: 0.67rem;
      font-size: 1rem;
      font-weight: 500;

      i {
        color: var(--p-primary-color);
        font-size: 1rem;
      }
    }

    .chip-label {
      min-width: 0;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .canvas {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 0.73rem;
      padding: 1rem;
      background:
        radial-gradient(circle, var(--app-border) 1px, transparent 1.2px) 0 0 / 1.2rem 1.2rem,
        var(--app-surface);
      border: 1px solid var(--app-border);
      border-radius: 0.87rem;
    }

    .node {
      display: flex;
      align-items: center;
      gap: 0.73rem;
      width: min(100%, 22rem);
      min-width: 0;
      padding: 0.67rem 0.87rem;
      background: var(--app-surface);
      border: 1px solid var(--app-border);
      border-radius: 0.73rem;

      &.lifted {
        border-color: var(--p-primary-color);
        box-shadow:
          0 10px 22px -12px var(--lp-glow),
          0 0 0 3px var(--acc-brand-bg);
        transform: rotate(-1deg);
      }
    }

    .node-icon {
      display: grid;
      place-items: center;
      flex-shrink: 0;
      width: 2.27rem;
      height: 2.27rem;
      border-radius: 0.6rem;
      background: var(--acc-brand-bg);
      color: var(--acc-brand-fg);

      i {
        font-size: 0.93rem;
      }
    }

    .node-text {
      display: flex;
      flex-direction: column;
      min-width: 0;

      strong,
      small {
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      strong {
        font-size: 1rem;
      }

      small {
        font-size: 0.88rem;
        color: var(--p-text-muted-color);
      }
    }

    .rule {
      display: inline-flex;
      align-items: center;
      gap: 0.47rem;
      margin-inline-start: 1.33rem;
      padding: 0.27rem 0.73rem;
      border-radius: 999px;
      background: var(--acc-rule-bg);
      color: var(--acc-rule-fg);
      font-size: 0.91rem;
      font-weight: 600;

      i {
        font-size: 0.87rem;
      }
    }
  `,
})
export class DesignerPreviewComponent {
  /** A subset of the real designer palette. */
  protected readonly palette = [
    { key: 'userTask', icon: 'pi pi-user' },
    { key: 'mainActivity', icon: 'pi pi-sitemap' },
    { key: 'decision', icon: 'pi pi-share-alt' },
    { key: 'timer', icon: 'pi pi-stopwatch' },
    { key: 'apiRequest', icon: 'pi pi-globe' },
    { key: 'email', icon: 'pi pi-envelope' },
  ];

  protected readonly canvas = [
    { key: 'review', icon: 'pi pi-user', lifted: false, condition: false },
    { key: 'finance', icon: 'pi pi-user', lifted: true, condition: true },
    { key: 'notify', icon: 'pi pi-envelope', lifted: false, condition: false },
  ];
}
