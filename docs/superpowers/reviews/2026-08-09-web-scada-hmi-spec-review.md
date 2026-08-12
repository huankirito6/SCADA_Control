# Review spec Web SCADA/HMI

**Ngày review:** 2026-08-09  
**Spec:** `docs/superpowers/specs/2026-08-04-web-scada-hmi-design.md`  
**Kết luận:** Chấp thuận có điều kiện. Không bắt đầu implementation có hardware thật hoặc write path trước khi đóng toàn bộ P0 trong tài liệu này.

## Phạm vi và phương pháp

Review dùng ba góc nhìn độc lập rồi phản biện chéo:

1. Kiến trúc, domain, config, historian, time, driver và thứ tự slice.
2. Threat model OT, command safety, identity, audit, backup, upgrade và deployment.
3. Scene/renderer/editor, telemetry UX, accessibility, alarm/trend và test strategy.

Các ước lượng thời gian và quy mô nhân sự trong spec không được dùng làm đầu vào cho review hay plan.

## Phán quyết ngắn

Spec mạnh ở cách nhìn failure-first: supervisory không phải safety, browser không chạm PLC, telemetry lossy tách historian/audit, command có `INDETERMINATE`, historian có seed/time-weighted aggregate, scene là dữ liệu whitelist, và editor được cắt khỏi renderer.

Tuy nhiên, spec chưa tự nhất quán ở các boundary quan trọng nhất:

- tuyên bố hai process nhưng không có slice nào thực hiện process split;
- write envelope chưa khóa vào đích vật lý;
- command chưa bind vào activation/semantic state mà operator đã nhìn;
- quality packing không tương thích với phép OR của `qMask`;
- historian “không hở” nhưng buffer lại được phép drop;
- config publish được dùng ở slice 2 nhưng chỉ được xây ở slice 3;
- stale/offline được gọi là quyết định an toàn nhưng chưa có state contract;
- L1 scene được nói là chia sẻ C# + JS nhưng chưa có nguồn chuẩn.

Vì vậy, hướng triển khai được chọn là **risk-first với hard gate**, không chuyển nguyên bảng slice hiện tại thành backlog.

## Ba hướng đã cân nhắc

### A. Triển khai nguyên spec theo bảng slice hiện tại

Ưu điểm: bám sát tài liệu và sớm có demo. Nhược điểm: slice 2 cần publish trước slice 3, process split không có owner, command security đến sau khi data/config semantics đã đóng cứng. **Không chọn.**

### B. Risk-first với contract và hard gate

Đóng domain primitives, config activation, process boundary, audit, telemetry FSM và storage semantics trước; chỉ sau đó mới nối hardware và write. Mỗi gate có test tự động và artifact demo. **Đây là hướng khuyến nghị.**

### C. Frontend/demo-first

Viết scene/renderer/editor trên fixture rồi nối Runtime sau. Có giá trị khám phá UI nhưng dễ khóa sai quality, subscription, command và scene schema. Chỉ dùng fixture frontend sau khi các wire/domain contract tối thiểu đã khóa. **Không dùng làm trục chính.**

## Những điểm spec làm tốt

- Định vị rõ supervisory layer, không phải E-stop/SIS/interlock/SIL/PL.
- Runtime là phía duy nhất được phép truy cập control network.
- Scene/import là untrusted data; cấm arbitrary code và SVG nguy hiểm.
- `TagId` bất biến, address có cấu trúc, scan-group/coalescing và skip-not-catch-up.
- `QueryTrend` có seed as-of, envelope min/max và time-weighted average loại khoảng Bad.
- Command dùng event stream, không update record cũ; phân biệt `Verified`, `Consistent`, `Failed`, `Indeterminate`.
- Config immutable, canonical hash, Runtime pull/poll và reconciliation theo resource.
- Audit nói đúng giới hạn tamper-evident, không hứa tamper-proof trước host admin.
- Frontend tách L1/L2/L3, hot/cold patch, static SSR HMI và editor JS-owned model.
- Acceptance tests nhắm concurrency, clock step, scan budget và WAL starvation.

## P0 — blocker phải đóng trước code nền hoặc hardware/write

### P0-1. Process boundary chưa tồn tại

**Spec:** §2.1, §4.1–4.3, §15.

