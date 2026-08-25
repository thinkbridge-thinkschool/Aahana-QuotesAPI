import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Quote {
  Id: number;
  Author: string;
  Text: string;
}

export interface CreateQuoteRequest {
  Author: string;
  Text: string;
}

@Injectable({
  providedIn: 'root'
})
export class QuoteService {
  private readonly http = inject(HttpClient);

  private readonly baseUrl =
    'http://localhost:5000/api/quotes';

  createQuote(
    request: CreateQuoteRequest
  ): Observable<Quote> {
    return this.http.post<Quote>(
      this.baseUrl + '/',
      request
    );
  }
}