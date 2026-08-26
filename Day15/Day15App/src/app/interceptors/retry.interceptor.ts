import {
  HttpErrorResponse,
  HttpInterceptorFn
} from '@angular/common/http';

import {
  Observable,
  throwError,
  timer
} from 'rxjs';

import {
  retry
} from 'rxjs/operators';

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: 2,

      delay: (
        error: HttpErrorResponse,
        retryCount: number
      ): Observable<number> => {
        if (
          error.status >= 400 &&
          error.status < 500
        ) {
          return throwError(() => error);
        }

        const delayMs = 250 * Math.pow(2, retryCount - 1);

        return timer(delayMs);
      }
    })
  );
};