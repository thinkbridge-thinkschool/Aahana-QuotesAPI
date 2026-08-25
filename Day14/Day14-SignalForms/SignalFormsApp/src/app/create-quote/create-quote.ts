import {
  Component,
  ElementRef,
  inject,
  signal,
  viewChild
} from '@angular/core';

import {
  form,
  FormField,
  required,
  minLength,
  maxLength
} from '@angular/forms/signals';

import { QuoteService } from '../services/quote.service';

interface CreateQuoteRequest {
  Author: string;
  Text: string;
}

@Component({
  selector: 'app-create-quote',
  standalone: true,
  imports: [FormField],
  templateUrl: './create-quote.html',
  styleUrl: './create-quote.css'
})
export class CreateQuoteComponent {
  private readonly quoteService = inject(QuoteService);

  private readonly authorInput =
    viewChild<ElementRef<HTMLInputElement>>('authorInput');

  readonly model = signal<CreateQuoteRequest>({
    Author: '',
    Text: ''
  });

  readonly quoteForm = form(
    this.model,
    (path) => {
      required(path.Author, {
        message: 'Author is required.'
      });

      minLength(path.Author, 1, {
        message: 'Author must contain at least 1 character.'
      });

      maxLength(path.Author, 200, {
        message: 'Author must be 200 characters or fewer.'
      });

      required(path.Text, {
        message: 'Quote text is required.'
      });

      minLength(path.Text, 1, {
        message: 'Quote text must contain at least 1 character.'
      });

      maxLength(path.Text, 1000, {
        message: 'Quote text must be 1000 characters or fewer.'
      });
    }
  );

  readonly submitting = signal(false);
  readonly serverError = signal('');
  readonly success = signal('');

  async submit(event: Event): Promise<void> {
    event.preventDefault();

    this.serverError.set('');
    this.success.set('');

    if (this.quoteForm().invalid()) {
      this.focusFirstError();
      return;
    }

    this.submitting.set(true);

    this.quoteService.createQuote(this.model()).subscribe({
      next: () => {
        this.submitting.set(false);
        this.success.set('Quote created successfully.');

        this.model.set({
          Author: '',
          Text: ''
        });
      },
      error: () => {
        this.submitting.set(false);
        this.serverError.set(
          'Unable to create the quote. Please try again.'
        );
      }
    });
  }

  private focusFirstError(): void {
    if (this.quoteForm.Author().invalid()) {
      this.authorInput()?.nativeElement.focus();
      return;
    }

    if (this.quoteForm.Text().invalid()) {
      document.getElementById('quote-text')?.focus();
    }
  }
}