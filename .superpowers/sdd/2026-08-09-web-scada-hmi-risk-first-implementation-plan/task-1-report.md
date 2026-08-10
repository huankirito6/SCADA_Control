# Task 1 Report — Close SCADA architecture blockers

## Status

**FIX ROUND 1 VERIFIED AND COMMITTED (`HEAD`)**

> Các kết luận semantic của round ban đầu dưới đây là evidence lịch sử của commit `cdfec02b`, không còn được dùng làm completion claim. Phần `## Fix round 1` ghi validator/checklist semantic và evidence thay thế.

## Baseline commit (round ban đầu)

- SHA: `cdfec02bd7f7f39cf9ef14c0740694bdba9b9530`
- Message: `docs: close SCADA architecture blockers`
- Branch: `task/1-close-scada-architecture-blockers`
- Push: không push remote.

## Files changed

Committed:

- `docs/superpowers/specs/2026-08-04-web-scada-hmi-design.md`
- `docs/adr/0001-process-and-ipc-boundary.md`
- `docs/adr/0002-quality-and-time-model.md`
- `docs/adr/0003-config-activation.md`
- `docs/adr/0004-command-authority-and-durability.md`
- `docs/adr/0005-historian-durability-and-partitioning.md`
- `docs/adr/0006-scene-authority-and-telemetry-fsm.md`

Task evidence (ignored by repository-local `.git/info/exclude`):

- `.superpowers/sdd/2026-08-09-web-scada-hmi-risk-first-implementation-plan/task-1-report.md`

Round ban đầu không sửa `DA.txt`, source plan, source review hoặc file Task 2. Fix round 1 được người dùng cho phép sửa phần source plan cần thiết cho SC-01, SC-04, SC-05, SC-06 và traceability; vẫn không triển khai code Task 2+.

## Key decisions recorded

1. Production tách `Scada.Web`/`Scada.Runtime` theo process và service identity trước mọi hardware; Web không driver, credential hoặc OT route.
2. Runtime-owned signed positive allowlist khóa full physical tuple; config chỉ được hẹp hơn.
3. Command bind activation/config/semantic/physical/observation context; one-time capability được consume nguyên tử với durable `DispatchIntent` trước I/O.
4. Quality tách severity/reason flags/native status; trend dùng masks/durations riêng, không OR packed ordinal.
5. Historian hứa no-silent-loss với stable ingest identity và persisted gap/high-water marker; alarm/audit/command queues có durability riêng.
6. Runtime sở hữu stale theo công thức bắt buộc; browser giữ `RuntimeDisconnected` riêng và mọi invalid state disable command.
7. Telemetry dùng subscription generation + epoch + snapshot watermark, serialized latest-wins mailbox và explicit resync FSM.
8. Minimal immutable config/publish/activation chuyển vào foundation; activation có state machine và atomic active pointer.
9. `alarms.db` do Runtime single-write, Web query qua RPC; migration thuộc writer-owner.
10. Scene authority là JSON Schema/widget manifest với generated C#/TS, server canonical hash và shared corpus.
11. Traceability table round ban đầu có đúng cardinality 10 P0 và 16 P1; review sau đó phát hiện mapping semantic chưa đầy đủ ở P0-9/P1-8/P1-9/P1-12. Fix round 1 thay thế claim này bằng checklist semantic từng dòng.

## Verification commands and results

### 1. Ambiguity scan (Windows/Git Bash equivalent)

Command:

```bash
if grep -Ein '(^|[^[:alnum:]_])(TBD|TODO|FTE)([^[:alnum:]_]|$)|quyết sau|phải chốt|~[0-9]+h|ngày công|tháng' docs/superpowers/specs/2026-08-04-web-scada-hmi-design.md; then echo 'UNRESOLVED MARKERS FOUND'; exit 1; else rc=$?; if [ "$rc" -eq 1 ]; then echo 'NO MATCHES'; exit 0; else exit "$rc"; fi; fi
```

Output:

```text
NO MATCHES
```

Exit code: `0`.

The word-boundary around `FTE` is intentional: the raw case-insensitive alternation also matches the `fte` substring inside required field name `StaleAfterMs`; this command preserves the review marker intent without that false positive.

### 2. Structural traceability/contract validation (không phải semantic proof)

Command:

