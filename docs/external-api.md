# Gradify ↔ Innovation Team — External API

This document describes the HTTP contract between **Gradify** and the
**Innovation Team** for the status-update webhook (and a few read-only
endpoints used by the Gradify student UI).

> **Admin-side note:** the *exact* JSON the Innovation Team sends does
> not have to match the canonical contract below. Gradify admins can
> configure field-name and status-value mappings at
> `/management/innovation-integration` — any active mapping is applied
> automatically before the webhook parses the payload. When no mappings
> are configured the canonical contract is used as-is.

> Last updated: 2026-05-14.
> This is a living document — the payload shape is intentionally lenient,
> see "Forward-compatibility" at the bottom.

> **About `ExternalRequestId`** — for the Airtable integration, send the
> **Airtable Record ID** (`recXXXXXXXXXXXXXX`, 17 chars, starts with `rec`).
> It's globally unique, stable for the lifetime of the row, and lets the
> Innovation Team re-push the same record any number of times — Gradify
> upserts on this value.

---

## 1. Base URL & TLS

All endpoints live under the Gradify server origin:

```
https://<gradify-host>/api/external-requests/...
https://<gradify-host>/api/external-forms/...
```

* HTTPS is **required** for production. The webhook endpoint will accept
  requests over plain HTTP only when the entire Gradify deployment is
  configured for HTTP (development environments).
* All request and response bodies use `application/json; charset=utf-8`.

---

## 2. Authentication

### 2.1 Inbound webhook — `X-External-Api-Key`

The webhook endpoint authenticates each request with a **pre-shared API
key**, sent as an HTTP header:

```
X-External-Api-Key: <secret>
```

* The key is stored on Gradify's side in configuration
  (`ExternalApi:ApiKey`); never hard-coded. In production we read it from
  an environment variable: `ExternalApi__ApiKey=<value>`.
* When the header is missing or does not match, the server responds with
  `401 Unauthorized`.
* When Gradify has not yet been provisioned with a key, the server
  responds with `503 Service Unavailable` and the body
  `{ "error": "external_api_key_not_configured" }`.

The compare is constant-time to defeat header-byte timing attacks.

### 2.2 Student endpoints — JWT

The student-facing read endpoints (`GET /api/external-requests/me`,
`/me/{id}/history`) require Gradify's normal JWT (sent as
`Authorization: Bearer <token>`). The Innovation Team does **not** need
to call these.

---

## 3. POST `/api/external-requests/update`

Used by the Innovation Team to push a status change for a request.

### 3.1 Behaviour

* The endpoint is **idempotent by `ExternalRequestId`**. Re-sending the
  same id upserts: an existing row is updated; a new row is created
  otherwise.
* Omitted fields preserve their previous value (server-side `COALESCE`
  on every column except `RawPayload`, which is always overwritten with
  the verbatim body for forensics).
* Every successful status change appends a row to
  `ExternalRequestStatusHistory` (queryable from the UI in future
  iterations) and raises an in-process event so notifications can fire.
* When the email matches a Gradify user, we resolve and store a local
  `StudentId` automatically.

### 3.2 Request payload

```json
{
  "ExternalRequestId": "innov-2026-0042",
  "StudentEmail":      "alice@example.edu",
  "StudentId":         null,
  "ProjectId":         null,
  "RequestType":       "innovation_request",
  "Status":            "in_review",
  "StatusLabel":       "בבדיקה",
  "UpdatedAt":         "2026-05-13T09:24:00Z",
  "Notes":             "Reviewing budget proposal."
}
```

#### Field reference

| Field               | Type         | Required | Notes                                                          |
| ------------------- | ------------ | :------: | -------------------------------------------------------------- |
| `ExternalRequestId` | string       |   yes    | Upsert key. Must be unique on the upstream side.               |
| `StudentEmail`      | string       |    —     | Used to resolve a local student when `StudentId` is not given. |
| `StudentId`         | integer null |    —     | Internal Gradify user id (if known).                           |
| `ProjectId`         | integer null |    —     | Internal Gradify project id (if known).                        |
| `RequestType`       | string       |    —     | Free-text category — see §5.                                   |
| `Status`            | string       |    —     | Machine-readable token — see §5.                               |
| `StatusLabel`       | string       |    —     | Hebrew display label. Falls back to a translation of `Status`. |
| `UpdatedAt`         | RFC3339 UTC  |    —     | Defaults to server now when missing.                           |
| `Notes`             | string       |    —     | Free text shown to the student.                                |

