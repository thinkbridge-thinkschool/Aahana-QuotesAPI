import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { QuoteService } from '../../services/quote.service';

@Component({
  selector: 'app-create-quote',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <main id="main-content" class="page">
      <div class="page-header">
        <span class="page-header__eyebrow">Library</span>
        <h1>Add a quote</h1>
      </div>

      <div class="card" style="max-width: 32rem;">
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          @if (serverError()) {
            <p class="alert" role="alert">{{ serverError() }}</p>
          }

          @if (success()) {
            <p role="status">{{ success() }}</p>
          }

          <div class="field">
            <label for="author">Author</label>
            <input
              #authorInput
              id="author"
              type="text"
              formControlName="author"
              maxlength="200"
              autocomplete="name"
              [attr.aria-invalid]="isInvalid('author') ? 'true' : 'false'"
              [attr.aria-describedby]="isInvalid('author') ? 'author-error' : null"
            />

            @if (isInvalid('author')) {
              <p id="author-error" class="error" role="alert">
                @if (form.controls.author.hasError('required')) {
                  Author is required.
                } @else if (form.controls.author.hasError('maxlength')) {
                  Author must be 200 characters or fewer.
                }
              </p>
            }
          </div>

          <div class="field">
            <label for="quote-text">Quote</label>
            <textarea
              id="quote-text"
              formControlName="text"
              maxlength="1000"
              rows="6"
              [attr.aria-invalid]="isInvalid('text') ? 'true' : 'false'"
              [attr.aria-describedby]="isInvalid('text') ? 'text-error' : null"
            ></textarea>

            @if (isInvalid('text')) {
              <p id="text-error" class="error" role="alert">
                @if (form.controls.text.hasError('required')) {
                  Quote text is required.
                } @else if (form.controls.text.hasError('maxlength')) {
                  Quote text must be 1000 characters or fewer.
                }
              </p>
            }
          </div>

          <button
            type="submit"
            class="btn btn-primary"
            [disabled]="submitting()"
            [attr.aria-busy]="submitting()"
          >
            @if (submitting()) {
              Creating…
            } @else {
              Create quote
            }
          </button>
        </form>
      </div>
    </main>
  `
})
export class CreateQuoteComponent {
  private readonly fb = inject(FormBuilder);
  private readonly quoteService = inject(QuoteService);
  private readonly router = inject(Router);

  private readonly authorInput =
    viewChild<ElementRef<HTMLInputElement>>('authorInput');

  readonly submitting = signal(false);
  readonly serverError = signal('');
  readonly success = signal('');
  readonly submitted = signal(false);

  readonly form = this.fb.nonNullable.group({
    author: ['', [Validators.required, Validators.maxLength(200)]],
    text: ['', [Validators.required, Validators.maxLength(1000)]]
  });

  isInvalid(name: 'author' | 'text'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || this.submitted());
  }

  submit(): void {
    this.submitted.set(true);
    this.serverError.set('');
    this.success.set('');

    if (this.form.invalid) {
      this.authorInput()?.nativeElement.focus();
      return;
    }

    this.submitting.set(true);

    this.quoteService
      .createQuote({
        author: this.form.controls.author.value,
        text: this.form.controls.text.value
      })
      .subscribe({
        next: quote => {
          this.submitting.set(false);
          this.router.navigate(['/quotes', quote.id]);
        },
        error: () => {
          this.submitting.set(false);
          this.serverError.set(
            'Unable to create the quote. Please try again.'
          );
        }
      });
  }
}
