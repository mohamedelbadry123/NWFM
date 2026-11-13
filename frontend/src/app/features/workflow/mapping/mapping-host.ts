import { InjectionToken } from '@angular/core';
import type { CallableTarget } from './mapping.models';

/**
 * What the surrounding designer tells the mapper. The mapper never reaches into designer state: it asks the host
 * for the selected node (the mapping owner), the pinned MAIN tree root the mapping APIs are addressed to, and a
 * guaranteed save so the server's catalog reflects the draft the author is looking at.
 */
export interface WorkflowMappingHost {
  /** The selected node in the workflow this designer edits (a child workflow's designer owns child nodes). */
  owner(): CallableTarget | null;
  /** The pinned main tree root. Differs from the owner's workflow when the designer edits a child. */
  root(): { definitionId: string; versionId: string } | null;
  /** Set when the open version is published/retired: everything stays inspectable but cannot change. */
  readonly(): boolean;
  /** The draft of this workflow and every ancestor is saved; rejects when a save failed. */
  ensureSaved(): Promise<void>;
  /** True when the tree root is a different workflow, i.e. the owner lives in a child. */
  isChild(): boolean;
}

export const WORKFLOW_MAPPING_HOST = new InjectionToken<WorkflowMappingHost>(
  'WORKFLOW_MAPPING_HOST',
);
