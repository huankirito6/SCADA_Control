# Web SCADA/HMI Risk-First Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xây dựng nền tảng Web SCADA/HMI on-premise đáp ứng bảy mục tiêu của spec, với process isolation thật, telemetry hội tụ, historian no-silent-loss, HMI/editor data-driven và command path có physical policy + durable audit.

**Architecture:** Hai process production (`Scada.Web`, `Scada.Runtime`) giao tiếp qua authenticated local IPC; application ports tách khỏi versioned protobuf/wire contracts. Web sở hữu identity/config/draft; Runtime sở hữu acquisition, latest state, historian, alarm, command và physical site policy. Browser HMI là Static SSR + JS/SVG renderer + SignalR MessagePack; config/editor là Blazor Interactive Server.

**Tech Stack:** .NET 10 LTS/C# 14, ASP.NET Core/Blazor, gRPC over named pipe/UDS, SignalR MessagePack, SQLite, TypeScript, SVG, xUnit, NetArchTest, Vitest và Playwright.

## Global Constraints

- Đây là supervisory layer, không phải E-stop, SIS, safety interlock, motion safety, burner safety hay logic SIL/PL.
- Browser và `Scada.Web` không giữ device credential, không có driver assembly và không có route trực tiếp tới control network.
- Runtime production là process/service identity riêng trước mọi kết nối hardware thật.
- Scene/config/import là untrusted data; không JavaScript/HTML/SVG tùy ý, `foreignObject`, URL hoặc code scripting.
- Không CDN, online license check hay outbound telemetry; runtime hoạt động khi Internet bị chặn.
- Audit append-only; command/audit fail-closed; operator không xóa audit.
- Write mặc định disabled. Config chỉ được hẹp hơn Runtime-owned physical positive allowlist.
- Telemetry là latest-value/convergence; historian là no-silent-loss sau acceptance; audit/command/alarm có durability riêng.
- Scale target: khoảng 2.000 tag; 100 tag @250 ms, 900 @1 s, 1.000 @5 s; 4–8 connections; 1–20 users; khoảng 300 tag/screen; 1.000 stored sample/s average và 5.000 burst.
- Windows được release-test đầy đủ; Linux/Docker có documented support matrix. Modbus RTU không support trong Docker.
- Không HA, clustering, Kubernetes, multi-tenant, cloud, mobile app, MES/ERP, distributed historian hoặc safety function.
- Mọi task thực hiện theo Red → Green → Refactor/verify → commit; không ghép task khi gate của task trước chưa xanh.

## Decisions Locked By Review

1. In-process Runtime chỉ là test adapter; Tasks 3 và 12 phải đóng two-process/service-identity production gate trước simulator-to-hardware transition ở Task 20.
2. Minimal immutable config/publish/activation thuộc foundation, trước scan-budget validation.
3. Activation semantic/envelope validation là all-or-nothing; connectivity failure tạo `ActiveDegraded`.
4. `TagQuality` tách severity, reason flags và native status; trend không OR raw code.
5. Runtime sở hữu `Stale`; browser chỉ sở hữu `RuntimeDisconnected`.
6. Scene schema có một nguồn JSON Schema/widget manifest; server quyết canonical bytes/hash.
7. Command consume one-time capability và durable `DispatchIntent` trước device I/O.
8. Canonical `SceneId`/`SceneRevision`/`SceneHash` là durable audit context mặc định, không phải Runtime authorization authority.
9. Historian partition query theo batch rồi merge; không `ATTACH` toàn retention range.
10. `alarms.db` tách khỏi historian và audit; Runtime là writer duy nhất.

## Target File Structure

```text
Scada.sln
global.json
Directory.Build.props
Directory.Packages.props
src/
  Scada.Domain/
  Scada.Application/
  Scada.Contracts/
  Scada.Runtime/
  Scada.Drivers.Abstractions/
  Scada.Drivers.Simulator/
  Scada.Drivers.ModbusTcp/
  Scada.Drivers.ModbusRtu/
  Scada.Drivers.OpcUa/
  Scada.Infrastructure.Sqlite/
  Scada.Web/
  Scada.Web.Client/
  Scada.Cli/
tests/
  Scada.ArchitectureTests/
  Scada.Domain.Tests/
  Scada.Runtime.Tests/
  Scada.Driver.Tests/
  Scada.Command.Tests/
  Scada.Alarm.Tests/
  Scada.Web.Tests/
  Scada.IntegrationTests/
  Scada.SecurityTests/
  Scada.LoadTests/
  Scada.Web.E2E/
perf/
  fixtures/
  baselines/
deploy/
  windows/
  linux/
  docker/
docs/
  adr/
  operations/
  support/
```

---

## Phase 0 — Design closure and foundation

### Task 1: Amend the spec and record irreversible decisions

**Files:**
- Modify: `docs/superpowers/specs/2026-08-04-web-scada-hmi-design.md`
- Create: `docs/adr/0001-process-and-ipc-boundary.md`
- Create: `docs/adr/0002-quality-and-time-model.md`
- Create: `docs/adr/0003-config-activation.md`
- Create: `docs/adr/0004-command-authority-and-durability.md`
- Create: `docs/adr/0005-historian-durability-and-partitioning.md`
- Create: `docs/adr/0006-scene-authority-and-telemetry-fsm.md`

**Produces:** Approved contracts for every P0 in the spec review.

- [ ] **Step 1: Patch contradictions explicitly**

Write the decisions from `docs/superpowers/reviews/2026-08-09-web-scada-hmi-spec-review.md` into the source spec: separate process before hardware, physical tuple policy, capability + `DispatchIntent`, quality masks, no-silent-loss, stale formula, snapshot watermark, minimal publish in foundation and `alarms.db` ownership.

- [ ] **Step 2: Run the ambiguity scan**

Run:

```powershell
Select-String -Path docs/superpowers/specs/2026-08-04-web-scada-hmi-design.md -Pattern 'TBD|TODO|quyết sau|phải chốt|~[0-9]+h|ngày công|FTE|tháng'
```

Expected: no unresolved design marker relevant to implementation; schedule/staffing text is removed from the implementation-driving sections.

- [ ] **Step 3: Review traceability**

