import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuoteService } from '../../services/quote.service';
import { Quote } from '../../models/quote';

@Component({
  selector: 'app-quotes',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main id="main-content" class="page">
      <div class="page-header">
        <span class="page-header__eyebrow">Library</span>
        <h1>Quotes</h1>
      </div>

      <div aria-live="polite">
        @if (loading) {
          <p class="status-message">
            <span class="spinner" aria-hidden="true"></span>
            Loading quotes…
          </p>
        }

        @if (error) {
          <p class="alert" role="alert">
            Failed to load quotes. Check that the API is running and try again.
          </p>
        }

        @if (!loading && !error && quotes.length === 0) {
          <p class="empty-state">No quotes yet.</p>
        }
      </div>

      @if (quotes.length > 0) {
        <ul class="quote-grid" style="list-style: none; padding: 0; margin-top: 0;">
          @for (quote of quotes; track quote.id) {
            <li class="card">
              <article>
                <p class="quote-card__text">&ldquo;{{ quote.text }}&rdquo;</p>
                <p class="quote-card__author">{{ quote.author }}</p>

                <a
                  class="btn-quiet"
                  [routerLink]="['/quotes', quote.id]"
                  [attr.aria-label]="'View details for the quote by ' + quote.author"
                >
                  View details →
                </a>
              </article>
            </li>
          }
        </ul>
      }
    </main>
  `
})
export class QuotesComponent {
  private readonly quoteService = inject(QuoteService);

  quotes: Quote[] = [];
  loading = true;
  error = false;

  constructor() {
    this.quoteService.getQuotes(1, 10).subscribe({
      next: quotes => {
        this.quotes = quotes;
        this.loading = false;
      },
      error: () => {
        this.error = true;
        this.loading = false;
      }
    });
  }
}
