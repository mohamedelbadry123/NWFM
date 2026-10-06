import { Component, DestroyRef, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LocaleService } from '@core/i18n/locale.service';
import { ReferenceItem, WorkflowWorkspaceService } from '../workspace/workflow-workspace.service';

@Component({ selector: 'app-workflow-task-palette', standalone: true, template: `
  <section [attr.aria-label]="t('Task Types','أنواع المهام')" class="px-1 mb-3" data-testid="task-type-palette">
    @if(!collapsed) { <h3 class="text-xs font-semibold uppercase mb-2">{{ t('Task Types','أنواع المهام') }}</h3> }
    @if(loading()) { <p role="status" class="text-xs">{{ t('Loading…','جارٍ التحميل…') }}</p> }
    @else if(error()) { <p role="alert" class="text-xs">{{ t('Could not load Task Types.','تعذر تحميل أنواع المهام.') }}</p><button type="button" (click)="load()">{{ t('Retry','إعادة المحاولة') }}</button> }
    @else {
      @for(item of filtered();track item.id) {
        <button type="button" [draggable]="!readonly" [disabled]="readonly" (dragstart)="drag($event,item)"
          (click)="add.emit(item)" [title]="label(item) + ' · ' + item.code" [attr.aria-label]="t('Add ','إضافة ') + label(item)"
          class="w-full flex items-center gap-2 border rounded-lg p-2 mb-1 text-start hover:border-primary cursor-grab disabled:opacity-50">
          <span aria-hidden="true" class="shrink-0 text-primary">▣</span>
          @if(!collapsed) { <span class="min-w-0"><span class="block truncate text-sm font-medium">{{ label(item) }}</span><span class="block text-xs opacity-60">{{ item.code }}</span></span> }
        </button>
      } @empty { @if(!collapsed) { <p role="status" class="text-xs">{{ query ? t('No matching Task Types.','لا توجد أنواع مهام مطابقة.') : t('No active Task Types.','لا توجد أنواع مهام نشطة.') }}</p> } }
    }
  </section>
` })
export class WorkflowTaskPaletteComponent implements OnInit {
  @Input() readonly=false;
  @Input() collapsed=false;
  @Input() query='';
  @Output() add=new EventEmitter<ReferenceItem>();
  private api=inject(WorkflowWorkspaceService);
  private locale=inject(LocaleService);
  private destroyRef=inject(DestroyRef);
  readonly items=signal<ReferenceItem[]>([]);
  readonly loading=signal(false);
  readonly error=signal(false);
  ngOnInit(){this.load();}
  t(en:string,ar:string){return this.locale.locale()==='ar'?ar:en;}
  label(item:ReferenceItem){return this.t(item.nameEn,item.nameAr || item.nameEn);}
  filtered(){const q=this.query.trim().toLowerCase();return this.items().filter(i=>!q || [i.nameEn,i.nameAr,i.code].some(v=>v?.toLowerCase().includes(q)));}
  load(){this.loading.set(true);this.error.set(false);this.api.references('task-types').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:items=>{this.items.set(items);this.loading.set(false);},error:()=>{this.error.set(true);this.loading.set(false);}});}
  drag(event:DragEvent,item:ReferenceItem){if(this.readonly){event.preventDefault();return;}event.dataTransfer?.setData('nodeType','MainActivity');event.dataTransfer?.setData('workflowTaskType',JSON.stringify(item));}
}
