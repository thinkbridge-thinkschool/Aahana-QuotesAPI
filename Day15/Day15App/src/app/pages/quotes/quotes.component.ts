import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
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
        <p>A shared collection of quotes worth remembering.</p>
      </div>

      <div aria-live="polite">
        @if (loading()) {
          <p class="status-message">
            <span class="spinner" aria-hidden="true"></span>
            Loading quotes…
          </p>
        }

        @if (error()) {
          <p class="alert" role="alert">
            Failed to load quotes. Check that the API is running and try again.
          </p>
        }

        @if (!loading() && !error() && quotes().length === 0) {
          <div class="empty-state">
            <span class="empty-state__icon" aria-hidden="true">&ldquo;&rdquo;</span>
            <p>No quotes yet.</p>
            @if (authService.isAuthenticated()) {
              <a routerLink="/quotes-new" class="btn btn-primary btn-sm">Add the first quote</a>
            }
          </div>
        }
      </div>

      @if (quotes().length > 0) {
        <ul class="quote-grid" style="list-style: none; padding: 0; margin-top: 0;">
          @for (quote of quotes(); track quote.id) {
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
  protected readonly authService = inject(AuthService);
  private readonly quoteService = inject(QuoteService);

  quotes = signal<Quote[]>([]);
  loading = signal(true);
  error = signal(false);

  constructor() {
    this.quoteService.getQuotes(1, 10).subscribe({
      next: quotes => {
        this.quotes.set(quotes);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      }
    });
  }
}
