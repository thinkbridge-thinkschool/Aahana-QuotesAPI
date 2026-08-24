\# Day 13 — Signals + zoneless + standalone



\## 1. Brief to the agent



Build an Angular 21 standalone, signals-first frontend against my real Week-1 Quotes API.



Real API:

GET /api/quotes/?page=1\&size=10



The API uses the real quote fields:

\- Id

\- Author

\- Text



The endpoint accepts page and size parameters and returns a bare JSON array because GetPagedAsync returns Task<IReadOnlyList<Quote>>.



Requirements:

\- Angular 21 standalone

\- No NgModules

\- bootstrapApplication

\- signal()

\- computed() derived from two signals

\- effect()

\- inject()

\- @if

\- @for with track quote.Id

\- @switch

\- page and size controls so the computed value changes when either signal changes



\## 2. Agent output



The agent created a standalone Angular application using:

\- signal() for page, size, quotes, loading and error

\- computed() for queryDescription using both page() and size()

\- computed() for UI state

\- effect() to reload quotes when page or size changes

\- inject() for services

\- @if for loading/error handling

\- @switch for loading/error/empty/ready states

\- @for (quote of quotes(); track quote.Id) for the quote list



The QuoteService calls:



GET /api/quotes/?page=...\&size=...



and expects Quote\[].



The agent initially assumed the API might return an { items: Quote\[] } envelope.



\## 3. Verification



I verified the real backend contract in the repository.



IQuoteRepository.GetPagedAsync returns:



Task<IReadOnlyList<Quote>>



Therefore the API returns a bare JSON array of quotes rather than an items envelope.



I caught the agent's incorrect assumption and directed it to change:



http.get<Quote\[] | { items: Quote\[] }>(...)



to:



http.get<Quote\[]>(...)



The unused response-unwrapping logic was also removed.



Angular verification:

\- `ng build` completed successfully.

\- `ng serve` completed successfully.

\- The application opened successfully at http://localhost:4200/.

\- The UI displayed Page 1 · 10 quotes and the page/size controls.

\- The frontend correctly displayed its error state when the real API was unavailable.

\- The real backend could not be exercised end-to-end during this verification because the Week-1 API was not running on localhost:5050.



The implementation uses no NgModule and uses standalone bootstrapApplication.



\## What did you learn this session?



I learned how Angular signals can represent reactive state, while computed() derives values from that state and effect() performs reactive side effects. I also learned how standalone Angular applications remove the need for NgModules and how @for with track improves list rendering.



\## What would break this?



A change to the API route, query parameters, response shape, or quote field names would break the frontend. For example, changing Id, Author, or Text or changing the response from a bare array to a different structure would require changes to the Angular model/service.

