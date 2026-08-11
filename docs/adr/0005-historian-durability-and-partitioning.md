# ADR-0005: Historian durability, partitions and alarm ownership

- **Status:** Accepted
- **Date:** 2026-08-09
- **Findings:** P0-7; P1-4, P1-6, P1-7, P1-9

## Context

A bounded queue can lose pre-acceptance samples, so unconditional historian losslessness is false. Silent loss is unacceptable. SQLite's default attach limit prevents attaching a full retention range. Alarm evidence requires durability and ownership independent of telemetry samples.

## Decision

1. Historian guarantees **no silent loss**, not unconditional end-to-end losslessness. After storage acknowledgement, a sample has stable ingest identity and retry is idempotent.
2. Any loss before acceptance persists a gap/high-water marker and raises a system alarm. Candidate, accepted, persisted and gap counters are distinct. Disk/WAL/queue warnings precede exhaustion.
3. Sample, alarm, audit and command queues are separate. Audit/command fail closed and use stronger durability than historian. Alarm events use their own durable profile.
4. Runtime is the single writer of `alarms.db`; Web accesses alarm snapshot/cursor/query only through RPC.
5. Historian uses one time-partition file per period. Long queries open/query a bounded number of partition files per batch and merge seed/decomposable aggregates in the repository. They never `ATTACH` the whole retention range. Retention deletion requires pin/refcount protection against active readers.
6. `QueryTrend` carries as-of seed, min/max envelope, time-weighted decomposition and the quality fields from ADR-0002.
7. Database migration follows writer ownership. Backup/restore follows the coordinated causal-cut rules in ADR-0003.

## Consequences

Operators can distinguish accepted durability from detected pre-acceptance gaps. Partition queries stay within SQLite limits and retention cannot remove data beneath readers.

## Automated gates

- Task 7: database ownership and migration tests.
- Task 15: ingest identity, retry, overflow/gap marker and early-warning tests.
- Task 16: a 12-week fixture exceeds SQLite's default attach limit, proves complete seed/buckets through observable bounded batches without whole-range `ATTACH`, and proves retention cannot delete a reader-pinned partition until the reader releases it.
- Task 27: durable `alarms.db` ownership and alarm-state recovery tests.
- Task 32: coordinated backup/restore tests.
