# ADR-0003: Immutable config activation and ownership

- **Status:** Accepted
- **Date:** 2026-08-09
- **Findings:** P0-10; P1-7, P1-9

## Context

Scan validation and hardware need published configuration, so publish cannot be deferred until after acquisition. A version-only activation key cannot represent rollback/reactivation. Connectivity failure must not be confused with invalid configuration. Database migration and backup ownership must follow the single writer.

## Decision

1. Minimal immutable config, canonical publish and activation are foundation capabilities, before scan-budget validation or hardware.
2. A config version has server-authoritative canonical bytes/hash including `schemaVersion`. Runtime pulls/polls the published pointer; notification is only a fast path.
3. Each attempt has a unique `activation_id` and state `Desired → Validating → Preparing → Active | ActiveDegraded | Rejected`.
4. Schema, canonical hash, semantic identity and Runtime physical-envelope validation are all-or-nothing before an atomic active-pointer switch. Connectivity failure yields `ActiveDegraded` and per-resource `tag_load_status`.
5. Reconciliation is resource-based; unchanged resources retain identity and service. `tag_semantic_revision` records meaning changes separately from activation.
6. The writer-owner migrates its databases: Web owns `config.db`/`audit-web.db`; Runtime owns historian catalog/partitions, `audit-runtime.db` and `alarms.db`; CLI only orchestrates offline.
7. Backup creates a causal manifest from service-coordinated snapshots, signs/verifies the package, rejects zip-slip/symlink/zip-bomb inputs and restores atomically after compatibility checks.

## Consequences

Foundation work is larger, but later scan, hardware and write tasks consume one coherent activation contract. Rollback is auditable and does not manufacture semantic changes.

## Automated gates

- Task 7: database ownership and migration-crash tests.
- Task 8: canonicalization, activation state-machine, notification-loss and rollback tests.
- Task 23: resource reconciliation and rollback continuity tests.
- Task 32: coherent backup, malicious-package and atomic-restore tests.
