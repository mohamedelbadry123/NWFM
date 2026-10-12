import { Component, Input, OnChanges, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import {
  WorkflowWorkspaceService,
  WorkspaceWorkflow,
} from '../workspace/workflow-workspace.service';
import { WorkflowVersionsService } from '../workflow-versions.service';
import { layoutWorkflowGraph, LayoutNodeType } from './workflow-canvas-layout';
import type { WorkflowVersionDetailDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-version-detail-dto';

interface PreviewNode {
  id: string;
  name: string;
  type: LayoutNodeType;
  x: number;
  y: number;
}

/** A projection only: this component never emits activities or configuration to its parent. */
@Component({
  selector: 'app-workflow-child-preview',
  standalone: true,
  imports: [RouterLink],
  template: ` <section
    class="h-full overflow-auto rounded-lg border bg-white p-3 text-slate-900"
    [attr.aria-label]="t('Linked child workflow, read only', 'سير العمل الفرعي المرتبط، للعرض فقط')"
    [attr.aria-busy]="loading()"
  >
    @if (loading()) {
      <p role="status">{{ t('Loading child workflow…', 'جارٍ تحميل سير العمل الفرعي…') }}</p>
    } @else if (error()) {
      <p role="alert">{{ error() }}</p>
      <button type="button" class="underline" (click)="load()">
        {{ t('Retry', 'إعادة المحاولة') }}
      </button>
    } @else if (child(); as c) {
      <header class="flex flex-wrap justify-between gap-2 text-xs mb-2">
        <strong>{{ locale.isRtl() ? c.nameAr || c.name : c.name }} · v{{ c.versionNumber }}</strong>
        <a
          class="underline"
          target="_blank"
          rel="noopener"
          [routerLink]="['/admin/workflow/definitions', c.id, 'versions', c.versionId, 'designer']"
          >{{ t('Open child workflow ↗', 'فتح سير العمل الفرعي ↗') }}</a
        >
      </header>
      <p class="text-xs mb-2">
        {{ t('Independent workflow · read-only preview', 'سير عمل مستقل · معاينة للقراءة فقط') }}
      </p>
      <button type="button" class="text-xs underline mb-2" (click)="actualSize.set(!actualSize())">
        {{
          actualSize()
            ? t('Fit diagram', 'ملاءمة المخطط')
            : t('View at full size', 'عرض بالحجم الكامل')
        }}
      </button>
      <div style="overflow:auto;max-height:245px">
        <svg
          [attr.width]="actualSize() ? diagramSize().width : '100%'"
          [attr.height]="actualSize() ? diagramSize().height : 245"
          [attr.viewBox]="viewBox()"
          role="img"
          [attr.aria-label]="c.name"
        >
          @for (e of edges(); track e.id) {
            <path [attr.d]="e.path" fill="none" stroke="#64748b" stroke-width="2" />
            <polygon [attr.points]="e.arrow" fill="#64748b" />
            <text [attr.x]="e.x" [attr.y]="e.y" font-size="11" fill="#475569">{{ e.label }}</text>
          }
          @for (n of nodes(); track n.id) {
            <g>
              <title>{{ n.name }} · {{ n.type }}</title>
              <rect
                [attr.x]="n.x"
                [attr.y]="n.y"
                width="210"
                height="72"
                [attr.rx]="n.type === 'Start' || n.type === 'End' ? 36 : 8"
                fill="#eef2ff"
                stroke="#6366f1"
                stroke-width="2"
              />
              <text
                [attr.x]="n.x + 105"
                [attr.y]="n.y + 30"
                text-anchor="middle"
                font-size="13"
                fill="#0f172a"
              >
                {{ n.name.length > 27 ? n.name.slice(0, 26) + '…' : n.name }}
              </text>
              <text
                [attr.x]="n.x + 105"
                [attr.y]="n.y + 51"
                text-anchor="middle"
                font-size="11"
                fill="#475569"
              >
                {{ n.type }}
              </text>
            </g>
          }
        </svg>
      </div>
      <details class="text-xs">
        <summary>{{ t('Activities', 'الأنشطة') }} ({{ nodes().length }})</summary>
        <ul>
          @for (n of nodes(); track n.id) {
            <li>{{ n.name }} · {{ n.type }}</li>
          }
        </ul>
      </details>
    }
  </section>`,
})
export class WorkflowChildPreviewComponent implements OnChanges {
  @Input({ required: true }) versionId = '';
  private workspace = inject(WorkflowWorkspaceService);
  private versions = inject(WorkflowVersionsService);
  readonly locale = inject(LocaleService);
  private request = 0;
  readonly loading = signal(false);
  readonly error = signal('');
  readonly child = signal<WorkspaceWorkflow | null>(null);
  readonly nodes = signal<PreviewNode[]>([]);
  readonly edges = signal<
    { id: string; path: string; arrow: string; x: number; y: number; label: string }[]
  >([]);
  readonly viewBox = signal('0 0 600 400');
  readonly actualSize = signal(false);
  readonly diagramSize = signal({ width: 600, height: 400 });
  t(en: string, ar: string) {
    return this.locale.isRtl() ? ar : en;
  }
  ngOnChanges() {
    this.load();
  }
  load() {
    const request = ++this.request;
    this.loading.set(true);
    this.error.set('');
    this.child.set(null);
    this.nodes.set([]);
    this.edges.set([]);
    this.workspace
      .children(this.versionId)
      .pipe(
        switchMap((children) => {
          const child = children.find((c) => c.versionId === this.versionId);
          if (!child) throw new Error('unavailable');
          if (request === this.request) this.child.set(child);
          return this.versions.getById(child.id, child.versionId);
        }),
      )
      .subscribe({
        next: (v) => {
          if (request !== this.request) return;
          this.draw(v);
          this.loading.set(false);
        },
        error: () => {
          if (request !== this.request) return;
          this.loading.set(false);
          this.error.set(
            this.t(
              'The linked child version could not be loaded.',
              'تعذر تحميل إصدار سير العمل الفرعي المرتبط.',
            ),
          );
        },
      });
  }
  private draw(v: WorkflowVersionDetailDto) {
    const activities = (v.activities ?? []).filter((a) => !!a.id);
    const links = (v.transitions ?? []).filter(
      (e) => !!e.fromActivityDefinitionId && !!e.toActivityDefinitionId,
    );
    const layout = layoutWorkflowGraph(
      activities.map((a) => ({ id: a.id!, type: a.activityType ?? 'UserTask' })),
      links.map((e) => ({
        fromNodeId: e.fromActivityDefinitionId!,
        toNodeId: e.toActivityDefinitionId!,
      })),
    );
    const nodes = activities.map((a) => ({
      id: a.id!,
      name: (this.locale.isRtl() ? a.nameAr || a.name : a.name) || a.nodeKey || '',
      type: a.activityType ?? 'UserTask',
      ...layout[a.id!],
    }));
    this.nodes.set(nodes);
    this.edges.set(
      links.flatMap((e, i) => {
        const a = nodes.find((n) => n.id === e.fromActivityDefinitionId),
          b = nodes.find((n) => n.id === e.toActivityDefinitionId);
        if (!a || !b) return [];
        const x1 = a.x + 105,
          y1 = a.y + 72,
          x2 = b.x + 105,
          y2 = b.y;
        return [
          {
            id: e.id ?? String(i),
            path: `M ${x1} ${y1} L ${x1} ${(y1 + y2) / 2} L ${x2} ${(y1 + y2) / 2} L ${x2} ${y2}`,
            arrow: `${x2 - 5},${y2 - 8} ${x2},${y2} ${x2 + 5},${y2 - 8}`,
            x: x1 + 5,
            y: (y1 + y2) / 2 - 5,
            label: e.transitionKey ?? '',
          },
        ];
      }),
    );
    const minX = Math.min(0, ...nodes.map((n) => n.x - 20)),
      minY = Math.min(0, ...nodes.map((n) => n.y - 20));
    const width = Math.max(300, ...nodes.map((n) => n.x + 230)) - minX,
      height = Math.max(150, ...nodes.map((n) => n.y + 100)) - minY;
    this.diagramSize.set({ width, height });
    this.viewBox.set(`${minX} ${minY} ${width} ${height}`);
  }
}
