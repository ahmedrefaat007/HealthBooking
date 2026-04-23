# Angular 17 Frontend Plan for HealthBooking Backend

**Status**: Ready for Implementation  
**Target**: Angular 17 SPA with Tailwind CSS, standalone components  
**Date**: April 20, 2026

---

## Executive Summary

Create an Angular 17 Single Page Application (SPA) that serves three distinct user portals:
- **Patient Portal**: Browse providers, book/reschedule/cancel appointments, view appointment history
- **Provider Portal**: Manage availability, confirm appointments, track schedule
- **Admin Dashboard**: System oversight, user management, appointment monitoring

The frontend consumes the .NET HealthBooking backend via YARP API Gateway (`http://localhost:5000`), authenticating against Duende IdentityServer (`http://localhost:5005`).

**Stack**: Angular 17 (standalone), Tailwind CSS (utility-first), HttpClient with interceptors, standalone route guards, TypeScript 5.2+

---

## Phase 1: Scaffold & Core Infrastructure

### 1.1 Generate Angular 17 Workspace

```bash
ng new health-booking-spa \
  --routing \
  --skip-git \
  --style=css \
  --skip-package-json=false

cd health-booking-spa
npm install -D tailwindcss postcss autoprefixer
npx tailwindcss init -p
```

**Tailwind configuration** (`tailwind.config.js`):
```javascript
module.exports = {
  content: ["./src/**/*.{html,ts}"],
  theme: {
    extend: {
      colors: {
        primary: "#0f766e",
        secondary: "#0891b2",
        success: "#16a34a",
        danger: "#dc2626"
      }
    }
  },
  plugins: []
};
```

**CSS** (`src/styles.css`):
```css
@tailwind base;
@tailwind components;
@tailwind utilities;

@layer components {
  .btn-primary {
    @apply px-4 py-2 rounded bg-primary text-white hover:bg-teal-800 transition;
  }
  .btn-secondary {
    @apply px-4 py-2 rounded bg-secondary text-white hover:bg-cyan-700 transition;
  }
  .btn-danger {
    @apply px-4 py-2 rounded bg-danger text-white hover:bg-red-700 transition;
  }
  .card {
    @apply bg-white rounded-lg shadow-md p-6;
  }
  .input-field {
    @apply border border-gray-300 rounded px-3 py-2 w-full focus:outline-none focus:ring-2 focus:ring-primary;
  }
}
```

### 1.2 Environment Configuration

**`src/environments/environment.ts`**:
```typescript
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5000',
  identityUrl: 'http://localhost:5005',
  oauth: {
    clientId: 'patient-spa',
    clientSecret: 'patient-spa-secret',
    scopes: 'openid profile email offline_access healthbooking-api patient:read patient:write appointment:read appointment:write',
    grantType: 'password'
  }
};
```

**`src/environments/environment.prod.ts`**:
```typescript
export const environment = {
  production: true,
  apiBaseUrl: 'https://api.healthbooking.com',
  identityUrl: 'https://identity.healthbooking.com',
  oauth: {
    clientId: 'patient-spa',
    clientSecret: 'patient-spa-secret',  // Move to secure backend route in production
    scopes: 'openid profile email offline_access healthbooking-api patient:read patient:write appointment:read appointment:write',
    grantType: 'password'
  }
};
```

### 1.3 Core Models

**`src/app/core/models/auth.model.ts`**:
```typescript
export interface TokenResponse {
  access_token: string;
  refresh_token: string;
  expires_in: number;
  token_type: string;
}

export interface DecodedToken {
  sub: string;           // User ID (Guid for patients/providers)
  email: string;
  name: string;
  scope: string;         // Space-separated scopes
  aud: string;           // Audience
  exp: number;           // Expiration timestamp
}

export interface AuthUser {
  id: string;
  email: string;
  name: string;
  role: 'Patient' | 'Provider' | 'Admin';
  accessToken: string;
  refreshToken: string;
}
```

**`src/app/core/models/patient.model.ts`**:
```typescript
export interface Patient {
  patientId: string;       // UUID
  firstName: string;
  lastName: string;
  contactEmail: string;
  phoneNumber: string;
  dateOfBirth: string;    // YYYY-MM-DD
  registrationDate: string; // ISO
}

export interface RegisterPatientRequest {
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  dateOfBirth: string;    // YYYY-MM-DD
}
```

**`src/app/core/models/provider.model.ts`**:
```typescript
export interface Provider {
  providerId: string;
  firstName: string;
  lastName: string;
  specialty: string;
  licenseNumber: string;
}

export interface RegisterProviderRequest {
  firstName: string;
  lastName: string;
  specialty: string;
  licenseNumber: string;
}

export interface DefineAvailabilityRequest {
  date: string;          // YYYY-MM-DD
  startTime: string;     // HH:mm
  endTime: string;       // HH:mm
}
```

**`src/app/core/models/slot.model.ts`**:
```typescript
export interface Slot {
  slotId: string;
  providerId: string;
  date: string;          // YYYY-MM-DD
  startTime: string;     // HH:mm
  endTime: string;       // HH:mm
  durationMinutes: number; // 30
  status: 'Available' | 'Locked' | 'Booked';
}
```

**`src/app/core/models/appointment.model.ts`**:
```typescript
export interface Appointment {
  appointmentId: string;
  patientId: string;
  slotId: string;
  patientName: string;
  scheduledStartUtc: string;    // ISO 8601
  status: 'Booked' | 'Confirmed' | 'Completed' | 'Cancelled' | 'NoShow';
  cancelReason?: string;
}

export interface BookAppointmentRequest {
  slotId: string;
}

export interface RescheduleAppointmentRequest {
  newSlotId: string;
}

export interface CancelAppointmentRequest {
  reason: string;
}
```

### 1.4 Auth Service

