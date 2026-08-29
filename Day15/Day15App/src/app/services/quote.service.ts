import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { Quote } from '../models/quote';
import { ProblemDetails } from '../models/problem-details';
import { environment } from '../../environments/environment';

export interface ApiError {
  status: number;
  problem: ProblemDetails;
}

@Injectable({
  providedIn: 'root'
})
export class QuoteService {
  private readonly http = inject(HttpClient);

  private readonly baseUrl =
    `${environment.apiBaseUrl}/api/quotes/`;

  getQuotes(
    page: number,
    size: number
  ): Observable<Quote[]> {
    const params = new HttpParams()
      .set('page', page)
      .set('size', size);

    return this.http.get<Quote[]>(
      this.baseUrl,
      { params }
    );
  }

  getQuoteById(id: number): Observable<Quote> {
    return this.http.get<Quote>(
      `${this.baseUrl}${id}`
    );
  }
}