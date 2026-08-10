# ADR-0004: Command authority, physical policy and durable dispatch

- **Status:** Accepted
- **Date:** 2026-08-09
- **Findings:** P0-2, P0-3, P0-4, P0-5; P1-8, P1-11, P1-12

## Context

Web owns human interaction and config, so a compromised Web can remap a valid tag to a dangerous physical target unless Runtime owns a positive physical policy. A username assertion is not authority. Device I/O before a durable intent can create an unaudited physical action.

## Decision

1. Runtime loads a signed administrator/root-owned positive allowlist. Missing/invalid policy disables writes. Config may only narrow the approved tuple:

   `driver + endpoint/device identity + unit/node + function/access mode + address + datatype + byte/word order + raw/engineering transform + raw/engineering range + write mode + max rate/pulse`.

   Broadcast/dangerous functions are denied by default; read/browse has a network allowlist. `other_writers_possible` defaults true.
2. Every command binds `command_id`, `activation_id`, active config hash, tag ID, source/value hashes, physical target digest, expected value/sample revision, quality, observed timestamp, device-session generation, subject/capability nonce and typed value. Scene identity is audit context, not authorization authority.
3. Capability is one-time, short-lived, audience/channel-bound and binds subject, exact target/value, activation/hashes, policy version, expiry and nonce. Runtime uses an authoritative authorization snapshot/revocation source or independent re-auth verifier. Without it, documentation must admit Web RCE authority within the physical envelope and untrusted audit subject.
4. Runtime atomically consumes nonce/idempotency and appends authorization, immutable target and `DispatchIntent` in a command/audit transaction with strong durability. Only after commit may driver I/O occur. Attempt, readback and outcome are appended afterward.
5. Restart with intent but no outcome projects `INDETERMINATE`; it never auto-dispatches. Precondition mismatch before intent is `PreconditionFailed`, requires reconfirmation and is not retried.
6. Audit records use canonical event bytes, algorithm/version, chain ID, monotonic sequence, genesis/previous hash and causal links. Each chain is externally sealed after at most 100 events or 60 seconds, whichever comes first; boot, clean shutdown and policy/key rotation force an immediate seal. The OS-protected/non-exportable signing key is held by a separate `AuditSealer` identity, excluded from ordinary backup/diagnostics, and writes to an append-only sink outside application ACL. At 120 seconds without a successful seal, the sink is overdue: raise a system alarm, mark health degraded and fail closed command/publish mutations until sealing recovers. Rotation emits a transition signed by old and new keys and preserves public trust history. Key loss starts a new chain epoch through an audited local recovery ceremony and exposes the discontinuity. Two chains are never presented as a cryptographic total order.
7. Deny-by-default RBAC is rechecked at sensitive endpoint execution; stale circuits and revocation cannot rely on cached principals. Commands use anti-forgery-protected POST, not hub invocation. There is deliberately **no break-glass bypass**, emergency role or token in product scope. Local bootstrap/account recovery may restore a normal administrator only after OS-local ceremony and audit; writes remain globally disabled until normal identity, policy and sealer health gates pass.

## Consequences

Command latency includes a durable commit and capability check. In exchange, no device action is knowingly initiated without immutable intent and every remap/replay/crash case has a defined terminal state.

## Automated gates

- Task 9: audit tamper/truncation, fake-clock cadence/boundary/forced seal, sealer ACL, rotation/key-loss, overdue fail-closed and cross-chain causality tests.
- Task 10: permission matrix, explicit no-break-glass-path, local-recovery-writes-disabled, expiry, revocation and capability replay tests.
- Task 11: field-by-field physical remap attack matrix.
- Tasks 20–22: physical-target enforcement, crash matrix, durable-before-I/O and command UX/E2E tests.