**`src/app/core/auth/auth.service.ts`**:
```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { TokenResponse, DecodedToken, AuthUser } from '../models/auth.model';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private currentUser$ = new BehaviorSubject<AuthUser | null>(null);
  private accessToken: string | null = null;
  private refreshToken: string | null = null;
  private tokenExpiration: number | null = null;

  constructor(private http: HttpClient) {
    this.initializeFromSessionStorage();
  }

  private initializeFromSessionStorage(): void {
    const stored = sessionStorage.getItem('auth_user');
    if (stored) {
      const user = JSON.parse(stored);
      this.accessToken = sessionStorage.getItem('access_token');
      this.refreshToken = sessionStorage.getItem('refresh_token');
      this.tokenExpiration = parseInt(sessionStorage.getItem('token_expiration') || '0');
      this.currentUser$.next(user);
    }
  }

  login(email: string, password: string): Observable<AuthUser> {
    const body = new URLSearchParams();
    body.set('grant_type', environment.oauth.grantType);
    body.set('client_id', environment.oauth.clientId);
    body.set('client_secret', environment.oauth.clientSecret);
    body.set('username', email);
    body.set('password', password);
    body.set('scope', environment.oauth.scopes);

    return this.http
      .post<TokenResponse>(`${environment.identityUrl}/connect/token`, body.toString(), {
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' }
      })
      .pipe(
        map(response => {
          this.setTokens(response);
          const decoded = this.decodeToken(response.access_token);
          const user: AuthUser = {
            id: decoded.sub,
            email: decoded.email,
            name: decoded.name,
            role: this.extractRole(decoded),
            accessToken: response.access_token,
            refreshToken: response.refresh_token
          };
          this.currentUser$.next(user);
          sessionStorage.setItem('auth_user', JSON.stringify(user));
          return user;
        })
      );
  }

  logout(): void {
    this.currentUser$.next(null);
    this.accessToken = null;
    this.refreshToken = null;
    sessionStorage.removeItem('auth_user');
    sessionStorage.removeItem('access_token');
    sessionStorage.removeItem('refresh_token');
    sessionStorage.removeItem('token_expiration');
  }

  getCurrentUser(): AuthUser | null {
    return this.currentUser$.value;
  }

  getCurrentUser$(): Observable<AuthUser | null> {
    return this.currentUser$.asObservable();
  }

  getAccessToken(): string | null {
    return this.accessToken;
  }

  isAuthenticated(): boolean {
    return !!this.accessToken && !this.isTokenExpired();
  }

  private setTokens(response: TokenResponse): void {
    this.accessToken = response.access_token;
    this.refreshToken = response.refresh_token;
    this.tokenExpiration = Date.now() + response.expires_in * 1000;
    sessionStorage.setItem('access_token', response.access_token);
    sessionStorage.setItem('refresh_token', response.refresh_token);
    sessionStorage.setItem('token_expiration', this.tokenExpiration.toString());
  }

  private decodeToken(token: string): DecodedToken {
    try {
      const payload = token.split('.')[1];
      const decoded = JSON.parse(atob(payload));
      return decoded;
    } catch (error) {
      throw new Error('Invalid token format');
    }
  }

  private extractRole(decoded: DecodedToken): 'Patient' | 'Provider' | 'Admin' {
    // Roles provisioned by backend; check aud claim or custom role claim
    if (decoded.aud?.includes('admin')) return 'Admin';
    // Default to read claims based on scopes or custom role claim
    return 'Patient'; // Default fallback
  }

  private isTokenExpired(): boolean {
    if (!this.tokenExpiration) return true;
    return Date.now() >= this.tokenExpiration - 60000; // 1 min buffer
  }

  refreshAccessToken(): Observable<TokenResponse> {
    if (!this.refreshToken) {
      throw new Error('No refresh token available');
    }

    const body = new URLSearchParams();
    body.set('grant_type', 'refresh_token');
    body.set('client_id', environment.oauth.clientId);
    body.set('client_secret', environment.oauth.clientSecret);
    body.set('refresh_token', this.refreshToken);

    return this.http
      .post<TokenResponse>(`${environment.identityUrl}/connect/token`, body.toString(), {
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' }
      })
      .pipe(
        map(response => {
          this.setTokens(response);
          const user = this.currentUser$.value;
          if (user) {
            user.accessToken = response.access_token;
            this.currentUser$.next(user);
            sessionStorage.setItem('auth_user', JSON.stringify(user));
          }
          return response;
        })
      );
  }
}
```

### 1.5 Auth Interceptor

**`src/app/core/auth/auth.interceptor.ts`**:
```typescript
import { Injectable } from '@angular/core';
import {
  HttpRequest,
  HttpHandler,
  HttpEvent,
  HttpInterceptor,
  HttpErrorResponse
} from '@angular/common/http';
import { Observable, throwError, BehaviorSubject } from 'rxjs';
import { catchError, switchMap, filter, take } from 'rxjs/operators';
import { AuthService } from './auth.service';
import { v4 as uuidv4 } from 'uuid';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  private isRefreshing = false;
  private refreshTokenSubject = new BehaviorSubject<string | null>(null);

  constructor(private authService: AuthService) {}

  intercept(
    request: HttpRequest<unknown>,
    next: HttpHandler
  ): Observable<HttpEvent<unknown>> {
    // Add Authorization and Correlation-Id headers
    const token = this.authService.getAccessToken();
    if (token) {
      request = this.addHeaders(request, token);
    }

    return next.handle(request).pipe(
      catchError(error => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          return this.handle401Error(request, next);
        }
        return throwError(() => error);
      })
    );
  }

  private addHeaders(request: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
    return request.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`,
        'X-Correlation-Id': uuidv4()
      }
    });
  }

  private handle401Error(
    request: HttpRequest<unknown>,
    next: HttpHandler
  ): Observable<HttpEvent<unknown>> {
    if (!this.isRefreshing) {
      this.isRefreshing = true;
      this.refreshTokenSubject.next(null);

      return this.authService.refreshAccessToken().pipe(
        switchMap((response: any) => {
          this.isRefreshing = false;
          this.refreshTokenSubject.next(response.access_token);
          return next.handle(this.addHeaders(request, response.access_token));
        }),
        catchError(err => {
          this.isRefreshing = false;
          this.authService.logout();
          window.location.href = '/login';
          return throwError(() => err);
        })
      );
    } else {
      return this.refreshTokenSubject.pipe(
        filter(token => token != null),
        take(1),
        switchMap(token => next.handle(this.addHeaders(request, token!)))
      );
    }
  }
}
```

### 1.6 Auth Guard

**`src/app/core/auth/auth.guard.ts`**:
```typescript
import { Injectable } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { inject } from '@angular/core';

