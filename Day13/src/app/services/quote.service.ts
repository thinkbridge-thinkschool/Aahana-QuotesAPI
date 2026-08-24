import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Quote {
  Id: number;
  Author: string;
  Text: string;
}

@Injectable({
  providedIn: 'root'
})
export class QuoteService {
  private readonly http = inject(HttpClient);

  private readonly baseUrl = 'http://localhost:5050/api/quotes';

  getQuotes(page: number, size: number): Observable<Quote[]> {
    const params = new HttpParams()
      .set('page', page)
      .set('size', size);

    return this.http.get<Quote[]>(this.baseUrl + '/', { params });
  }

  getQuote(id: number): Observable<Quote> {
    return this.http.get<Quote>(`${this.baseUrl}/${id}`);
  }
}