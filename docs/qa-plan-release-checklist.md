# QA Plan and Release Checklist (Next Iteration)

Version: 1.0
Date: 2025-12-29
Owner: QA and Engineering

## Objectives
- Validate multi-tenant safety and tenant discovery across base domain and org contexts.
- Ensure reservations remain correct under availability rules and approval workflows.
- Keep public organization browsing accessible without auth.
- Detect regressions early via repeatable smoke coverage.

## Scope
- Tenant discovery and org selection.
- Org admin workflows: buildings, rooms, availability hours, blackouts.
- User workflows: registration, login, reservation creation, approval flow.
- Public universities directory and search.
- Profile avatar upload.

## Out of scope
- Infrastructure provisioning and cost optimization.
- Disaster recovery and backups (tracked separately in Ops).
- Email deliverability beyond verifying request/response behavior.

## Quality risks
- Tenant isolation leaks or missing tenant context.
- Time window errors: timezone, lead time, buffer overlap, max duration.
- Migration drift between code and database schema.
- Approval flow regressions (approve/reject visibility, status).
- Public endpoints accidentally gated by auth.

## Environments and data
- Local dev for UI smoke.
- Prod-like environment for API smoke with controlled test org.
- Dedicated test org slug and admin user for automation.
- Ephemeral entities only; cleanup required in all automation.

## Test coverage
### Functional
- Tenant selection, login, org admin login.
- Buildings and rooms CRUD.
- Building and room availability hours.
- Availability slots and blackouts.
- Reservation create, approve, reject, status transitions.
- Public org list and detail search.
- Avatar upload.

### Negative and edge cases
- Invalid org slug or missing tenant context.
- Reservation outside hours, during blackout, or within buffer.
- Duplicate room codes or building name conflicts.
- Unauthorized role actions (non-admin attempting admin endpoints).
- Multi-tenant user with multiple orgs (selection path).

### Non-functional
- Basic performance checks on login, search, and reservations list.
- Session stability (refresh token usage via proxy).
- CORS and public endpoint access verification.

## Automation
- API smoke: `scripts/smoke-api.ps1`
  - Required secrets: `SMOKE_BASE_URL`, `SMOKE_ORG_SLUG`, `SMOKE_ADMIN_USERNAME`, `SMOKE_ADMIN_PASSWORD`.
- UI smoke: `npm run smoke:e2e`, `npm run smoke:ui`
  - Required env: `SMOKE_BASE_URL`, `SMOKE_ORG_ID`, `SMOKE_USERNAME`, `SMOKE_PASSWORD`.
- Schedule: daily smoke in GitHub Actions with alerts on failure.

## Entry criteria
- Code complete and reviewed.
- Migrations generated and verified locally.
- Secrets and environment config updated.
- Smoke tests pass on local dev.

## Exit criteria
- All P0 and P1 defects resolved or waived.
- API smoke and UI smoke pass.
- No data isolation issues found.
- Manual UX spot checks completed for login and universities directory.

## Release checklist
### Pre-release
- Confirm schema migrations applied to target environment.
- Verify `SMOKE_*` secrets and deployment config.
- Run API smoke and UI smoke.
- Run manual UX spot check for `/login`, `/universities`, and org admin pages.

### Release
- Deploy backend, then frontend.
- Verify health endpoints and public org list.
- Run API smoke and UI smoke against the deployed version.

### Post-release
- Monitor logs and alerting for auth or availability errors.
- Validate reservation approval flow on real data.
- Document any hotfixes or follow-up items.

### Rollback readiness
- Identify last known good build.
- Confirm DB rollback or forward-fix plan.
- Snapshot critical data before destructive migrations.
