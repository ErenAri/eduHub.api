# Auth Contract

This document defines expected authentication and authorization outcomes for the API.

## Goals
- Provide a stable contract for clients.
- Avoid tenant enumeration while preserving actionable error codes.
- Keep cross-tenant access clearly rejected.

## Status Codes
- 401 Unauthorized: missing, invalid, expired, revoked, or tenant-mismatched token.
- 403 Forbidden: authenticated but role or policy does not permit the action.
- 404 Tenant not found: tenant context could not be resolved for `/api/org/*`.

## Expected Behavior
- Missing or invalid token -> 401 with `code: Unauthorized`.
- Revoked or expired token -> 401 with `code: InvalidToken`.
- Tenant mismatch (token `org_id` != resolved tenant) -> 401 with `code: TenantMismatch`.
- Valid token, tenant match, policy fails -> 403 with `code: Forbidden`.
- Platform admin tokens are valid across tenants and `api/platform/*`.

## Response Shape
- Use `application/problem+json`.
- Include `code` and `traceId` for troubleshooting.

## Notes
- Authentication runs after tenant resolution for `/api/org/*` routes.
- Tokens are rejected if the organization or membership is inactive.