IPC interface và copy DTO không tạo security boundary nếu Web và Runtime cùng process. Web RCE khi đó thừa hưởng route OT, credential, driver object và policy memory.

**Quyết định:**

- In-process chỉ được dùng trong unit/integration test hoặc simulator read-only với OT network bị chặn.
- Runtime và Web phải tách process/service identity trước mọi kết nối hardware thật, không chỉ trước write.
- Startup phải fail nếu hardware/write được bật nhưng identity, ACL, network route và IPC authentication chưa đạt gate.
- Driver assemblies và device credentials không được nằm trong Web deployment.

### P0-2. Write envelope chưa bind physical target

**Spec:** §5.3, §8.1, §9.3.

Một Web bị chiếm có thể giữ `TagId`/`AreaId` hợp lệ nhưng remap connection, Unit ID, address, function, datatype hoặc scaling sang điểm nguy hiểm. Area allowlist + deny-list tag không ngăn được chuỗi này.

**Quyết định:** Runtime-owned policy là positive allowlist của tuple:

```text
driver + endpoint/device identity + unit/node + function/access mode +
address + datatype + byte/word order + raw/engineering transform +
raw/engineering range + write mode + max rate/pulse
```

Config chỉ được hẹp hơn tuple đã duyệt. Cấm broadcast write và function nguy hiểm theo mặc định. Read/browse endpoint cũng có network allowlist để Runtime không trở thành OT scanner.

### P0-3. Command chưa bind đúng revision và observation

**Spec:** §8.1–8.5, §11.3.

Giữa confirm và dispatch có thể xảy ra publish/reconcile hoặc process value thay đổi. `semantic_hash` hiện không gồm full source identity.

**Quyết định:** mọi command mang và Runtime re-check ngay trước durable intent:

```text
command_id, activation_id, active_config_hash,
tag_id, source_binding_hash, value_meaning_hash,
physical_target_digest, expected value/sample revision,
quality, observed timestamp, device-session generation,
subject/capability nonce, requested typed value
```

- `sceneId/revision/hash` được audit để chứng minh UI tạo intent, nhưng không phải security authority mặc định.
- Với guarded/CAS command, expected sample revision là precondition bắt buộc.
- Với absolute set-value, freshness/quality/device generation luôn bắt buộc; equality của sample revision chỉ áp dụng khi policy yêu cầu, tránh reject liên tục trên tag nhanh.
- Mismatch trước durable intent là `PreconditionFailed` và yêu cầu confirm lại; không retry.

### P0-4. Identity authority chưa phải thiết kế hoàn chỉnh

**Spec:** §9.1, §9.4.

“Runtime phát hành assertion” không tự ngăn Web mint admin nếu Runtime cấp assertion chỉ dựa trên username do Web gửi.

**Quyết định:** capability cho command phải one-time, short-lived, audience/channel-bound và bind subject, exact target/value, activation/hashes, policy version, expiry và nonce. Runtime consume capability nguyên tử cùng `DispatchIntent`. Runtime cần authoritative authorization snapshot/revocation hoặc verifier re-auth độc lập. Nếu chưa có cơ chế này, threat model phải thừa nhận Web RCE có toàn quyền trong physical envelope và audit subject không đáng tin.

### P0-5. Durable ordering của command chưa khóa

**Spec:** §8.3–8.5.

Nếu device I/O xảy ra trước journal commit, mất điện có thể để lại một hành động PLC không có bằng chứng.

**Quyết định:**

1. Trong một durable transaction, consume nonce/idempotency và append authorization + immutable physical target + `DispatchIntent`.
2. Commit với durability profile của command/audit, không dùng historian `synchronous=NORMAL`.
3. Chỉ sau commit mới gọi driver.
4. Append attempt/readback/outcome sau I/O.
5. Restart thấy intent chưa có outcome phải project thành `INDETERMINATE`, tuyệt đối không auto-dispatch lại.

Momentary vẫn yêu cầu PLC watchdog.

### P0-6. Quality encoding và `qMask` sai đại số

**Spec:** §5.2, §7.5, §11.4.

Nếu severity/reason là ordinal code, OR hai code có thể tạo code thứ ba không tồn tại. `Uncertain (01) OR Bad (10) = 11` cũng không có nghĩa.

**Quyết định:** domain và wire tách:

