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

1. In-process Runtime chỉ là test adapter; production từ slice đầu chạy hai process.
2. Minimal immutable config/publish/activation thuộc foundation, trước scan-budget validation.
3. Activation semantic/envelope validation là all-or-nothing; connectivity failure tạo `ActiveDegraded`.
4. `TagQuality` tách severity, reason flags và native status; trend không OR raw code.
5. Runtime sở hữu `Stale`; browser chỉ sở hữu `RuntimeDisconnected`.
6. Scene schema có một nguồn JSON Schema/widget manifest; server quyết canonical bytes/hash.
7. Command consume one-time capability và durable `DispatchIntent` trước device I/O.
8. Scene revision là audit context mặc định, không phải Runtime authorization authority.
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

- [ ] **Step 1: Write fake-clock failures**

Cover NTP step backward/forward, many tags in one microsecond, process restart with OS time below persisted high-water, and OPC UA source timestamps arriving out of order.

- [ ] **Step 2: Implement one Runtime-wide clock**

Anchor logical UTC to monotonic elapsed, never re-anchor backward, persist high-water checkpoints, expose `ClockDegraded` and record re-anchor/deviation audit events. Use logical time as ordering key; retain source time only as metadata.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Runtime.Tests --filter MonotonicLogicalClockTests`  
Expected: strictly increasing logical stamps across all scenarios; backward wall step never creates per-tag clocks.

- [ ] **Step 4: Commit**

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

Cover key order, Unicode, cultures, floats, schemaVersion inclusion, publish crash before/after commit, re-activation of an old version and poll recovery after notification loss.

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

**Produces:** SHA-256 chain with canonical event bytes, chain ID, monotonic sequence, genesis, previous hash, build/boot/runtime IDs and signed external head seal.

- [ ] **Step 1: Write tamper/failure tests**

Modify, delete, reorder and truncate events; replace genesis; roll back a DB; create cross-chain command causality. With a fake clock, test seals at exactly 100 events or 60 seconds (whichever first), forced boot/clean-shutdown/policy/key-rotation seals, no early/late off-by-one, sink unavailable through the 120-second overdue boundary, recovery, key ACL denial, dual-signed rotation, retained public trust history and key-loss epoch discontinuity. Expected verifier identifies exact break and never claims a cryptographic total order between chains.

- [ ] **Step 2: Implement durability profiles**

Audit/command DB uses WAL + `synchronous=FULL`; append is one transaction. Seal sink writes signed head files to an operator-configured append-only directory outside application ACL. A separate `AuditSealer` identity reads the OS-protected/non-exportable key. Seal after at most 100 events or 60 seconds; boot, clean shutdown and policy/key rotation force a seal. At 120 seconds overdue, raise a system alarm, degrade health and fail closed command/publish mutations. Rotation is dual-signed and public trust history is retained; key loss starts an explicitly discontinuous epoch through local administrator recovery.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.SecurityTests --filter "AuditTamperTests|AuditSealLifecycleTests"; dotnet run --project src/Scada.Cli -- audit verify --fixture tests/fixtures/audit/valid`
Expected: PASS for cadence/key/sink lifecycle; exit code 0 for valid fixture and nonzero for each tampered fixture.

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

Cover missing permission, stale circuit, disabled user, expiry, replayed nonce, changed tag/value/hash/channel, revoked authorization snapshot, and assert no emergency role/header/token/path can bypass the permission matrix. Local account recovery must leave global writes disabled until normal identity, policy and sealer-health gates pass.

- [ ] **Step 2: Implement bootstrap and revocation rules**

