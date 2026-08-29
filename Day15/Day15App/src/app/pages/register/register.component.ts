import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../services/auth.service';
import { environment } from '../../../environments/environment';

interface RegisterResponse {
  access_token: string;
  refresh_token: string;
  expires_in: number;
}

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <main id="main-content" class="page">
      <div class="page-header">
        <span class="page-header__eyebrow">Sign up</span>
        <h1>Create an account</h1>
      </div>

      <div class="card" style="max-width: 26rem;">
        <form (ngSubmit)="register()" novalidate>
          @if (errorMessage()) {
            <p class="alert" role="alert">{{ errorMessage() }}</p>
          }

          <div class="field">
            <label for="email">Email</label>
            <input
              id="email"
              name="email"
              type="email"
              autocomplete="email"
              required
              [(ngModel)]="email"
            />
          </div>

          <div class="field">
            <label for="password">Password</label>
            <input
              id="password"
              name="password"
              type="password"
              autocomplete="new-password"
              required
              minlength="8"
              [(ngModel)]="password"
            />
            <p class="field-hint">At least 8 characters.</p>
          </div>

          <button
            type="submit"
            class="btn btn-primary"
            [disabled]="loading()"
            [attr.aria-busy]="loading()"
          >
            @if (loading()) {
              <span class="spinner" aria-hidden="true"></span>
            }
            {{ loading() ? 'Creating account…' : 'Sign up' }}
          </button>

          <p class="field-hint">
            Already have an account? <a routerLink="/login">Log in</a>
          </p>
        </form>
      </div>
    </main>
  `
})
export class RegisterComponent {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);

  email = '';
  password = '';

  loading = signal(false);
  errorMessage = signal('');

  register(): void {
    if (!this.email || !this.password) {
      this.errorMessage.set('Enter both an email and a password.');
      return;
    }

    if (this.password.length < 8) {
      this.errorMessage.set('Password must be at least 8 characters.');
      return;
    }

    this.errorMessage.set('');
    this.loading.set(true);

    this.http
      .post<RegisterResponse>(`${environment.apiBaseUrl}/api/auth/register`, {
        email: this.email,
        password: this.password
      })
      .subscribe({
        next: response => {
          this.authService.setSession(
            response.access_token,
            response.refresh_token
          );

          this.loading.set(false);
          this.router.navigate(['/quotes']);
        },
        error: (error: HttpErrorResponse) => {
          this.loading.set(false);

          if (error.status === 409) {
            this.errorMessage.set(
              'An account with this email already exists.'
            );
            return;
          }

          this.errorMessage.set(
            'Unable to create an account. Check your details and try again.'
          );
        }
      });
  }
}
