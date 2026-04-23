# HealthBooking System: API Endpoints Complete Guide

**Target Audience**: Frontend developers (Angular team)  
**Purpose**: Complete reference for all backend endpoints, request formats, and expected responses  
**Last Updated**: April 20, 2026

---

## Table of Contents

1. [Authentication Endpoints](#authentication-endpoints)
2. [Patient Endpoints](#patient-endpoints)
3. [Provider Endpoints](#provider-endpoints)
4. [Appointment Endpoints](#appointment-endpoints)
5. [Common Response Patterns](#common-response-patterns)
6. [Error Handling](#error-handling)
7. [Headers & Authentication](#headers--authentication)

---

## Authentication Endpoints

### Base URL
```
http://localhost:5005  (IdentityServer)
```

### 1. Login (Obtain Access Token)

**Endpoint**: `POST /connect/token`

**Purpose**: Exchange user credentials for JWT access token and refresh token

**Authentication**: None (public endpoint)

**Request Headers**:
```
Content-Type: application/x-www-form-urlencoded
```

**Request Body** (form-encoded):
```
grant_type=password
&username=john@example.com
&password=mypassword123
&client_id=patient-spa
&client_secret=patient-spa-secret
&scope=openid profile email offline_access healthbooking-api
```

**Frontend Example (Angular HttpClient)**:
```typescript
// auth.service.ts
login(username: string, password: string): Observable<TokenResponse> {
  const body = new FormData();
  body.append('grant_type', 'password');
  body.append('username', username);
  body.append('password', password);
  body.append('client_id', 'patient-spa');
  body.append('client_secret', 'patient-spa-secret');
  body.append('scope', 'openid profile email offline_access healthbooking-api');

  return this.http.post<TokenResponse>(
    'http://localhost:5005/connect/token',
    body
  ).pipe(
    tap(response => {
      // Store tokens in localStorage
      localStorage.setItem('access_token', response.access_token);
      localStorage.setItem('refresh_token', response.refresh_token);
      localStorage.setItem('expires_in', response.expires_in.toString());
      localStorage.setItem('login_time', Date.now().toString());
    })
  );
}
```

**Success Response (200 OK)**:
```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6IkE2NjAxOTAzQ0Y4NDcyMkIxNThDMjVENTU2NzAwNzYzIiwidHlwIjoiYXQrand0In0.eyJzdWIiOiI1NTBlODQwMC1lMjliLTQxZDQtYTcxNi00NDY2NTU0NDAwMDAiLCJlbWFpbCI6ImpvaG5AZXhhbXBsZS5jb20iLCJuYW1lIjoiSm9obiBEb2UiLCJzY29wZSI6Im9wZW5pZCBwcm9maWxlIGVtYWlsIGhlYWx0aGJvb2tpbmctYXBpIiwiYXVkIjoiaGVhbHRoYm9va2luZy1hcGkiLCJleHAiOjE3MTM2MDcyMDAsImlzcyI6Imh0dHA6Ly9sb2NhbGhvc3Q6NTAwNSIsImlhdCI6MTcxMzYwMzYwMH0.signature_here",
  "refresh_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6IkE2NjAxOTAzQ0Y4NDcyMkIxNThDMjVENTU2NzAwNzYzIiwidHlwIjoiYXRrand0In0.eyJzdWIiOiI1NTBlODQwMC1lMjliLTQxZDQtYTcxNi00NDY2NTU0NDAwMDAiLCJleHAiOjE3MTQyMDg0MDB9.signature_here",
  "expires_in": 3600,
  "token_type": "Bearer"
}
```

**Error Response (400 Bad Request)**:
```json
{
  "error": "invalid_grant",
  "error_description": "Invalid username or password"
}
```

**Common Errors**:
| Error | Reason | Solution |
|-------|--------|----------|
| `invalid_grant` | Wrong username/password | Verify credentials |
| `invalid_client` | Invalid client_id/secret | Check config |
| `invalid_scope` | Scope not allowed | Verify scope values |

---

### 2. Refresh Access Token

**Endpoint**: `POST /connect/token`

**Purpose**: Get a new access token using refresh token (when old one expires)

**Authentication**: None (public endpoint)

**Request Body** (form-encoded):
```
grant_type=refresh_token
&refresh_token=eyJhbGciOiJSUzI1NiI...
&client_id=patient-spa
&client_secret=patient-spa-secret
```

**Frontend Example (Angular)**:
```typescript
// auth.service.ts - Called automatically by interceptor
refreshAccessToken(): Observable<TokenResponse> {
  const refreshToken = localStorage.getItem('refresh_token');
  
  const body = new FormData();
  body.append('grant_type', 'refresh_token');
  body.append('refresh_token', refreshToken);
  body.append('client_id', 'patient-spa');
  body.append('client_secret', 'patient-spa-secret');

  return this.http.post<TokenResponse>(
    'http://localhost:5005/connect/token',
    body
  ).pipe(
    tap(response => {
      localStorage.setItem('access_token', response.access_token);
      localStorage.setItem('refresh_token', response.refresh_token);
      localStorage.setItem('expires_in', response.expires_in.toString());
      localStorage.setItem('login_time', Date.now().toString());
    })
  );
}
```

**Success Response (200 OK)**:
```json
{
  "access_token": "eyJhbGciOiJSUzI1NiI...",
  "refresh_token": "eyJhbGciOiJSUzI1NiI...",
  "expires_in": 3600,
  "token_type": "Bearer"
}
```

---

## Patient Endpoints

### Base URL
```
http://localhost:5000/api  (API Gateway routes to PatientService)
```

### 1. Register Patient

**Endpoint**: `POST /patients/register`

**Purpose**: Create new patient account

**Authentication**: None (public endpoint)

**Request Headers**:
```
Content-Type: application/json
X-Correlation-Id: <unique-uuid>  (optional, but recommended)
```

**Request Body**:
```json
{
  "firstName": "John",
  "lastName": "Doe",
  "email": "john@example.com",
  "phoneNumber": "+12125551234",
  "dateOfBirth": "1990-05-15",
  "password": "MySecurePass123!"
}
```

**Validation Rules**:
| Field | Validation |
|-------|-----------|
| firstName | Required, 1-100 chars, letters and spaces only |
| lastName | Required, 1-100 chars, letters and spaces only |
| email | Required, valid email format, max 255 chars |
| phoneNumber | Required, E.164 format (7-15 digits) |
| dateOfBirth | Required, must be past date, 18+ years old |
| password | Required, 8+ chars, 1 uppercase, 1 digit, 1 special char |

**Frontend Example (Angular)**:
```typescript
// patient.service.ts
registerPatient(request: RegisterPatientRequest): Observable<RegisterPatientResponse> {
  return this.http.post<RegisterPatientResponse>(
    'http://localhost:5000/api/patients/register',
    request,
    {
      headers: {
        'X-Correlation-Id': this.generateUUID()
      }
    }
  );
}

// Usage in component
this.patientService.registerPatient({
  firstName: this.form.get('firstName').value,
  lastName: this.form.get('lastName').value,
  email: this.form.get('email').value,
  phoneNumber: this.form.get('phoneNumber').value,
  dateOfBirth: this.form.get('dateOfBirth').value,
  password: this.form.get('password').value
}).subscribe({
  next: (response) => {
    console.log('Patient registered:', response.patientId);
    // Redirect to login page
  },
  error: (error) => {
    console.error('Registration failed:', error.error.errors);
  }
});
```

**Success Response (201 Created)**:
```json
{
  "patientId": "550e8400-e29b-41d4-a716-446655440000",
  "email": "john@example.com",
  "firstName": "John",
  "lastName": "Doe",
  "registrationDate": "2026-04-20T14:30:00Z"
}
```

**Error Response (400 Bad Request)**:
```json
{
  "errors": {
    "email": ["A user with this email already exists"],
    "dateOfBirth": ["User must be at least 18 years old"],
    "password": ["Password must contain at least one uppercase letter"]
  }
}
```

**Error Response (409 Conflict)**:
```json
{
  "error": "patient_already_exists",
  "message": "An account with this email already exists"
}
```

---

### 2. Get Current Patient Profile

**Endpoint**: `GET /patients/me`

**Purpose**: Get authenticated patient's profile

**Authentication**: Required ✅
```
Authorization: Bearer <access_token>
```

**Request Parameters**: None

**Frontend Example**:
```typescript
// patient.service.ts
getCurrentPatient(): Observable<PatientDto> {
  return this.http.get<PatientDto>(
    'http://localhost:5000/api/patients/me',
    {
      headers: {
        'Authorization': `Bearer ${this.getAccessToken()}`
      }
    }
  );
}

// In auth.interceptor.ts (intercepts ALL requests automatically)
intercept(req: HttpRequest<any>, next: HttpHandler) {
  const token = localStorage.getItem('access_token');
  if (token && !req.url.includes('/connect/token')) {
    req = req.clone({
      setHeaders: {
        'Authorization': `Bearer ${token}`,
        'X-Correlation-Id': this.generateUUID()
      }
    });
  }
  
  return next.handle(req).pipe(
    catchError(error => {
      if (error.status === 401) {
        // Token expired, refresh it
        return this.authService.refreshAccessToken().pipe(
          switchMap(response => {
            // Retry with new token
            return next.handle(req.clone({
              setHeaders: { 'Authorization': `Bearer ${response.access_token}` }
            }));
          })
        );
      }
      return throwError(() => error);
    })
  );
}
```

**Success Response (200 OK)**:
```json
{
  "patientId": "550e8400-e29b-41d4-a716-446655440000",
  "firstName": "John",
  "lastName": "Doe",
  "email": "john@example.com",
  "phoneNumber": "+12125551234",
  "dateOfBirth": "1990-05-15",
  "registrationDate": "2026-03-01T10:00:00Z"
}
```

**Error Response (401 Unauthorized)**:
```json
{
  "error": "unauthorized",
  "message": "Bearer token is invalid or expired"
}
```

---

### 3. Update Patient Profile

**Endpoint**: `PUT /patients/{patientId}`

**Purpose**: Update patient's profile information

**Authentication**: Required ✅

**URL Parameters**:
| Parameter | Type | Required |
|-----------|------|----------|
| patientId | UUID | Yes |

**Request Body**:
```json
{
  "firstName": "John",
  "lastName": "Smith",
  "phoneNumber": "+12125559999"
}
```

**Frontend Example**:
```typescript
// patient.service.ts
updatePatient(patientId: string, request: UpdatePatientRequest): Observable<PatientDto> {
  return this.http.put<PatientDto>(
    `http://localhost:5000/api/patients/${patientId}`,
    request
  );
}

// Usage
this.patientService.updatePatient(this.patientId, {
  firstName: this.form.get('firstName').value,
  lastName: this.form.get('lastName').value,
  phoneNumber: this.form.get('phoneNumber').value
}).subscribe({
  next: (response) => {
    console.log('Profile updated');
  },
  error: (error) => {
    console.error('Update failed', error);
  }
});
```

**Success Response (200 OK)**:
```json
{
  "patientId": "550e8400-e29b-41d4-a716-446655440000",
  "firstName": "John",
  "lastName": "Smith",
  "email": "john@example.com",
  "phoneNumber": "+12125559999",
  "dateOfBirth": "1990-05-15",
  "registrationDate": "2026-03-01T10:00:00Z"
}
```

**Error Response (404 Not Found)**:
```json
{
  "error": "patient_not_found",
  "message": "Patient with ID 550e8400-e29b-41d4-a716-446655440000 does not exist"
}
```

---

## Provider Endpoints

### Base URL
```
http://localhost:5000/api  (API Gateway routes to ProviderService)
```

### 1. Register Provider

**Endpoint**: `POST /providers/register`

**Purpose**: Create new provider (doctor) account

**Authentication**: None (public endpoint)

**Request Headers**:
```
Content-Type: application/json
X-Correlation-Id: <unique-uuid>
```

**Request Body**:
```json
{
  "firstName": "Dr.",
  "lastName": "Smith",
  "email": "doctor@hospital.com",
  "phoneNumber": "+12125551234",
  "specialization": "Cardiology",
  "licenseNumber": "MD-12345-CA",
  "password": "DocSecurePass123!"
}
```

**Frontend Example**:
```typescript
// provider.service.ts
registerProvider(request: RegisterProviderRequest): Observable<RegisterProviderResponse> {
  return this.http.post<RegisterProviderResponse>(
    'http://localhost:5000/api/providers/register',
    request,
    {
      headers: {
        'X-Correlation-Id': this.generateUUID()
      }
    }
  );
}
```

**Success Response (201 Created)**:
```json
{
  "providerId": "550e8400-e29b-41d4-a716-446655440001",
  "firstName": "Dr.",
  "lastName": "Smith",
  "email": "doctor@hospital.com",
  "specialization": "Cardiology",
  "registrationDate": "2026-04-20T14:30:00Z"
}
```

---

### 2. Get Provider Details

**Endpoint**: `GET /providers/{providerId}`

**Purpose**: Get provider profile (available to patients browsing providers)

**Authentication**: Required (Patient or Provider)

**URL Parameters**:
| Parameter | Type | Required |
|-----------|------|----------|
| providerId | UUID | Yes |

**Frontend Example**:
```typescript
// provider.service.ts
getProvider(providerId: string): Observable<ProviderDto> {
  return this.http.get<ProviderDto>(
    `http://localhost:5000/api/providers/${providerId}`
  );
}

// Usage: Browse providers
ngOnInit() {
  this.providerService.getProvider(this.providerId).subscribe({
    next: (provider) => {
      this.provider = provider;
      this.loadAvailableSlots(provider.providerId);
    }
  });
}
```

**Success Response (200 OK)**:
```json
{
  "providerId": "550e8400-e29b-41d4-a716-446655440001",
  "firstName": "Dr.",
  "lastName": "Smith",
  "email": "doctor@hospital.com",
  "phoneNumber": "+12125551234",
  "specialization": "Cardiology",
  "licenseNumber": "MD-12345-CA",
  "registrationDate": "2026-01-15T09:00:00Z",
  "averageRating": 4.8,
  "totalReviews": 45
}
```

---

### 3. Define Provider Availability

**Endpoint**: `POST /providers/{providerId}/availability`

**Purpose**: Doctor defines working hours and available slots (usually once per day)

**Authentication**: Required ✅ (Only own provider account)

**URL Parameters**:
| Parameter | Type | Required |
|-----------|------|----------|
| providerId | UUID | Yes |

**Request Body**:
```json
{
  "date": "2026-04-25",
  "startTime": "09:00:00",
  "endTime": "17:00:00",
  "slotDurationMinutes": 30
}
```

**Frontend Example (Provider Dashboard)**:
```typescript
// provider.service.ts
defineAvailability(providerId: string, request: DefineAvailabilityRequest): Observable<AvailabilityResponse> {
  return this.http.post<AvailabilityResponse>(
    `http://localhost:5000/api/providers/${providerId}/availability`,
    request
  );
}

// Usage in provider dashboard
this.providerService.defineAvailability(this.providerId, {
  date: this.selectedDate,
  startTime: '09:00:00',
  endTime: '17:00:00',
  slotDurationMinutes: 30
}).subscribe({
  next: (response) => {
    console.log(`Created ${response.slotsCreated} available slots`);
  }
});
```

**Success Response (201 Created)**:
```json
{
  "slotsCreated": 16,
  "date": "2026-04-25",
  "slots": [
    {
      "slotId": "550e8400-e29b-41d4-a716-446655440010",
      "startTime": "2026-04-25T09:00:00Z",
      "endTime": "2026-04-25T09:30:00Z",
      "status": "available"
    },
    {
      "slotId": "550e8400-e29b-41d4-a716-446655440011",
      "startTime": "2026-04-25T09:30:00Z",
      "endTime": "2026-04-25T10:00:00Z",
      "status": "available"
    }
  ]
}
```

---

### 4. Get Available Slots

**Endpoint**: `GET /providers/{providerId}/available-slots`

**Purpose**: Get provider's available slots for appointment booking (patient uses this)

**Authentication**: Optional (public can browse)

**URL Parameters**:
| Parameter | Type | Required |
|-----------|------|----------|
| providerId | UUID | Yes |

**Query Parameters**:
| Parameter | Type | Required | Default |
|-----------|------|----------|---------|
| fromDate | ISO8601 | No | Today |
| toDate | ISO8601 | No | 30 days from today |
| specialization | string | No | None |

**Frontend Example**:
```typescript
// provider.service.ts
getAvailableSlots(
  providerId: string,
  fromDate?: Date,
  toDate?: Date
): Observable<AvailableSlot[]> {
  let params = new HttpParams();
  
  if (fromDate) params = params.set('fromDate', fromDate.toISOString());
  if (toDate) params = params.set('toDate', toDate.toISOString());
  
  return this.http.get<AvailableSlot[]>(
    `http://localhost:5000/api/providers/${providerId}/available-slots`,
    { params }
  );
}

// Usage in patient dashboard: Browse available slots
ngOnInit() {
  this.providerService.getAvailableSlots(
    this.providerId,
    new Date(),
    new Date(Date.now() + 30*24*60*60*1000)
  ).subscribe({
    next: (slots) => {
      this.availableSlots = slots;
      // Display calendar of available times
    }
  });
}
```

**Success Response (200 OK)**:
```json
{
  "providerId": "550e8400-e29b-41d4-a716-446655440001",
  "providerName": "Dr. Smith",
  "slots": [
    {
      "slotId": "550e8400-e29b-41d4-a716-446655440010",
      "startTime": "2026-04-25T09:00:00Z",
      "endTime": "2026-04-25T09:30:00Z",
      "status": "available",
      "isBooked": false
    },
    {
      "slotId": "550e8400-e29b-41d4-a716-446655440011",
      "startTime": "2026-04-25T09:30:00Z",
      "endTime": "2026-04-25T10:00:00Z",
      "status": "available",
      "isBooked": false
    },
    {
      "slotId": "550e8400-e29b-41d4-a716-446655440012",
      "startTime": "2026-04-25T10:00:00Z",
      "endTime": "2026-04-25T10:30:00Z",
      "status": "locked",  ← Another patient is booking this
      "isBooked": false
    }
  ]
}
```

---

## Appointment Endpoints

### Base URL
```
http://localhost:5000/api  (API Gateway routes to AppointmentService)
```

### 1. Book Appointment (Create)

**Endpoint**: `POST /appointments`

**Purpose**: Patient books an appointment with a provider

**Authentication**: Required ✅

**Request Headers**:
```
Content-Type: application/json
Authorization: Bearer <access_token>
Idempotency-Key: <unique-uuid>  ← CRITICAL: Prevent duplicate bookings
X-Correlation-Id: <unique-uuid>
```

**Request Body**:
```json
{
  "slotId": "550e8400-e29b-41d4-a716-446655440010",
  "reasonForVisit": "Regular checkup",
  "notes": "First time appointment, have insurance"
}
```

**Validation Rules**:
| Field | Rule |
|-------|------|
| slotId | Must exist and be available (not locked/booked) |
| reasonForVisit | Required, max 500 chars |
| notes | Optional, max 2000 chars |

**Frontend Example**:
```typescript
// appointment.service.ts
bookAppointment(request: BookAppointmentRequest): Observable<BookAppointmentResponse> {
  const idempotencyKey = this.generateUUID();
  
  // Store idempotency key for retry safety
  sessionStorage.setItem('last_booking_idempotency_key', idempotencyKey);
  
  return this.http.post<BookAppointmentResponse>(
    'http://localhost:5000/api/appointments',
    request,
    {
      headers: {
        'Idempotency-Key': idempotencyKey,
        'X-Correlation-Id': this.generateUUID()
      }
    }
  );
}

// Usage in patient dashboard
onBookAppointmentClick(slot: AvailableSlot) {
  this.appointmentService.bookAppointment({
    slotId: slot.slotId,
    reasonForVisit: this.form.get('reason').value,
    notes: this.form.get('notes').value
  }).subscribe({
    next: (response) => {
      console.log('Appointment booked:', response.appointmentId);
      this.showSuccessMessage('Appointment confirmed!');
      this.navigateToAppointmentDetails(response.appointmentId);
    },
    error: (error) => {
      if (error.status === 409) {
        // Slot was locked by another user
        this.showErrorMessage('This slot was just booked. Please select another.');
        this.reloadAvailableSlots();
      } else if (error.status === 400) {
        this.showErrorMessage('Validation error: ' + error.error.message);
      }
    }
  });
}
```

**Success Response (201 Created)**:
```json
{
  "appointmentId": "550e8400-e29b-41d4-a716-446655440100",
  "patientId": "550e8400-e29b-41d4-a716-446655440000",
  "providerId": "550e8400-e29b-41d4-a716-446655440001",
  "slotId": "550e8400-e29b-41d4-a716-446655440010",
  "scheduledStartUtc": "2026-04-25T09:00:00Z",
  "scheduledEndUtc": "2026-04-25T09:30:00Z",
  "status": "booked",
  "reasonForVisit": "Regular checkup",
  "bookedAt": "2026-04-20T14:32:15Z"
}
```

**Error Response (409 Conflict - Slot Already Locked)**:
```json
{
  "error": "slot_unavailable",
  "message": "This slot was just booked by another patient. Please select another time.",
  "suggestedAction": "reload_available_slots"
}
```

**Error Response (400 Bad Request - Invalid Data)**:
```json
{
  "error": "validation_failed",
  "errors": {
    "slotId": ["Slot must be in the future"],
    "reasonForVisit": ["Cannot be empty"]
  }
}
```

**Error Response (409 Conflict - Duplicate Request)**:
```json
{
  "error": "duplicate_booking",
  "message": "A booking with this Idempotency-Key already exists",
  "previousResult": {
    "appointmentId": "550e8400-e29b-41d4-a716-446655440100"
  }
}
```

---

### 2. Get My Appointments

**Endpoint**: `GET /appointments/my-appointments`

**Purpose**: Get all appointments for authenticated patient

**Authentication**: Required ✅

**Query Parameters**:
| Parameter | Type | Optional | Default |
|-----------|------|----------|---------|
| status | string | Yes | all (booked, confirmed, completed, cancelled) |
| fromDate | ISO8601 | Yes | All past & future |
| toDate | ISO8601 | Yes | All past & future |

**Frontend Example**:
```typescript
// appointment.service.ts
getMyAppointments(
  status?: string,
  fromDate?: Date,
  toDate?: Date
): Observable<AppointmentDto[]> {
  let params = new HttpParams();
  
  if (status) params = params.set('status', status);
  if (fromDate) params = params.set('fromDate', fromDate.toISOString());
  if (toDate) params = params.set('toDate', toDate.toISOString());
  
  return this.http.get<AppointmentDto[]>(
    'http://localhost:5000/api/appointments/my-appointments',
    { params }
  );
}

// Usage in patient dashboard
ngOnInit() {
  this.appointmentService.getMyAppointments().subscribe({
    next: (appointments) => {
      this.upcomingAppointments = appointments.filter(
        a => a.status !== 'completed' && a.status !== 'cancelled'
      );
      this.pastAppointments = appointments.filter(
        a => a.status === 'completed' || a.status === 'cancelled'
      );
    }
  });
}
```

**Success Response (200 OK)**:
```json
[
  {
    "appointmentId": "550e8400-e29b-41d4-a716-446655440100",
    "patientId": "550e8400-e29b-41d4-a716-446655440000",
    "providerId": "550e8400-e29b-41d4-a716-446655440001",
    "providerName": "Dr. Smith",
    "specialization": "Cardiology",
    "scheduledStartUtc": "2026-04-25T09:00:00Z",
    "scheduledEndUtc": "2026-04-25T09:30:00Z",
    "status": "booked",
    "reasonForVisit": "Regular checkup",
    "notes": "First time appointment",
    "bookedAt": "2026-04-20T14:32:15Z"
  },
  {
    "appointmentId": "550e8400-e29b-41d4-a716-446655440101",
    "patientId": "550e8400-e29b-41d4-a716-446655440000",
    "providerId": "550e8400-e29b-41d4-a716-446655440002",
    "providerName": "Dr. Johnson",
    "specialization": "General Practice",
    "scheduledStartUtc": "2026-05-01T14:00:00Z",
    "scheduledEndUtc": "2026-05-01T14:30:00Z",
    "status": "confirmed",
    "reasonForVisit": "Follow-up",
    "notes": null,
    "bookedAt": "2026-04-15T10:00:00Z"
  }
]
```

---

### 3. Get Appointment Details

**Endpoint**: `GET /appointments/{appointmentId}`

**Purpose**: Get full details of a single appointment

**Authentication**: Required ✅ (Patient or Provider)

**URL Parameters**:
| Parameter | Type | Required |
|-----------|------|----------|
| appointmentId | UUID | Yes |

**Frontend Example**:
```typescript
// appointment.service.ts
getAppointmentDetails(appointmentId: string): Observable<AppointmentDetailDto> {
  return this.http.get<AppointmentDetailDto>(
    `http://localhost:5000/api/appointments/${appointmentId}`
  );
}

// Usage
ngOnInit() {
  const appointmentId = this.route.snapshot.paramMap.get('id');
  this.appointmentService.getAppointmentDetails(appointmentId).subscribe({
    next: (appointment) => {
      this.appointment = appointment;
    }
  });
}
```

**Success Response (200 OK)**:
```json
{
  "appointmentId": "550e8400-e29b-41d4-a716-446655440100",
  "patientId": "550e8400-e29b-41d4-a716-446655440000",
  "patientName": "John Doe",
  "patientEmail": "john@example.com",
  "patientPhone": "+12125551234",
  "providerId": "550e8400-e29b-41d4-a716-446655440001",
  "providerName": "Dr. Smith",
  "providerEmail": "doctor@hospital.com",
  "providerPhone": "+12125551235",
  "specialization": "Cardiology",
  "scheduledStartUtc": "2026-04-25T09:00:00Z",
  "scheduledEndUtc": "2026-04-25T09:30:00Z",
  "status": "booked",
  "reasonForVisit": "Regular checkup",
  "notes": "First time appointment, have insurance",
  "location": "Hospital Downtown - Room 205",
  "bookedAt": "2026-04-20T14:32:15Z",
  "confirmedAt": null,
  "completedAt": null,
  "cancelledAt": null
}
```

---

### 4. Confirm Appointment (Provider)

**Endpoint**: `POST /appointments/{appointmentId}/confirm`

**Purpose**: Provider confirms they will be present for the appointment

**Authentication**: Required ✅ (Only appointment's provider)

**URL Parameters**:
| Parameter | Type | Required |
|-----------|------|----------|
| appointmentId | UUID | Yes |

**Request Body**:
```json
{
  "notes": "Confirmed. Please bring your insurance card."
}
```

**Frontend Example (Provider Dashboard)**:
```typescript
// appointment.service.ts
confirmAppointment(appointmentId: string, notes?: string): Observable<AppointmentDto> {
  return this.http.post<AppointmentDto>(
    `http://localhost:5000/api/appointments/${appointmentId}/confirm`,
    { notes }
  );
}

// Usage
onConfirmClick(appointmentId: string) {
  this.appointmentService.confirmAppointment(appointmentId, 'Confirmed').subscribe({
    next: (appointment) => {
      this.showSuccessMessage('Appointment confirmed');
      this.appointment = appointment;  // Update view
    }
  });
}
```

**Success Response (200 OK)**:
```json
{
  "appointmentId": "550e8400-e29b-41d4-a716-446655440100",
  "status": "confirmed",
  "confirmedAt": "2026-04-20T15:00:00Z",
  "providerNotes": "Confirmed. Please bring your insurance card."
}
```

---

### 5. Reschedule Appointment

**Endpoint**: `POST /appointments/{appointmentId}/reschedule`

**Purpose**: Patient or provider reschedule to a different available slot

**Authentication**: Required ✅

**URL Parameters**:
| Parameter | Type | Required |
|-----------|------|----------|
| appointmentId | UUID | Yes |

**Request Body**:
```json
{
  "newSlotId": "550e8400-e29b-41d4-a716-446655440020",
  "reason": "Conflict with work schedule"
}
```

**Frontend Example**:
```typescript
// appointment.service.ts
rescheduleAppointment(
  appointmentId: string,
  newSlotId: string,
  reason: string
): Observable<AppointmentDto> {
  return this.http.post<AppointmentDto>(
    `http://localhost:5000/api/appointments/${appointmentId}/reschedule`,
    { newSlotId, reason }
  );
}

// Usage
onRescheduleClick(appointmentId: string, newSlotId: string) {
  this.appointmentService.rescheduleAppointment(
    appointmentId,
    newSlotId,
    'Need to reschedule due to conflict'
  ).subscribe({
    next: (updatedAppointment) => {
      console.log('Appointment rescheduled');
    }
  });
}
```

**Success Response (200 OK)**:
```json
{
  "appointmentId": "550e8400-e29b-41d4-a716-446655440100",
  "scheduledStartUtc": "2026-04-26T10:00:00Z",
  "scheduledEndUtc": "2026-04-26T10:30:00Z",
  "status": "booked",
  "rescheduledAt": "2026-04-20T15:05:00Z",
  "rescheduleReason": "Conflict with work schedule"
}
```

---

### 6. Cancel Appointment

**Endpoint**: `POST /appointments/{appointmentId}/cancel`

**Purpose**: Cancel an appointment (patient or provider)

**Authentication**: Required ✅

**URL Parameters**:
| Parameter | Type | Required |
|-----------|------|----------|
| appointmentId | UUID | Yes |

**Request Body**:
```json
{
  "reason": "No longer need the appointment",
  "notifyOtherParty": true
}
```

**Validation Rules**:
| Rule | Error |
|------|-------|
| Can only cancel if ≥2 hours before appointment | "Cannot cancel within 2 hours of appointment" |
| Cancel reason max 500 chars | "Reason too long" |

**Frontend Example**:
```typescript
// appointment.service.ts
cancelAppointment(appointmentId: string, reason: string): Observable<AppointmentDto> {
  return this.http.post<AppointmentDto>(
    `http://localhost:5000/api/appointments/${appointmentId}/cancel`,
    { reason, notifyOtherParty: true }
  );
}

// Usage with confirmation dialog
onCancelClick(appointmentId: string) {
  if (confirm('Are you sure you want to cancel this appointment?')) {
    this.appointmentService.cancelAppointment(
      appointmentId,
      'No longer need the appointment'
    ).subscribe({
      next: () => {
        this.showSuccessMessage('Appointment cancelled');
      },
      error: (error) => {
        if (error.status === 400) {
          this.showErrorMessage(error.error.message);
        }
      }
    });
  }
}
```

**Success Response (200 OK)**:
```json
{
  "appointmentId": "550e8400-e29b-41d4-a716-446655440100",
  "status": "cancelled",
  "cancelledAt": "2026-04-20T15:10:00Z",
  "cancelReason": "No longer need the appointment",
  "slotReleasedAt": "2026-04-20T15:10:01Z"
}
```

**Error Response (400 Bad Request - Too Close to Appointment)**:
```json
{
  "error": "cannot_cancel_appointment",
  "message": "Cannot cancel within 2 hours of appointment start time",
  "appointmentTime": "2026-04-20T16:00:00Z",
  "canCancelUntil": "2026-04-20T14:00:00Z"
}
```

---

## Common Response Patterns

### Success Responses

**2xx Status Codes**:
| Code | Meaning | Example |
|------|---------|---------|
| 200 | OK | GET request, PUT update |
| 201 | Created | POST creates new resource |
| 204 | No Content | DELETE successful |

### Error Responses

**4xx Status Codes** (Client Errors):
| Code | Meaning | Example |
|------|--------|---------|
| 400 | Bad Request | Invalid request data |
| 401 | Unauthorized | Missing or invalid token |
| 403 | Forbidden | Token valid but no permission |
| 404 | Not Found | Resource doesn't exist |
| 409 | Conflict | Slot already booked, duplicate |
| 422 | Unprocessable | Validation error |

**5xx Status Codes** (Server Errors):
| Code | Meaning |
|------|---------|
| 500 | Internal Server Error |
| 503 | Service Unavailable |

### Standard Error Format

All errors follow this format:

```json
{
  "error": "error_code",
  "message": "Human-readable error description",
  "details": {
    "field1": ["Error 1", "Error 2"],
    "field2": ["Error 3"]
  },
  "timestamp": "2026-04-20T15:10:00Z",
  "traceId": "0HN1GHME773DJ:00000001"
}
```

---

## Error Handling

### Frontend Error Handling Pattern

```typescript
// error-handler.service.ts
import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';

@Injectable({
  providedIn: 'root'
})
export class ErrorHandlerService {
  
  handleError(error: HttpErrorResponse): string {
    if (error.error instanceof ErrorEvent) {
      // Client-side error
      return `Network error: ${error.error.message}`;
    }
    
    const errorCode = error.error?.error || 'unknown_error';
    const message = error.error?.message || 'An error occurred';
    
    switch (error.status) {
      case 400:
        return this.formatValidationErrors(error.error?.details) || message;
      case 401:
        // Token expired or invalid, redirect to login
        this.authService.logout();
        return 'Your session has expired. Please login again.';
      case 403:
        return 'You do not have permission to perform this action.';
      case 404:
        return 'The requested resource was not found.';
      case 409:
        return `Cannot complete request: ${message}`;
      case 500:
        return 'Server error. Please try again later.';
      case 503:
        return 'Service is temporarily unavailable. Please try again.';
      default:
        return message;
    }
  }
  
  private formatValidationErrors(details: Record<string, string[]>): string {
    if (!details) return null;
    
    return Object.entries(details)
      .map(([field, errors]) => `${field}: ${errors.join(', ')}`)
      .join('\n');
  }
}

// Usage in components
this.appointmentService.bookAppointment(request).subscribe({
  next: (response) => {
    console.log('Success', response);
  },
  error: (error: HttpErrorResponse) => {
    const errorMessage = this.errorHandler.handleError(error);
    this.toastService.showError(errorMessage);
  }
});
```

---

## Headers & Authentication

### Required Headers for All Authenticated Endpoints

```typescript
// interceptor handles these automatically
headers: {
  'Content-Type': 'application/json',
  'Authorization': 'Bearer <access_token>',
  'X-Correlation-Id': '<uuid>',  // For request tracing
  'Accept': 'application/json'
}
```

### Idempotency Header (For Booking Endpoints)

```typescript
// Critical for preventing duplicate bookings
headers: {
  'Idempotency-Key': '<unique-uuid>'
}
```

**Why?** If the network fails after server processes but before response returns:
- Without key: User retries, gets duplicate booking
- With key: Server recognizes it's a retry, returns same result

---

## Summary: Common Frontend Patterns

### Login Flow
```typescript
// 1. User enters credentials
this.authService.login(email, password).subscribe({
  next: (tokens) => {
    // Tokens automatically stored
    this.router.navigate(['/patient/dashboard']);
  }
});

// 2. Interceptor auto-adds token to all requests
// 3. If 401 received, interceptor refreshes token automatically
```

### Book Appointment Flow
```typescript
// 1. Get available slots
this.providerService.getAvailableSlots(providerId).subscribe(...);

// 2. User selects a slot
// 3. Submit booking with Idempotency-Key
this.appointmentService.bookAppointment(request).subscribe(...);

// 4. Handle slot conflicts gracefully
```

### View Appointments Flow
```typescript
// 1. Load user's appointments on dashboard
this.appointmentService.getMyAppointments().subscribe(...);

// 2. Show upcoming vs. past appointments
// 3. Allow rescheduling (with 2-hour minimum buffer)
// 4. Allow cancellation (with 2-hour minimum buffer)
```

---

**Last Updated**: April 20, 2026  
**API Version**: 1.1  
**Status**: All endpoints tested and working