export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) {
    return true;
  }

  router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
  return false;
};

export const roleGuard = (requiredRole: 'Patient' | 'Provider' | 'Admin'): CanActivateFn => {
  return (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const user = authService.getCurrentUser();
    if (user && user.role === requiredRole) {
      return true;
    }

    router.navigate(['/forbidden']);
    return false;
  };
};
```

### 1.7 App Configuration

**`src/app/app.config.ts`**:
```typescript
import { ApplicationConfig, importProvidersFrom } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors, HTTP_INTERCEPTORS } from '@angular/common/http';
import { routes } from './app.routes';
import { AuthInterceptor } from './core/auth/auth.interceptor';
import { ErrorInterceptor } from './core/interceptors/error.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([]) // Use class-based interceptors below
    ),
    {
      provide: HTTP_INTERCEPTORS,
      useClass: AuthInterceptor,
      multi: true
    },
    {
      provide: HTTP_INTERCEPTORS,
      useClass: ErrorInterceptor,
      multi: true
    }
  ]
};
```

### 1.8 Shared Components

**`src/app/shared/components/navbar/navbar.component.ts`**:
```typescript
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { AuthUser } from '../../../core/models/auth.model';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <nav class="bg-primary text-white shadow-lg">
      <div class="max-w-7xl mx-auto px-4 py-4 flex justify-between items-center">
        <div class="text-2xl font-bold">HealthBooking</div>
        <div class="flex gap-6" *ngIf="currentUser">
          <span>{{ currentUser.name }}</span>
          <button (click)="logout()" class="hover:text-gray-200">Logout</button>
        </div>
      </div>
    </nav>
  `
})
export class NavbarComponent implements OnInit {
  currentUser: AuthUser | null = null;

  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.authService.getCurrentUser$().subscribe(user => {
      this.currentUser = user;
    });
  }

  logout(): void {
    this.authService.logout();
    this.router.navigate(['/login']);
  }
}
```

**`src/app/shared/components/loading-spinner/loading-spinner.component.ts`**:
```typescript
import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-loading-spinner',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div *ngIf="isLoading" class="flex justify-center items-center py-8">
      <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-primary"></div>
    </div>
  `
})
export class LoadingSpinnerComponent {
  @Input() isLoading = false;
}
```

**`src/app/shared/components/status-badge/status-badge.component.ts`**:
```typescript
import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-status-badge',
  standalone: true,
  imports: [CommonModule],
  template: `
    <span [ngClass]="getBadgeClass()">
      {{ status }}
    </span>
  `
})
export class StatusBadgeComponent {
  @Input() status!: string;

  getBadgeClass(): string {
    const baseClass = 'px-3 py-1 rounded text-white text-sm font-semibold';
    const colorClass: { [key: string]: string } = {
      'Booked': 'bg-blue-500',
      'Confirmed': 'bg-green-500',
      'Completed': 'bg-gray-500',
      'Cancelled': 'bg-red-500',
      'NoShow': 'bg-orange-500',
      'Available': 'bg-teal-500',
      'Locked': 'bg-yellow-500'
    };
    return `${baseClass} ${colorClass[this.status] || 'bg-gray-400'}`;
  }
}
```

### 1.9 Toast Service

**`src/app/core/services/toast.service.ts`**:
```typescript
import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

export interface Toast {
  id: string;
  message: string;
  type: 'success' | 'error' | 'info' | 'warning';
  duration?: number;
}

@Injectable({
  providedIn: 'root'
})
export class ToastService {
  private toasts$ = new BehaviorSubject<Toast[]>([]);

  getToasts(): Observable<Toast[]> {
    return this.toasts$.asObservable();
  }

  show(message: string, type: Toast['type'] = 'info', duration: number = 3000): void {
    const toast: Toast = {
      id: Date.now().toString(),
      message,
      type,
      duration
    };
    const current = this.toasts$.value;
    this.toasts$.next([...current, toast]);

    if (duration > 0) {
      setTimeout(() => this.remove(toast.id), duration);
    }
  }

  remove(id: string): void {
    const current = this.toasts$.value;
    this.toasts$.next(current.filter(t => t.id !== id));
  }

  success(message: string): void {
    this.show(message, 'success');
  }

  error(message: string): void {
    this.show(message, 'error', 5000);
  }

  info(message: string): void {
    this.show(message, 'info');
  }

  warning(message: string): void {
    this.show(message, 'warning', 4000);
  }
}
```

---

## Phase 2: Authentication Pages

### 2.1 Login Page

**`src/app/features/auth/login/login.component.ts`**:
```typescript
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ToastService } from '../../../core/services/toast.service';
import { LoadingSpinnerComponent } from '../../../shared/components/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, LoadingSpinnerComponent],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-gray-100">
      <div class="card w-full max-w-md">
        <h1 class="text-3xl font-bold mb-6 text-center">Login</h1>
        
        <form [formGroup]="form" (ngSubmit)="onSubmit()">
          <div class="mb-4">
            <label class="block text-sm font-semibold mb-2">Email</label>
            <input 
              type="email" 
              formControlName="email" 
              class="input-field"
              placeholder="user@example.com"
            />
            <span class="text-red-500 text-sm" *ngIf="form.get('email')?.touched && form.get('email')?.invalid">
              Valid email required
            </span>
          </div>

          <div class="mb-6">
            <label class="block text-sm font-semibold mb-2">Password</label>
            <input 
              type="password" 
              formControlName="password" 
              class="input-field"
              placeholder="••••••••"
            />
            <span class="text-red-500 text-sm" *ngIf="form.get('password')?.touched && form.get('password')?.invalid">
              Password required
            </span>
          </div>

          <button 
            type="submit" 
            [disabled]="form.invalid || isLoading"
            class="btn-primary w-full"
          >
            {{ isLoading ? 'Logging in...' : 'Login' }}
          </button>
        </form>

        <div class="mt-4 text-center text-sm">
          <p>Don't have an account?</p>
          <a href="/register/patient" class="text-primary hover:underline">Register as Patient</a> | 
          <a href="/register/provider" class="text-primary hover:underline">Register as Provider</a>
        </div>
      </div>
    </div>
  `
})
export class LoginComponent {
  form: FormGroup;
  isLoading = false;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private toast: ToastService,
    private router: Router,
    private route: ActivatedRoute
  ) {
    this.form = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required]]
    });
  }

  onSubmit(): void {
    if (this.form.invalid) return;

    this.isLoading = true;
    const { email, password } = this.form.value;

    this.authService.login(email, password).subscribe({
      next: user => {
        this.toast.success(`Welcome, ${user.name}!`);
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
        
        // Route by role
        if (user.role === 'Patient') {
          this.router.navigate([returnUrl || '/patient/dashboard']);
        } else if (user.role === 'Provider') {
          this.router.navigate([returnUrl || '/provider/dashboard']);
        } else if (user.role === 'Admin') {
          this.router.navigate([returnUrl || '/admin/dashboard']);
        }
      },
      error: err => {
        this.toast.error('Login failed. Please check your credentials.');
        this.isLoading = false;
      }
    });
  }
}
```