No default password. First-run and account-recovery admin ceremonies are OS-local, audited and expire after completion. Service accounts are non-interactive. Circuit/session authorization is rechecked at sensitive endpoint execution. Product scope has no break-glass bypass; recovery creates/restores a normal administrator and never bypasses command policy.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.SecurityTests --filter "PermissionMatrixTests|CommandCapabilityReplayTests"`  
Expected: all unauthorized/replayed mutations are rejected and audited.

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

**Produces:** root/administrator-owned signed policy file, global write kill switch and exact read/browse/write positive allowlists.

- [ ] **Step 1: Write remap attack matrix**

Change one field at a time: driver, endpoint, device identity, unit/node, function, address, type, endian, scaling, raw/engineering range, rate and pulse. Every widened mapping must reject.

- [ ] **Step 2: Implement fail-closed load**

Missing/invalid/unsigned policy means writes disabled. Policy changes require Runtime restart and create audit + seal events. Web identity cannot write policy path.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.SecurityTests --filter ConfigRemapAttackTests`  
Expected: only exact or narrower config passes.

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

Launch real child processes with distinct identities; reject wrong SID/UID/certificate, expired capability and contract incompatibility. Stop Web and assert Runtime scan loop remains alive.

- [ ] **Step 2: Implement mapping adapters**

Generated protobuf types never enter Domain/Application APIs. Map/copy `Int64`, typed values, quality width and timestamps explicitly. Runtime pulls immutable config artifact from Web feed and caches last active version locally.

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

Prove no catch-up, p99 jitter calculation, one in-flight RTU request per bus, one bad slave not tripping other logical devices, command quota not starving scan and impossible 9600-baud config warning. For stale behavior, use an injected fake monotonic/logical clock and cover publish rejection immediately below/above the formula bounds, acceptance at both bounds, no transition one tick before `StaleAfterMs`, transition at the exact threshold, large logical-clock advance, scan recovery to current quality, process restart with a new boot ID and persisted last-observation state, and no dependence on wall/source-clock steps.

- [ ] **Step 2: Implement fixed scan groups**

Planner validates structured addresses and uses protocol limits. `StaleAfterMs` publish validation follows the locked formula. Runtime stale evaluation is driven only by the injected monotonic/logical clock. Quality transitions into and out of Stale are published and persisted as quality-only transitions that bypass historian deadband/store-rate.

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
- Test: `tests/Scada.LoadTests/HistorianConcurrentReadWriteTests.cs`

**Produces:** per-tag seed, buckets with first/last/min/max/time-weighted sum/durations/quality masks, partition batch merge and retention pin/refcount.

- [ ] **Step 1: Write hand-calculated query tests**

Cover seed across partition boundary, Bad/Stale duration exclusion, min/max spike preservation, bool/enum duration/count, string rejection for numeric aggregate, semantic revision marker and NTP backward fixture.

- [ ] **Step 2: Implement bounded partition query**

Open/query partitions in bounded batches, merge decomposable aggregates and keep correct seed. Do not attach the full retention range. Retention deletes only unpinned closed partitions.

- [ ] **Step 3: Run concurrency acceptance**

Run: `dotnet test tests/Scada.LoadTests --filter HistorianConcurrentReadWriteTests -c Release`  
Expected: 1.000 stored rows/s average, 5.000 burst, two concurrent 8-hour readers, zero unhandled `SQLITE_BUSY`, p99 commit <200 ms, WAL below configured threshold within 30 seconds after readers end.

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

Assert zero Blazor circuit on HMI page; Bootstrapping/Stale/Bad/Offline cues; keyboard focus; 44×44 CSS-pixel hit targets after camera transform; reduced-motion static alarm cue; touch `pointercancel` recovery.

- [ ] **Step 2: Implement state matrix**

Do not render last-known as normal. Use pattern + icon + text, not color alone. `RuntimeDisconnected` is transport state and does not mutate tag quality. All command affordances are disabled outside Live+Good unless a stricter Runtime policy still denies them.

- [ ] **Step 3: Verify**

Run: `npx playwright test --config tests/Scada.Web.E2E/playwright.config.ts telemetry-reconnect.spec.ts touch-accessibility.spec.ts`  
Expected: PASS at 100%, 150% and 200% browser zoom.

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
    TagValue RequestedValue,
    string CapabilityToken);