Unknown top-level fields are accepted and persisted in the verbatim
`RawPayload` (server-side). They are also captured into a generic
`Extra` dictionary, so the server can be extended to surface new fields
without breaking old payloads.

### 3.3 Response codes

| Code | Meaning                                                                          |
| ---- | -------------------------------------------------------------------------------- |
| 200  | Upsert succeeded. Body: `{ externalRequestId, action: "created"\|"updated", … }` |
| 400  | Bad request — `empty_body`, `invalid_json`, `missing_ExternalRequestId`          |
| 401  | Missing or invalid `X-External-Api-Key`                                          |
| 413  | Payload exceeded the 64 KB size cap                                              |
| 500  | Server error during the upsert. The payload is persisted to                     |
|      | `ExternalRequestFailedPayloads` for our team to investigate.                     |
| 503  | Gradify has no API key configured.                                               |

Failed-payload rows include the request body (truncated to 64 KB), the
remote IP, and a short error tag. Retrying a 5xx after a brief delay is
safe — the upsert is idempotent.

### 3.4 Sample interactions

**Create**

```http
POST /api/external-requests/update HTTP/1.1
Host: gradify.example.edu
X-External-Api-Key: <secret>
Content-Type: application/json

{
  "ExternalRequestId": "innov-2026-0042",
  "StudentEmail":      "alice@example.edu",
  "RequestType":       "innovation_request",
  "Status":            "received"
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json

{ "externalRequestId": "innov-2026-0042", "id": 17, "action": "created" }
```

**Update to `in_review`**

```http
POST /api/external-requests/update HTTP/1.1
Host: gradify.example.edu
X-External-Api-Key: <secret>
Content-Type: application/json

{
  "ExternalRequestId": "innov-2026-0042",
  "Status":            "in_review",
  "StatusLabel":       "בבדיקה",
  "Notes":             "Awaiting client meeting on the 21st."
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json

{ "externalRequestId": "innov-2026-0042", "action": "updated", "statusChanged": true }
```

---

## 4. Student read endpoints

These are documented here for completeness; the Innovation Team does
not call them.

### 4.1 `GET /api/external-requests/me`

Returns the calling student's requests (matched by `StudentId` or
case-insensitive email).

```json
[
  {
    "id": 17,
    "externalRequestId": "innov-2026-0042",
    "requestType":       "innovation_request",
    "status":            "in_review",
    "statusLabel":       "בבדיקה",
    "notes":             "Awaiting client meeting on the 21st.",
    "createdAt":         "2026-05-13T09:00:11Z",
    "updatedAt":         "2026-05-13T09:24:00Z"
  }
]
```

### 4.2 `GET /api/external-requests/me/{externalRequestId}/history`

Status timeline for one request the caller owns. Returns 404 if the
request belongs to another user.

```json
[
  { "id": 1, "externalRequestId": "innov-2026-0042",
    "oldStatus": "",          "newStatus": "received",
    "oldStatusLabel": "",     "newStatusLabel": "התקבל",
    "notes": "",              "changedAt": "2026-05-13T09:00:11Z" },
  { "id": 2, "externalRequestId": "innov-2026-0042",
    "oldStatus": "received",  "newStatus": "in_review",
    "oldStatusLabel": "התקבל","newStatusLabel": "בבדיקה",
    "notes": "Awaiting client meeting on the 21st.",
    "changedAt": "2026-05-13T09:24:00Z" }
]
```

---

## 5. Status tokens & request types

Gradify recognises the following **machine status tokens**. Unknown
values are accepted and stored verbatim — they just won't have a Hebrew
label or a visual bucket.