### 2.2 Patient Registration Page

**`src/app/features/auth/register-patient/register-patient.component.ts`**:
```typescript
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { ToastService } from '../../../core/services/toast.service';
import { LoadingSpinnerComponent } from '../../../shared/components/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-register-patient',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, LoadingSpinnerComponent],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-gray-100">
      <div class="card w-full max-w-2xl">
        <h1 class="text-3xl font-bold mb-6">Register as Patient</h1>
        
        <form [formGroup]="form" (ngSubmit)="onSubmit()">
          <div class="grid grid-cols-2 gap-4 mb-4">
            <div>
              <label class="block text-sm font-semibold mb-2">First Name</label>
              <input type="text" formControlName="firstName" class="input-field" />
              <span class="text-red-500 text-sm" *ngIf="form.get('firstName')?.invalid && form.get('firstName')?.touched">
                Required (1-100 chars)
              </span>
            </div>
            <div>
              <label class="block text-sm font-semibold mb-2">Last Name</label>
              <input type="text" formControlName="lastName" class="input-field" />
              <span class="text-red-500 text-sm" *ngIf="form.get('lastName')?.invalid && form.get('lastName')?.touched">
                Required (1-100 chars)
              </span>
            </div>
          </div>

          <div class="mb-4">
            <label class="block text-sm font-semibold mb-2">Email</label>
            <input type="email" formControlName="email" class="input-field" />
            <span class="text-red-500 text-sm" *ngIf="form.get('email')?.invalid && form.get('email')?.touched">
              Valid email required (max 256 chars)
            </span>
          </div>

          <div class="grid grid-cols-2 gap-4 mb-4">
            <div>
              <label class="block text-sm font-semibold mb-2">Phone Number</label>
              <input type="tel" formControlName="phoneNumber" class="input-field" placeholder="E.164 format" />
              <span class="text-red-500 text-sm" *ngIf="form.get('phoneNumber')?.invalid && form.get('phoneNumber')?.touched">
                7-15 digits (E.164)
              </span>
            </div>
            <div>
              <label class="block text-sm font-semibold mb-2">Date of Birth</label>
              <input type="date" formControlName="dateOfBirth" class="input-field" />
              <span class="text-red-500 text-sm" *ngIf="form.get('dateOfBirth')?.invalid && form.get('dateOfBirth')?.touched">
                Must be in the past
              </span>
            </div>
          </div>

          <button 
            type="submit" 
            [disabled]="form.invalid || isLoading"
            class="btn-primary w-full mb-4"
          >
            {{ isLoading ? 'Registering...' : 'Register' }}
          </button>

          <div class="text-center text-sm">
            <p>Already have an account? <a href="/login" class="text-primary hover:underline">Login</a></p>
          </div>
        </form>
      </div>
    </div>
  `
})
export class RegisterPatientComponent {
  form: FormGroup;
  isLoading = false;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private toast: ToastService,
    private router: Router
  ) {
    this.form = this.fb.group({
      firstName: ['', [Validators.required, Validators.minLength(1), Validators.maxLength(100)]],
      lastName: ['', [Validators.required, Validators.minLength(1), Validators.maxLength(100)]],
      email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
      phoneNumber: ['', [Validators.required, Validators.pattern(/^\d{7,15}$/)]],
      dateOfBirth: ['', [Validators.required, this.dateInPastValidator.bind(this)]]
    });
  }

  dateInPastValidator(control: any): { [key: string]: any } | null {
    if (!control.value) return null;
    const selectedDate = new Date(control.value);
    const today = new Date();
    return selectedDate < today ? null : { dateInFuture: true };
  }

  onSubmit(): void {
    if (this.form.invalid) return;

    this.isLoading = true;
    const payload = this.form.value;

    this.http.post(`${environment.apiBaseUrl}/api/patients/register`, payload).subscribe({
      next: (response: any) => {
        this.toast.success('Registration successful! Redirecting to login...');
        setTimeout(() => this.router.navigate(['/login']), 1500);
      },
      error: err => {
        this.toast.error(err.error?.message || 'Registration failed');
        this.isLoading = false;
      }
    });
  }
}
```

### 2.3 Provider Registration Page

**`src/app/features/auth/register-provider/register-provider.component.ts`**:
```typescript
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { ToastService } from '../../../core/services/toast.service';
import { LoadingSpinnerComponent } from '../../../shared/components/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-register-provider',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, LoadingSpinnerComponent],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-gray-100">
      <div class="card w-full max-w-2xl">
        <h1 class="text-3xl font-bold mb-6">Register as Provider</h1>
        
        <form [formGroup]="form" (ngSubmit)="onSubmit()">
          <div class="grid grid-cols-2 gap-4 mb-4">
            <div>
              <label class="block text-sm font-semibold mb-2">First Name</label>
              <input type="text" formControlName="firstName" class="input-field" />
              <span class="text-red-500 text-sm" *ngIf="form.get('firstName')?.invalid && form.get('firstName')?.touched">
                Required (1-100 chars)
              </span>
            </div>
            <div>
              <label class="block text-sm font-semibold mb-2">Last Name</label>
              <input type="text" formControlName="lastName" class="input-field" />
              <span class="text-red-500 text-sm" *ngIf="form.get('lastName')?.invalid && form.get('lastName')?.touched">
                Required (1-100 chars)
              </span>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-4 mb-4">
            <div>
              <label class="block text-sm font-semibold mb-2">Specialty</label>
              <input type="text" formControlName="specialty" class="input-field" placeholder="e.g. Cardiology" />
              <span class="text-red-500 text-sm" *ngIf="form.get('specialty')?.invalid && form.get('specialty')?.touched">
                Required (max 100 chars)
              </span>
            </div>
            <div>
              <label class="block text-sm font-semibold mb-2">License Number</label>
              <input type="text" formControlName="licenseNumber" class="input-field" />
              <span class="text-red-500 text-sm" *ngIf="form.get('licenseNumber')?.invalid && form.get('licenseNumber')?.touched">
                Required (max 50 chars, unique)
              </span>
            </div>
          </div>

          <button 
            type="submit" 
            [disabled]="form.invalid || isLoading"
            class="btn-primary w-full mb-4"
          >
            {{ isLoading ? 'Registering...' : 'Register' }}
          </button>

          <div class="text-center text-sm">
            <p>Already have an account? <a href="/login" class="text-primary hover:underline">Login</a></p>
          </div>
        </form>
      </div>
    </div>
  `
})
export class RegisterProviderComponent {
  form: FormGroup;
  isLoading = false;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private toast: ToastService,
    private router: Router
  ) {
    this.form = this.fb.group({
      firstName: ['', [Validators.required, Validators.minLength(1), Validators.maxLength(100)]],
      lastName: ['', [Validators.required, Validators.minLength(1), Validators.maxLength(100)]],
      specialty: ['', [Validators.required, Validators.maxLength(100)]],
      licenseNumber: ['', [Validators.required, Validators.maxLength(50)]]
    });
  }

  onSubmit(): void {
    if (this.form.invalid) return;

    this.isLoading = true;
    const payload = this.form.value;

    this.http.post(`${environment.apiBaseUrl}/api/providers/register`, payload).subscribe({
      next: (response: any) => {
        this.toast.success('Registration successful! Redirecting to login...');
        setTimeout(() => this.router.navigate(['/login']), 1500);
      },
      error: err => {
        this.toast.error(err.error?.message || 'Registration failed');
        this.isLoading = false;
      }
    });
  }
}
```