```text
severity: Good | Uncertain | Bad
reasonFlags: independent flags
nativeStatus: optional protocol status
```

Trend bucket trả `severitySeenMask`, `worstSeverity`, `reasonFlagsSeen`, `durGoodMs`, `durValidMs`, `durBadMs`, `durStaleMs`; không OR raw packed quality.

### P0-7. Historian guarantee tự mâu thuẫn

**Spec:** §7.1, §7.7, §10.3.

Bounded buffer có overflow/drop nên không thể hứa end-to-end “không hở”. Không có ingest sequence/gap record cũng không chứng minh được guarantee.

**Quyết định:** đổi contract thành **no silent loss**:

- sau khi storage acknowledge, record có stable ingest identity và retry không tạo trùng;
- loss trước acceptance phải tạo persisted gap/high-water marker và system alarm;
- sample queue, alarm journal và audit/command queue tách biệt;
- audit/command fail-closed và dùng durability mạnh hơn historian;
- disk/WAL/queue warnings phải xuất hiện trước exhaustion.

### P0-8. Telemetry epoch chưa tạo atomic snapshot→delta handoff

**Spec:** §7.1–7.2.

Epoch ngăn wrong-tag mapping nhưng không giải quyết delta phát sinh trong lúc snapshot, mất delta của một tag đứng yên, queue overflow hay ordering.

**Quyết định:** giao thức có `subscriptionGeneration + epoch + snapshotWatermark`. Snapshot tại watermark `W`; per-client serialized mailbox chỉ áp delta `> W`. Queue là dirty-map latest-wins. Khi không thể coalesce, server phát `ResyncRequired`; client bỏ queue cũ và đi qua FSM `Bootstrapping → Live → Lagging/Resyncing → Offline`.

### P0-9. Stale/offline vẫn để mở

**Spec:** §7.4, §11.6.

Đây là quyết định an toàn vận hành nhưng spec chưa chốt ai tính, ngưỡng nào, last-known hiển thị thế nào và khi nào khóa command.

**Quyết định:** Runtime là nguồn duy nhất chuyển tag sang Stale bằng monotonic/logical clock. Published tag bắt buộc có `StaleAfterMs`, validator yêu cầu:

```text
max(3 × ScanPeriodMs, 2_000) <= StaleAfterMs <= 60_000
```

UI có transport state riêng `RuntimeDisconnected`; không tự sửa domain quality. Bad/Stale/NoData/Disconnected luôn có pattern + icon/text + age, global invalid count và disable command. Draft UI có thể gợi ý default, nhưng publish không chấp nhận implicit default.

### P0-10. Config activation và thứ tự slice chưa khả thi

**Spec:** §6.2, §8.1–8.2, §15.

Slice 2 cần publish-time validation trong khi config version/publish ở slice 3. Process split cũng không có owner.

**Quyết định:** đưa minimal immutable config/publish/activation vào foundation. Activation có `activation_id` riêng với version và state `Desired → Validating → Preparing → Active | ActiveDegraded | Rejected`. Schema/hash/envelope validation là all-or-nothing trước atomic pointer switch; connectivity failure không làm config invalid mà tạo `ActiveDegraded` + per-resource `tag_load_status`. Process split là gate trước Modbus TCP hardware.

## P1 — phải đóng trước production deployment của subsystem liên quan

