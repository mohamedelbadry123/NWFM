import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import type { ReferenceItem } from './workflow-workspace.service';

export interface LibraryScope {
  level: string;
  code: string;
  clusterCode: string;
  cbuCode?: string;
  nameEn?: string;
  nameAr?: string;
}
export interface LibraryWorkflow {
  id: string;
  definitionKey: string;
  name: string;
  nameAr?: string;
  description?: string;
  descriptionAr?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  versionCount: number;
  versionId?: string;
  versionNumber?: number;
  versionStatus: string;
  kind: string;
  parentDefinitionId?: string;
  parentNodeKey?: string;
  classification?: ReferenceItem;
  activityCount: number;
  scopes: LibraryScope[];
  inheritsScope: boolean;
  needsAttention: boolean;
  children: LibraryWorkflow[];
}
export interface LibraryPage {
  items: LibraryWorkflow[];
  totalGroups: number;
  totalWorkflows: number;
  page: number;
  pageSize: number;
  scopes: LibraryScope[];
}
export interface LibraryQuery {
  page: number;
  pageSize: number;
  search: string;
  status: 'Active' | 'Archived' | 'All';
  scope: string;
  sort: 'Updated' | 'Created' | 'Name';
}
@Injectable({ providedIn: 'root' })
export class WorkflowLibraryService {
  private readonly http = inject(HttpClient);
  list(query: LibraryQuery) {
    return this.http.get<LibraryPage>('/api/workflow/workspace/library', { params: { ...query } });
  }
}
