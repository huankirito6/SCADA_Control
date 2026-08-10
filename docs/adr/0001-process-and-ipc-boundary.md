# ADR-0001: Process, privilege and IPC boundary

- **Status:** Accepted
- **Date:** 2026-08-09
- **Findings:** P0-1; P1-10, P1-11, P1-16

## Context

An in-process interface does not isolate an HTTP-facing Web compromise from OT routes, device credentials, drivers or Runtime policy memory. The product must also work on Windows, Linux and Docker without shared secrets or Internet dependencies.

## Decision

1. Production always runs `Scada.Web` and `Scada.Runtime` as separate processes and service identities before any real hardware connection, including read-only acquisition.
2. In-process Runtime is permitted only for unit/integration tests or a read-only simulator with OT networking denied.
3. Startup fails closed when hardware/write is enabled unless service identity, filesystem ACL, OT route isolation, authenticated IPC and peer identity checks pass.
4. Web deployment closure contains no driver assembly or device credential and Web has no direct control-network route.
5. Application ports remain independent from versioned protobuf/wire contracts. Boundary DTOs are immutable.
6. Windows uses named pipes with SID ACL and peer-process verification; Linux uses UDS with peer credentials; Docker uses a protected shared-volume UDS. Explicit TCP fallback requires mTLS. No environment/shared-secret authentication.
7. Drivers expose async cancellation/deadline, capabilities, typed partial results, native status/source time, thread-safety and read/write arbitration. OPC UA requires signed+encrypted profiles and trustlist; Modbus relies on documented zone/conduit/ACL controls because it has no protocol authentication.
8. Product wording may claim only that the design intent partially supports IEC 62443 until applicability mapping, evidence and independent conformance exist.

## Consequences

Process isolation and deployment checks become hard gates rather than later refactoring. Local IPC packaging is platform-specific, but a Web RCE no longer automatically inherits OT reachability or driver objects.

## Automated gates

- Task 3: architecture and Web deployment-closure tests.
- Task 12: two-process peer/ACL/IPC authentication and contract-compatibility tests.
- Tasks 13–14: driver capability, cancellation and arbitration tests.
- Tasks 20, 31, 33–34: Modbus/OPC UA security, packaging, egress-deny and hardware acceptance tests.
