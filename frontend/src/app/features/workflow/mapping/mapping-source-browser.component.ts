import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MappingDraggableDirective } from './mapping-dnd.directives';
import { MappingEditContext } from './mapping-edit-context';
import type { MappingSchema, SourceDescriptor } from './mapping.models';
import {
  availabilityInfo,
  breadcrumbText,
  buildSourceTree,
  describeSchema,
  filterSources,
  sourceKey,
  type SourceGroup,
} from './mapping-config';

/**
 * Hierarchical source browser: this workflow › Task Type › Step › Form › fields, event outputs, and the
 * related workflows chosen in the criteria. Rows can be gathered into the tray (multi-select), dragged onto a
 * destination, or mapped with the "Map" button to the active target — three routes to the same result.
 * It only shows what the server's catalog returned; it never guesses at data the user may not read.
 */
@Component({
  selector: 'app-mapping-source-browser',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, NgTemplateOutlet, MappingDraggableDirective],
  styleUrl: './mapping.css',
  template: `
    <section class="mx-stage" [attr.aria-label]="ctx.t('Source browser', 'متصفح المصادر')">
      <div class="mx-row">
        <h3 class="mx-stage-title">{{ ctx.t('Sources', 'المصادر') }}</h3>
        <button
          type="button"
          class="wf-btn-secondary"
          [disabled]="ctx.loading()"
          (click)="refresh.emit()"
        >
          {{
            ctx.loading()
              ? ctx.t('Loading…', 'جارٍ التحميل…')
              : ctx.t('Refresh sources', 'تحديث المصادر')
          }}
        </button>
      </div>
      <p class="mx-help">
        {{
          ctx.t(
            'Tick sources to gather them in the tray, drag one onto a destination, or pick a destination (Use as target) and press Map.',
            'حدد المصادر لجمعها في الصينية، أو اسحب مصدراً إلى وجهة، أو اختر وجهة (استخدم كهدف) ثم اضغط ربط.'
          )
        }}
      </p>
      @if (ctx.catalogError()) {
        <p class="mx-error" role="alert">{{ ctx.catalogError() }}</p>
      }
      @for (message of diagnostics(); track message) {
        <p class="mx-help mx-warn" role="status">{{ message }}</p>
      }
      <label class="mx-field">
        {{ ctx.t('Search sources', 'ابحث في المصادر') }}
        <input
          class="wf-input"
          type="search"
          [ngModel]="query()"
          (ngModelChange)="query.set($event)"
        />
      </label>

      <div
        class="mx-tray"
        role="list"
        [attr.aria-label]="ctx.t('Source tray', 'صينية المصادر')"
        data-testid="tray"
      >
        @for (d of ctx.liveTray(); track key(d)) {
          <span class="mx-chip" role="listitem" [mappingDraggable]="d">
            {{ d.label }}
            <button
              type="button"
              class="mx-link"
              [attr.aria-label]="
                ctx.t('Remove ' + d.label + ' from the tray', 'أزل ' + d.label + ' من الصينية')
              "
              (click)="ctx.removeFromTray(d)"
            >
              ×
            </button>
          </span>
        } @empty {
          <span class="mx-help">{{ ctx.t('The tray is empty.', 'الصينية فارغة.') }}</span>
        }
        @if (ctx.liveTray().length) {
          <button type="button" class="mx-link" (click)="ctx.clearTray()">
            {{ ctx.t('Clear tray', 'تفريغ الصينية') }}
          </button>
        }
      </div>
      @if (ctx.activeTarget(); as target) {
        <p class="mx-help" role="status">
          {{ ctx.t('Map buttons write to:', 'أزرار الربط تكتب إلى:') }}
          <strong>{{ target.label }}</strong>
          <button type="button" class="mx-link" (click)="ctx.setActive(null)">
            {{ ctx.t('Clear target', 'مسح الهدف') }}
          </button>
        </p>
      }

      <div class="mx-scroll mx-tree" data-testid="source-tree">
        @if (!filteredTree().count) {
          <p class="mx-help">
            {{
              ctx.t(
                'No sources match. Sources appear once the workflow is saved and forms are published.',
                'لا توجد مصادر مطابقة. تظهر المصادر بعد حفظ سير العمل ونشر النماذج.'
              )
            }}
          </p>
        }
        <ng-container
          *ngTemplateOutlet="groupTemplate; context: { $implicit: filteredTree(), root: true }"
        />
      </div>
    </section>

    <ng-template #groupTemplate let-group let-root="root">
      @if (!root) {
        <button
          type="button"
          class="mx-link"
          [attr.aria-expanded]="isOpen(group.path)"
          (click)="toggle(group.path)"
        >
          {{ isOpen(group.path) ? '▾' : '▸' }} {{ group.label }} ({{ group.count }})
        </button>
      }
      @if (root || isOpen(group.path)) {
        <ul>
          @for (child of group.groups; track child.path) {
            <li>
              <ng-container
                *ngTemplateOutlet="groupTemplate; context: { $implicit: child, root: false }"
              />
            </li>
          }
          @for (d of group.sources; track key(d)) {
            <li
              class="mx-tree-leaf"
              [mappingDraggable]="d"
              [attr.data-source]="d.reference.fieldKey"
            >
              <label>
                <input
                  type="checkbox"
                  [checked]="ctx.inTray(d)"
                  [disabled]="ctx.readonly() || !avail(d).known"
                  [attr.aria-label]="
                    ctx.t('Add ' + d.label + ' to the tray', 'أضف ' + d.label + ' إلى الصينية')
                  "
                  (change)="ctx.toggleTray(d)"
                />
                <strong>{{ d.label }}</strong>
              </label>
              <span class="mx-chip">{{ describe(d.schema) }}</span>
              @if (d.schema.required) {
                <span class="mx-chip">{{ ctx.t('Required', 'مطلوب') }}</span>
              }
              <span class="mx-chip" [class.mx-chip-warn]="avail(d).waits || !avail(d).known">{{
                avail(d).text
              }}</span>
              @if (d.sensitive) {
                <span class="mx-chip mx-chip-warn">{{ ctx.t('Sensitive', 'حساس') }}</span>
              }
              <span class="mx-crumbs" [attr.title]="crumbs(d)">{{ crumbs(d) }}</span>
              @if (ctx.activeTarget(); as target) {
                <button
                  type="button"
                  class="wf-btn-secondary"
                  [disabled]="ctx.readonly() || !avail(d).known"
                  [attr.aria-label]="
                    ctx.t(
                      'Map ' + d.label + ' to ' + target.label,
                      'اربط ' + d.label + ' بـ ' + target.label
                    )
                  "
                  (click)="ctx.mapToActive(d)"
                >
                  {{ ctx.t('Map', 'ربط') }}
                </button>
              }
            </li>
          }
        </ul>
      }
    </ng-template>
    <p class="mx-sr" aria-live="polite" role="status">{{ ctx.announcement() }}</p>
  `,
})
export class MappingSourceBrowserComponent {
  protected readonly ctx = inject(MappingEditContext);
  @Output() refresh = new EventEmitter<void>();
  protected readonly query = signal('');
  /** Groups the user toggled away from their default open/closed state. */
  private readonly expanded = signal<Set<string>>(new Set());

