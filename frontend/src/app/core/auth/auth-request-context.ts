import { HttpContextToken } from '@angular/common/http';

/** Marks a request the auth interceptor must not answer with a silent refresh (its 401 goes straight to the caller). */
export const SKIP_REFRESH = new HttpContextToken<boolean>(() => false);
