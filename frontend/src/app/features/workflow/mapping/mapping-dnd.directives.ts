import { Directive, HostBinding, HostListener, Input, inject, signal } from '@angular/core';
import { MappingEditContext, type MappingTarget } from './mapping-edit-context';
import type { SourceDescriptor } from './mapping.models';
import { breadcrumbText, sourceKey } from './mapping-config';

export const SOURCE_DRAG_TYPE = 'application/x-nwfm-mapping-source';

/**
 * Makes an element a draggable source. The dragged descriptor is also kept in the shared context, because a
 * drop target cannot read `dataTransfer` before the drop, and it needs to know whether to accept the drag.
 * Dragging is only a shortcut: every mapping has a click/keyboard equivalent in the source browser.
 */
@Directive({ selector: '[mappingDraggable]', standalone: true })
export class MappingDraggableDirective {
  private readonly ctx = inject(MappingEditContext);
  @Input({ required: true, alias: 'mappingDraggable' }) descriptor!: SourceDescriptor;

  @HostBinding('attr.draggable') get draggable() {
    return this.ctx.readonly() ? null : 'true';
  }

  @HostListener('dragstart', ['$event'])
  start(event: DragEvent) {
    if (this.ctx.readonly()) {
      event.preventDefault();
      return;
    }
    event.dataTransfer?.setData(SOURCE_DRAG_TYPE, sourceKey(this.descriptor.reference));
    event.dataTransfer?.setData('text/plain', breadcrumbText(this.descriptor));
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy';
    this.ctx.dragging.set(this.descriptor);
  }

  @HostListener('dragend')
  end() {
    this.ctx.dragging.set(null);
  }
}

/** Makes an element a drop destination for a dragged source. Only the innermost target handles a drop. */
@Directive({ selector: '[mappingDropTarget]', standalone: true })
export class MappingDropTargetDirective {
  private readonly ctx = inject(MappingEditContext);
  @Input({ required: true, alias: 'mappingDropTarget' }) target!: MappingTarget;
  private readonly hover = signal(false);

  @HostBinding('class.mapping-drop-over') get over() {
    return this.hover();
  }

  @HostListener('dragover', ['$event'])
  dragOver(event: DragEvent) {
    if (!this.ctx.dragging() || this.ctx.readonly()) return;
    event.preventDefault();
    event.stopPropagation();
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
    this.hover.set(true);
  }

  @HostListener('dragleave')
  dragLeave() {
    this.hover.set(false);
  }

  @HostListener('drop', ['$event'])
  drop(event: DragEvent) {
    const dragged = this.ctx.dragging();
    this.hover.set(false);
    if (!dragged || this.ctx.readonly()) return;
    event.preventDefault();
    event.stopPropagation();
    this.ctx.mapTo(this.target, dragged);
    this.ctx.dragging.set(null);
  }
}