```bash
python - <<'PY'
from pathlib import Path
import re, sys
spec=Path('docs/superpowers/specs/2026-08-04-web-scada-hmi-design.md').read_text(encoding='utf-8')
errors=[]
for level,n in [('P0',10),('P1',16)]:
  for i in range(1,n+1):
    rows=re.findall(rf'^\| {level}-{i}(?:\s|\|)',spec,re.M)
    if len(rows)!=1: errors.append(f'{level}-{i}: expected one traceability row, got {len(rows)}')
adr_names=['process-and-ipc-boundary','quality-and-time-model','config-activation','command-authority-and-durability','historian-durability-and-partitioning','scene-authority-and-telemetry-fsm']
for i,name in enumerate(adr_names,1):
  p=Path(f'docs/adr/{i:04d}-{name}.md')
  if not p.is_file(): errors.append(f'missing {p}')
  elif '**Status:** Accepted' not in p.read_text(encoding='utf-8'): errors.append(f'{p}: not Accepted')
for token in ['process/service identity riêng','physical tuple','DispatchIntent','severitySeenMask','No-silent-loss','StaleAfterMs','snapshotWatermark','Minimal immutable config/publish/activation','alarms.db']:
  if token.casefold() not in spec.casefold(): errors.append('missing contract token: '+token)
print('PASS: all 10 P0 and 16 P1 findings map exactly once; 6 accepted ADRs and all required closure contracts are present' if not errors else '\n'.join(errors))
sys.exit(bool(errors))
PY
```

Output lịch sử (nhãn `PASS` này chỉ phản ánh cardinality/token, không chứng minh semantic mapping):

```text
PASS (STRUCTURAL ONLY): all 10 P0 and 16 P1 IDs occur exactly once; 6 accepted ADR files and selected contract tokens are present
```

Exit code: `0`.

### 3. Staged diff integrity and scope

Command:

```bash
git diff --cached --check
git diff --cached --name-status
git diff --cached --stat
```

Output summary:

```text
A  docs/adr/0001-process-and-ipc-boundary.md
A  docs/adr/0002-quality-and-time-model.md
A  docs/adr/0003-config-activation.md
A  docs/adr/0004-command-authority-and-durability.md
A  docs/adr/0005-historian-durability-and-partitioning.md
A  docs/adr/0006-scene-authority-and-telemetry-fsm.md
M  docs/superpowers/specs/2026-08-04-web-scada-hmi-design.md
7 files changed, 319 insertions(+), 70 deletions(-)
```

Exit code: `0`. Git emitted only expected LF→CRLF working-copy warnings; `git diff --check` found no whitespace errors.

### 4. Commit and clean branch verification

Command:

```bash
git show -s --format='%H%n%s' HEAD
git status --short --branch
```

Output:

```text
cdfec02bd7f7f39cf9ef14c0740694bdba9b9530
docs: close SCADA architecture blockers
## task/1-close-scada-architecture-blockers
```

Exit code: `0`.

## Self-review

- Re-read the complete source spec and review, then compared the staged diff against every Task 1 requirement.
- Removed the old in-process-before-split, packed-quality/qMask, unconditional historian-lossless, deferred-config and Runtime-only-migrator contradictions.
- Round ban đầu chỉ kiểm cardinality/token và đã diễn giải quá mức thành semantic PASS. Claim này bị rút lại; xem checklist semantic ở Fix round 1.
- Removed schedule/staffing estimates from implementation-driving sections and ran the ambiguity scan successfully.
- Confirmed only the spec and six requested ADR files were staged/committed; no Task 2 implementation, source plan/review or `DA.txt` changes.
- Confirmed exact commit message and no remote push.

## Concerns

- Round review phát hiện blocker SC-01..SC-10, TQ-02 và MN-01; Fix round 1 ghi trạng thái đóng từng finding ở phần bổ sung bên dưới.
- Task 1 chỉ thay đổi tài liệu nên không có executable product test suite; verification là ambiguity scan, structural traceability/contract validation, staged diff integrity và post-commit Git checks.
- Round ban đầu để report dưới repository-local exclude. Fix round 1 force-add cùng report này theo yêu cầu cập nhật spec/ADR/plan/report; report dùng symbolic `HEAD` cho fix commit để tránh self-referential SHA.

## Fix round 1

### Status and scope

**VERIFIED — commit được biểu diễn bằng symbolic `HEAD` trong tracked report**

- Baseline reviewed: `cdfec02bd7f7f39cf9ef14c0740694bdba9b9530`.
- User decision: hướng 1, cho phép sửa đúng các phần plan cần cho SC-01, SC-04, SC-05, SC-06 và traceability.
- Không triển khai code Task 2+; không sửa source review hoặc `DA.txt`.
- TQ-01 (regenerate non-truncated review package) thuộc controller sau commit, không được tuyên bố đã đóng ở round này.

### Finding closure

