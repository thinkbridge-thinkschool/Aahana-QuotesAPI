import {
  HttpErrorResponse,
  HttpInterceptorFn
} from '@angular/common/http';

import { catchError } from 'rxjs';
import { throwError } from 'rxjs';

import { ApiError } from '../services/quote.service';
import { ProblemDetails } from '../models/problem-details';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const problem = error.error as ProblemDetails;

      if (
        problem &&
        typeof problem === 'object' &&
        (
          typeof problem.title === 'string' ||
          typeof problem.status === 'number' ||
          typeof problem.errors === 'object'
        )
      ) {
        const apiError: ApiError = {
          status: error.status,
          problem
        };

        return throwError(() => apiError);
      }

      return throwError(() => error);
    })
  );
};