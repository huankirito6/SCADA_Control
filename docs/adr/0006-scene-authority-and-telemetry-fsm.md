# ADR-0006: Scene authority, telemetry handoff and UI state machines

- **Status:** Accepted
- **Date:** 2026-08-09
- **Findings:** P0-8, P0-9; P1-2, P1-3, P1-5, P1-13, P1-14, P1-15

## Context

Epoch alone prevents handle aliasing but not deltas lost during snapshot, overflow or reconnect. Parallel C#/JavaScript Scene models can diverge. The editor, alarm/trend transports, accessibility and performance need normative contracts before implementation.

## Decision

1. Telemetry identity is `subscriptionGeneration + epoch + snapshotWatermark`. Snapshot is taken at `W`; a serialized per-client mailbox applies only deltas `> W` for the same generation/epoch.
2. Mailbox is a latest-wins dirty map. If it cannot coalesce safely, server sends `ResyncRequired`; client discards the old queue and follows `Bootstrapping → Live → Lagging/Resyncing → Offline`.
3. Runtime owns domain quality/Stale; browser owns only `RuntimeDisconnected`. The UI state matrix in the spec is normative and commands are disabled outside trustworthy Live state.
4. One JSON Schema plus widget manifest is the Scene authority. C# and TypeScript types/validators are generated. Server owns canonical bytes/hash and both runtimes execute a shared accept/reject conformance corpus.
5. Schema normatively defines stable IDs, canvas/viewBox, ordered parent/layers, widget props, structured geometry, typed targets/actions/parameters, symbols/instances, dangling-reference behavior and complexity limits. Scene/config/import remains untrusted data with no arbitrary HTML/SVG/URL/code.
6. Alarm/trend transport uses snapshot + cursor + idempotent events with reconnect/backfill; render model explicitly represents invalid gaps and interaction modifier state.
7. Editor state is JS-owned with optimistic concurrency, unsupported-node round-trip, save/conflict/disconnected states and transaction semantics.
8. Accessibility gates cover post-camera-transform target size, keyboard parity, focus, names, non-color cues, reduced motion and pointer cancel. Performance gates pin baseline hardware/browser, node cap, mount/frame budget, queue/payload/heap/RSS and 20 clients × 300 tags.

## Consequences

Snapshot-to-delta handoff converges without pretending telemetry is lossless. Scene evolution and rendering remain cross-language deterministic, and UI safety/performance are measurable contracts rather than visual review.

## Automated gates

- Task 6: cross-language Scene corpus and canonical hash tests.
- Task 17: snapshot/watermark race, overflow and reconnect FSM tests.
- Tasks 18–19: renderer lifecycle plus Playwright/accessibility-tree gates for pointer/keyboard state-transition parity, exact role/name/state, post-transform target size, non-color cues, reduced motion, pointer cancel and focus across camera/interaction/reconnect.
- Tasks 24–26: real-screen schema validation, editor conflict/transaction tests and the same actionable-control accessibility/focus parity gate for editor interactions.
- Task 28: alarm/trend cursor, backfill and invalid-gap tests.
- Tasks 29 and 34: 20-client performance and final system acceptance gates.