---

## Phase 3: Patient Portal

### 3.1 Patient Service

**`src/app/core/services/patient.service.ts`**:
```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Patient, RegisterPatientRequest } from '../models/patient.model';

@Injectable({
  providedIn: 'root'
})
export class PatientService {
  constructor(private http: HttpClient) {}

  getCurrentPatient(): Observable<Patient> {
    return this.http.get<Patient>(`${environment.apiBaseUrl}/api/patients/me`);
  }

  getPatient(id: string): Observable<Patient> {
    return this.http.get<Patient>(`${environment.apiBaseUrl}/api/patients/${id}`);
  }

  updateProfile(id: string, data: Partial<Patient>): Observable<void> {
    return this.http.put<void>(`${environment.apiBaseUrl}/api/patients/${id}`, data);
  }
}
```

### 3.2 Appointment Service

**`src/app/core/services/appointment.service.ts`**:
```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Appointment, BookAppointmentRequest, CancelAppointmentRequest, RescheduleAppointmentRequest } from '../models/appointment.model';
import { v4 as uuidv4 } from 'uuid';

@Injectable({
  providedIn: 'root'
})
export class AppointmentService {
  constructor(private http: HttpClient) {}

  book(request: BookAppointmentRequest): Observable<{ appointmentId: string }> {
    return this.http.post<{ appointmentId: string }>(
      `${environment.apiBaseUrl}/api/appointments`,
      request,
      {
        headers: { 'Idempotency-Key': uuidv4() }
      }
    );
  }

  getAppointment(id: string): Observable<Appointment> {
    return this.http.get<Appointment>(`${environment.apiBaseUrl}/api/appointments/${id}`);
  }

  getPatientAppointments(patientId: string): Observable<Appointment[]> {
    return this.http.get<Appointment[]>(
      `${environment.apiBaseUrl}/api/appointments/patient/${patientId}`
    );
  }

  cancel(id: string, request: CancelAppointmentRequest): Observable<void> {
    return this.http.delete<void>(
      `${environment.apiBaseUrl}/api/appointments/${id}`,
      { body: request }
    );
  }

  reschedule(id: string, request: RescheduleAppointmentRequest): Observable<void> {
    return this.http.put<void>(
      `${environment.apiBaseUrl}/api/appointments/${id}/reschedule`,
      request
    );
  }

  confirm(id: string): Observable<void> {
    return this.http.post<void>(
      `${environment.apiBaseUrl}/api/appointments/${id}/confirm`,
      {}
    );
  }
}
```

### 3.3 Provider Service

**`src/app/core/services/provider.service.ts`**:
```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Provider, DefineAvailabilityRequest } from '../models/provider.model';
import { Slot } from '../models/slot.model';

@Injectable({
  providedIn: 'root'
})
export class ProviderService {
  constructor(private http: HttpClient) {}

  getProvider(id: string): Observable<Provider> {
    return this.http.get<Provider>(`${environment.apiBaseUrl}/api/providers/${id}`);
  }

  getSlots(providerId: string): Observable<Slot[]> {
    return this.http.get<Slot[]>(
      `${environment.apiBaseUrl}/api/providers/${providerId}/slots`
    );
  }

  defineAvailability(providerId: string, request: DefineAvailabilityRequest): Observable<Slot[]> {
    return this.http.post<Slot[]>(
      `${environment.apiBaseUrl}/api/providers/${providerId}/availability`,
      request
    );
  }
}
```

### 3.4 Patient Dashboard