| Token         | Hebrew label | Bucket   |
| ------------- | ------------ | -------- |
| `pending`     | ממתין        | pending  |
| `received`    | התקבל        | pending  |
| `in_review`   | בבדיקה       | progress |
| `in_progress` | בטיפול       | progress |
| `on_hold`     | מושהה        | pending  |
| `approved`    | אושר         | done     |
| `completed`   | הושלם        | done     |
| `rejected`    | נדחה         | rejected |
| `cancelled`   | בוטל         | rejected |
| `closed`      | נסגר         | neutral  |

Terminal statuses (`approved`, `completed`, `rejected`, `cancelled`,
`closed`) move a request out of the "active" bucket in our UI.

**Suggested request types** (free-text — not enforced):
`innovation_request`, `feedback`, `meeting_request`,
`resource_request`, `general`.

Definitive source of truth:
`Shared/AuthSharedModels/ExternalRequestConstants.cs`.

---

## 6. External forms (iframe embed)

This is internal to Gradify — the Innovation Team does not call these
endpoints. We mention them here so the team knows what URLs Gradify
will embed.

* Forms are configured by Gradify admins/lecturers at
  `/management/external-forms`.
* Each row stores: `Name`, `Description`, `FormType`, `IframeUrl`,
  `IsActive`, `AcademicYearId` (nullable). Deletes are soft —
  `IsDeleted = 1`, audit row appended.
* **URL policy**: only `https://…` URLs are accepted. We reject
  `javascript:`, `data:`, `vbscript:`, `file:`, `blob:`, `about:`,
  embedded user-info, and any URL longer than 2048 chars.
* The iframe is rendered with:

  ```html
  <iframe sandbox="allow-forms allow-scripts allow-same-origin allow-popups"
          referrerpolicy="no-referrer"
          loading="lazy">
  ```

If your form needs additional capabilities (e.g. payment redirects,
clipboard), please reach out so we can adjust the sandbox attributes for
your URL.

---

## 7. Forward-compatibility & retries

* **Adding fields**: append-only. Old clients will ignore unknown
  fields; the server persists them verbatim in `RawPayload`.
* **Retries**: 5xx responses are safe to retry on any cadence
  (upsert is idempotent on `ExternalRequestId`). A retry budget of
  ~5 attempts with exponential backoff is suggested.
* **Backfills**: re-pushing historical statuses is supported, but each
  retry creates a `ExternalRequestStatusHistory` row only when the
  current `Status` actually differs — so backfilling the same value is
  a no-op for the timeline.
* **Schema changes**: any breaking change will move under a new path
  (`/api/external-requests/v2/update`); the current path will stay
  stable.

---

## 8. Operational notes (Gradify-internal)

* Failed payloads are persisted in `ExternalRequestFailedPayloads`
  (capped at 64 KB body, IP + short error tag).
* Status events fire through the in-process `ExternalRequestEvents`
  static bus (`StatusChanged`, `Created`). Subscribers attach during
  startup wiring — currently none. When notifications land, they'll
  subscribe here.
* Indexes:
  `ix_ExternalRequests_StudentEmail`,
  `ix_ExternalRequests_StudentId`,
  `ix_ExternalRequests_UpdatedAt`,
  `ix_ExternalForms_AcademicYearId`,
  `ix_ExternalForms_IsActive`,
  `ix_ExternalForms_IsDeleted`,
  `ix_ExternalRequestStatusHistory_Eid`,
  `ix_ExternalFormAuditLog_FormId`.

---

## 9. Quick reference — curl

Replace `<host>` and `<secret>` with your environment values. `<host>` is
the Gradify server origin (e.g. `gradify.example.edu`). `<secret>` is the
configured value of `ExternalApi:ApiKey`.

**Create a new request (or first push for an id):**

```bash
curl -X POST 'https://<host>/api/external-requests/update' \
  -H 'Content-Type: application/json' \
  -H 'X-External-Api-Key: <secret>' \
  -d '{
    "ExternalRequestId": "recAAAAAAAAAAAAAA",
    "StudentEmail":      "alice@example.edu",
    "RequestType":       "innovation_request",
    "Status":            "received",
    "StatusLabel":       "התקבל",
    "Notes":             "Initial submission."
  }'
```

**Update an existing request to `in_review`:**