Create a table mapping every P0/P1 finding to an ADR and a later task number in this plan. Expected: every P0 has an ADR and automated gate.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs docs/superpowers/reviews docs/adr
git commit -m "docs: close SCADA architecture blockers"
```

### Task 2: Bootstrap repository, toolchain and deterministic builds

**Files:**
- Create: `.gitignore`
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `Scada.sln`
- Create: `src/Scada.Web.Client/package.json`
- Create: `src/Scada.Web.Client/package-lock.json`
- Create: `src/Scada.Web.Client/tsconfig.json`
- Create: `src/Scada.Web.Client/eslint.config.js`
- Test: `tests/Scada.IntegrationTests/BuildMetadataTests.cs`

**Produces:** `net10.0` solution and locked frontend dependency graph.

- [ ] **Step 1: Write the failing build-metadata test**

```csharp
[Fact]
public void EveryProductAssemblyTargetsNet10AndHasDeterministicBuild()
{
    RepoBuildMetadata.AssertTargetFramework("net10.0");
    RepoBuildMetadata.AssertProperty("Deterministic", "true");
    RepoBuildMetadata.AssertWarningsAsErrors();
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Scada.IntegrationTests/Scada.IntegrationTests.csproj --filter BuildMetadataTests`  
Expected: FAIL because solution metadata does not exist.

- [ ] **Step 3: Add deterministic project defaults**

Pin SDK floor `10.0.100` with roll-forward to the newest installed .NET 10 feature band; set nullable, implicit usings, deterministic build, analyzers and warnings-as-errors. Use Node 24 for TypeScript tooling and commit `package-lock.json`.

- [ ] **Step 4: Verify green and offline restore cache behavior**

Run: `dotnet restore --locked-mode; dotnet build -c Release; npm ci --prefix src/Scada.Web.Client; npm test --prefix src/Scada.Web.Client`  
Expected: all commands exit 0.

- [ ] **Step 5: Commit**

```bash
git add .gitignore global.json Directory.* Scada.sln src tests
git commit -m "build: bootstrap deterministic SCADA solution"
```

### Task 3: Enforce project graph and privilege boundaries

**Files:**
- Create: product `.csproj` files under `src/`
- Create: `tests/Scada.ArchitectureTests/DependencyRulesTests.cs`
- Create: `tests/Scada.SecurityTests/DeploymentClosureTests.cs`

**Interfaces:**
- Produces application layer `Scada.Application` independent of protobuf.
- Produces wire-only `Scada.Contracts`.

- [ ] **Step 1: Write failing architecture rules**

```csharp
[Fact]
public void WebCannotReferenceDriversRuntimeOrSqlite()
    => Architecture.AssertNoReferences("Scada.Web", "Scada.Runtime", "Scada.Drivers", "Microsoft.Data.Sqlite");

[Fact]
public void DomainHasNoProductDependencies()
    => Architecture.AssertOnlySystemReferences("Scada.Domain");
```

- [ ] **Step 2: Add exact references**

`Domain ← Application ← Runtime/Web`; `Contracts` is mapped by adapters only. Only `Scada.Infrastructure.Sqlite` references `Microsoft.Data.Sqlite`; only Runtime references driver projects; Web deployment closure must not contain driver DLLs.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.ArchitectureTests; dotnet test tests/Scada.SecurityTests --filter DeploymentClosureTests`  
Expected: PASS and Web publish directory contains no `Scada.Drivers.*.dll`.

- [ ] **Step 4: Commit**

```bash
git add src tests
git commit -m "test: enforce SCADA architecture boundaries"
```

### Task 4: Implement typed values, semantic identity and quality

**Files:**
- Create: `src/Scada.Domain/Tags/TagId.cs`
- Create: `src/Scada.Domain/Tags/TagValue.cs`
- Create: `src/Scada.Domain/Tags/TagQuality.cs`
- Create: `src/Scada.Domain/Tags/TagSemanticIdentity.cs`
- Test: `tests/Scada.Domain.Tests/Tags/TagQualityTests.cs`
- Test: `tests/Scada.Domain.Tests/Tags/SemanticHashTests.cs`

**Produces:**

```csharp
public enum QualitySeverity : byte { Good = 0, Uncertain = 1, Bad = 2 }
[Flags] public enum QualityReason : ushort { CommFail = 1, DeviceError = 2, ConfigError = 4, Stale = 8, LastKnown = 16, OutOfRange = 32, NotInitialized = 64, Simulated = 128, Forced = 256 }
public readonly record struct TagQuality(QualitySeverity Severity, QualityReason Reasons, uint? NativeStatus);
public sealed record TagSemanticIdentity(string SourceBindingHash, string ValueMeaningHash, string PhysicalTargetDigest);
```

- [ ] **Step 1: Write failing algebra and hash tests**

Assert that mixed Uncertain+Bad produces `WorstSeverity=Bad`, reason flags are preserved, no undefined severity is created, and changing endpoint/unit/address/type/scaling/unit changes the relevant hash.

- [ ] **Step 2: Implement immutable discriminated `TagValue`**

Support Bool, Int16, Int32, Int64, Float32, Float64, String and Enum without coercion; reject NaN/Infinity in config and preserve Int64 beyond JavaScript `2^53` as string/BigInt wire representation.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Domain.Tests --filter "TagQualityTests|SemanticHashTests"`  
Expected: PASS for all quality combinations and semantic hash vectors.

- [ ] **Step 4: Commit**

```bash
git add src/Scada.Domain tests/Scada.Domain.Tests
git commit -m "feat: define typed tag and quality primitives"
```

### Task 5: Implement logical time and sample identity

**Files:**
- Create: `src/Scada.Application/Time/ILogicalClock.cs`
- Create: `src/Scada.Runtime/Time/MonotonicLogicalClock.cs`
- Create: `src/Scada.Runtime/Time/ClockState.cs`
- Create: `src/Scada.Domain/Tags/SampleStamp.cs`
- Test: `tests/Scada.Runtime.Tests/Time/MonotonicLogicalClockTests.cs`

**Produces:**

```csharp
public readonly record struct SampleStamp(long LogicalTsUs, long? SourceTsUs, long MonotonicTicks, Guid BootId, long StateRevision);
public interface ILogicalClock { SampleStamp Next(long? sourceTsUs = null); ClockHealth Health { get; } }
```

- [ ] **Step 1: Add compile-only typed test seam, then write fake-clock failures**

Create the Runtime test project and a typed compile-only `MonotonicLogicalClock` skeleton with the required constructor/API but intentionally incorrect behavior. The skeleton exists solely so tests call the production API directly; it must not return canned results, contain production ordering logic, or be presented as RED evidence. Then write fake-`TimeProvider` tests for NTP step backward/forward, many tags in one microsecond, process restart with OS time below persisted high-water, and OPC UA source timestamps arriving out of order.

- [ ] **Step 2: Verify behavioral RED**

Run: `dotnet test tests/Scada.Runtime.Tests --filter MonotonicLogicalClockTests`
Expected: test assembly compiles and scenario assertions fail because the typed skeleton produces incorrect logical-time behavior. A missing type/constructor, reflection lookup assertion, restore error, or compilation error is not valid RED evidence. Preserve the test names and failure output in the local Task 5 report.

- [ ] **Step 3: Implement one Runtime-wide clock**

Replace the incorrect skeleton behavior with one serialized Runtime-wide clock. Anchor logical UTC to monotonic elapsed; never re-anchor backward; on a forward wall-clock deviation, re-anchor only forward and expose a typed `ClockDeviation` hook/event record for Task 9. Persist high-water checkpoints before returning a sample. File checkpoints must use a same-directory unique temporary path, flush to disk, use `File.Replace` when replacing an existing Windows destination and an atomic move only for first creation, and clean up only the temporary file owned by the operation. Corrupt/null checkpoint, save/access failure, and timestamp/revision overflow must fail closed: set `ClockDegraded` and return no sample. Use logical time as ordering key; retain source time only as metadata. Audit append integration is deliberately deferred to Task 9 because `IAuditAppender` does not exist yet; Task 5 must expose the typed event data required for that later wiring, but must not create an audit API early.

- [ ] **Step 4: Verify GREEN, durability and handoff**

Run: `dotnet test tests/Scada.Runtime.Tests --filter MonotonicLogicalClockTests`
Expected: strictly increasing logical stamps across all scenarios; backward wall step never creates per-tag clocks; forward deviation produces a typed hook record without moving backward; restart and successful file-store replacement preserve high-water order; corrupt/null checkpoint, save/access failure, and overflow emit no sample and set `ClockDegraded`. Replace initial reflection-only tests with direct typed tests after the valid RED has been recorded. Record the Task 9 audit-event handoff fields: event kind (`ClockReanchored` or `ClockDeviationDetected`), boot ID, prior/new anchor logical microseconds, observed wall logical microseconds, monotonic ticks, and state revision.

- [ ] **Step 5: Commit**

```bash
git add src tests/Scada.Runtime.Tests
git commit -m "feat: add persisted monotonic logical clock"
```

### Task 6: Define the authoritative Scene contract

**Files:**
- Create: `src/Scada.Contracts/Scenes/scene.schema.json`
- Create: `src/Scada.Contracts/Scenes/widget-manifest.json`
- Create: `src/Scada.Application/Scenes/ISceneCanonicalizer.cs`
- Create: `src/Scada.Web.Client/src/scene/schema.generated.ts`
- Create: `tests/fixtures/scenes/schema-v1/*.json`
- Test: `tests/Scada.Domain.Tests/Scenes/SceneCorpusTests.cs`
- Test: `src/Scada.Web.Client/src/scene/scene-corpus.test.ts`

**Produces:** Stable screen/element/asset IDs, fixed canvas/viewBox, ordered layers, box/points/path/link geometry, typed bindings/actions, parameters, reserved group/instance/tagScope, limits and forward-only migrations.

- [ ] **Step 1: Write shared accept/reject corpus**

Include valid scenes and malicious cases: unknown widget, arbitrary `attr:*`, `javascript:`/URL, NaN/Infinity, dangling link, cycle, recursion, oversized nesting/vertices/path/string and unknown schema version.

- [ ] **Step 2: Generate C# and TypeScript models**

Server owns migration and canonical bytes/hash. Client uses generated types/validator. Geometry quantization is an explicit editor operation; canonical serialization preserves numeric meaning.

- [ ] **Step 3: Verify cross-language conformance**

Run: `dotnet test tests/Scada.Domain.Tests --filter SceneCorpusTests; npm test --prefix src/Scada.Web.Client -- scene-corpus.test.ts`  
Expected: C# and TypeScript accept/reject the same corpus; server canonical bytes are stable across `en-US` and `tr-TR`.

- [ ] **Step 4: Commit**

```bash
git add src tests/fixtures
git commit -m "feat: define canonical Scene contract"
```

### Task 7: Establish database ownership and migrations

**Files:**
- Create: `src/Scada.Infrastructure.Sqlite/Migrations/Config/*`
- Create: `src/Scada.Infrastructure.Sqlite/Migrations/Historian/*`
- Create: `src/Scada.Infrastructure.Sqlite/Migrations/AuditWeb/*`
- Create: `src/Scada.Infrastructure.Sqlite/Migrations/AuditRuntime/*`
- Create: `src/Scada.Infrastructure.Sqlite/Migrations/Alarms/*`
- Create: `tests/Scada.IntegrationTests/DatabaseOwnershipTests.cs`
- Create: `tests/Scada.IntegrationTests/MigrationCrashTests.cs`

**Produces:** Web migrates `config.db`/`audit-web.db`; Runtime migrates `historian catalog/partitions`, `audit-runtime.db`, `alarms.db`; CLI only orchestrates services offline.

- [ ] **Step 1: Write ownership and crash tests**

Assert wrong process identity cannot write another owner’s DB; each forward-only migration resumes or rolls back cleanly after fault injection at every statement boundary.

- [ ] **Step 2: Implement migration ledgers and locks**

Each DB has schema version, migration checksum and single-owner lock. Startup refuses network-path DBs and refuses incompatible newer schemas.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.IntegrationTests --filter "DatabaseOwnershipTests|MigrationCrashTests"`  
Expected: PASS on Windows filesystem and temp local-volume fixture.

- [ ] **Step 4: Commit**

```bash
git add src/Scada.Infrastructure.Sqlite tests/Scada.IntegrationTests
git commit -m "feat: establish single-owner database migrations"
```

### Task 8: Implement immutable config, publish and activation

**Files:**
- Create: `src/Scada.Domain/Configuration/ConfigVersion.cs`
- Create: `src/Scada.Domain/Configuration/ConfigActivation.cs`
- Create: `src/Scada.Application/Configuration/IConfigVersionStore.cs`
- Create: `src/Scada.Application/Configuration/IActivationStore.cs`
- Create: `src/Scada.Web/Configuration/PublishService.cs`
- Create: `src/Scada.Runtime/Configuration/ActivationCoordinator.cs`
- Test: `tests/Scada.IntegrationTests/Configuration/ActivationStateMachineTests.cs`
- Test: `tests/Scada.Domain.Tests/Configuration/CanonicalConfigTests.cs`

**Produces:** `Desired → Validating → Preparing → Active | ActiveDegraded | Rejected`, unique `activation_id`, immutable canonical version and atomic active pointer.

- [ ] **Step 1: Write failing canonical/publish tests**

Cover key order, Unicode, cultures, floats, `schemaVersion` inclusion, byte-for-byte canonical output and server-authoritative hash recomputation/mismatch rejection, plus publish crash before/after commit, re-activation of an old version and poll recovery after notification loss.

- [ ] **Step 2: Implement activation semantics**

Reject whole activation on schema/hash/envelope failure. Prepare resources, atomically switch active pointer, retain previous resources until switch. Connectivity failures yield `ActiveDegraded` with per-resource `tag_load_status`.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Domain.Tests --filter CanonicalConfigTests; dotnet test tests/Scada.IntegrationTests --filter ActivationStateMachineTests`  
Expected: PASS; unchanged resources retain identity across activation.

- [ ] **Step 4: Commit**

```bash
git add src tests
git commit -m "feat: add immutable config activation state machine"
```

### Task 9: Build audit chains, seals and verification CLI

**Files:**
- Create: `src/Scada.Application/Audit/IAuditAppender.cs`
- Create: `src/Scada.Application/Audit/IAuditSealer.cs`
- Create: `src/Scada.Infrastructure.Sqlite/Audit/HashChainAppender.cs`
- Create: `src/Scada.Infrastructure.Sqlite/Audit/AuditVerifier.cs`
- Create: `src/Scada.Cli/Commands/AuditVerifyCommand.cs`
- Test: `tests/Scada.SecurityTests/Audit/AuditTamperTests.cs`
- Test: `tests/Scada.SecurityTests/Audit/AuditSealLifecycleTests.cs`
- Test: `tests/Scada.SecurityTests/Audit/AuditSealerProviderTests.cs`

**Produces:** SHA-256 chain with canonical event bytes, chain ID, monotonic sequence, genesis, previous hash, build/boot/runtime IDs and signed external head seal.

- [ ] **Step 1: Write tamper/failure tests**

Modify, delete, reorder and truncate events; replace genesis; roll back a DB; create cross-chain command causality. Add a Task 5 logical-clock handoff matrix: append canonical `ClockReanchored` and `ClockDeviationDetected` events from the Task 5 typed hook, asserting event kind, boot ID, prior/new anchor logical microseconds, observed wall logical microseconds, monotonic ticks, and state revision; reject incomplete/malformed payloads and prove a failed append makes the Runtime health/mutation path fail closed according to the audit durability policy. With a fake clock, test seals at exactly 100 events or 60 seconds (whichever first), forced boot/clean-shutdown/policy/key-rotation seals, no early/late off-by-one, sink unavailable through the 120-second overdue boundary and recovery. On Windows, Linux and Docker support fixtures, record provider capability outcome; distinguish permission to sign from permission to export, prove private-key export fails in non-exportable provider mode even for `AuditSealer`, and prove Web/Runtime cannot sign or export. Test required-provider absence/failed probe at startup, allowed-fallback ACL/custody, dual-signed rotation, retained public trust history and key-loss epoch discontinuity. Expected verifier identifies the exact chain break and never claims a cryptographic total order between chains.

- [ ] **Step 2: Implement durability profiles**

Audit/command DB uses WAL + `synchronous=FULL`; append is one transaction. Seal sink writes signed head files to an operator-configured append-only directory outside application ACL. A separate `AuditSealer` identity owns an OS-protected key that is non-exportable where supported. Commissioning selects `RequireNonExportable` or `AllowProtectedSoftwareFallback` and persists the provider/capability outcome: Windows prefers a probed CNG machine-key provider; Linux a configured PKCS#11 TPM/HSM; Docker an explicitly mounted PKCS#11/TPM/HSM provider/device. The fallback is a protected-at-rest software key with signer-only ACL (dedicated secret volume in Docker; never env/image/ordinary backup) and a documented limitation that host administrators can recover software key material. Required-provider absence/failed probe makes sealer startup unhealthy and command/publish mutations fail closed; explicitly allowed fallback verifies ACL, reports degraded custody and remains operational. Seal after at most 100 events or 60 seconds; boot, clean shutdown and policy/key rotation force a seal. At 120 seconds overdue, raise a system alarm, degrade health and fail closed command/publish mutations. Rotation is dual-signed and public trust history is retained; key loss starts an explicitly discontinuous epoch through local administrator recovery.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.SecurityTests --filter "AuditTamperTests|AuditSealLifecycleTests|AuditSealerProviderTests"; dotnet run --project src/Scada.Cli -- audit verify --fixture tests/fixtures/audit/valid`
Expected: PASS for cadence/key/sink lifecycle and every declared platform/provider/fallback outcome; required-provider absence is fail-closed, fallback custody/ACL and host-admin limitation are evidenced; exit code 0 for valid fixture and nonzero for each tampered fixture.

- [ ] **Step 4: Commit**

```bash
git add src tests
git commit -m "feat: add tamper-evident audit verification"
```

### Task 10: Implement identity, deny-by-default RBAC and command capability

**Files:**
- Create: `src/Scada.Domain/Security/Permission.cs`
- Create: `src/Scada.Application/Security/IAuthorizationSnapshot.cs`
- Create: `src/Scada.Application/Security/ICommandCapabilityIssuer.cs`
- Create: `src/Scada.Runtime/Security/CommandCapabilityService.cs`
- Create: `src/Scada.Web/Security/PermissionPolicies.cs`
- Test: `tests/Scada.SecurityTests/Identity/PermissionMatrixTests.cs`
- Test: `tests/Scada.SecurityTests/Identity/CommandCapabilityReplayTests.cs`

**Produces:** permissions for view, command, ack, shelve, draft, publish, rollback, identity, cert, backup, restore, audit and policy operations; one-time command capability.

- [ ] **Step 1: Write deny/replay tests**

Cover missing permission, stale circuit, disabled user, expiry, replayed nonce, changed tag/value/hash/channel and revoked authorization snapshot. Issue otherwise-valid capabilities, then submit one under a different subject, one to the wrong Runtime/site/service audience, and one issued under active policy version/digest N after policy N+1 activates. Each case must produce a typed/audited `CapabilityBindingMismatch` reason before `DispatchIntent` and before any fake driver call, leave dispatch unavailable until a fresh capability is issued, and never be accepted merely because signature/expiry/nonce are otherwise valid. Also assert no emergency role/header/token/path can bypass the permission matrix. Local account recovery must leave global writes disabled until normal identity, policy and sealer-health gates pass.

- [ ] **Step 2: Implement bootstrap and revocation rules**

No default password. First-run and account-recovery admin ceremonies are OS-local, audited and expire after completion. Service accounts are non-interactive. Circuit/session authorization is rechecked at sensitive endpoint execution. Product scope has no break-glass bypass; recovery creates/restores a normal administrator and never bypasses command policy.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.SecurityTests --filter "PermissionMatrixTests|CommandCapabilityReplayTests"`  
Expected: all unauthorized/replayed/rebound/stale-policy mutations are typed and audited; changed subject, wrong audience and N→N+1 policy cases create no `DispatchIntent` or driver I/O and require a fresh capability.

- [ ] **Step 4: Commit**

```bash
git add src tests/Scada.SecurityTests
git commit -m "feat: add RBAC and one-time command capability"
```

### Task 11: Implement Runtime-owned physical site policy

**Files:**
- Create: `src/Scada.Domain/Security/PhysicalTarget.cs`
- Create: `src/Scada.Application/Security/IRuntimeSitePolicy.cs`
- Create: `src/Scada.Runtime/Security/RuntimeSitePolicyLoader.cs`
- Create: `src/Scada.Runtime/Security/PhysicalTargetPolicyEvaluator.cs`
- Test: `tests/Scada.SecurityTests/RuntimePolicy/ConfigRemapAttackTests.cs`

**Produces:** administrator/root-owned, ACL deny-write, atomically replaced local policy file with version/digest, global write kill switch and exact read/browse/write positive allowlists; no physical-policy signature or signing key.

- [ ] **Step 1: Write remap attack matrix**

Starting from an otherwise accepted mapping, widen exactly one independent tuple dimension at a time: driver; endpoint/device identity; unit/node; function; access mode; address; datatype; byte order; word order; raw/engineering transform; raw range; engineering range; write mode; max rate; pulse. Every one-field widening must reject before config activation and before command dispatch/driver I/O; the unchanged exact mapping and strictly narrower variants are the only passing controls.

- [ ] **Step 2: Implement fail-closed load**

Administrator/root updates use temp-write + flush + atomic replace. On restart, Runtime verifies owner, directory/file ACL, schema, monotonic policy version and canonical digest before enabling writes. Test missing, malformed/torn, wrong-owner and invalid-ACL loads independently; each disables writes before activation/dispatch. Test that Web and every service identity are denied policy-path updates, an authorized atomic update is not active before restart, and after restart the exact new version/digest is loaded with audit + forced seal evidence. There is no policy signature or policy-signing key.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.SecurityTests --filter ConfigRemapAttackTests`  
Expected: every independent one-field widening and every missing/invalid/unauthorized policy or update/load path fails closed before activation/dispatch; only exact/narrower config under the atomically loaded administrator policy passes after restart.

- [ ] **Step 4: Commit**

```bash
git add src tests/Scada.SecurityTests
git commit -m "feat: enforce physical Runtime site policy"
```

### Task 12: Create authenticated two-process IPC and config feed

**Files:**
- Create: `src/Scada.Contracts/Runtime/runtime_control.proto`
- Create: `src/Scada.Contracts/Configuration/config_feed.proto`
- Create: `src/Scada.Runtime/Ipc/RuntimeControlEndpoint.cs`
- Create: `src/Scada.Web/Ipc/ConfigurationFeedEndpoint.cs`
- Create: `src/Scada.Web/RuntimeClient/GrpcRuntimeClient.cs`
- Test: `tests/Scada.IntegrationTests/Ipc/PrivilegeBoundaryTests.cs`
- Test: `tests/Scada.IntegrationTests/Ipc/ContractCompatibilityTests.cs`

**Consumes:** Application ports from Tasks 8–11.  
**Produces:** Windows named pipe, Linux/Docker UDS shared-volume endpoint; mTLS only for explicit TCP fallback.

- [ ] **Step 1: Write process/peer tests**

Launch real child processes with distinct identities; reject wrong SID/UID/certificate, expired capability and contract incompatibility. For config feed, authenticate the peer before returning a pointer/artifact; assert Runtime recomputes the hash over the received canonical bytes, accepts and caches only an exact pointer-hash match, and on mismatch rejects the artifact while retaining the last verified local active cache. Stop Web and assert Runtime scan loop remains alive from that verified cache. Config artifacts have no signature/config-signing key; Runtime physical policy is administrator/root-owned and ACL-protected with no signing key, while backup package signing remains a separate contract.

- [ ] **Step 2: Implement mapping adapters**

Generated protobuf types never enter Domain/Application APIs. Map/copy `Int64`, typed values, quality width and timestamps explicitly. Runtime pulls the published pointer and immutable canonical bytes from the authenticated Web config feed, verifies the recomputed canonical hash against the pointer, and caches only the verified active version locally.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.IntegrationTests --filter "PrivilegeBoundaryTests|ContractCompatibilityTests"`  
Expected: PASS on the current OS; unauthorized peer receives no application response.

- [ ] **Step 4: Commit**

```bash
git add src tests/Scada.IntegrationTests
git commit -m "feat: establish authenticated process boundary"
```

---

## Phase 1 — Read path: simulator, historian, telemetry and HMI

### Task 13: Define driver contract and simulator

**Files:**
- Create: `src/Scada.Drivers.Abstractions/IDeviceDriver.cs`
- Create: `src/Scada.Drivers.Abstractions/DriverCapabilities.cs`
- Create: `src/Scada.Drivers.Abstractions/DriverResult.cs`
- Create: `src/Scada.Drivers.Simulator/SimulatorDriver.cs`
- Test: `tests/Scada.Driver.Tests/DriverContractTests.cs`

**Produces:** async connect/disconnect/browse/read/write/subscribe diagnostics with cancellation, deadline, typed partial result, native status/source timestamp and declared thread-safety.

- [ ] **Step 1: Write contract tests**

Cancel read, expire deadline, return one failed item in a successful block, reconnect, reject concurrent write and expose deterministic sine/bool/counter fixtures.

- [ ] **Step 2: Implement simulator without hardware privileges**

Simulator supports clock injection, scripted disconnect, delay, partial failure and timeout-after-execute for later command tests.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Driver.Tests --filter DriverContractTests`  
Expected: PASS; simulator replay is deterministic for a fixed seed.

- [ ] **Step 4: Commit**

```bash
git add src/Scada.Drivers* tests/Scada.Driver.Tests
git commit -m "feat: define driver contract and simulator"
```

### Task 14: Implement scan planner, scheduler and transport arbitration

**Files:**
- Create: `src/Scada.Runtime/Acquisition/ScanPlanner.cs`
- Create: `src/Scada.Runtime/Acquisition/ScanScheduler.cs`
- Create: `src/Scada.Runtime/Acquisition/TransportArbiter.cs`
- Create: `src/Scada.Runtime/Acquisition/ScanBudgetValidator.cs`
- Test: `tests/Scada.Runtime.Tests/Acquisition/ScanSchedulerTests.cs`
- Test: `tests/Scada.Runtime.Tests/Acquisition/StaleTransitionTests.cs`

**Produces:** register coalescing with max gap/max block, virtual-clock schedule, skip-and-count, logical-device breaker and physical-transport arbitration.

- [ ] **Step 1: Write deterministic schedule tests**

Prove no catch-up, p99 jitter calculation, one in-flight RTU request per bus, one bad slave not tripping other logical devices, command quota not starving scan and impossible 9600-baud config warning. In `StaleTransitionTests`, use an injected fake monotonic/logical clock and assert publish rejection immediately below/above the formula bounds, acceptance at both bounds, no transition one tick before `StaleAfterMs`, transition at the exact threshold, large logical-clock advance, scan recovery to current quality and no dependence on wall/source-clock steps. For process restart, seed a persisted `Good` last observation with the previous `BootId`, start Runtime with a new `BootId`, and assert before any fresh scan that it is immediately projected as `Stale + LastKnown`, command is disabled, and the quality-only transition is published and persisted despite deadband/store-rate. Vary the old monotonic ticks and logical timestamp to assert neither is compared/used to retain `Good`; then assert only the first successful scan carrying the new `BootId` clears `Stale`, to that scan's actual quality.

- [ ] **Step 2: Implement fixed scan groups**

Planner validates structured addresses and uses protocol limits. `StaleAfterMs` publish validation follows the locked formula. Within one boot, Runtime stale evaluation is driven only by the injected monotonic/logical clock. Across boots it never compares monotonic ticks: before the current boot's first successful fresh scan, an old-boot persisted observation is projected immediately to `Stale + LastKnown`; its old logical timestamp remains audit/history context only. Quality transitions into and out of Stale are published and persisted as quality-only transitions that bypass historian deadband/store-rate.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Runtime.Tests --filter "ScanSchedulerTests|StaleTransitionTests"`
Expected: PASS; transport trace contains no catch-up burst and the complete fake-clock stale matrix passes.

- [ ] **Step 4: Commit**

```bash
git add src/Scada.Runtime tests/Scada.Runtime.Tests
git commit -m "feat: add deterministic scan scheduler"
```

### Task 15: Implement historian ingest, deadband, heartbeat and gaps

**Files:**
- Create: `src/Scada.Application/Historian/IHistorianWriter.cs`
- Create: `src/Scada.Domain/Historian/HistorianSample.cs`
- Create: `src/Scada.Runtime/Historian/HistorianPipeline.cs`
- Create: `src/Scada.Infrastructure.Sqlite/Historian/SqliteHistorianWriter.cs`
- Test: `tests/Scada.Runtime.Tests/Historian/HistorianPolicyTests.cs`
- Test: `tests/Scada.IntegrationTests/Historian/HistorianDurabilityTests.cs`

**Produces:** stable ingest sequence, typed value columns, logical/source time, quality fields, store-on-change/deadband against last stored value, heartbeat, max store rate and persisted data-gap marker.

- [ ] **Step 1: Write policy tests**

Cover slow drift, bool/enum/string change, quality-only transition, heartbeat bound, noisy tag rate limit, duplicate retry, queue overflow and storage recovery.

- [ ] **Step 2: Implement no-silent-loss contract**

Sample queue is bounded and latest observations remain in Runtime cache. Overflow records exact lost sequence interval; recovery persists the gap and raises `System.Historian.DataGapActive`. Audit/alarm/command never share this queue.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Runtime.Tests --filter HistorianPolicyTests; dotnet test tests/Scada.IntegrationTests --filter HistorianDurabilityTests`  
Expected: no duplicate accepted row; every induced loss has a persisted gap marker.

- [ ] **Step 4: Commit**

```bash
git add src tests
git commit -m "feat: add no-silent-loss historian ingest"
```

### Task 16: Implement historian partitions and `QueryTrend`

**Files:**
- Create: `src/Scada.Domain/Historian/TrendQuery.cs`
- Create: `src/Scada.Application/Historian/ITrendRepository.cs`
- Create: `src/Scada.Infrastructure.Sqlite/Historian/PartitionCatalog.cs`
- Create: `src/Scada.Infrastructure.Sqlite/Historian/SqliteTrendRepository.cs`
- Test: `tests/Scada.IntegrationTests/Historian/TrendCorrectnessTests.cs`
- Test: `tests/Scada.IntegrationTests/Historian/HistorianRetentionPinTests.cs`
- Test: `tests/Scada.LoadTests/HistorianConcurrentReadWriteTests.cs`

**Produces:** per-tag seed, buckets with first/last/min/max/time-weighted sum/durations/quality masks, partition batch merge and retention pin/refcount.

- [ ] **Step 1: Write hand-calculated query tests**

Cover seed across partition boundary, Bad/Stale duration exclusion, min/max spike preservation, bool/enum duration/count, string rejection for numeric aggregate, semantic revision marker and NTP backward fixture. Add a 12-week fixture (strictly more than SQLite's default attach limit of 10 databases) with a seed before the requested range and hand-calculated buckets spanning all partitions. Query the complete range and assert the exact seed plus every expected bucket. Instrument repository partition-open/batch diagnostics with configured batch size 4; assert at least three bounded batches are observed, every batch/open set is ≤4, and no connection executes a whole-range `ATTACH`.

- [ ] **Step 2: Implement bounded partition query**

Open/query partitions in bounded batches, merge decomposable aggregates and keep correct seed. Expose deterministic batch/open diagnostics for the acceptance test. Do not attach the full retention range. Retention deletes only unpinned closed partitions.

- [ ] **Step 3: Run concurrency acceptance**

First hold a read cursor open on an eligible closed partition and observe its pin/refcount; invoke retention and assert the catalog entry and file remain. Release/dispose the reader, invoke retention again and assert the now-eligible catalog entry and file are deleted. Then run: `dotnet test tests/Scada.IntegrationTests --filter "TrendCorrectnessTests|HistorianRetentionPinTests"; dotnet test tests/Scada.LoadTests --filter HistorianConcurrentReadWriteTests -c Release`
Expected: the 12-week exact seed/bucket query completes through observable ≤4-partition batches without whole-range `ATTACH`; deletion is blocked while pinned and succeeds after release; load sustains 1.000 stored rows/s average, 5.000 burst, two concurrent 8-hour readers, zero unhandled `SQLITE_BUSY`, p99 commit <200 ms, and WAL below the configured threshold within 30 seconds after readers end.

- [ ] **Step 4: Commit**

```bash
git add src tests
git commit -m "feat: add partitioned trend queries"
```

### Task 17: Implement latest-state cache and telemetry subscription FSM

**Files:**
- Create: `src/Scada.Runtime/Telemetry/LatestStateStore.cs`
- Create: `src/Scada.Runtime/Telemetry/TelemetrySubscriptionService.cs`
- Create: `src/Scada.Contracts/Runtime/telemetry.proto`
- Create: `src/Scada.Web/Telemetry/TelemetryHub.cs`
- Create: `src/Scada.Web.Client/src/telemetry/telemetry-store.ts`
- Test: `tests/Scada.IntegrationTests/Telemetry/TelemetryConvergenceTests.cs`
- Test: `src/Scada.Web.Client/src/telemetry/telemetry-store.test.ts`

**Produces:** monotonically increasing latest-state revision, epoch, handle map, snapshot watermark, dirty-map mailbox and `ResyncRequired`.

- [ ] **Step 1: Write race permutations**

Permute snapshot/delta ordering, reconnect, handle remap, queue overflow, lost absolute delta for a tag that then stops changing, duplicate frame and stale generation.

- [ ] **Step 2: Implement protocol FSM**

Client states: `Bootstrapping`, `Live`, `Lagging`, `Resyncing`, `Offline`, `Fatal`. Snapshot at `W`; apply only deltas with revision `>W` and matching generation/epoch. Any non-coalescible overflow forces resync.

- [ ] **Step 3: Verify convergence and fan-out**

Run: `dotnet test tests/Scada.IntegrationTests --filter TelemetryConvergenceTests; npm test --prefix src/Scada.Web.Client -- telemetry-store.test.ts`  
Expected: client equals server latest state within 2.000 ms after every induced resync and never applies wrong-epoch data.

- [ ] **Step 4: Commit**

```bash
git add src tests
git commit -m "feat: add atomic telemetry subscription FSM"
```

### Task 18: Build L2 renderer lifecycle and diagnostics

**Files:**
- Create: `src/Scada.Web.Client/src/renderer/renderer-session.ts`
- Create: `src/Scada.Web.Client/src/renderer/scene-renderer.ts`
- Create: `src/Scada.Web.Client/src/renderer/quality-presentation.ts`
- Create: `src/Scada.Web.Client/src/renderer/screen-inspector.ts`
- Test: `src/Scada.Web.Client/src/renderer/renderer-session.test.ts`

**Produces:**

```ts
interface RendererSession {
  applyValues(frame: TelemetryFrame): void;
  applyStructural(ops: readonly StructuralOp[]): void;
  setCamera(camera: Camera): void;
  hitTest(point: ScreenPoint): ElementId | null;
  inspect(): RendererDiagnostics;
  dispose(): void;
}
```

- [ ] **Step 1: Write lifecycle/boundary tests**

Repeated mount/unmount, double dispose, font readiness, navigation, root replacement and reconnect must leave exactly one rAF loop/listener set. Lint must reject editor imports/querySelector/parent-child traversal into renderer internals.

- [ ] **Step 2: Implement hot/cold paths**

One `<g>` per element, handle arrays by index, no structural operation in run update loop, format-then-compare for text, no object/array/closure allocation per value update, `AbortController` for teardown.

- [ ] **Step 3: Verify**

Run: `npm test --prefix src/Scada.Web.Client -- renderer-session.test.ts; npm run lint --prefix src/Scada.Web.Client`  
Expected: PASS and leak counters return to zero after every dispose.

- [ ] **Step 4: Commit**

```bash
git add src/Scada.Web.Client
git commit -m "feat: add lifecycle-safe SVG renderer"
```

### Task 19: Deliver Static SSR HMI shell and validity UX

**Files:**
- Create: `src/Scada.Web/Components/Pages/Hmi.razor`
- Create: `src/Scada.Web/Components/Layout/HmiStaticLayout.razor`
- Create: `src/Scada.Web/wwwroot/js/hmi-bootstrap.js`
- Create: `tests/Scada.Web.E2E/telemetry-reconnect.spec.ts`
- Create: `tests/Scada.Web.E2E/touch-accessibility.spec.ts`

**Produces:** fixed canvas + scale-to-fit/letterbox, Static SSR with zero Blazor circuit, screen-level invalid count, age/timestamp, non-color cues and command-disabled validity states.

- [ ] **Step 1: Write Playwright failures**

Assert zero Blazor circuit on HMI page and retain Bootstrapping/Stale/Bad/Offline pattern + icon + text cues, 44×44 CSS-pixel hit targets after camera transform, reduced-motion static alarm cue and touch `pointercancel` recovery. For every actionable control in the fixture, use Playwright's accessibility snapshot/tree to assert the expected role, a correct non-empty accessible name, and the applicable state (`disabled`, `checked`, `pressed`, `expanded` or value). Drive the same action once by pointer and once by keyboard navigation/activation and assert the identical application state transition and command eligibility. Keep the focused control/element stable and visibly focused across camera pan/zoom, pointer or keyboard interaction, and telemetry reconnect/resync unless that control is removed, in which case focus moves to the documented fallback.

- [ ] **Step 2: Implement state matrix**

Do not render last-known as normal. Use pattern + icon + text, not color alone. `RuntimeDisconnected` is transport state and does not mutate tag quality. All command affordances are disabled outside Live+Good unless a stricter Runtime policy still denies them.

- [ ] **Step 3: Verify**

Run: `npx playwright test --config tests/Scada.Web.E2E/playwright.config.ts telemetry-reconnect.spec.ts touch-accessibility.spec.ts`  
Expected: PASS at 100%, 150% and 200% browser zoom with complete role/name/state assertions, pointer/keyboard transition parity and focus continuity across camera/interaction/reconnect.

- [ ] **Step 4: Commit**

```bash
git add src/Scada.Web src/Scada.Web.Client tests/Scada.Web.E2E
git commit -m "feat: deliver trustworthy HMI runtime shell"
```

---

## Phase 2 — Hardware read gate and command spike

### Task 20: Add Modbus TCP with physical-target enforcement

**Files:**
- Create: `src/Scada.Drivers.ModbusTcp/ModbusTcpDriver.cs`
- Create: `src/Scada.Drivers.ModbusTcp/ModbusAddress.cs`
- Create: `tests/Scada.Driver.Tests/Modbus/ModbusTcpMappingTests.cs`
- Create: `tests/Scada.SecurityTests/RuntimePolicy/ModbusTargetPolicyTests.cs`
- Create: `tests/Scada.HardwareTests/ModbusTcpReadScenarios.cs`

**Produces:** coils/discrete/input/holding read mapping, endian conversion, exception/native status and policy-limited endpoint/unit/address/function.

- [ ] **Step 1: Write protocol vectors and attack tests**

Cover zero/one-based UI conversion, signedness, float word orders, partial block exception, reconnect and attempts to scan/write an endpoint/unit/function outside policy.

- [ ] **Step 2: Implement read-only hardware gate first**

Driver starts read-only. Hardware mode requires separate Runtime process, policy file, approved endpoint and no Web route to device. Modbus has no protocol authentication; document required OT zone/conduit and endpoint ACL.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Driver.Tests --filter ModbusTcpMappingTests; dotnet test tests/Scada.SecurityTests --filter ModbusTargetPolicyTests`  
Expected: PASS; hardware fixture reads known values and rejects every out-of-policy target.

- [ ] **Step 4: Commit**

```bash
git add src/Scada.Drivers.ModbusTcp tests
git commit -m "feat: add policy-bound Modbus TCP reads"
```

### Task 21: Implement durable command journal and simulator executor

**Files:**
- Create: `src/Scada.Domain/Commands/CommandRequest.cs`
- Create: `src/Scada.Domain/Commands/CommandEvent.cs`
- Create: `src/Scada.Application/Commands/ICommandJournal.cs`
- Create: `src/Scada.Runtime/Commands/CommandExecutor.cs`
- Create: `src/Scada.Runtime/Commands/CommandRecovery.cs`
- Test: `tests/Scada.Command.Tests/CommandCrashBoundaryTests.cs`
- Test: `tests/Scada.Command.Tests/CommandRevisionPreconditionTests.cs`
- Test: `tests/Scada.Command.Tests/CommandSceneAuditContextTests.cs`

**Produces:** Requested/Rejected in Web audit; Authorized/DispatchIntent/Attempt/Outcome in Runtime chain; `PreconditionFailed`, `Verified`, `Consistent`, `Failed`, `Indeterminate` projections.

```csharp
public sealed record CommandRequest(
    Guid CommandId,
    long ActivationId,
    string ActiveConfigHash,
    TagId TagId,
    string SourceBindingHash,
    string ValueMeaningHash,
    string PhysicalTargetDigest,
    long? ExpectedStateRevision,
    TagValue? ExpectedValue,
    TagQuality ObservedQuality,
    long ObservedAtLogicalUs,
    Guid DeviceSessionGeneration,
    string SceneId,
    long SceneRevision,
    string SceneHash,
    TagValue RequestedValue,
    string CapabilityToken);
```

- [ ] **Step 1: Write fault-injection matrix**

Crash before/after capability consume, before/after intent commit, before/after fake protocol send and before outcome commit. Test duplicate idempotency, activation/semantic/target/device-session mismatch, stale quality and changed guarded sample revision. Repeat the otherwise-valid changed-subject, wrong Runtime/site/service audience and policy-version/digest N→N+1 capability cases from Task 10 at the executor boundary; assert a typed/audited rejection, zero `DispatchIntent`, zero fake-driver I/O and mandatory fresh capability. Canonicalize a Scene fixture through the Task 6 authority, submit its canonical `SceneId`, `SceneRevision` and `SceneHash`, then re-read `DispatchIntent` and durable audit rows and assert all three are exact and `SceneHash` equals the hash of the exact canonical Scene bytes, not client/source JSON; changing Scene context alone must not grant authority.

- [ ] **Step 2: Implement exact ordering**

Validate every capability binding, then consume nonce and append durable intent atomically before I/O. Persist canonical `SceneId`/`SceneRevision`/`SceneHash` in the intent/audit transaction as non-authoritative context. One write outstanding per logical device; transport arbiter preserves scan quota. Restart never redispatches an intent without outcome.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Command.Tests --filter "CommandCrashBoundaryTests|CommandRevisionPreconditionTests|CommandSceneAuditContextTests"`
Expected: every ambiguous crash projects `INDETERMINATE`; fake driver call count never exceeds one; rebound/wrong-audience/stale-policy capabilities have no intent/I/O and require replacement; durable Scene context matches the exact canonical Scene bytes hash.

- [ ] **Step 4: Commit**

```bash
git add src tests/Scada.Command.Tests
git commit -m "feat: add durable command state machine"
```

### Task 22: Build command POST endpoint and operator UX

**Files:**
- Create: `src/Scada.Web/Commands/CommandEndpoints.cs`
- Create: `src/Scada.Web/Commands/CommandRequestService.cs`
- Create: `src/Scada.Web.Client/src/commands/command-store.ts`
- Create: `tests/Scada.Web.Tests/Commands/CommandSecurityTests.cs`
- Create: `tests/Scada.Web.E2E/command-precondition.spec.ts`

**Produces:** anti-forgery POST only; no hub command invocation; confirm/capability/pending/outcome UX with no optimistic echo.

- [ ] **Step 1: Write security and UX tests**

CSRF, wrong Origin, stale session, focused input receiving telemetry, config change between confirm/dispatch, sample freshness failure, timeout and Indeterminate guidance.

- [ ] **Step 2: Implement exact state flow**

`Ready → Confirming → AcquiringCapability → Pending → Verified|Consistent|Failed|Indeterminate`; mismatch before intent is `PreconditionFailed → refresh → reconfirm`. Show old/new value, unit, quality, age and active hash context.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Web.Tests --filter CommandSecurityTests; npx playwright test --config tests/Scada.Web.E2E/playwright.config.ts command-precondition.spec.ts`  
Expected: PASS; no client code retries a command automatically.

- [ ] **Step 4: Commit**

```bash
git add src tests
git commit -m "feat: add safe operator command UX"
```

---

## Phase 3 — Reconciliation and real-scene schema gate

### Task 23: Complete resource reconciliation and rollback

**Files:**
- Create: `src/Scada.Runtime/Configuration/ReconciliationPlanner.cs`
- Create: `src/Scada.Runtime/Configuration/ResourceSupervisor.cs`
- Create: `tests/Scada.IntegrationTests/Configuration/ConfigReconciliationTests.cs`

**Produces:** diff by connection/scan group/tag semantic/source/scene; unchanged resources continue without interruption.

- [ ] **Step 1: Write reconciliation tests**

Change only scene, description, path, deadband, source binding and scaling separately; rollback and reactivate old version; inject one offline device.

- [ ] **Step 2: Implement prepare/switch/retire**

Physical source rebind creates source marker and, when it changes the real measurement identity, retires old `TagId` and requires a new one. Value-meaning changes create trend markers. Offline device yields ActiveDegraded, not partial semantic activation.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.IntegrationTests --filter ConfigReconciliationTests`  
Expected: unchanged tags have no scan gap and command preconditions invalidate on relevant hash change.

- [ ] **Step 4: Commit**

```bash
git add src/Scada.Runtime tests/Scada.IntegrationTests
git commit -m "feat: reconcile config resources without global restart"
```

### Task 24: Hand-author the schema validation screens

**Files:**
- Create: `tests/fixtures/scenes/plant-overview.scene.json`
- Create: `tests/fixtures/scenes/motor-detail.scene.json`
- Create: `tests/fixtures/scenes/trend.scene.json`
- Create: `tests/fixtures/scenes/alarm-list.scene.json`
- Create: `tests/fixtures/scenes/dense-300.scene.json`
- Test: `tests/Scada.Web.E2E/manual-scenes.spec.ts`

**Produces:** 3–5 real screens totaling about 300 elements, using only schema/widget manifest features.

- [ ] **Step 1: Build fixtures without editor code**

Use overview, parameterized motor detail with command input, trend, alarm list and dense screen. Include Good/Uncertain/Bad/Stale/Disconnected states and source/value semantic revision markers.

- [ ] **Step 2: Run schema and renderer tests**

Run: `dotnet test tests/Scada.Domain.Tests --filter SceneCorpusTests; npx playwright test --config tests/Scada.Web.E2E/playwright.config.ts manual-scenes.spec.ts`  
Expected: every fixture validates, mounts, unmounts and exposes correct inspector metrics.

- [ ] **Step 3: Amend schema only through migration**

Any gap discovered becomes a versioned schema/manifest change, a forward migration and a golden corpus entry before editor work starts.

- [ ] **Step 4: Commit**

```bash
git add tests/fixtures tests/Scada.Web.E2E src/Scada.Contracts/Scenes
git commit -m "test: validate Scene schema with real HMI screens"
```

---

## Phase 4 — Editor MVP

### Task 25: Implement editor store and transaction log

**Files:**
- Create: `src/Scada.Web.Client/src/editor/editor-store.ts`
- Create: `src/Scada.Web.Client/src/editor/transactions.ts`
- Create: `src/Scada.Web.Client/src/editor/spatial-index.ts`
- Test: `src/Scada.Web.Client/src/editor/transactions.test.ts`

**Produces:** one JS authoritative editor model with begin/dispatch/commit/cancel/undo/redo and derived spatial index.

- [ ] **Step 1: Write gesture transaction tests**

Hundreds of pointer moves create exactly one undo entry; `pointercancel`/lost capture restores exact pre-state; undo/redo preserves canonical scene; editor never mutates L2 DOM.

- [ ] **Step 2: Implement broad/narrow hit testing**

Broad phase uses model index for opacity-0, groups, clips and off-viewport elements; narrow phase calls published `RendererSession.hitTest`. Use Pointer Events and camera state shared through one module.

- [ ] **Step 3: Verify**

Run: `npm test --prefix src/Scada.Web.Client -- transactions.test.ts; npm run lint --prefix src/Scada.Web.Client`  
Expected: PASS and restricted DOM/import rules remain enforced.

- [ ] **Step 4: Commit**

```bash
git add src/Scada.Web.Client
git commit -m "feat: add transactional Scene editor core"
```

### Task 26: Implement editor MVP interactions and persistence

**Files:**
- Create: `src/Scada.Web.Client/src/editor/gestures.ts`
- Create: `src/Scada.Web.Client/src/editor/property-panel.ts`
- Create: `src/Scada.Web.Client/src/editor/layer-panel.ts`
- Create: `src/Scada.Application/Scenes/ISceneDraftRepository.cs`
- Create: `src/Scada.Web/Scenes/SceneDraftController.cs`
- Create: `tests/Scada.Web.E2E/editor-reconnect.spec.ts`
- Create: `tests/Scada.Web.E2E/editor-accessibility.spec.ts`

**Produces:** add/move/resize, grid snap, layer z-order, manifest-driven property panel, T1/T2 binding, save/load/validate, optimistic concurrency and disconnected recovery.

- [ ] **Step 1: Write editor state tests**

Cover Clean/Dirty/Saving/SaveFailed/Conflict/Disconnected/UnsupportedSchema/ReadOnly. Assert `SaveDraft(screenId, expectedRevision, canonicalScene)` returns a new revision or 409 without overwrite. In Playwright, enumerate every actionable editor control and assert its accessibility-tree role, correct non-empty name and applicable state. For add/move/resize, layer/property actions, save and conflict recovery, drive pointer and keyboard navigation/activation variants from the same initial model and assert the same canonical editor state transition. Assert focus survives camera pan/zoom, gesture commit/cancel and reconnect (or moves to the documented fallback if the node disappears). Retain post-transform 44×44 targets, non-color state cues, reduced-motion behavior and `pointercancel` rollback assertions.

- [ ] **Step 2: Preserve unsupported schema features**

Rotation/group/instance nodes that MVP cannot author render locked and round-trip byte-semantically; never flatten/drop them. Blazor panel sends intent only; JS store remains authoritative through reconnect.

- [ ] **Step 3: Verify**

Run: `npx playwright test --config tests/Scada.Web.E2E/playwright.config.ts editor-reconnect.spec.ts editor-accessibility.spec.ts`
Expected: draft survives reconnect; conflict never silently overwrites another revision; every editor action has role/name/state evidence, pointer/keyboard transition parity and focus continuity through camera/interaction/reconnect.

- [ ] **Step 4: Commit**

```bash
git add src tests/Scada.Web.E2E
git commit -m "feat: deliver optimistic-concurrency editor MVP"
```

---

## Phase 5 — Alarm, trend UI and system observability

### Task 27: Implement alarm state machine and durable alarm store

**Files:**
- Create: `src/Scada.Domain/Alarms/AlarmState.cs`
- Create: `src/Scada.Runtime/Alarms/AlarmEngine.cs`
- Create: `src/Scada.Infrastructure.Sqlite/Alarms/SqliteAlarmStore.cs`
- Create: `src/Scada.Application/Alarms/IAlarmRepository.cs`
- Test: `tests/Scada.Alarm.Tests/AlarmStateMachineTests.cs`
- Test: `tests/Scada.IntegrationTests/Alarms/AlarmRestartTests.cs`

**Produces:** condition state × ack state plus Shelved/Suppressed/OOS modifiers, hysteresis/delays/latching, parent comm-fail suppression, current projection and append-only events in `alarms.db`.

- [ ] **Step 1: Write full state matrix**

Cover Good threshold, quality Bad/Stale, ack before/after clear, shelve expiry, suppression, restart at every state, duplicate event retry and monotonic on/off delay.

- [ ] **Step 2: Implement loss/durability behavior**

Process alarm events never share sample drop queue. Persist transition before publish; storage failure raises critical system health and blocks alarm acknowledgement mutation until durable append succeeds.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Alarm.Tests; dotnet test tests/Scada.IntegrationTests --filter AlarmRestartTests`  
Expected: all state transitions and restart projections match the matrix.

- [ ] **Step 4: Commit**

```bash
git add src tests
git commit -m "feat: add durable alarm state machine"
```

### Task 28: Implement alarm channel and trend presentation

**Files:**
- Create: `src/Scada.Contracts/Runtime/alarms.proto`
- Create: `src/Scada.Web/Alarms/AlarmHub.cs`
- Create: `src/Scada.Web.Client/src/alarms/alarm-store.ts`
- Create: `src/Scada.Web.Client/src/trends/trend-render-model.ts`
- Create: `tests/Scada.Web.E2E/alarm-trend-reconnect.spec.ts`

**Produces:** `AlarmSnapshot(cursor, states)` + ordered idempotent events; trend gaps, quality masks, semantic markers, cancellation and live↔historical splice.

- [ ] **Step 1: Write reconnect/render tests**

Lose alarm events, reconnect from cursor, duplicate event, ack pending/failure, Bad source, 6-hour trend, invalid gap, old query response arriving after viewport change and live reconnect backfill.

- [ ] **Step 2: Implement accessible presentation**

No line across Bad/Stale/NoData interval. Series differ by line style/marker as well as color. Provide legend, pause/zoom, current value and accessible/tabular fallback. Reduced-motion alarm uses static high-contrast cue.

- [ ] **Step 3: Verify**

Run: `npx playwright test --config tests/Scada.Web.E2E/playwright.config.ts alarm-trend-reconnect.spec.ts`  
Expected: state converges after reconnect and no invalid interval is visually bridged.

- [ ] **Step 4: Commit**

```bash
git add src tests/Scada.Web.E2E
git commit -m "feat: add reconnect-safe alarm and trend UI"
```

### Task 29: Add `System.*` tags and measurable performance budgets

**Files:**
- Create: `src/Scada.Runtime/Diagnostics/SystemTagPublisher.cs`
- Create: `perf/fixtures/dense-300.scene.json`
- Create: `tests/Scada.LoadTests/TelemetryFanoutTests.cs`
- Create: `tests/Scada.Web.E2E/renderer-performance.spec.ts`
- Create: `perf/baselines/release-profile.json`

**Produces:** scan jitter/rate/skips, connection state/rate, WAL/write/gap metrics, clock health, config load errors, telemetry queue depth/age, Runtime RSS and browser renderer counters.

- [ ] **Step 1: Encode the release profile**

Store exact CPU/RAM/OS/browser build used for release gate. Scenario: 20 clients each with 100 tags @4 Hz + 200 tags @1 Hz, 300 bindings/screen, alarm burst, 6-hour trend and repeated navigation.

- [ ] **Step 2: Enforce the initial numeric budgets**

Use these release gates on the recorded baseline profile: expanded SVG nodes ≤4.000; mount p95 ≤750 ms; `applyValues`/rAF p95 ≤8 ms and p99 ≤16 ms; zero steady-state long task >50 ms in a 60-second window; total fan-out ≤80 messages/s for 20 clients by batching slow and fast tags into the same 4 Hz tick; payload p95 ≤64 KiB/s/client; steady queue depth ≤2 frames and age ≤500 ms; queue depth 8 forces `ResyncRequired`; DOM writes ≤600/s for the target mix; browser heap growth <10% and <50 MiB after the soak; Runtime RSS growth <5% and <100 MiB after the soak. A budget change requires committed baseline evidence and review.

- [ ] **Step 3: Verify no unbounded growth**

Run: `dotnet test tests/Scada.LoadTests -c Release; npx playwright test --config tests/Scada.Web.E2E/playwright.config.ts renderer-performance.spec.ts`  
Expected: every measured value is at or below `release-profile.json`; queue drains after induced slow client.

- [ ] **Step 4: Commit**

```bash
git add src tests perf
git commit -m "test: establish SCADA performance release gates"
```

---

## Phase 6 — Remaining drivers, operations and release

### Task 30: Add Modbus RTU/RS-485

**Files:**
- Create: `src/Scada.Drivers.ModbusRtu/ModbusRtuDriver.cs`
- Create: `src/Scada.Drivers.ModbusRtu/SerialTransport.cs`
- Create: `tests/Scada.Driver.Tests/Modbus/ModbusRtuTimingTests.cs`
- Create: `tests/Scada.HardwareTests/ModbusRtuScenarios.cs`

**Produces:** native-install RTU support with serialized bus, silence/turnaround timing, USB reconnect and per-logical-device breaker.

- [ ] **Step 1: Write virtual and hardware timing tests**

Cover 3.5-char silence, timeout, CRC error, wrong slave, one unplugged slave, USB COM re-enumeration, no catch-up and write/readback arbitration.

- [ ] **Step 2: Implement support gate**

Reject Docker RTU configuration at publish/startup. Record tested OS, adapter chipset/driver, baud/parity and device firmware in support matrix.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Driver.Tests --filter ModbusRtuTimingTests` and the approved hardware fixture suite.  
Expected: no concurrent request on one bus and failure isolation per logical device.

- [ ] **Step 4: Commit**

```bash
git add src/Scada.Drivers.ModbusRtu tests docs/support
git commit -m "feat: add native Modbus RTU support"
```

### Task 31: Add secure OPC UA

**Files:**
- Create: `src/Scada.Drivers.OpcUa/OpcUaDriver.cs`
- Create: `src/Scada.Drivers.OpcUa/OpcUaTrustStore.cs`
- Create: `tests/Scada.Driver.Tests/OpcUa/OpcUaSecurityProfileTests.cs`
- Create: `tests/Scada.HardwareTests/OpcUaScenarios.cs`

**Produces:** browse/read/write/subscription, source/native status preservation, signed+encrypted endpoints and explicit trustlist.

- [ ] **Step 1: Write security/reconnect tests**

Reject `SecurityPolicy=None`, unknown/expired/mismatched certificate and hostname; test rollover, subscription sequence gap, reconnect/resubscribe, out-of-order source timestamps and bad status mapping.

- [ ] **Step 2: Implement fail-closed trust**

Unknown cert never auto-trusts. Certificate import/rotation is privileged and audited. Application private key is Runtime-only and excluded from ordinary diagnostic export.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Driver.Tests --filter OpcUaSecurityProfileTests` and approved multi-server fixture suite.  
Expected: only trusted signed+encrypted endpoints connect.

- [ ] **Step 4: Commit**

```bash
git add src/Scada.Drivers.OpcUa tests docs/support
git commit -m "feat: add secure OPC UA driver"
```

### Task 32: Implement coherent backup, signed restore and upgrade

**Files:**
- Create: `src/Scada.Cli/Commands/BackupCommand.cs`
- Create: `src/Scada.Cli/Commands/RestoreCommand.cs`
- Create: `src/Scada.Cli/Commands/MigrateCommand.cs`
- Create: `src/Scada.Cli/Commands/DiagCommand.cs`
- Create: `src/Scada.Application/Operations/BackupManifest.cs`
- Create: `tests/Scada.IntegrationTests/Operations/BackupRestoreAdversarialTests.cs`
- Create: `tests/Scada.IntegrationTests/Operations/UpgradePowerFailureTests.cs`

**Produces:** causal snapshot manifest with DB high-water marks/audit heads/config activation/policy digest; signed package; staging + verify + atomic activation; writer-owned migrations.

- [ ] **Step 1: Write adversarial package tests**

Invalid signature, unknown/wrong site key, rollback version, zip-slip, symlink, decompression/file-count/size limits, missing DB/key, partial snapshot, chain-head mismatch and power cut at each restore/migration phase. Test OS ACL denial for non-backup identities, non-exportability where supported, dual-signed key rotation, old-package verification through retained public trust history, separate trust-set export/import, private-key loss/new epoch, and refusal to silently trust a replacement key.

- [ ] **Step 2: Implement coordinated quiesce/snapshot**

Do not copy live WAL files. Owners create SQLite backup snapshots and report causal checkpoints; coordinator writes manifest only after all snapshots verify. A commissioning-generated site backup-signing key is OS-protected/non-exportable where supported and accessible only to the backup signer identity; private key material is excluded from package/diagnostics/ordinary backup. Manifests carry algorithm/key ID. Rotation is dual-signed with retained public trust history; disaster recovery exports the public trust set separately. Private-key loss requires audited local recovery and a new signing epoch. Restore remains offline until signature/trust, schema, audit, policy and reconciliation checks pass.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.IntegrationTests --filter "BackupRestoreAdversarialTests|UpgradePowerFailureTests"`  
Expected: every malformed/partial package fails before activation; valid restore passes `audit verify` and config reconciliation.

- [ ] **Step 4: Commit**

```bash
git add src tests docs/operations
git commit -m "feat: add signed coherent backup and upgrade"
```

### Task 33: Package Windows, Linux and Docker with zero-Internet gates

**Files:**
- Create: `deploy/windows/*`
- Create: `deploy/linux/*`
- Create: `deploy/docker/compose.yml`
- Create: `deploy/docker/Dockerfile.runtime`
- Create: `deploy/docker/Dockerfile.web`
- Create: `docs/support/platform-matrix.md`
- Create: `tests/Scada.SecurityTests/Deployment/NoEgressTests.cs`
- Create: `tests/Scada.IntegrationTests/Deployment/ProcessIsolationTests.cs`

**Produces:** Windows services with separate accounts/ACL; Linux systemd units; Docker two containers with shared UDS volume, separate DB volumes and TCP-only driver support; explicit audit-sealer provider/fallback support outcome per target.

- [ ] **Step 1: Write deployment security tests**

Assert Web cannot open OT endpoint/policy/Runtime DB or the audit key/fallback blob; Runtime cannot write Web DB or sign/export audit keys; wrong pipe/UDS peer rejected; Data Protection keys persist and are protected; cert expiry/rotation alarms; no default/exposed remote access.

- [ ] **Step 2: Enforce no-egress behavior**

Run container smoke with `--network none`; run Windows/Linux firewall/DNS capture test with only local UI and approved OT endpoints allowed. Any DNS/TCP/UDP to external addresses or console error caused by missing Internet fails.

- [ ] **Step 3: Verify support matrix**

Run offline install, first-run CA public certificate export, login, simulator, backup, upgrade and restore on each target. For the audit sealer, every Windows/Linux/Docker row records the preferred provider, non-exportability probe result, configured fallback permission, observed startup/fail-closed outcome, signer-only ACL and host-administrator limitation; execute the Task 9 provider tests for that row. Docker must reject RTU, must not store secrets in environment variables and must mount any fallback only in the dedicated sealer volume.

- [ ] **Step 4: Commit**

```bash
git add deploy docs/support tests
git commit -m "ops: package isolated zero-Internet deployments"
```

### Task 34: Run the final system, chaos and hardware acceptance gates

**Files:**
- Create: `tests/Scada.HardwareTests/support-matrix.json`
- Create: `tests/Scada.HardwareTests/CommandAmbiguityScenarios.cs`
- Create: `tests/Scada.LoadTests/EightHourSoakTests.cs`
- Create: `docs/operations/commissioning-checklist.md`
- Create: `docs/operations/incident-recovery.md`
- Create: `docs/support/security-claims.md`
- Create: `tests/Scada.SecurityTests/Documentation/SecurityClaimsTests.cs`

**Produces:** traceable evidence for all seven goals without claiming safety or IEC certification.

- [ ] **Step 1: Execute acceptance matrix**

Cover Web crash, Runtime restart, disk full, WAL starvation, clock step, config rollback, telemetry overflow/resync, alarm burst, audit/seal outage, PLC executed-but-response-lost, other master, failed momentary OFF, USB reconnect and OPC UA trust failure.

- [ ] **Step 2: Execute scale/soak tests**

Use target tag distribution, 4–8 connections, 20 clients, 300 bindings/screen, 1.000 stored sample/s average and 5.000 burst. Assert configured RSS/heap/queue/WAL/frame budgets and zero silent data/audit loss.

- [ ] **Step 3: Execute HIL and commissioning evidence**

Record device/vendor/firmware/adapter/OS for every passing scenario. Critical commands require PLC handshake; direct Tier-2 writes are limited to explicitly classified non-critical commands.

- [ ] **Step 4: Verify documentation claims**

Implement `SecurityClaimsTests` as a case-insensitive, whitespace-normalized scan of `docs/support/security-claims.md` and all release-facing Markdown under `docs/support` and `docs/operations`. Require these exact normalized statements in `security-claims.md`: `tamper-evident, not tamper-proof`; `supervisory system, not a safety system`; `IEC 62443 design intent only; not compliant or certified`; `Modbus has no protocol authentication`; `Docker does not support Modbus RTU`. Reject positive-claim regexes equivalent to `is tamper-proof`, `is a safety system`, `provides a safety function`, `SIL ... certified`, `IEC 62443 (compliant|certified)`, `certified to IEC 62443`, `Modbus (provides|has|uses) (protocol )?authentication`, or `Docker supports Modbus RTU`. Run: `dotnet test tests/Scada.SecurityTests --filter SecurityClaimsTests`. Expected: all required phrases occur and every forbidden positive claim count is zero; attach the scan output to the Task 34 evidence checklist.

- [ ] **Step 5: Commit**

```bash
git add tests docs
git commit -m "test: complete SCADA system acceptance gates"
```

---

## Plan Self-Review Matrix

| Spec area | Implemented by |
|---|---|
| Goals, process boundary, zero-Internet | Tasks 1–3, 12, 33–34 |
| Tag/value/quality/time | Tasks 4–5 |
| Scene schema and renderer | Tasks 6, 18–19, 24–26 |
| Config/publish/reconciliation | Tasks 7–8, 23 |
| Security/RBAC/audit/write policy | Tasks 9–12, 20–22, 32–34 |
| Driver/scheduler | Tasks 13–14, 20, 30–31 |
| Historian/trend | Tasks 15–16, 28–29 |
| Telemetry | Tasks 17–19, 29 |
| Editor | Tasks 24–26 |
| Alarm | Tasks 27–29 |
| Backup/upgrade/deployment | Tasks 32–34 |

## Execution Handoff

Execute strictly in task order until Task 24. Tasks 25–26 (editor) and Tasks 27–28 (alarm/trend UI) may proceed in parallel only after Scene, telemetry, config and manual-screen gates are green. Hardware and command tasks require the process/policy gates regardless of unit-test success.

Recommended execution mode: `superpowers:subagent-driven-development`, one fresh implementer per task with specification review and code-quality review before the next gate.