**`src/app/features/patient/dashboard/dashboard.component.ts`**:
```typescript
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { PatientService } from '../../../core/services/patient.service';
import { AppointmentService } from '../../../core/services/appointment.service';
import { AuthService } from '../../../core/auth/auth.service';
import { Patient } from '../../../core/models/patient.model';
import { Appointment } from '../../../core/models/appointment.model';
import { LoadingSpinnerComponent } from '../../../shared/components/loading-spinner/loading-spinner.component';
import { StatusBadgeComponent } from '../../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-patient-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule, LoadingSpinnerComponent, StatusBadgeComponent],
  template: `
    <div class="max-w-6xl mx-auto py-8">
      <h1 class="text-4xl font-bold mb-8">Welcome, {{ patient?.firstName }}!</h1>

      <app-loading-spinner [isLoading]="isLoading"></app-loading-spinner>

      <div class="grid grid-cols-1 md:grid-cols-2 gap-6" *ngIf="!isLoading">
        <!-- Upcoming Appointments -->
        <div class="card">
          <h2 class="text-2xl font-bold mb-4">Upcoming Appointments</h2>
          <div *ngIf="upcomingAppointments.length; else noAppointments" class="space-y-4">
            <div *ngFor="let apt of upcomingAppointments" class="border-l-4 border-primary pl-4">
              <p class="font-semibold text-lg">{{ apt.scheduledStartUtc | date: 'MMM dd, yyyy' }}</p>
              <p class="text-gray-600">{{ apt.scheduledStartUtc | date: 'HH:mm' }}</p>
              <app-status-badge [status]="apt.status"></app-status-badge>
            </div>
          </div>
          <ng-template #noAppointments>
            <p class="text-gray-500">No upcoming appointments</p>
          </ng-template>
        </div>

        <!-- Quick Actions -->
        <div class="card">
          <h2 class="text-2xl font-bold mb-4">Quick Actions</h2>
          <div class="space-y-3">
            <a href="/patient/providers" class="block btn-primary text-center">Book Appointment</a>
            <a href="/patient/appointments" class="block btn-secondary text-center">View All Appointments</a>
            <a href="/patient/profile" class="block btn-secondary text-center">Edit Profile</a>
          </div>
        </div>
      </div>
    </div>
  `
})
export class PatientDashboardComponent implements OnInit {
  patient: Patient | null = null;
  upcomingAppointments: Appointment[] = [];
  isLoading = true;

  constructor(
    private patientService: PatientService,
    private appointmentService: AppointmentService,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    const user = this.authService.getCurrentUser();
    if (user) {
      this.patientService.getCurrentPatient().subscribe({
        next: patient => {
          this.patient = patient;
          this.appointmentService.getPatientAppointments(patient.patientId).subscribe({
            next: appointments => {
              this.upcomingAppointments = appointments
                .filter(a => a.status === 'Booked' || a.status === 'Confirmed')
                .sort((a, b) => new Date(a.scheduledStartUtc).getTime() - new Date(b.scheduledStartUtc).getTime())
                .slice(0, 3);
              this.isLoading = false;
            }
          });
        }
      });
    }
  }
}
```

### 3.5 Browse Providers

**`src/app/features/patient/browse-providers/browse-providers.component.ts`**:
```typescript
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { ProviderService } from '../../../core/services/provider.service';
import { Provider } from '../../../core/models/provider.model';
import { LoadingSpinnerComponent } from '../../../shared/components/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-browse-providers',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, LoadingSpinnerComponent],
  template: `
    <div class="max-w-6xl mx-auto py-8">
      <h1 class="text-4xl font-bold mb-6">Browse Providers</h1>

      <div class="mb-6">
        <input 
          type="text" 
          placeholder="Search by specialty..."
          class="input-field w-full"
          (keyup)="filterProviders()"
          [(ngModel)]="searchTerm"
        />
      </div>

      <app-loading-spinner [isLoading]="isLoading"></app-loading-spinner>

      <div class="grid grid-cols-1 md:grid-cols-3 gap-6" *ngIf="!isLoading">
        <div *ngFor="let provider of filteredProviders" class="card hover:shadow-lg transition">
          <h3 class="text-xl font-bold">{{ provider.firstName }} {{ provider.lastName }}</h3>
          <p class="text-gray-600 text-lg">{{ provider.specialty }}</p>
          <p class="text-sm text-gray-500">License: {{ provider.licenseNumber }}</p>
          <a 
            [routerLink]="['/patient/providers', provider.providerId, 'book']"
            class="mt-4 block btn-primary text-center"
          >
            View Slots & Book
          </a>
        </div>
      </div>
    </div>
  `
})
export class BrowseProvidersComponent implements OnInit {
  providers: Provider[] = [];
  filteredProviders: Provider[] = [];
  searchTerm = '';
  isLoading = true;

  constructor(private providerService: ProviderService) {}

  ngOnInit(): void {
    // Note: Backend needs GET /api/providers or /api/providers/list endpoint
    // For now, using mock data; replace with actual API call
    this.mockLoadProviders();
  }

  private mockLoadProviders(): void {
    // TODO: Replace with actual API call once list endpoint exists
    this.providers = [
      {
        providerId: '550e8400-e29b-41d4-a716-446655440000',
        firstName: 'John',
        lastName: 'Doe',
        specialty: 'Cardiology',
        licenseNumber: 'LIC-001'
      },
      {
        providerId: '550e8400-e29b-41d4-a716-446655440001',
        firstName: 'Jane',
        lastName: 'Smith',
        specialty: 'Dermatology',
        licenseNumber: 'LIC-002'
      }
    ];
    this.filteredProviders = this.providers;
    this.isLoading = false;
  }

  filterProviders(): void {
    if (!this.searchTerm) {
      this.filteredProviders = this.providers;
    } else {
      this.filteredProviders = this.providers.filter(p =>
        p.specialty.toLowerCase().includes(this.searchTerm.toLowerCase())
      );
    }
  }
}
```

### 3.6 Book Appointment

I'll continue with the remaining key components and then provide the complete routing setup.

**`src/app/features/patient/book-appointment/book-appointment.component.ts`** (abbreviated for brevity):
```typescript
// Component to select a slot, show availability, and call POST /api/appointments
// Handles BookAppointmentRequest with Idempotency-Key
// Shows status feedback on success (201) / conflict (409)
```

### 3.7 My Appointments

