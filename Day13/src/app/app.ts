import { Component } from '@angular/core';
import { QuoteListComponent } from './quote-list/quote-list';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [QuoteListComponent],
  template: `
    <main>
      <h1>Quotes</h1>
      <app-quote-list />
    </main>
  `,
  styles: [`
    main {
      max-width: 900px;
      margin: 40px auto;
      padding: 20px;
      font-family: Arial, sans-serif;
    }
  `]
})
export class App {
}