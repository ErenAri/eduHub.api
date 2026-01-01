# Tenant Resolve Durability Plan

## Problem
Tenant resolution tokens are stored in memory and do not survive restarts or scale-out.

## Proposed Design
- Store verification tokens in durable storage (DB table or Redis).
- Hash tokens before storage; enforce single-use and expiry.
- Keep email delivery outside the API using an async provider or queue.

## Data Model (DB option)
- TenantResolveToken
  - Id (GUID)
  - Email (normalized)
  - TokenHash
  - CreatedAtUtc
  - ExpiresAtUtc
  - UsedAtUtc
  - CreatedFromIp (optional)

## Flow
1) `/api/tenant/resolve` creates token, stores hash + metadata, and sends email.
2) `/api/tenant/resolve/verify` validates token, marks it used, returns tenants.
3) Expired or used tokens return an empty tenant list.

## Operations
- Periodic cleanup job for expired/used tokens.
- Metrics: resolve requests, verifies, failures, expires, and send errors.
- Rate limiting by email + IP to prevent abuse.

## Security
- Do not return debug tokens outside Development.
- Avoid differentiating "unknown email" vs "known email" to reduce enumeration risk.
