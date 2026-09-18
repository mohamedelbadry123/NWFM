import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { WorkflowBindingsService } from '../workflow-bindings.service';
import { WorkflowRuntimeService } from '../workflow-runtime.service';
import { LocaleService } from '@core/i18n/locale.service';
@Component({standalone:true,imports:[FormsModule],template:`
  <div class="max-w-2xl"><p class="text-xs uppercase tracking-widest text-ink-400 mb-2">NWFM</p><h1 class="text-2xl font-semibold">{{locale.isRtl() ? 'بدء سير عمل' : 'Start workflow'}}</h1>
  <p class="mt-2 text-ink-500">{{locale.isRtl() ? 'اختر ارتباطاً نشطاً لبدء طلب جديد.' : 'Choose an active binding to begin a new request.'}}</p>
  <form (ngSubmit)="start()" class="mt-8 rounded-2xl border border-ink-200 bg-white p-6 space-y-5">
    <label class="block text-sm">{{locale.isRtl() ? 'الارتباط' : 'Workflow binding'}}<select name="binding" [(ngModel)]="bindingId" class="wf-input mt-2" required><option value="">{{locale.isRtl() ? 'اختر سير عمل' : 'Choose a workflow'}}</option>@for(b of bindings();track b.id){<option [value]="b.id">{{b.label}}</option>}</select></label>
    <label class="block text-sm">{{locale.isRtl() ? 'مرجع الطلب' : 'Request reference'}}<input name="reference" [(ngModel)]="reference" class="wf-input mt-2" required maxlength="200" /></label>
    @if(error()){<p role="alert" class="text-red-700 text-sm">{{error()}}</p>}
    <button class="wf-btn-primary" [disabled]="busy() || !bindingId || !reference.trim()">{{locale.isRtl() ? 'بدء الطلب' : 'Start request'}}</button>
  </form></div>`})
export class WorkflowStartComponent {
  readonly locale=inject(LocaleService); private readonly service=inject(WorkflowRuntimeService);private readonly router=inject(Router);
  readonly bindings=signal<{id:string;label:string}[]>([]);readonly error=signal('');readonly busy=signal(false);
  bindingId='';reference='REQ-'+Date.now();private readonly key=crypto.randomUUID();
  constructor(){inject(WorkflowBindingsService).listAll(1,200).subscribe({next:r=>{this.bindings.set((r.items??[]).filter(b=>b.isActive && b.mode==='Active').map(b=>({id:b.id!,label:b.description || b.moduleKey+' / '+b.triggerEvent})));},error:()=>this.error.set('Unable to load workflow bindings.')});}
  start(){this.busy.set(true);this.error.set('');this.service.startInstance({workflowBindingId:this.bindingId,businessEntityId:this.reference.trim(),idempotencyKey:this.key}).subscribe({next:r=>{void this.router.navigate(['/org/workflow/requests',r.id]);},error:e=>{this.busy.set(false);this.error.set(e.error?.message || 'Unable to start the workflow.');}});}
}
