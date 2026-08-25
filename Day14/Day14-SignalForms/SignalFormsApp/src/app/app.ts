import { Component } from '@angular/core';
import { CreateQuoteComponent } from './create-quote/create-quote';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CreateQuoteComponent],
  template: `
    <main>
      <h1>Quotes API</h1>
      <app-create-quote />
    </main>
  `,
  styles: [`
    main {
      max-width: 900px;
      margin: auto;
      padding: 20px;
    }
  `]
})
export class App {}