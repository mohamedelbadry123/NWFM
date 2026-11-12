import { Injectable } from '@angular/core';
import type { LibraryQuery } from './workflow-library.service';

/** Navigation preferences only; no workflow contents or credentials are persisted. */
@Injectable({ providedIn: 'root' })
export class WorkflowLibraryViewState {
  query: LibraryQuery | null = null;
  expanded: Set<string> | null = null;
}