```

- [ ] **Step 1: Write fault-injection matrix**

Crash before/after capability consume, before/after intent commit, before/after fake protocol send and before outcome commit. Test duplicate idempotency, activation/semantic/target/device-session mismatch, stale quality and changed guarded sample revision.

- [ ] **Step 2: Implement exact ordering**

Consume nonce and append durable intent atomically before I/O. One write outstanding per logical device; transport arbiter preserves scan quota. Restart never redispatches an intent without outcome.

- [ ] **Step 3: Verify**

Run: `dotnet test tests/Scada.Command.Tests --filter "CommandCrashBoundaryTests|CommandRevisionPreconditionTests"`  
Expected: every ambiguous crash projects `INDETERMINATE`; fake driver call count never exceeds one.

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

**Produces:** add/move/resize, grid snap, layer z-order, manifest-driven property panel, T1/T2 binding, save/load/validate, optimistic concurrency and disconnected recovery.

- [ ] **Step 1: Write editor state tests**

Cover Clean/Dirty/Saving/SaveFailed/Conflict/Disconnected/UnsupportedSchema/ReadOnly. Assert `SaveDraft(screenId, expectedRevision, canonicalScene)` returns a new revision or 409 without overwrite.

- [ ] **Step 2: Preserve unsupported schema features**

Rotation/group/instance nodes that MVP cannot author render locked and round-trip byte-semantically; never flatten/drop them. Blazor panel sends intent only; JS store remains authoritative through reconnect.

- [ ] **Step 3: Verify**

Run: `npx playwright test --config tests/Scada.Web.E2E/playwright.config.ts editor-reconnect.spec.ts`  
Expected: draft survives reconnect; conflict never silently overwrites another revision.

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

Do not copy live WAL files. Owners create SQLite backup snapshots and report causal checkpoints; coordinator writes manifest only after all snapshots verify. A commissioning-generated site backup-signing key is OS-protected/non-exportable and accessible only to the backup signer identity; private key material is excluded from package/diagnostics/ordinary backup. Manifests carry algorithm/key ID. Rotation is dual-signed with retained public trust history; disaster recovery exports the public trust set separately. Private-key loss requires audited local recovery and a new signing epoch. Restore remains offline until signature/trust, schema, audit, policy and reconciliation checks pass.

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

**Produces:** Windows services with separate accounts/ACL; Linux systemd units; Docker two containers with shared UDS volume, separate DB volumes and TCP-only driver support.

- [ ] **Step 1: Write deployment security tests**

Assert Web cannot open OT endpoint/policy/Runtime DB; Runtime cannot write Web DB; wrong pipe/UDS peer rejected; Data Protection keys persist and are protected; cert expiry/rotation alarms; no default/exposed remote access.

- [ ] **Step 2: Enforce no-egress behavior**

Run container smoke with `--network none`; run Windows/Linux firewall/DNS capture test with only local UI and approved OT endpoints allowed. Any DNS/TCP/UDP to external addresses or console error caused by missing Internet fails.

- [ ] **Step 3: Verify support matrix**

Run offline install, first-run CA public certificate export, login, simulator, backup, upgrade and restore on each target. Docker must reject RTU and must not store secrets in environment variables.

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

**Produces:** traceable evidence for all seven goals without claiming safety or IEC certification.

- [ ] **Step 1: Execute acceptance matrix**

Cover Web crash, Runtime restart, disk full, WAL starvation, clock step, config rollback, telemetry overflow/resync, alarm burst, audit/seal outage, PLC executed-but-response-lost, other master, failed momentary OFF, USB reconnect and OPC UA trust failure.

- [ ] **Step 2: Execute scale/soak tests**

Use target tag distribution, 4–8 connections, 20 clients, 300 bindings/screen, 1.000 stored sample/s average and 5.000 burst. Assert configured RSS/heap/queue/WAL/frame budgets and zero silent data/audit loss.

- [ ] **Step 3: Execute HIL and commissioning evidence**

Record device/vendor/firmware/adapter/OS for every passing scenario. Critical commands require PLC handshake; direct Tier-2 writes are limited to explicitly classified non-critical commands.

- [ ] **Step 4: Verify documentation claims**

`security-claims.md` must say tamper-evident, not tamper-proof; supervisory, not safety; IEC 62443 design intent only, not compliant/certified; Modbus has no protocol authentication; Docker has no RTU.

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
