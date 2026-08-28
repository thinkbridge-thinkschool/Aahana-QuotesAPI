import { computed, inject, Injectable, signal } from '@angular/core';

import { QuoteService } from '../services/quote.service';
import { Quote } from '../models/quote';

@Injectable({
  providedIn: 'root'
})
export class QuotesStore {
  private readonly quoteService = inject(QuoteService);

  private readonly _quotes = signal<Quote[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal(false);
  private readonly _page = signal(1);

  readonly quotes = this._quotes.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly page = this._page.asReadonly();

  readonly isEmpty = computed(
    () => !this._loading() && !this._error() && this._quotes().length === 0
  );

  // Guards against a slower, older request resolving after a newer one
  // and overwriting its result — see quotes.store.spec.ts, "concurrent
  // updates". Only the response matching the most recent load() call is
  // applied; anything else is a stale response and is dropped.
  private latestRequestId = 0;

  load(page: number, size: number): void {
    const requestId = ++this.latestRequestId;

    this._loading.set(true);
    this._error.set(false);

    this.quoteService.getQuotes(page, size).subscribe({
      next: quotes => {
        if (requestId !== this.latestRequestId) {
          return;
        }

        this._quotes.set(quotes);
        this._page.set(page);
        this._loading.set(false);
      },
      error: () => {
        if (requestId !== this.latestRequestId) {
          return;
        }

        this._error.set(true);
        this._loading.set(false);
      }
    });
  }
}