```bash
curl -X POST 'https://<host>/api/external-requests/update' \
  -H 'Content-Type: application/json' \
  -H 'X-External-Api-Key: <secret>' \
  -d '{
    "ExternalRequestId": "recAAAAAAAAAAAAAA",
    "Status":            "in_review",
    "StatusLabel":       "בבדיקה",
    "Notes":             "Awaiting client meeting on the 21st."
  }'
```

**Wrong key (401 expected):**

```bash
curl -i -X POST 'https://<host>/api/external-requests/update' \
  -H 'Content-Type: application/json' \
  -H 'X-External-Api-Key: nope' \
  -d '{ "ExternalRequestId": "recAAAAAAAAAAAAAA" }'
```

**Missing id (400 + persisted in `ExternalRequestFailedPayloads`):**

```bash
curl -i -X POST 'https://<host>/api/external-requests/update' \
  -H 'Content-Type: application/json' \
  -H 'X-External-Api-Key: <secret>' \
  -d '{ "Status": "received" }'
```

---

## 10. Test data — Postman / Thunder Client / Insomnia

For local testing against a Gradify dev server (`http://localhost:5xxx`):

1. Set the API key in your local config — for example, set the env var
   `ExternalApi__ApiKey=dev-secret-please-change` before starting the
   server. Without a configured key the endpoint returns 503.
2. Create a POST request to `http://localhost:5xxx/api/external-requests/update`.
3. Add headers:
   * `Content-Type: application/json`
   * `X-External-Api-Key: dev-secret-please-change`
4. Use one of the payloads below. All `ExternalRequestId` values are
   fake Airtable-style record ids — re-using the same id upserts.

### 10.1 Create a request

```json
{
  "ExternalRequestId": "recTEST0000000001",
  "StudentEmail":      "student.demo@example.edu",
  "RequestType":       "innovation_request",
  "Status":            "received",
  "StatusLabel":       "התקבל",
  "UpdatedAt":         "2026-05-14T08:30:00Z",
  "Notes":             "First contact — student submitted the form."
}
```

Expected: `200 OK`, body `{ "externalRequestId": "recTEST0000000001", "id": <n>, "action": "created" }`.

### 10.2 Move to `in_review` (status history row will be appended)

```json
{
  "ExternalRequestId": "recTEST0000000001",
  "Status":            "in_review",
  "StatusLabel":       "בבדיקה",
  "Notes":             "Reviewing with the innovation team."
}
```

Expected: `200 OK`, body `{ "externalRequestId": "recTEST0000000001", "action": "updated", "statusChanged": true }`.

### 10.3 Send the same status again (no new history row)

Run §10.2 a second time. Expected: `200 OK`, body
`{ "externalRequestId": "recTEST0000000001", "action": "updated", "statusChanged": false }`.

### 10.4 Terminal status

```json
{
  "ExternalRequestId": "recTEST0000000001",
  "Status":            "approved",
  "StatusLabel":       "אושר",
  "Notes":             "Request approved for full mentor support."
}
```

### 10.5 Project-linked request (when both ids are known)

```json
{
  "ExternalRequestId": "recTEST0000000002",
  "StudentId":         42,
  "ProjectId":         17,
  "RequestType":       "meeting_request",
  "Status":            "in_progress",
  "StatusLabel":       "בטיפול",
  "Notes":             "Pairing the team with a senior mentor."
}
```

### 10.6 Negative tests

| Scenario              | Payload                                             | Expected           |
| --------------------- | --------------------------------------------------- | ------------------ |
| Missing API key       | any body, no `X-External-Api-Key` header            | 401 + persisted failed payload row when body present |
| Wrong API key         | any body, `X-External-Api-Key: wrong`               | 401 |
| Empty body            | `(empty)`                                           | 400 `empty_body`   |
| Invalid JSON          | `not-json`                                          | 400 `invalid_json` |
| Missing required id   | `{ "Status": "received" }`                          | 400 `missing_ExternalRequestId` |
| Body > 64 KB          | very large payload                                  | 413 `payload_too_large` |

### 10.7 Verify what the student sees

After §10.1, sign in as the student whose email matches `StudentEmail`
and visit `/external-requests`. The "סטטוס בקשות" section should now
contain a card for `recTEST0000000001` with the latest status label,
the last-updated timestamp, and the notes line.