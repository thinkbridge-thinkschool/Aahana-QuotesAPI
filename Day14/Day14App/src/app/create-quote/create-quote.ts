import {
  Component,
  ElementRef,
  inject,
  signal,
  viewChild
} from '@angular/core';

import {
  FormBuilder,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';

import { QuoteService } from '../services/quote.service';

@Component({
  selector: 'app-create-quote',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './create-quote.html',
  styleUrl: './create-quote.css'
})
export class CreateQuoteComponent {
  private readonly fb = inject(FormBuilder);
  private readonly quoteService = inject(QuoteService);

  private readonly authorInput =
    viewChild<ElementRef<HTMLInputElement>>('authorInput');

  readonly submitting = signal(false);
  readonly serverError = signal('');
  readonly success = signal('');
  readonly submitted = signal(false);

  readonly form = this.fb.nonNullable.group({
    Author: [
      '',
      [
        Validators.required,
        Validators.minLength(1),
        Validators.maxLength(200)
      ]
    ],
    Text: [
      '',
      [
        Validators.required,
        Validators.minLength(1),
        Validators.maxLength(1000)
      ]
    ]
  });

  isInvalid(name: 'Author' | 'Text'): boolean {
    const control = this.form.controls[name];

    return control.invalid &&
      (control.touched || this.submitted());
  }

  submit(): void {
    this.submitted.set(true);
    this.serverError.set('');
    this.success.set('');

    if (this.form.invalid) {
      this.focusFirstError();
      return;
    }

    this.submitting.set(true);

    this.quoteService.createQuote({
      Author: this.form.controls.Author.value,
      Text: this.form.controls.Text.value
    }).subscribe({
      next: () => {
        this.submitting.set(false);
        this.success.set('Quote created successfully.');
        this.form.reset();
        this.submitted.set(false);
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
    if (this.form.controls.Author.invalid) {
      this.authorInput()?.nativeElement.focus();
      return;
    }

    if (this.form.controls.Text.invalid) {
      document.getElementById('quote-text')?.focus();
    }
  }
}