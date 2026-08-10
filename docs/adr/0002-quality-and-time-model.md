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

5. Browser transport state `RuntimeDisconnected` is separate from domain quality. Bad/Stale/NoData/Disconnected always show a pattern plus icon/text and age, contribute to the global invalid count, and disable command.
6. Invalid quality poisons expressions/bindings; there is no silent coercion.

## Consequences

Domain and wire representations require explicit conversion, but quality aggregation is algebraically valid and display/command behavior is deterministic through clock failure and disconnects.

## Automated gates

- Task 4: exhaustive quality-combination and semantic hash tests.
- Task 5: backward/forward clock step, restart and persisted high-water tests.
- Tasks 16–17: trend quality-duration and Runtime stale-transition tests.
- Task 19: validity/accessibility state matrix and command-disable E2E tests.