1. **Clock model chưa đủ:** schema sample thiếu source time, ingest/logical time, monotonic ticks và boot ID; restart/clock step chưa có persisted high-water policy.
2. **Scene L1 có hai authority:** cần một JSON Schema/widget manifest, generated C#/TS types, server-authoritative canonical bytes/hash và shared conformance corpus.
3. **Scene schema thiếu phần chuẩn tắc:** stable IDs, canvas/viewBox, parent/layer order, widget props, typed target, action, parameters, symbols/instances, dangling refs và complexity limits.
4. **Alarm storage mâu thuẫn:** nói alarm log tách historian nhưng chưa có physical owner/DB. Chọn `alarms.db`, Runtime single writer, query qua RPC.
5. **Alarm/trend transport thiếu cursor contract:** cần snapshot + cursor + idempotent events, reconnect/backfill, modifier state và invalid-gap rendering.
6. **Historian partition:** SQLite mặc định chỉ attach 10 DB; query dài phải query partition theo batch và merge aggregate/seed ở repository, không attach toàn retention range. Retention cần pin/refcount.
7. **DB migration ownership:** writer-owner migrate DB của mình; CLI chỉ orchestration offline. “Runtime migrator duy nhất” không phù hợp với `config.db` do Web sở hữu.
8. **Audit L3:** thiếu canonical bytes, algorithm, genesis, chain ID/sequence, seal frequency, signing key custody, append-only sink và behavior khi sink quá hạn.
9. **Backup/restore:** ba DB chưa có consistent causal cut; package signature/key lifecycle, zip-slip/symlink/zip-bomb limits và atomic restore còn thiếu.
10. **Driver contract:** thiếu async cancellation/deadline, capabilities, typed partial result, native status/time, thread-safety và read/write arbitration.
11. **OT device security:** OPC UA cần signed+encrypted profile/trustlist; Modbus phải ghi rõ không có protocol auth và yêu cầu zone/conduit/ACL.
12. **RBAC:** thiếu deny-by-default permission matrix, bootstrap/break-glass, revocation, service account và stale Blazor circuit handling.
13. **Editor state:** thiếu optimistic concurrency, unsupported-node round-trip, save/conflict/disconnected state và complete transaction semantics.
14. **Accessibility:** touch target phải đo sau camera transform; cần keyboard parity, focus, accessible names, non-color cues, reduced-motion và pointer-cancel FSM.
15. **Frontend performance gate:** chưa chốt baseline hardware/browser, node cap, mount/frame budget, queue/payload/heap/RSS và 20-client scenario với đủ 300 tag.
16. **IEC 62443 wording:** chỉ được nói “design intent hỗ trợ một phần” cho đến khi có applicability, mapping, evidence và independent conformance decision.

## P2 — chỉnh factual/cross-reference trước khi đóng spec

- SQLite từ 3.22 có thể mở WAL read-only nếu `-wal/-shm` tồn tại, directory cho phép tạo chúng, hoặc DB được mở immutable. Lập luận dùng RPC vẫn hợp lý cho ownership/isolation/Docker, nhưng câu “mode=ro không recover được WAL” đang quá tuyệt đối.
- Data Protection key bị mất thường làm cookie cũ không giải mã được và buộc login lại; không tự làm cookie forgeable. Persistence và protection-at-rest là hai yêu cầu khác nhau.
- Merge hai audit chain theo wall time không tạo cryptographic total order; CLI phải hiển thị causal link và uncertainty.
- `other_writers_possible` phải mặc định `true` cho tới khi commissioning evidence chứng minh single writer.
- “gap hiển thị” không thể kiểm bằng canonical Scene serialization; phải kiểm semantic render state/accessibility tree/trend render model.
- `applyStructural` không chạy ở runtime update loop, nhưng initial mount/controlled full remount vẫn phải được định nghĩa.
- 1.000 sample/s phải được định nghĩa là observed, candidate hay stored-row rate; plan dùng stored-row rate cho storage acceptance và đo thêm candidate/accepted/gap counters.

## State matrix bắt buộc

### Telemetry/validity

| State | Giá trị | Cue | Command |
|---|---|---|---|
| Bootstrapping | Placeholder | Đang lấy snapshot | Disabled |
| Live + Good | Current + timestamp | Bình thường | Theo RBAC/policy |
| Uncertain/Simulated/Forced | Value + validity | Icon/text/pattern | Deny mặc định; policy có thể hẹp hơn |
| Bad/Stale/NoData | Last-known tham khảo hoặc `?` | Hatch + age + invalid count | Disabled |
| Lagging/Resyncing | Freeze last-known | Banner + watermark | Disabled |
| RuntimeDisconnected | Last-known không đáng tin | Banner toàn màn hình | Disabled |
| Contract/schema fatal | Không render bình thường | Error + correlation ID | Disabled |

### Command UX

| State | Hành vi |
|---|---|
| Ready | Hiện value, unit, quality, age và active revision |
| Confirming | Chốt old→new và identity/hash context |
| AcquiringCapability | Khóa control; chưa có optimistic success |
| Pending/Dispatched | Hiện trạng thái riêng; input focus không bị telemetry overwrite |
| PreconditionFailed | Refresh và confirm lại; không retry |
| Verified/Consistent | Dùng đúng nhãn backend |
| Failed | Chỉ khi biết chắc không thành công |
| Indeterminate | Cảnh báo nổi bật; yêu cầu kiểm tra hiện trường; không auto-retry |

