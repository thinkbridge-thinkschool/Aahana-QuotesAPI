import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { QuoteService } from '../../services/quote.service';
import { Quote } from '../../models/quote';

@Component({
  selector: 'app-quote-detail',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main id="main-content" class="page">
      <a class="back-link" routerLink="/quotes">← Back to quotes</a>

      <div class="page-header">
        <span class="page-header__eyebrow">Detail</span>
        <h1>Quote</h1>
      </div>

      <div aria-live="polite">
        @if (loading()) {
          <p class="status-message">
            <span class="spinner" aria-hidden="true"></span>
            Loading quote…
          </p>
        }

        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }
      </div>

      @if (quote(); as quote) {
        <section class="card quote-detail">
          <p class="quote-card__text" style="font-size: 1.25rem;">
            &ldquo;{{ quote.text }}&rdquo;
          </p>
          <p class="quote-card__author">{{ quote.author }}</p>

          <dl>
            <dt>Quote ID</dt>
            <dd>{{ quote.id }}</dd>

            <dt>Submitted by user</dt>
            <dd>{{ quote.userId }}</dd>

            <dt>Status</dt>
            <dd>{{ quote.isDeleted ? 'Deleted' : 'Active' }}</dd>
          </dl>
        </section>
      }
    </main>
  `
})
export class QuoteDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly quoteService = inject(QuoteService);

  quote = signal<Quote | null>(null);
  loading = signal(true);
  error = signal('');

  constructor() {
    const idParam = this.route.snapshot.paramMap.get('id');
    const id = Number(idParam);

    if (!idParam || !Number.isInteger(id) || id < 1) {
      this.loading.set(false);
      this.error.set('Invalid quote ID.');
      return;
    }

    this.quoteService.getQuoteById(id).subscribe({
      next: quote => {
        this.quote.set(quote);
        this.loading.set(false);
      },
      error: response => {
        this.loading.set(false);

        if (response.status === 404) {
          this.error.set('Quote not found.');
        } else {
          this.error.set('Failed to load quote.');
        }
      }
    });
  }
}