| Finding | Fix round 1 |
|---|---|
| SC-01 | Task 14 có `StaleTransitionTests.cs` và fake-clock matrix: formula bounds, exact threshold, logical/monotonic advance, recovery, restart/boot, wall/source-clock independence, deadband/store-rate bypass; ADR-0002 và P0-9 trỏ đúng Tasks 14/19. |
| SC-02 | Spec §4.4/§7.8 thống nhất ADR-0005/Task 16: bounded partition open/query + repository merge, không `ATTACH` toàn retention range, retention pin/refcount. |
| SC-03 | Runtime không mở `config.db`; authenticated config feed + local verified cache là topology chuẩn, kể cả Docker separate volumes. |
| SC-04 | Audit khóa cadence 100 events/60 seconds, forced seals, `AuditSealer` custody, append-only sink, 120-second overdue fail-closed, dual-signed rotation, trust history và key-loss epoch; Task 9 có lifecycle tests. |
| SC-05 | Backup dùng site-scoped commissioning key, OS custody/non-exportability, key ID, dual-signed rotation, separate public trust export, key-loss/new epoch và explicit offline trust import; Task 32 test lifecycle. |
| SC-06 | Cắt break-glass bypass khỏi scope một cách chuẩn tắc; local recovery chỉ khôi phục normal admin và giữ writes disabled tới khi normal gates pass; Task 10 assert không có bypass path/token. |
| SC-07 | Sửa factual wording cho SQLite WAL read-only, semantic gap verification, và Data Protection key loss. |
| SC-08 / MN-01 | Loại timing marker legacy (`slice`, `NGAY BÂY GIỜ`, `chưa được build`, `build sau`); thay bằng Task/hard gate hoặc deferred scope rõ; mở rộng ambiguity scan. |
| SC-09 | Hạ IEC wording thành partial design intent, không clause match/compliance/certification nếu chưa có mapping/evidence độc lập. |
| SC-10 | Đổi safety rhetoric thành operational-integrity/fail-visible và nhắc protection/interlock độc lập. |
| TQ-02 | Rút semantic overclaim của validator cũ; validator mới chỉ claim structural resolution, còn checklist dưới đây ghi đối chiếu semantic từng finding. |

### Semantic checklist: finding → ADR decision → later task → test/gate

Checklist này được re-read theo source review, nội dung ADR và task body; không suy diễn từ cardinality/token scan.

| Finding | Semantic decision checked | Later automated evidence checked | Result |
|---|---|---|---|
| P0-1 | ADR-0001 separate identities before hardware | T3/T12/T33 deployment, peer/ACL/IPC isolation | PASS |
| P0-2 | ADR-0004 full physical tuple, config only narrower | T11/T20 field-by-field remap/policy attacks | PASS |
| P0-3 | ADR-0004 binds activation/semantic/target/observation | T4/T8/T10/T21–22 hash + precondition matrix | PASS |
| P0-4 | ADR-0004 one-time capability + authoritative revocation | T10/T21–22 replay/expiry/revocation/channel tests | PASS |
| P0-5 | ADR-0004 durable intent before I/O | T9/T21 fault-injection crash matrix | PASS |
| P0-6 | ADR-0002 separated quality algebra | T4/T16 exhaustive combination/duration tests | PASS |
| P0-7 | ADR-0005 accepted identity + visible gap | T15–16/T29 overflow/retry/gap/load gates | PASS |
| P0-8 | ADR-0006 generation/epoch/watermark/mailbox FSM | T17 race/overflow/resync permutations | PASS |
| P0-9 | ADR-0002 Runtime stale formula; ADR-0006 UI transport state | T14 exact fake-clock stale matrix + T19 validity E2E | PASS |
| P0-10 | ADR-0003 foundation activation FSM/atomic pointer | T8/T23 crash/rollback/reconciliation | PASS |
| P1-1 | ADR-0002 logical/source/monotonic/boot/high-water | T5 clock-step/restart/source-order tests | PASS |
| P1-2 | ADR-0006 one Scene schema/manifest authority | T6 cross-language corpus/canonical hash | PASS |
| P1-3 | ADR-0006 normative Scene shape/limits/refs | T6/T24 malicious corpus + real screens | PASS |
| P1-4 | ADR-0005 Runtime-owned `alarms.db` | T7/T27 ownership + crash/recovery | PASS |
| P1-5 | ADR-0006 alarm cursor/backfill + trend invalid gaps | T27–28 reconnect/render-state tests | PASS |
| P1-6 | ADR-0005 bounded partition batches/pins | T16 long-range + concurrent retention | PASS |
| P1-7 | ADR-0003/0005 writer-owned migration | T7 wrong-owner ACL + crash boundaries | PASS |
| P1-8 | ADR-0004 complete L3 cadence/custody/sink/overdue/lifecycle | T9 fake-clock seal/key/sink/tamper matrix | PASS |
| P1-9 | ADR-0003/0005 causal backup + complete signing-key lifecycle | T32 key/ACL/rotation/loss/archive/restore matrix | PASS |
| P1-10 | ADR-0001 driver cancellation/deadline/result/arbitration | T13–14 conformance and arbitration | PASS |
| P1-11 | ADR-0001/0004 OPC trust + Modbus zone/policy | T20/T31/T33 security/deployment gates | PASS |
| P1-12 | ADR-0004 deny-default, local recovery, no break-glass | T10/T22 no-bypass + endpoint revocation | PASS |
| P1-13 | ADR-0006 JS-owned transaction/conflict FSM | T25–26 conflict/disconnect/undo | PASS |
| P1-14 | ADR-0006 transformed touch/keyboard/focus/non-color | T18–19/T26 accessibility E2E | PASS |
| P1-15 | ADR-0006 pinned performance budgets | T18/T29/T34 repeatable 20-client × 300-tag gate | PASS |
| P1-16 | ADR-0001 partial design intent only | T33–34 claims scan/evidence checklist | PASS |