## Quyết định đồng thuận của ba agent

1. Tách process trước mọi hardware thật.
2. Physical tuple + Runtime-owned positive allowlist là security boundary thật của write.
3. Capability one-time được consume cùng durable `DispatchIntent` trước I/O.
4. Scene revision là audit context mặc định; Runtime policy không phụ thuộc scene.
5. Minimal publish/activation chuyển xuống foundation.
6. Activation validate semantic/envelope all-or-nothing; connectivity tạo `ActiveDegraded`.
7. Runtime sở hữu stale; UI chỉ thêm transport disconnected state.
8. Quality aggregate dùng masks riêng, không OR raw code.
9. Telemetry cần snapshot watermark và serialized mailbox.
10. Historian hứa no-silent-loss, không hứa lossless vô điều kiện.
11. Query partition theo batch và merge, không `ATTACH` toàn range.
12. Application ports tách versioned wire/protobuf contracts.

## Hard gates

- Không hardware nếu process isolation/ACL/IPC auth chưa pass.
- Không write nếu physical policy, identity capability, durable intent và crash matrix chưa pass.
- Không editor nếu Scene schema/corpus/manual screens chưa pass.
- Không alarm UI nếu alarm snapshot/cursor/state projection chưa pass.
- Không release nếu restore/upgrade rehearsal, egress-deny và HIL support matrix chưa pass.
- Không claim IEC 62443 compliance/certification khi chưa có evidence độc lập.

## Final authority fix round — Task 6

**Scope:** Close the carried Important finding that geometry enforcement literals and property shapes could silently diverge among the manifest, Draft 2020-12 schema, C# canonicalizer, and generated TypeScript validator.

- Added `geometry` policy to the existing authoritative `widget-manifest.json`: box/point/points/path/link required and allowed fields, points minimum/maximum, path maximum, and path-segment kinds.
- C# `SceneCanonicalizer` now consumes that geometry policy for every geometry field set, point/path bounds, and path-segment-kind allowlist. The TypeScript generated validator consumes the same JSON policy for the corresponding client enforcement; the schema remains the separately embedded Draft 2020-12 contract.
- Added `ManifestMakesEveryGeometryShapeAndBoundSchemaAuthoritative`, which deterministically parses both embedded manifest and schema and checks every geometry required/allowed set, points min/max, path maximum, and the exact path segment enum. This makes a schema/manifest drift fail locally before either validator can silently diverge.

**Strict TDD evidence:** The new focused test first ran RED against the old manifest, failing with `KeyNotFoundException` at `geometry` (`SceneCorpusTests.cs:52`); this assertion-level failure demonstrated the missing authority policy. After adding the manifest policy and consuming it in both validators, the same focused test was GREEN: **1/1 passed**.

**Final verification:** `dotnet test tests/Scada.Domain.Tests/Scada.Domain.Tests.csproj --filter SceneCorpusTests --no-restore` (**6/6**); `npm test -- scene-corpus.test.ts` (typecheck, lint, **5/5**); `dotnet test Scada.sln --no-restore` (Domain **57/57**, Integration **2/2**, Security **3/3**, Runtime **16/16**, Architecture **8/8**); `git diff --check` passed.


- [.NET 10 là LTS đang active](https://dotnet.microsoft.com/en-us/platform/support/policy).
- [Blazor static SSR không có interactivity/circuit](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0).
- [SignalR hỗ trợ MessagePack và yêu cầu strict type/casing ở JS](https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol?view=aspnetcore-10.0).
- [SQLite WAL read-only có điều kiện từ 3.22](https://www.sqlite.org/wal.html#readonly).
- [SQLite mặc định tối đa 10 attached databases](https://www.sqlite.org/limits.html#max_attached).
- [SQLite `ATTACH` với WAL không bảo đảm atomic cross-file commit](https://sqlite.org/lang_attach.html).
- [WCAG yêu cầu không dựa vào màu và giới hạn nội dung flashing](https://www.w3.org/WAI/WCAG22/Understanding/use-of-color.html).

