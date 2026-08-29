import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'quotes'
  },
  {
    path: 'login',
    title: 'Log in — thinkschool quotes',
    loadComponent: () =>
      import('./pages/login/login.component')
        .then(m => m.LoginComponent)
  },
  {
    path: 'register',
    title: 'Sign up — thinkschool quotes',
    loadComponent: () =>
      import('./pages/register/register.component')
        .then(m => m.RegisterComponent)
  },
  {
    path: 'quotes',
    title: 'Quotes — thinkschool quotes',
    loadComponent: () =>
      import('./pages/quotes/quotes.component')
        .then(m => m.QuotesComponent)
  },
  {
    path: 'quotes/:id',
    title: 'Quote detail — thinkschool quotes',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./pages/quote-detail/quote-detail.component')
        .then(m => m.QuoteDetailComponent)
  },
  {
    path: 'quotes-new',
    title: 'Add a quote — thinkschool quotes',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./pages/create-quote/create-quote.component')
        .then(m => m.CreateQuoteComponent)
  },
  {
    path: '**',
    redirectTo: 'quotes'
  }
];
