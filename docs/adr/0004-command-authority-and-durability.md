# ADR-0004: Command authority, physical policy and durable dispatch

- **Status:** Accepted
- **Date:** 2026-08-09
- **Findings:** P0-2, P0-3, P0-4, P0-5; P1-8, P1-11, P1-12

## Context

Web owns human interaction and config, so a compromised Web can remap a valid tag to a dangerous physical target unless Runtime owns a positive physical policy. A username assertion is not authority. Device I/O before a durable intent can create an unaudited physical action.

## Decision

1. Runtime loads an administrator/root-owned positive allowlist from a local file whose directory and file ACL deny write access to Web and service identities. An authorized administrator/root update is temp-write + flush + atomic replace; Runtime verifies owner, ACL, schema, monotonic policy version and canonical policy digest on restart. A missing, malformed, torn/non-atomic, wrong-owner or ACL-unauthorized policy disables writes. Every accepted update is audited and forces an external audit seal. The physical policy has no signature and no physical-policy signing key. Config may only narrow the approved tuple:

   `driver + endpoint/device identity + unit/node + function + access mode + address + datatype + byte order + word order + raw/engineering transform + raw range + engineering range + write mode + max rate + pulse`.

   Broadcast/dangerous functions are denied by default; read/browse has a network allowlist. `other_writers_possible` defaults true.
2. Every command binds `command_id`, `activation_id`, active config hash, tag ID, source/value hashes, physical target digest, expected value/sample revision, quality, observed timestamp, device-session generation, subject/capability nonce and typed value. It also carries canonical `SceneId`, `SceneRevision` and `SceneHash`; `SceneHash` must equal the hash produced over the exact server-authoritative canonical Scene bytes. All three Scene fields are copied unchanged into `DispatchIntent` and durable audit context, but Scene context is evidence only and never authorization authority.
3. Capability is one-time, short-lived, audience/channel-bound and binds subject, exact target/value, activation/hashes, active physical-policy version and digest, expiry and nonce to the intended Runtime, site and service audience. Runtime uses an authoritative authorization snapshot/revocation source or independent re-auth verifier. An otherwise-valid capability presented under a different subject, to the wrong Runtime/site/service audience, or after policy N changes to version/digest N+1 is rejected before `DispatchIntent` and before driver I/O as a typed, audited `CapabilityBindingMismatch` with a specific reason; dispatch requires a fresh capability. Without an authoritative verifier, documentation must admit Web RCE authority within the physical envelope and untrusted audit subject.
4. Runtime atomically consumes nonce/idempotency and appends authorization, immutable target and `DispatchIntent` in a command/audit transaction with strong durability. Only after commit may driver I/O occur. Attempt, readback and outcome are appended afterward.
5. Restart with intent but no outcome projects `INDETERMINATE`; it never auto-dispatches. Precondition mismatch before intent is `PreconditionFailed`, requires reconfirmation and is not retried.
6. Audit records use canonical event bytes, algorithm/version, chain ID, monotonic sequence, genesis/previous hash and causal links. Each chain is externally sealed after at most 100 events or 60 seconds, whichever comes first; boot, clean shutdown and policy/key rotation force an immediate seal. The signing key is held by a separate `AuditSealer` identity, excluded from ordinary backup/diagnostics, and writes to an append-only sink outside application ACL. At 120 seconds without a successful seal, the sink is overdue: raise a system alarm, mark health degraded and fail closed command/publish mutations until sealing recovers. Rotation emits a transition signed by old and new keys and preserves public trust history. Key loss starts a new chain epoch through an audited local recovery ceremony and exposes the discontinuity. Two chains are never presented as a cryptographic total order.
7. The audit-sealer private key is OS-protected and non-exportable **where the selected provider supports that property**. Commissioning records `RequireNonExportable` or `AllowProtectedSoftwareFallback`, the provider and the capability-probe outcome:

   | Target | Preferred provider/outcome | Allowed software fallback |
   |---|---|---|
   | Windows service | CNG machine-key provider that passes a non-exportability probe | Protected-at-rest software key under machine protection, stored with `AuditSealer`-only filesystem/key ACL |
   | Linux service | Configured PKCS#11 TPM/HSM provider that reports a non-extractable private key | Encrypted software key on local storage with root-owned directory and `AuditSealer`-only UID ACL |
   | Docker | Explicitly mounted PKCS#11/TPM/HSM provider/device that reports a non-extractable private key | Encrypted key in a dedicated secret volume mounted only into `AuditSealer`, never an environment variable, image layer or ordinary backup |

   In provider mode, `AuditSealer` may sign but private-key export must fail even for that identity; Web/Runtime may neither sign nor export. If `RequireNonExportable` is configured and the provider is absent or fails its capability probe, sealer health is failed at startup and command/publish mutations remain fail-closed, while read/verify paths may remain available. If fallback is explicitly allowed, startup reports the platform outcome and custody mode, verifies signer-only ACL, and raises a documented degraded-custody status. The fallback protects against application/service compromise but not a host administrator, who can ultimately recover software key material; product threat documentation must state that limitation. Rotation and key-loss recovery apply in both modes.
8. Deny-by-default RBAC is rechecked at sensitive endpoint execution; stale circuits and revocation cannot rely on cached principals. Commands use anti-forgery-protected POST, not hub invocation. There is deliberately **no break-glass bypass**, emergency role or token in product scope. Local bootstrap/account recovery may restore a normal administrator only after OS-local ceremony and audit; writes remain globally disabled until normal identity, policy and sealer health gates pass.

## Consequences

Command latency includes a durable commit and capability check. In exchange, no device action is knowingly initiated without immutable intent and every remap/replay/crash case has a defined terminal state.

## Automated gates

- Task 9: audit tamper/truncation, fake-clock cadence/boundary/forced seal, platform provider/support outcomes, sign-versus-export access, required-provider absence, fallback custody/ACL/threat disclosure, rotation/key-loss, overdue fail-closed and cross-chain causality tests.
- Tasks 10 and 21: otherwise-valid changed-subject, wrong Runtime/site/service audience and stale policy-version/digest capabilities produce typed/audited rejection before `DispatchIntent`/driver I/O and require a fresh capability, in addition to permission, no-break-glass, recovery, expiry, revocation and replay gates.
- Task 11: independent one-field widening for every physical-tuple dimension rejects before activation/dispatch; missing/invalid/wrong-ACL policy and unauthorized update/restart-load cases fail closed.
- Tasks 20–22: physical-target enforcement, crash matrix, durable-before-I/O and command UX/E2E tests.