  protected readonly filteredTree = computed<SourceGroup>(() =>
    buildSourceTree(filterSources(this.ctx.allSources(), this.query())),
  );
  protected diagnostics = computed(() => this.ctx.catalog()?.diagnostics ?? []);
  protected key = (d: SourceDescriptor) => sourceKey(d.reference);
  protected crumbs = breadcrumbText;
  protected describe = (schema: MappingSchema) =>
    describeSchema(schema, (en) => this.ctx.t(en, en));
  protected avail(d: SourceDescriptor) {
    return availabilityInfo(d.availability, (en) => this.ctx.t(en, ARABIC_AVAILABILITY[en] ?? en));
  }
  /** The first level starts open and deeper levels closed; each toggle flips that default. */
  protected isOpen(path: string) {
    if (this.query().trim().length > 0) return true;
    const firstLevel = path.split('\u0001').length <= 2;
    return firstLevel !== this.expanded().has(path);
  }
  protected toggle(path: string) {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  }
}

const ARABIC_AVAILABILITY: Record<string, string> = {
  'Available when the event starts': 'متاح عند بدء الحدث',
  'Available once the form is submitted (may need to wait)':
    'متاح بعد إرسال النموذج (قد يتطلب انتظاراً)',
  'Available once that event completes (may need to wait)':
    'متاح بعد اكتمال ذلك الحدث (قد يتطلب انتظاراً)',
  'Needs a declared type before it can be mapped': 'يحتاج إلى نوع معرّف قبل الربط',
};
