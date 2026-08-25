\# Day 14 — Reactive Forms + Accessibility



\## 1. Brief to the agent



Build a standalone Angular reactive create-a-quote form against my real Week-1 Quotes API.



Real endpoint:



POST /api/quotes/



The request body contains exactly these fields:



\- Author: required, minimum 1 character, maximum 200 characters

\- Text: required, minimum 1 character, maximum 1000 characters



Do not invent additional fields.



Use Angular reactive forms with validators matching the API contract. The form must have clear validation errors, loading/submitting state, and server-error handling.



Accessibility requirements:

\- Every input must have an associated label.

\- Use aria-invalid when a field is invalid.

\- Use aria-describedby to connect fields to their error messages.

\- The complete form must be keyboard-operable.

\- On invalid submit, move focus to the first invalid field.

\- Do not use any TypeScript `any`.



Use standalone Angular components and inject() for dependencies.



\## 2. Implementation output



The implementation contains:



\- `create-quote.ts`

\- `create-quote.html`

\- `create-quote.css`

\- `app.config.ts`

\- `services/quote.service.ts`



The form uses Angular Reactive Forms with:



\- `Validators.required`

\- `Validators.minLength(1)`

\- `Validators.maxLength(200)` for Author

\- `Validators.minLength(1)`

\- `Validators.maxLength(1000)` for Text



The service sends the real request:



POST /api/quotes/



with:



{

&#x20; "Author": "...",

&#x20; "Text": "..."

}



The component uses `inject(FormBuilder)` and `inject(QuoteService)`.



The template uses associated `<label>` elements, `aria-invalid`, `aria-describedby`, `role="alert"` for errors, and `role="status"` for success.



\## 3. Verification log



\### Empty / invalid state



Submitted the empty form.



Observed:



\- "Author is required."

\- "Quote text is required."

\- Focus moves to the first invalid field, Author.



\### Valid state



Tested:



Author:

Aahana Tyagi



Text:

testing day14 reactive form



The form accepted the values because they satisfy the real API constraints.



\### Submitting state



The submit button is disabled while the HTTP request is in progress and displays the submitting state.



\### Server-error state



Submitted valid data to the real POST endpoint.



The request was attempted and the UI displayed:



"Unable to create the quote. Please try again."



This verified that a failed server request is surfaced to the user rather than silently swallowed.



\### Accessibility verification



The form was exercised using the keyboard.



The fields have associated labels, invalid fields expose `aria-invalid`, and validation messages are connected using `aria-describedby`.



On invalid submission, focus is moved to the first invalid field.



\### Concrete issue caught



The first implementation only displayed a placeholder message saying the form was ready to submit. It did not actually call the real API.



I corrected this by adding `QuoteService.createQuote()` using:



POST /api/quotes/



and connected the form submission to the real HTTP request. The server-error state was then verified.



\## What did you learn this session?



I learned how to build a reactive Angular form from a real API contract, keep client-side validators aligned with backend constraints, handle submission and server errors, and make validation accessible with keyboard-friendly focus management and ARIA attributes.



\## What would break this?



If the API changes the `/api/quotes/` endpoint, renames `Author` or `Text`, adds a new required field, or changes the length limits, the form validators, request model, and template would need to be updated.

