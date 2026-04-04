# HealthBooking — Product Overview & Angular Integration Guide

> For Sales Teams, Business Developers, and Front-End Engineers

---

## Table of Contents

**Part A — Business & Product**
1. [What Is HealthBooking?](#1-what-is-healthbooking)
2. [Who Is It For?](#2-who-is-it-for)
3. [Core Capabilities](#3-core-capabilities)
4. [Key Differentiators](#4-key-differentiators)
5. [Integration & White-Label Options](#5-integration--white-label-options)
6. [Data, Privacy & Compliance](#6-data-privacy--compliance)
7. [Deployment Options](#7-deployment-options)
8. [SLA & Reliability Story](#8-sla--reliability-story)

**Part B — Angular Integration Guide**
9. [Integration Architecture](#9-integration-architecture)
10. [Authentication (Login with IdentityServer)](#10-authentication-login-with-identityserver)
11. [Angular Project Setup](#11-angular-project-setup)
12. [API Service Layer](#12-api-service-layer)
13. [Patient Registration & Login](#13-patient-registration--login)
14. [Browsing Providers & Slots](#14-browsing-providers--slots)
15. [Booking an Appointment](#15-booking-an-appointment)
16. [Managing Appointments](#16-managing-appointments)
17. [Error Handling & UX Guidance](#17-error-handling--ux-guidance)
18. [Security Best Practices for the SPA](#18-security-best-practices-for-the-spa)
19. [Environment Configuration](#19-environment-configuration)
20. [Sample Component Snippets](#20-sample-component-snippets)

---

# Part A — Business & Product

---

## 1. What Is HealthBooking?

**HealthBooking** is a **ready-to-deploy, API-first healthcare appointment management platform**. It provides the complete back-end infrastructure for:

- Patient self-registration and profile management
- Provider (doctor / specialist) calendaring and slot management
- Real-time appointment booking with double-booking prevention
- Automated email notifications on booking and cancellation
- A secure API gateway as the single integration point

The platform is built on an **enterprise microservices architecture** (.NET 9) and is designed to be consumed by any front-end — Angular, React, iOS, Android, or third-party integrations via REST.

---

## 2. Who Is It For?

### Healthcare Providers & Clinics
Clinics, specialist practices, and multi-location hospital groups that need to offer online booking for patients without building back-end infrastructure from scratch.

### Digital Health Startups
Startups building a patient-facing mobile or web application who want a proven, scalable booking back-end so they can focus on UX.

### Hospital IT / System Integrators
Teams integrating a new patient portal into an existing HIS (Hospital Information System). HealthBooking provides clean REST and gRPC APIs that fit alongside existing systems.

### SaaS Builders
Organisations building a healthcare scheduling SaaS product who need a multi-tenant-ready (with minor extension) booking engine as a foundation.

---

## 3. Core Capabilities

### Patient Management
- Patient self-registration with email verification
- Secure profile management (name, contact, date of birth)
- Token-based login (OAuth2 / OpenID Connect)
- `/api/patients/me` — instant profile fetch from JWT

### Provider Management
- Provider (doctor/specialist) registration
- Daily availability scheduling — define open hours, system generates 30-minute slots automatically
- Real-time slot availability browsing with sub-second response (Redis-cached)

### Appointment Booking
- **Guaranteed no double-booking** — optimistic concurrency locks slots atomically during booking
- **Idempotent booking** — submitting the same booking request twice returns the same appointment (no accidental duplicates)
- **Saga-based booking flow** — if any step (patient lookup, slot lock, record creation) fails, the system automatically compensates and releases any reserved resources
- Patient appointment history — full list per patient

### Appointment Management
- Cancel appointment with reason
- Cancellation triggers automatic patient notification

### Notifications
- Automatic email on booking confirmation
- Automatic email on cancellation
- (Extensible: add SMS, push notifications without changing the booking flow)

### Security & Compliance
- OAuth2 / OpenID Connect (Duende IdentityServer)
- JWT tokens with configurable scopes per role
- Rate limiting (300 requests/minute) to prevent abuse
- All requests traceable with correlation IDs
- Audit trail: every entity records `createdAt`, `createdBy`, `modifiedAt`, `modifiedBy`

### Observability
- Distributed tracing across all services (Jaeger / any OpenTelemetry backend)
- Structured JSON logs (compatible with Elasticsearch, Datadog, Azure Monitor)
- Health check endpoints for monitoring and alerting

---

## 4. Key Differentiators

| Feature | HealthBooking | Typical Custom Build |
|---|---|---|
| **No double-booking** | Atomic slot locking with database-level concurrency control | Requires complex custom logic |
| **Idempotent API** | Built-in `Idempotency-Key` support on every booking | Usually missed, causes support tickets |
| **Saga with compensation** | Automatic rollback if booking partially fails | Usually missing; leaves system in inconsistent state |
| **Audit trail** | Every entity records who created/modified it | Often bolted on late |
| **Email notifications** | Pluggable email provider, fires automatically | Custom per project |
| **Redis-cached slot browsing** | Sub-millisecond slot list reads | Full DB query on every request |
| **Distributed tracing** | End-to-end request visible in Jaeger | No visibility across service calls |
| **API-first design** | Any SPA, mobile app, or third-party can integrate | Tightly coupled to one front-end |
| **Clean, extensible architecture** | Add a new service without touching existing ones | Spaghetti gets worse over time |

---

## 5. Integration & White-Label Options

### REST API Integration
Any client with HTTP capability can integrate. The API Gateway at port 5000 is the only entry point:

```
https://api.yourdomain.com/api/patients/...
https://api.yourdomain.com/api/providers/...
https://api.yourdomain.com/api/appointments/...
```

All responses are standard JSON. All errors return RFC 7807 Problem Details format.

### White-Label SPA
Deliver the Angular front-end (see Part B) with your clinic's branding — logo, colours, fonts. The back-end is untouched.

### Third-Party System Integration
Integrate with your existing HIS, EMR, or CRM via:
- REST API calls authenticated with the `admin-client` credentials
- RabbitMQ event subscriptions (add a new consumer service that listens to `V1_AppointmentBookedEvent` and pushes to your system)

### Webhook Delivery (Extension Point)
Add a "WebhookDeliveryService" that consumes appointment events from RabbitMQ and performs HTTP POST to registered webhook endpoints — no changes to existing services required.

---

## 6. Data, Privacy & Compliance

### Data Stored

| Data Type | Where Stored | Retention |
|---|---|---|
| Patient PII (name, email, phone, DOB) | SQL Server (patients DB) | Configurable; supports soft delete |
| Provider information | SQL Server (providers DB) | As above |
| Appointment records | SQL Server (appointments DB) | Permanent (audit purposes) |
| Notification logs | SQL Server (notifications DB) | Configurable |
| Session tokens | IdentityServer SQL DB | Expire per token TTL |
| Trace data | Jaeger (in-memory or configurable) | Typically 7 days |
| Cache (slot lists) | Redis | 60-second TTL, ephemeral |

### Audit Trail
Every entity in the system records `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy` automatically — no code required in individual handlers.

### GDPR / HIPAA Readiness
- Patient data is isolated in its own database — easier to implement right-to-erasure
- Email normalised at ingestion (lowercase) — no duplicate patient records for email variants
- API access controlled by scopes — patient data readable only with `patient:read` scope
- All timestamps in UTC with timezone offset (`DateTimeOffset`)
- No patient data in log messages (logs contain IDs, not names or emails)

> **Note:** Full GDPR / HIPAA compliance requires additional legal review, encryption at rest (SQL Server TDE), and HTTPS enforcement for all external traffic.

---

## 7. Deployment Options

### Option 1: Docker Compose (Dev / Staging)
```bash
docker compose up -d
```
Entire stack running in minutes. All services, databases, RabbitMQ, Redis, and Jaeger in one command.

### Option 2: Kubernetes (Production)
Each service has a Docker image and health check endpoints (`/health/live`, `/health/ready`) — standard K8s liveness/readiness probes. Helm charts can be generated from the existing Docker Compose configuration.

### Option 3: Cloud PaaS
Each service is a standard .NET 9 application. Can run on:
- **Azure**: Azure Container Apps or AKS; Azure SQL; Azure Cache for Redis; Azure Service Bus (replace RabbitMQ with 1 config change)
- **AWS**: ECS Fargate; RDS SQL Server; ElastiCache; Amazon MQ
- **GCP**: Cloud Run; Cloud SQL; Memorystore; Pub/Sub

### Option 4: Hybrid
Run infrastructure (SQL Server, RabbitMQ, Redis) on managed cloud services; run application containers on premises or in a private cloud.

---

## 8. SLA & Reliability Story

| Concern | How It's Addressed |
|---|---|
| **Slot contention (race conditions)** | Optimistic concurrency at DB level — only one booking wins; others get a clear error |
| **Service goes down during booking** | Saga persists to database — resumes from last committed step on restart |
| **RabbitMQ temporarily unavailable** | Outbox Pattern — events are in the DB, retried when RabbitMQ recovers |
| **Downstream service slow** | Polly circuit breaker opens after repeated failures — fails fast instead of waiting |
| **Slow network / client retry** | Idempotency keys — client retries safely, same result returned |
| **Abuse / scraping** | Rate limiter: 300 req/min per client at gateway level |
| **Silent failures** | Health checks + OpenTelemetry traces expose failures within seconds |

---

# Part B — Angular Integration Guide

---

## 9. Integration Architecture

```
Angular SPA (Browser)
        │
        │  HTTPS (REST + JSON)
        ▼
API Gateway  http://api.yourdomain.com  (:5000)
        │
        │  JWT Bearer token required for most routes
        ├─▶ /api/patients/**   → PatientService
        ├─▶ /api/providers/**  → ProviderService
        └─▶ /api/appointments/** → AppointmentService

IdentityServer  http://auth.yourdomain.com  (:5005)
        │
        ├─▶ /connect/token           (get JWT)
        ├─▶ /connect/userinfo        (get profile claims)
        └─▶ /.well-known/openid-configuration  (discovery)
```

---

## 10. Authentication (Login with IdentityServer)

### Grant Type for Angular SPA

For **current setup** (development): Resource Owner Password grant  
For **production**: Migrate to **Authorization Code + PKCE** (more secure for SPAs)

### Getting a Token (ROPC — current)

```typescript
// auth.service.ts
async login(username: string, password: string): Promise<void> {
  const body = new URLSearchParams({
    grant_type:    'password',
    client_id:     'patient-spa',
    client_secret: 'patient-spa-secret',
    username,
    password,
    scope:         'openid profile email healthbooking-api',
  });

  const response = await fetch('http://localhost:5005/connect/token', {
    method:  'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body:    body.toString(),
  });

  if (!response.ok) throw new Error('Login failed');

  const tokens = await response.json();
  // { access_token, refresh_token, expires_in, token_type }
  this.storeTokens(tokens);
}
```

### Token Storage — Security Note

**Do NOT store tokens in `localStorage`** (vulnerable to XSS).  
**Recommended:** Keep `access_token` in memory; store `refresh_token` in an `HttpOnly` cookie served by your own BFF (Backend-for-Frontend), or use a secure cookie from IdentityServer.

For this guide we store in memory for simplicity:

```typescript
private accessToken: string | null = null;

storeTokens(tokens: any): void {
  this.accessToken = tokens.access_token;
  // Schedule refresh: tokens.expires_in seconds
}

getAccessToken(): string | null {
  return this.accessToken;
}
```

### HTTP Interceptor

```typescript
// jwt.interceptor.ts
import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

export const jwtInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.getAccessToken();

  if (token) {
    req = req.clone({
      setHeaders: { Authorization: `Bearer ${token}` }
    });
  }

  return next(req);
};
```

Register in `app.config.ts`:

```typescript
provideHttpClient(withInterceptors([jwtInterceptor]))
```

---

## 11. Angular Project Setup

### Install dependencies

```bash
ng new health-booking-app --routing --style scss
cd health-booking-app
npm install                 # no extra packages required beyond Angular 17+ HttpClient
```

### Environment files

```typescript
// src/environments/environment.ts  (local dev)
export const environment = {
  production:        false,
  apiBaseUrl:        'http://localhost:5000',
  identityServerUrl: 'http://localhost:5005',
  clientId:          'patient-spa',
  clientSecret:      'patient-spa-secret',   // move to server-side BFF in production
};

// src/environments/environment.prod.ts
export const environment = {
  production:        true,
  apiBaseUrl:        'https://api.yourdomain.com',
  identityServerUrl: 'https://auth.yourdomain.com',
  clientId:          'patient-spa',
  clientSecret:      '',  // PKCE — no client secret needed
};
```

### Recommended project structure

```
src/
├── app/
│   ├── core/
│   │   ├── auth/
│   │   │   ├── auth.service.ts
│   │   │   ├── auth.guard.ts
│   │   │   └── jwt.interceptor.ts
│   │   └── api/
│   │       ├── patient.service.ts
│   │       ├── provider.service.ts
│   │       └── appointment.service.ts
│   ├── features/
│   │   ├── registration/
│   │   ├── login/
│   │   ├── providers/
│   │   ├── booking/
│   │   └── appointments/
│   └── shared/
│       └── models/
│           ├── patient.model.ts
│           ├── provider.model.ts
│           ├── slot.model.ts
│           └── appointment.model.ts
```

---

## 12. API Service Layer

### Models (`src/app/shared/models/`)

```typescript
// patient.model.ts
export interface Patient {
  id:               string;
  firstName:        string;
  lastName:         string;
  contactEmail:     string;
  phoneNumber:      string;
  dateOfBirth:      string;    // ISO 8601 date
  registrationDate: string;    // ISO 8601 datetime
}

// provider.model.ts
export interface Provider {
  id:            string;
  firstName:     string;
  lastName:      string;
  specialty:     string;
  licenseNumber: string;
}

// slot.model.ts
export interface Slot {
  id:         string;
  providerId: string;
  date:       string;       // "YYYY-MM-DD"
  startTime:  string;       // "HH:mm:ss"
  endTime:    string;
  status:     'Available' | 'Locked' | 'Booked' | 'Cancelled';
}

// appointment.model.ts
export interface Appointment {
  id:          string;
  patientId:   string;
  slotId:      string;
  patientName: string;
  status:      'Booked' | 'Cancelled' | 'Completed';
  cancelledAt?: string;
  cancellationReason?: string;
}
```

### PatientService (`core/api/patient.service.ts`)

```typescript
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Patient } from '../../shared/models/patient.model';

@Injectable({ providedIn: 'root' })
export class PatientApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/patients`;

  register(payload: {
    firstName: string; lastName: string; email: string;
    phoneNumber: string; dateOfBirth: string;
  }): Observable<{ patientId: string }> {
    return this.http.post<{ patientId: string }>(`${this.base}/register`, payload);
  }

  getMe(): Observable<Patient> {
    return this.http.get<Patient>(`${this.base}/me`);
  }

  getById(id: string): Observable<Patient> {
    return this.http.get<Patient>(`${this.base}/${id}`);
  }

  updateProfile(id: string, payload: {
    firstName: string; lastName: string; phoneNumber: string;
  }): Observable<void> {
    return this.http.put<void>(`${this.base}/${id}`, payload);
  }
}
```

### ProviderApiService (`core/api/provider.service.ts`)

```typescript
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Provider } from '../../shared/models/provider.model';
import { Slot } from '../../shared/models/slot.model';

@Injectable({ providedIn: 'root' })
export class ProviderApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/providers`;

  getById(id: string): Observable<Provider> {
    return this.http.get<Provider>(`${this.base}/${id}`);
  }

  getSlots(providerId: string): Observable<Slot[]> {
    return this.http.get<Slot[]>(`${this.base}/${providerId}/slots`);
  }
}
```

### AppointmentApiService (`core/api/appointment.service.ts`)

```typescript
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Appointment } from '../../shared/models/appointment.model';

@Injectable({ providedIn: 'root' })
export class AppointmentApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/appointments`;

  book(slotId: string): Observable<{ appointmentId: string }> {
    const idempotencyKey = crypto.randomUUID();
    return this.http.post<{ appointmentId: string }>(this.base,
      { slotId },
      { headers: { 'Idempotency-Key': idempotencyKey } }
    );
  }

  // Safe retry: re-use the same key if the previous attempt may have succeeded
  bookWithKey(slotId: string, idempotencyKey: string): Observable<{ appointmentId: string }> {
    return this.http.post<{ appointmentId: string }>(this.base,
      { slotId },
      { headers: { 'Idempotency-Key': idempotencyKey } }
    );
  }

  getById(id: string): Observable<Appointment> {
    return this.http.get<Appointment>(`${this.base}/${id}`);
  }

  getForPatient(patientId: string): Observable<Appointment[]> {
    return this.http.get<Appointment[]>(`${this.base}/patient/${patientId}`);
  }

  cancel(id: string, reason: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`, { body: { reason } });
  }
}
```

---

## 13. Patient Registration & Login

### Registration Component

```typescript
// features/registration/register.component.ts
import { Component, inject } from '@angular/core';
import { FormBuilder, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { PatientApiService } from '../../core/api/patient.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()">
      <input formControlName="firstName"  placeholder="First Name" />
      <input formControlName="lastName"   placeholder="Last Name" />
      <input formControlName="email"      placeholder="Email" type="email" />
      <input formControlName="phone"      placeholder="Phone (+447911...)" />
      <input formControlName="dob"        type="date" />
      <button type="submit" [disabled]="form.invalid || loading">Register</button>
      <p *ngIf="error">{{ error }}</p>
    </form>
  `
})
export class RegisterComponent {
  private readonly fb      = inject(FormBuilder);
  private readonly patient = inject(PatientApiService);
  private readonly router  = inject(Router);

  loading = false;
  error   = '';

  form = this.fb.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName:  ['', [Validators.required, Validators.maxLength(100)]],
    email:     ['', [Validators.required, Validators.email]],
    phone:     ['', Validators.required],
    dob:       ['', Validators.required],
  });

  submit(): void {
    if (this.form.invalid) return;
    this.loading = true;
    const v = this.form.value;

    this.patient.register({
      firstName:   v.firstName!,
      lastName:    v.lastName!,
      email:       v.email!,
      phoneNumber: v.phone!,
      dateOfBirth: v.dob!,
    }).subscribe({
      next:  ()  => this.router.navigate(['/login']),
      error: err => {
        this.loading = false;
        this.error = err.status === 400
          ? 'Please check your details.'
          : 'Registration failed. Please try again.';
      }
    });
  }
}
```

### Login Component

```typescript
// features/login/login.component.ts
import { Component, inject } from '@angular/core';
import { FormBuilder, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()">
      <input formControlName="email"    placeholder="Email" type="email" />
      <input formControlName="password" placeholder="Password" type="password" />
      <button type="submit" [disabled]="form.invalid || loading">Log In</button>
      <p *ngIf="error">{{ error }}</p>
    </form>
  `
})
export class LoginComponent {
  private readonly fb     = inject(FormBuilder);
  private readonly auth   = inject(AuthService);
  private readonly router = inject(Router);

  loading = false;
  error   = '';

  form = this.fb.group({
    email:    ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.loading = true;
    try {
      await this.auth.login(this.form.value.email!, this.form.value.password!);
      this.router.navigate(['/appointments']);
    } catch {
      this.error = 'Invalid credentials.';
    } finally {
      this.loading = false;
    }
  }
}
```

---

## 14. Browsing Providers & Slots

### Providers List Page

```typescript
// features/providers/providers.component.ts
import { Component, inject, OnInit, signal } from '@angular/core';
import { ProviderApiService } from '../../core/api/provider.service';
import { Slot } from '../../shared/models/slot.model';

@Component({
  selector: 'app-providers',
  standalone: true,
  template: `
    <div *ngFor="let slot of slots()">
      <p>{{ slot.date }} {{ slot.startTime }} – {{ slot.endTime }}</p>
      <button [disabled]="slot.status !== 'Available'" (click)="selectSlot(slot)">
        {{ slot.status === 'Available' ? 'Book' : slot.status }}
      </button>
    </div>
  `
})
export class ProviderSlotsComponent implements OnInit {
  private readonly providerApi = inject(ProviderApiService);
  slots = signal<Slot[]>([]);

  selectedProviderId = 'your-provider-id-here';  // route param in real app

  ngOnInit(): void {
    this.providerApi.getSlots(this.selectedProviderId).subscribe(
      slots => this.slots.set(slots.filter(s => s.status === 'Available'))
    );
  }

  selectSlot(slot: Slot): void {
    // Navigate to booking confirmation page with slotId
  }
}
```

---

## 15. Booking an Appointment

### The Idempotency Key Strategy

The booking API requires an `Idempotency-Key` header. This protects against network errors causing duplicate bookings:

```
Strategy A — New key per click:
  User clicks "Book"  →  generate fresh UUID  →  POST
  If network error    →  show retry button
  User retries        →  generate ANOTHER UUID  →  POST (new booking attempt)

Strategy B — Persist key across retries (recommended):
  User clicks "Book"  →  generate UUID + store in component state
  If network error    →  show retry button
  User retries        →  reuse SAME UUID  →  POST (server returns same result)
```

```typescript
// features/booking/booking.component.ts
import { Component, inject, Input, signal } from '@angular/core';
import { AppointmentApiService } from '../../core/api/appointment.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-booking',
  standalone: true,
  template: `
    <h2>Confirm your appointment</h2>
    <p>Slot: {{ slotId }}</p>
    <button (click)="book()" [disabled]="loading()">
      {{ loading() ? 'Booking...' : 'Confirm Booking' }}
    </button>
    <p *ngIf="error()">{{ error() }}</p>
  `
})
export class BookingComponent {
  @Input() slotId = '';

  private readonly appointmentApi = inject(AppointmentApiService);
  private readonly router         = inject(Router);

  // Generate once per booking session; persist for retries
  private readonly idempotencyKey = crypto.randomUUID();

  loading = signal(false);
  error   = signal('');

  book(): void {
    this.loading.set(true);
    this.error.set('');

    this.appointmentApi.bookWithKey(this.slotId, this.idempotencyKey).subscribe({
      next: ({ appointmentId }) => {
        this.router.navigate(['/appointments', appointmentId]);
      },
      error: err => {
        this.loading.set(false);
        if (err.status === 409) {
          this.error.set('This slot is no longer available. Please choose another.');
        } else if (err.status === 503) {
          this.error.set('Booking service is temporarily unavailable. Please retry.');
        } else {
          this.error.set('An error occurred. Please try again.');
        }
      }
    });
  }
}
```

### HTTP Status → UX Mapping

| HTTP Status | Meaning | Recommended UX |
|---|---|---|
| `201 Created` | Booking confirmed | Navigate to confirmation page |
| `200 OK` | Duplicate key — same booking already exists | Show existing confirmation |
| `400 Bad Request` | Invalid request body or missing Idempotency-Key | Show validation error |
| `401 Unauthorized` | JWT expired or missing | Redirect to login |
| `409 Conflict` | Slot was just locked by another user | Show "slot unavailable" message; refresh slot list |
| `503 Service Unavailable` | Saga timed out (>30 s) | Show retry button |

---

## 16. Managing Appointments

### Appointment List Page

```typescript
// features/appointments/appointments.component.ts
import { Component, inject, OnInit, signal } from '@angular/core';
import { AppointmentApiService } from '../../core/api/appointment.service';
import { PatientApiService } from '../../core/api/patient.service';
import { Appointment } from '../../shared/models/appointment.model';

@Component({
  selector: 'app-appointments',
  standalone: true,
  template: `
    <h1>My Appointments</h1>
    <div *ngFor="let appt of appointments()">
      <h3>Appointment on {{ appt.slotId }}</h3>
      <p>Status: {{ appt.status }}</p>
      <button *ngIf="appt.status === 'Booked'" (click)="cancel(appt)">
        Cancel
      </button>
    </div>
    <p *ngIf="appointments().length === 0">No appointments found.</p>
  `
})
export class AppointmentsComponent implements OnInit {
  private readonly appointmentApi = inject(AppointmentApiService);
  private readonly patientApi     = inject(PatientApiService);

  appointments = signal<Appointment[]>([]);
  patientId    = '';

  async ngOnInit(): Promise<void> {
    const me = await this.patientApi.getMe().toPromise();
    this.patientId = me!.id;
    this.appointmentApi.getForPatient(this.patientId).subscribe(
      list => this.appointments.set(list)
    );
  }

  cancel(appt: Appointment): void {
    const reason = prompt('Reason for cancellation?');
    if (!reason) return;

    this.appointmentApi.cancel(appt.id, reason).subscribe({
      next: () => {
        this.appointments.update(list =>
          list.map(a => a.id === appt.id ? { ...a, status: 'Cancelled' } : a)
        );
      },
      error: () => alert('Could not cancel. Please try again.')
    });
  }
}
```

---

## 17. Error Handling & UX Guidance

### Global Error Interceptor

```typescript
// core/http-error.interceptor.ts
import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth/auth.service';

export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const auth   = inject(AuthService);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401) {
        auth.logout();
        router.navigate(['/login']);
      }
      if (error.status === 429) {
        // Rate limited — show a "too many requests" toast
        console.warn('Rate limit reached. Please slow down.');
      }
      return throwError(() => error);
    })
  );
};
```

Register alongside `jwtInterceptor` in `app.config.ts`.

### Problem Details Format

All `4xx` / `5xx` responses from the API follow RFC 7807:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": ["'Email' is not a valid email address."]
  }
}
```

Parse `error.error.errors` in your Angular `HttpErrorResponse` handler to show field-level validation messages.

---

## 18. Security Best Practices for the SPA

| Risk | Mitigation |
|---|---|
| **XSS steals JWT** | Store `access_token` in memory only; use `HttpOnly` cookies for `refresh_token` |
| **Clickjacking** | Configure `X-Frame-Options: DENY` on the gateway (add in YARP headers transform) |
| **CSRF** | Not a risk for token-based APIs; no session cookies |
| **Secret exposure** | Do NOT embed `client_secret` in production SPA — migrate to PKCE Authorization Code flow |
| **Token replay after logout** | IdentityServer supports reference tokens or short-lived JWTs + refresh token rotation |
| **Over-fetching sensitive data** | Use `/api/patients/me` instead of hardcoding patient IDs in URLs |
| **Exposing stack traces** | API returns Problem Details only; no stack traces in `title`/`detail` fields |

### Production Auth Upgrade (PKCE)

For production, replace the Resource Owner Password grant with **Authorization Code + PKCE**:

1. Change IdentityServer client `AllowedGrantTypes` to `GrantTypes.Code`
2. Add `RedirectUris` to the client configuration
3. Use `angular-oauth2-oidc` or Auth.js in Angular:

```bash
npm install angular-oauth2-oidc
```

```typescript
// app.config.ts
import { AuthConfig, OAuthModule } from 'angular-oauth2-oidc';

export const authConfig: AuthConfig = {
  issuer:                 'http://localhost:5005',
  redirectUri:            window.location.origin + '/callback',
  clientId:               'patient-spa',
  responseType:           'code',
  scope:                  'openid profile email healthbooking-api',
  useSilentRefresh:       true,
  silentRefreshTimeout:   5000,
  requireHttps:           false,   // true in production
};
```

---

## 19. Environment Configuration

### CORS

Add CORS configuration in each service's `Program.cs` to allow requests from the Angular dev server:

```csharp
// In PatientService, ProviderService, AppointmentService Program.cs
builder.Services.AddCors(opts =>
    opts.AddPolicy("spa", pol => pol
        .WithOrigins("http://localhost:4200")  // Angular dev server
        .AllowAnyMethod()
        .AllowAnyHeader()));

// In app.Use... pipeline:
app.UseCors("spa");
```

In production, replace `http://localhost:4200` with your actual SPA domain.

### Angular Proxy (Development)

Avoid CORS issues during development by proxying through Angular's dev server:

```json
// proxy.conf.json
{
  "/api": {
    "target": "http://localhost:5000",
    "secure": false,
    "changeOrigin": true
  }
}
```

```json
// angular.json → "serve" → "options"
"proxyConfig": "proxy.conf.json"
```

Then use `/api/...` in Angular without the full gateway URL.

---

## 20. Sample Component Snippets

### Auth Guard

```typescript
// core/auth/auth.guard.ts
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => {
  const auth   = inject(AuthService);
  const router = inject(Router);
  if (auth.isLoggedIn()) return true;
  return router.createUrlTree(['/login']);
};
```

```typescript
// app.routes.ts
import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: 'register',    loadComponent: () => import('./features/registration/register.component').then(m => m.RegisterComponent) },
  { path: 'login',       loadComponent: () => import('./features/login/login.component').then(m => m.LoginComponent) },
  { path: 'providers',   loadComponent: () => import('./features/providers/providers.component').then(m => m.ProviderSlotsComponent), canActivate: [authGuard] },
  { path: 'book/:slotId',loadComponent: () => import('./features/booking/booking.component').then(m => m.BookingComponent), canActivate: [authGuard] },
  { path: 'appointments',loadComponent: () => import('./features/appointments/appointments.component').then(m => m.AppointmentsComponent), canActivate: [authGuard] },
  { path: '',            redirectTo: '/appointments', pathMatch: 'full' },
];
```

### Quick Start Checklist for Angular Developers

- [ ] Copy `proxy.conf.json` and configure Angular dev proxy
- [ ] Create `environment.ts` with correct `apiBaseUrl` and `identityServerUrl`
- [ ] Add `jwtInterceptor` and `httpErrorInterceptor` to `provideHttpClient`
- [ ] Implement `AuthService.login()` using ROPC grant (dev) or PKCE (prod)
- [ ] Store `access_token` in memory — NOT `localStorage`
- [ ] Use `crypto.randomUUID()` for `Idempotency-Key` — generate once per booking, reuse on retry
- [ ] Map HTTP `409` on booking → "slot unavailable, choose another" message
- [ ] Map HTTP `503` on booking → "retry" button (saga timed out)
- [ ] Add CORS configuration to API services for your origin
- [ ] Validate forms client-side before API calls; parse `error.error.errors` for server-side validation messages
