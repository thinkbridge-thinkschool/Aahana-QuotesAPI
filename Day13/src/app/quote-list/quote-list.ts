import {
  Component,
  computed,
  effect,
  inject,
  signal
} from '@angular/core';

import {
  Quote,
  QuoteService
} from '../services/quote.service';

@Component({
  selector: 'app-quote-list',
  standalone: true,
  templateUrl: './quote-list.html',
  styleUrl: './quote-list.css'
})
export class QuoteListComponent {
  private readonly quoteService = inject(QuoteService);

  readonly page = signal(1);
  readonly size = signal(10);

  readonly quotes = signal<Quote[]>([]);
  readonly selectedQuote = signal<Quote | null>(null);

  readonly listLoading = signal(false);
  readonly detailLoading = signal(false);

  readonly listError = signal('');
  readonly detailError = signal('');

  readonly selectedId = signal<number | null>(null);

  readonly hasQuotes = computed(() => this.quotes().length > 0);

  private listRequest = 0;
  private detailRequest = 0;

  constructor() {
    effect((onCleanup) => {
      const page = this.page();
      const size = this.size();
      const requestId = ++this.listRequest;

      this.listLoading.set(true);
      this.listError.set('');

      const subscription = this.quoteService
        .getQuotes(page, size)
        .subscribe({
          next: (quotes) => {
            if (requestId !== this.listRequest) {
              return;
            }

            this.quotes.set(quotes);
            this.listLoading.set(false);
          },
          error: () => {
            if (requestId !== this.listRequest) {
              return;
            }

            this.listError.set('Unable to load quotes.');
            this.listLoading.set(false);
          }
        });

      onCleanup(() => subscription.unsubscribe());
    });
  }

  selectQuote(id: number): void {
    const requestId = ++this.detailRequest;

    this.selectedId.set(id);
    this.selectedQuote.set(null);
    this.detailError.set('');
    this.detailLoading.set(true);

    this.quoteService.getQuote(id).subscribe({
      next: (quote) => {
        if (requestId !== this.detailRequest) {
          return;
        }

        this.selectedQuote.set(quote);
        this.detailLoading.set(false);
      },
      error: () => {
        if (requestId !== this.detailRequest) {
          return;
        }

        this.detailError.set('Unable to load quote details.');
        this.detailLoading.set(false);
      }
    });
  }

  previousPage(): void {
    if (this.page() > 1) {
      this.page.update(value => value - 1);
    }
  }

  nextPage(): void {
    this.page.update(value => value + 1);
  }

  changeSize(event: Event): void {
    const value = Number(
      (event.target as HTMLSelectElement).value
    );

    this.size.set(value);
  }
}