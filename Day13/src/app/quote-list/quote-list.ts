import {
  Component,
  computed,
  effect,
  inject,
  signal
} from '@angular/core';
import { Quote, QuoteService } from '../services/quote.service';

@Component({
  selector: 'app-quote-list',
  standalone: true,
  templateUrl: './quote-list.html',
  styleUrl: './quote-list.css'
})
export class QuoteListComponent {
  private quoteService = inject(QuoteService);

  page = signal(1);
  size = signal(10);

  quotes = signal<Quote[]>([]);
  loading = signal(false);
  error = signal('');

  queryDescription = computed(
    () => `Page ${this.page()} · ${this.size()} quotes`
  );

  uiState = computed(() => {
    if (this.loading()) return 'loading';
    if (this.error()) return 'error';
    if (this.quotes().length === 0) return 'empty';
    return 'ready';
  });

  constructor() {
    effect((onCleanup) => {
      const page = this.page();
      const size = this.size();

      this.loading.set(true);
      this.error.set('');

      const subscription = this.quoteService
        .getQuotes(page, size)
        .subscribe({
          next: (quotes) => {
            this.quotes.set(quotes);
            this.loading.set(false);
          },
          error: () => {
            this.error.set('Unable to load quotes.');
            this.loading.set(false);
          }
        });

      onCleanup(() => subscription.unsubscribe());
    });
  }

  previousPage() {
    if (this.page() > 1) {
      this.page.update(value => value - 1);
    }
  }

  nextPage() {
    this.page.update(value => value + 1);
  }

  changeSize(event: Event) {
    const value = Number(
      (event.target as HTMLSelectElement).value
    );

    this.size.set(value);
  }
}