# ADR-0002: Quality, stale and logical-time model

- **Status:** Accepted
- **Date:** 2026-08-09
- **Findings:** P0-6, P0-9; P1-1

## Context

Ordinal packed quality is not closed under bitwise OR, so bucket aggregation can invent invalid states. Wall-clock steps and restarts can violate ordering. An implicit or browser-owned stale rule can leave an unsafe-looking normal display and allow commands against untrustworthy observations.

## Decision

1. `TagQuality` separates `severity: Good | Uncertain | Bad`, independent `reasonFlags`, and optional protocol `nativeStatus`.
2. Trend aggregate returns `severitySeenMask`, `worstSeverity`, `reasonFlagsSeen`, `durGoodMs`, `durValidMs`, `durBadMs` and `durStaleMs`; it never ORs raw packed quality.
3. Samples carry logical/ingest time, optional source time, monotonic ticks, boot ID and state revision. Runtime owns one monotonic logical clock, persists a high-water mark and never orders by untrusted source time.
4. Runtime is the only authority that changes domain quality to `Stale`. Every published tag explicitly sets `StaleAfterMs`, validated as:

   `max(3 × ScanPeriodMs, 2_000) <= StaleAfterMs <= 60_000`.

5. A process start creates a new `BootId`. Before the first successful fresh scan in that boot, every persisted last observation whose `BootId` differs from the current boot is projected immediately as `Stale + LastKnown`; commands are disabled. Monotonic ticks are never compared across boot IDs, and the persisted logical timestamp is audit/history context only: neither can preserve `Good` or delay the boot-stale transition. The transition is published and persisted even when value deadband or historian store-rate would otherwise suppress a sample. Only a successful fresh scan carrying the current `BootId` may clear `Stale`, and it does so according to the scan's actual quality.
6. Browser transport state `RuntimeDisconnected` is separate from domain quality. Bad/Stale/NoData/Disconnected always show a pattern plus icon/text and age, contribute to the global invalid count, and disable command.
7. Invalid quality poisons expressions/bindings; there is no silent coercion.

## Consequences

Domain and wire representations require explicit conversion, but quality aggregation is algebraically valid and display/command behavior is deterministic through clock failure and disconnects.

## Automated gates

- Task 4: exhaustive quality-combination and semantic hash tests.
- Task 5: backward/forward clock step, restart and persisted high-water tests.
- Task 14: fake-clock Runtime stale-transition matrix covering lower/upper publish bounds, exact threshold, monotonic/logical advance, scan recovery, and exact restart assertions: an old-boot persisted observation is immediately `Stale + LastKnown` with command disabled before the first fresh scan; no cross-boot monotonic comparison or old logical timestamp may retain `Good`; that transition is published/persisted despite deadband/store-rate; only a current-boot successful scan clears `Stale` according to actual quality.
- Task 16: trend quality-duration tests.
- Task 19: validity/accessibility state matrix and command-disable E2E tests.