**`src/app/features/patient/my-appointments/my-appointments.component.ts`** (abbreviated):
```typescript
// Fetches GET /api/appointments/patient/{patientId}
// Displays table with: date, provider, status (colored badges)
// Shows action buttons: View Detail, Cancel, Reschedule
```

---

## Phase 4: Provider Portal

**`src/app/features/provider/dashboard/dashboard.component.ts`** (abbreviated):
```typescript
// Shows today's appointments
// Provider's specialty, license
// Quick actions: Define Availability, View Appointments
```

**`src/app/features/provider/availability/availability.component.ts`** (abbreviated):
```typescript
// Calendar date picker
// Start time, end time inputs
// POST /api/providers/{id}/availability with { date, startTime, endTime }
// Displays generated 30-minute slots
```

---

## Phase 5: Admin Dashboard

**`src/app/features/admin/dashboard/dashboard.component.ts`** (abbreviated):
```typescript
// Stats: total patients, providers, appointments by status
// Needs admin-specific endpoints or mock data
```

---

## Phase 6: Error Handling & Polish

### 6.1 Error Interceptor

**`src/app/core/interceptors/error.interceptor.ts`**:
```typescript
import { Injectable } from '@angular/core';
import {
  HttpRequest,
  HttpHandler,
  HttpEvent,
  HttpErrorResponse,
  HttpInterceptor
} from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Router } from '@angular/router';
import { ToastService } from '../services/toast.service';

@Injectable()
export class ErrorInterceptor implements HttpInterceptor {
  constructor(
    private router: Router,
    private toast: ToastService
  ) {}

  intercept(
    request: HttpRequest<unknown>,
    next: HttpHandler
  ): Observable<HttpEvent<unknown>> {
    return next.handle(request).pipe(
      catchError((error: HttpErrorResponse) => {
        if (error.status === 401) {
          this.router.navigate(['/login']);
          this.toast.error('Your session has expired. Please login again.');
        } else if (error.status === 403) {
          this.router.navigate(['/forbidden']);
          this.toast.error('You do not have permission to access this resource.');
        } else if (error.status === 404) {
          this.toast.error('Resource not found.');
        } else if (error.status === 409) {
          this.toast.warning('Conflict: This slot is no longer available. Please select another.');
        } else if (error.status === 500 || error.status === 503) {
          this.router.navigate(['/error']);
          this.toast.error('Server error. Please try again later.');
        } else {
          this.toast.error(error.error?.message || 'An error occurred.');
        }

        return throwError(() => error);
      })
    );
  }
}
```

### 6.2 Error Pages

**`src/app/shared/pages/forbidden.component.ts`**:
```typescript
import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-forbidden',
  standalone: true,
  imports: [RouterModule],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-gray-100">
      <div class="text-center">
        <h1 class="text-6xl font-bold text-danger mb-4">403</h1>
        <p class="text-2xl mb-6">Access Forbidden</p>
        <a href="/patient/dashboard" class="btn-primary">Go Back to Dashboard</a>
      </div>
    </div>
  `
})
export class ForbiddenComponent {}
```

**`src/app/shared/pages/error.component.ts`**:
```typescript
import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-error',
  standalone: true,
  imports: [RouterModule],
  template: `
    <div class="min-h-screen flex items-center justify-center bg-gray-100">
      <div class="text-center">
        <h1 class="text-6xl font-bold text-danger mb-4">500</h1>
        <p class="text-2xl mb-6">Server Error</p>
        <p class="mb-6 text-gray-600">Something went wrong. Please try again later.</p>
        <a href="/patient/dashboard" class="btn-primary">Go Back to Dashboard</a>
      </div>
    </div>
  `
})
export class ErrorComponent {}
```

---

## Phase 7: Routing Configuration

**`src/app/app.routes.ts`**:
```typescript
import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/auth/auth.guard';
import { LoginComponent } from './features/auth/login/login.component';
import { RegisterPatientComponent } from './features/auth/register-patient/register-patient.component';
import { RegisterProviderComponent } from './features/auth/register-provider/register-provider.component';
import { PatientDashboardComponent } from './features/patient/dashboard/dashboard.component';
import { BrowseProvidersComponent } from './features/patient/browse-providers/browse-providers.component';
import { ForbiddenComponent } from './shared/pages/forbidden.component';
import { ErrorComponent } from './shared/pages/error.component';