### Verification commands and observed results

#### Expanded ambiguity scan

Command:

```bash
if grep -Ein '(^|[^[:alnum:]_])(TBD|TODO|FTE)([^[:alnum:]_]|$)|quyết sau|phải chốt|~[0-9]+h|ngày công|tháng|(^|[^[:alnum:]_])slice([^[:alnum:]_]|$)|NGAY BÂY GIỜ|chưa được build|build sau|reserve[^.\n]{0,80}(ngay|bây giờ)' docs/superpowers/specs/2026-08-04-web-scada-hmi-design.md; then echo 'UNRESOLVED MARKERS FOUND'; exit 1; else rc=$?; if [ "$rc" -eq 1 ]; then echo 'NO UNRESOLVED IMPLEMENTATION-TIMING MARKERS'; exit 0; else exit "$rc"; fi; fi
```

Output: `NO UNRESOLVED IMPLEMENTATION-TIMING MARKERS`
Exit code: `0`.

#### Structural cross-reference validator (supporting evidence, not semantic proof)

Command:

```bash
python 'C:\Users\huan2\AppData\Local\Temp\validate_task1_traceability.py' .
```

Observed summary:

```text
PASS: 26/26 traceability rows resolve to Accepted ADRs, existing later tasks, and executable test/assertion gate text
PASS P0-1 ... PASS P0-10
PASS P1-1 ... PASS P1-16
PASS: high-risk exact contracts present for P0-9, P1-8, P1-9 and P1-12
PASS: targeted stale contradictions absent
```

Exit code: `0`. Full per-row output corresponds one-for-one to the semantic checklist above; the script proves link/structure and high-risk contract presence only.

#### Targeted contradiction scan

Command: Python literal scan for the rejected direct-config, whole-range-attach, IEC-clause-match, cookie-forgery, canonical-gap-test and safety-rhetoric phrases.

Output: `PASS: no targeted factual/topology/safety/IEC contradictions`
Exit code: `0`.

#### Staged diff integrity and scope

Command:

```bash
git diff --cached --check
git diff --cached --name-status
git diff --cached --stat
```

Observed output:

```text
A  .superpowers/sdd/2026-08-09-web-scada-hmi-risk-first-implementation-plan/task-1-report.md
M  docs/adr/0002-quality-and-time-model.md
M  docs/adr/0003-config-activation.md
M  docs/adr/0004-command-authority-and-durability.md
M  docs/adr/0005-historian-durability-and-partitioning.md
M  docs/superpowers/plans/2026-08-09-web-scada-hmi-risk-first-implementation-plan.md
M  docs/superpowers/specs/2026-08-04-web-scada-hmi-design.md
7 files changed, 360 insertions(+), 54 deletions(-)
```

Exit code: `0`; `git diff --cached --check` không báo whitespace error. Các warning LF→CRLF chỉ mô tả working-copy conversion.

### Files modified in fix round 1

- `docs/superpowers/specs/2026-08-04-web-scada-hmi-design.md`
- `docs/superpowers/plans/2026-08-09-web-scada-hmi-risk-first-implementation-plan.md`
- `docs/adr/0002-quality-and-time-model.md`
- `docs/adr/0003-config-activation.md`
- `docs/adr/0004-command-authority-and-durability.md`
- `docs/adr/0005-historian-durability-and-partitioning.md`
- `.superpowers/sdd/2026-08-09-web-scada-hmi-risk-first-implementation-plan/task-1-report.md`

### Self-review and concern

- Re-read all 26 trace rows against the complete finding text, corresponding accepted ADR decision and cited task test/assertion body.
- Confirmed plan edits are limited to Tasks 9, 10, 14 and 32 (SC-04, SC-06, SC-01, SC-05); no Task 2+ implementation code was added.
- Checked spec no longer offers direct `config.db` access to Runtime, whole-range partition attach, clause-level IEC match, cookie-forgery claim, canonical-serialization gap test, legacy slice timing, or safety-function rhetoric.
- Report does not claim product tests ran: this is a docs-only closure; commands are document semantic/structural and Git integrity checks.
- Remaining concern/owner: TQ-01 package regeneration is intentionally left to controller after this commit. No other known Task 1 finding is intentionally deferred.
