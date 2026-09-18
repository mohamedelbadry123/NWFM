import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { AppContextService } from './app-context.service';
export const contextInterceptor: HttpInterceptorFn = (req, next) => {
  const context = inject(AppContextService);
  const id = context.selectedParticipantId();
  return next(id && req.url.startsWith('/api/') && !req.url.endsWith('/app-context')
    ? req.clone({ setHeaders: { 'X-Workflow-Participant-Id': id } }) : req);
};
