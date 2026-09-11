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

const MAX_RETRIES = 2;
const BASE_DELAY_MS = 250;
const MAX_RETRY_AFTER_MS = 30_000;

// A 429/408 means the request was rejected or timed out before the
// server did any work, so it's safe to resubmit even for POST/PUT.
// Other 4xx (validation, auth, not-found) are not transient — don't
// retry those. 5xx on a non-GET is left alone too: the server may
// have already written the row before the response was lost, so
// blindly resubmitting risks creating a duplicate quote.
function isRetryable(method: string, status: number): boolean {
  if (status === 408 || status === 429) {
    return true;
  }

  return method === 'GET' && status >= 500;
}

function retryAfterMs(error: HttpErrorResponse): number | null {
  const header = error.headers?.get('Retry-After');

  if (!header) {
    return null;
  }

  const seconds = Number(header);

  if (!Number.isNaN(seconds)) {
    return Math.min(seconds * 1000, MAX_RETRY_AFTER_MS);
  }

  const untilMs = Date.parse(header) - Date.now();

  return Number.isNaN(untilMs)
    ? null
    : Math.min(Math.max(untilMs, 0), MAX_RETRY_AFTER_MS);
}

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    retry({
      count: MAX_RETRIES,

      delay: (
        error: HttpErrorResponse,
        retryCount: number
      ): Observable<number> => {
        if (!isRetryable(req.method, error.status)) {
          return throwError(() => error);
        }

        const backoffMs = BASE_DELAY_MS * Math.pow(2, retryCount - 1);

        return timer(retryAfterMs(error) ?? backoffMs);
      }
    })
  );
};