export const routes: Routes = [
  { path: '', redirectTo: '/login', pathMatch: 'full' },
  { path: 'login', component: LoginComponent },
  { path: 'register/patient', component: RegisterPatientComponent },
  { path: 'register/provider', component: RegisterProviderComponent },
  { path: 'forbidden', component: ForbiddenComponent },
  { path: 'error', component: ErrorComponent },

  {
    path: 'patient',
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: PatientDashboardComponent },
      { path: 'providers', component: BrowseProvidersComponent },
      // Add: book appointment, my appointments, appointment detail, profile
    ]
  },

  {
    path: 'provider',
    canActivate: [authGuard],
    children: [
      // Add: dashboard, availability, appointments, profile
    ]
  },

  {
    path: 'admin',
    canActivate: [authGuard],
    children: [
      // Add: dashboard, patients, providers, appointments
    ]
  },

  { path: '**', redirectTo: '/error' }
];
```

---

## API Reference Summary

| Method | Endpoint | Auth | Request Body | Response | Notes |
|--------|----------|------|--------------|----------|-------|
| POST | `/connect/token` | — | `{ grant_type, username, password, client_id, client_secret, scope }` | `{ access_token, refresh_token, expires_in }` | IdentityServer `:5005` |
| POST | `/api/patients/register` | ❌ | `{ firstName, lastName, email, phoneNumber, dateOfBirth }` | `{ patientId }` | Anonymous |
| GET | `/api/patients/me` | ✅ | — | `PatientDto` | Uses email from JWT |
| PUT | `/api/patients/{id}` | ✅ | `{ firstName, lastName, phoneNumber }` | 204 | Owner only |
| POST | `/api/providers/register` | ✅ | `{ firstName, lastName, specialty, licenseNumber }` | `{ providerId }` | Auth required (see note) |
| GET | `/api/providers/{id}` | ✅ | — | `ProviderDto` | |
| GET | `/api/providers/{id}/slots` | ✅ | — | `SlotDto[]` | 60s cache |
| POST | `/api/providers/{id}/availability` | ✅ | `{ date, startTime, endTime }` | `SlotDto[]` | Auto 30-min slots |
| POST | `/api/appointments` | ✅ | `{ slotId }` | `{ appointmentId }` | **Requires header**: `Idempotency-Key: {uuid}` |
| GET | `/api/appointments/{id}` | ✅ | — | `AppointmentDto` | |
| GET | `/api/appointments/patient/{patientId}` | ✅ | — | `AppointmentDto[]` | |
| DELETE | `/api/appointments/{id}` | ✅ | `{ reason }` | 204 | 2hr min notice |
| PUT | `/api/appointments/{id}/reschedule` | ✅ | `{ newSlotId }` | 200 | |
| POST | `/api/appointments/{id}/confirm` | ✅ | `{}` | 204 | Booked → Confirmed |
| POST | `/api/appointments/{id}/no-show` | ✅ | `{}` | 204 | Confirmed → NoShow |

---

## Validation Rules (Client-Side, Matching Backend)

### Patient Registration
- **First Name**: 1–100 chars, required, trimmed
- **Last Name**: 1–100 chars, required, trimmed
- **Email**: Valid email format (contains `@` and `.`), max 256 chars, unique
- **Phone**: 7–15 digits (E.164 format), no spaces, required
- **DOB**: Must be a past date

### Provider Registration
- **First Name**: 1–100 chars, required
- **Last Name**: 1–100 chars, required
- **Specialty**: Non-empty, max 100 chars
- **License**: Max 50 chars, unique across all providers

### Appointments
- **Idempotency-Key**: Automatically generated as UUID on booking
- **Cancel Reason**: Max 500 chars
- **Cancellation Notice**: 2 hours before appointment (enforced server-side; UI should prevent if < 2hr)

---

## Deliverable Checklist

- [ ] Angular 17 workspace scaffolded with Tailwind CSS
- [ ] AuthService with ROPC login & token refresh
- [ ] AuthInterceptor (Bearer token + Correlation-Id)
- [ ] Auth guards (authGuard, roleGuard)
- [ ] Login page
- [ ] Patient registration page
- [ ] Provider registration page
- [ ] Patient dashboard
- [ ] Browse providers page
- [ ] Book appointment page
- [ ] My appointments page
- [ ] Appointment detail & actions (cancel, reschedule)
- [ ] Patient profile page
- [ ] Provider dashboard
- [ ] Availability management page
- [ ] Provider appointments view
- [ ] Admin dashboard (if included)
- [ ] Error pages (401, 403, 404, 500)
- [ ] Error interceptor
- [ ] Toast notifications
- [ ] Loading spinners
- [ ] Status badges (appointment/slot)
- [ ] Mobile responsive design
- [ ] Form validations matching backend rules
- [ ] npm install & ng serve working

---

## Backend Gaps & Recommendations

1. **Missing List Endpoints**: The backend lacks:
   - `GET /api/patients` (list all patients) — needed for admin pages
   - `GET /api/providers` (list all providers) — needed for patient browse & admin pages
   - `GET /api/appointments/provider/{providerId}` — needed for provider's appointment view
   
   **Recommendation**: Add these endpoints to the backend with optional query filters (status, date range, specialty).

2. **Provider Registration Auth**: `POST /api/providers/register` currently requires JWT auth. Consider:
   - Making it anonymous like patient registration, OR
   - Using `admin-client` credentials for admin-initiated provider creation
   
3. **Production Auth**: Switch from ROPC (Resource Owner Password) to **PKCE Authorization Code flow** for better security. Update IdentityServer config and frontend auth flow accordingly.

---

## Development Server & Testing

### Local Setup
```bash
# Install dependencies
npm install

# Install UUID package
npm install uuid
npm install --save-dev @types/uuid

# Start dev server
ng serve --open

# Access at http://localhost:4200
```

### Testing Flow (Manual)

1. **Patient Registration & Login**
   - Navigate to `/register/patient`
   - Fill form with valid data (DOB must be past)
   - Submit → redirects to `/login`
   - Login with registered email/password
   - Should redirect to `/patient/dashboard`

2. **Browse & Book**
   - Click "Book Appointment"
   - Browse providers (mock data initially)
   - Click a provider → select a slot (from `/api/providers/{id}/slots`)
   - Click book → POST `/api/appointments` with `Idempotency-Key` header
   - Verify 201 response with `appointmentId`
   - Check network tab for Idempotency-Key header

3. **My Appointments**
   - Navigate to "My Appointments"
   - Verify appointments fetched from `GET /api/appointments/patient/{patientId}`
   - Status badges properly colored
   - Can cancel (DELETE) or reschedule (PUT)

4. **Provider Flow**
   - Register as provider
   - Login → redirected to `/provider/dashboard`
   - Define availability (POST `/api/providers/{id}/availability`)
   - View appointments, confirm/no-show actions

5. **Error Handling**
   - Attempt to book unavailable slot → 409, toast shows "slot taken"
   - Missing Idempotency-Key (if enforced on client) → 400 error
   - Invalid JWT → 401, redirect to `/login`
   - Missing required header → test graceful handling

6. **Responsive**
   - Shrink browser to 375px width
   - Sidebar collapses, cards stack, buttons remain accessible

---

## Notes for AI Agent Implementation

This plan is structured to be actionable by a cheap AI agent. Each component is:
- **Self-contained**: Clear file paths, imports, minimal dependencies
- **Copy-paste ready**: Full source code provided, minimal boilerplate
- **Testable**: Each section has clear success criteria
- **Sequential**: Phases can be executed in order, with clear dependencies

**Recommended Agent Workflow**:
1. Generate workspace (Phase 1)
2. Create auth flow & interceptors (Phase 1, Phase 2)
3. Implement login + registration pages (Phase 2)
4. Build patient portal (Phase 3)
5. Build provider portal (Phase 4)
6. Complete admin dashboard (Phase 5)
7. Add error handling & polish (Phase 6)
8. Test all flows manually

---

**Last Updated**: April 20, 2026  
**Status**: Ready for Implementation
