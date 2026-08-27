import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';

import { AuthService } from '../../services/auth.service';

interface LoginResponse {
  access_token: string;
  refresh_token: string;
  expires_in: number;
}

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule],
  template: `
    <main id="main-content" class="page">
      <div class="page-header">
        <span class="page-header__eyebrow">Sign in</span>
        <h1>Log in</h1>
      </div>

      <div class="card" style="max-width: 26rem;">
        <form (ngSubmit)="login()" novalidate>
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
              autocomplete="current-password"
              required
              [(ngModel)]="password"
            />
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
            {{ loading() ? 'Signing in…' : 'Log in' }}
          </button>

          <p class="field-hint">
            Demo credentials: demo&#64;thinkschool.local / Interview#2026
          </p>
        </form>
      </div>
    </main>
  `
})
export class LoginComponent {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);

  email = '';
  password = '';

  loading = signal(false);
  errorMessage = signal('');

  login(): void {
    if (!this.email || !this.password) {
      this.errorMessage.set('Enter both an email and a password.');
      return;
    }

    this.errorMessage.set('');
    this.loading.set(true);

    this.http
      .post<LoginResponse>('http://localhost:5145/api/auth/login', {
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
        error: () => {
          this.loading.set(false);
          this.errorMessage.set('Invalid email or password.');
        }
      });
  }
}
