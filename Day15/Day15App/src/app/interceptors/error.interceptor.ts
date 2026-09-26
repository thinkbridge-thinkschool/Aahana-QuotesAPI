import { inject } from '@angular/core';
import {
  HttpErrorResponse,
  HttpInterceptorFn
} from '@angular/common/http';

import { Router } from '@angular/router';

import { catchError } from 'rxjs';
import { throwError } from 'rxjs';

import { AuthService } from '../services/auth.service';
import { ApiError } from '../services/quote.service';
import { ProblemDetails } from '../models/problem-details';

const isAuthEndpoint = (url: string): boolean => url.includes('/api/auth/');

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      if (
        error.status === 401 &&
        !isAuthEndpoint(req.url) &&
        authService.isAuthenticated()
      ) {
        authService.logout();
        router.navigate(['/login']);
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