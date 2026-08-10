# Nền tảng SCADA/HMI trên Web — Thiết kế kiến trúc

**Ngày:** 2026-08-04
**Trạng thái:** Design — chờ duyệt trước khi lập kế hoạch triển khai
**Bối cảnh:** Nghiên cứu & phát triển, hướng tới startup. Một người phát triển, part-time (~0.4 FTE).

---

## 1. Mục tiêu

Bảy mục tiêu là định nghĩa duy nhất của phạm vi dự án. Mọi quyết định trong tài liệu này truy vết được về ít nhất một trong số chúng.

| # | Mục tiêu |
|---|---|
| 1 | Kết nối và đọc dữ liệu từ nhiều loại thiết bị công nghiệp (PLC, thiết bị Modbus, OPC UA...) |
| 2 | Hiển thị dữ liệu real-time trên trình duyệt |
| 3 | Cho phép người dùng tự thiết kế màn hình giám sát bằng kéo-thả |
| 4 | Lưu lịch sử dữ liệu và cảnh báo |
| 5 | Gửi lệnh điều khiển xuống thiết bị một cách có kiểm soát và có audit |
| 6 | Chạy được trên Windows, Linux, Docker |
| 7 | Không phụ thuộc Internet — hoạt động hoàn toàn trong mạng nội bộ |

`DA.txt` trong thư mục dự án là tài liệu tham khảo. Nó **không** phải nguồn chuẩn cho tài liệu này; nơi hai bên khác nhau, tài liệu này thắng.

### 1.1. Định vị hệ thống — điều này KHÔNG phải

Đây là **lớp giám sát (supervisory layer)**, không phải bộ điều khiển an toàn.

Hệ thống này **không bao giờ** được dùng làm: E-stop, SIS trip, safety interlock, motion safety, burner safety, hay bất kỳ logic SIL/PL nào. Nó không được là lớp bảo vệ duy nhất cho bất kỳ nguy cơ nào. Mọi interlock an toàn phải nằm trong PLC hoặc hardwired.

Lý do ghi điều này vào mục đầu: nó là ràng buộc thiết kế thật, không phải lời tuyên bố miễn trừ. Nó cho phép chấp nhận telemetry mất mát (§7.1), chấp nhận `INDETERMINATE` là trạng thái lệnh hợp lệ (§8.4), và chấp nhận không đảm bảo non-repudiation (§9.2). Nếu định vị này đổi, phần lớn tài liệu phải viết lại.

---

## 2. Ràng buộc cố định

Những điều dưới đây không thương lượng trong toàn bộ vòng đời dự án.

### 2.1. Biên giới mạng

Trình duyệt **không bao giờ** được: kết nối trực tiếp tới PLC, gửi packet Modbus, mở session OPC UA, giữ credential của thiết bị, tự quyết định lệnh thành công hay thất bại, đánh giá interlock, hay lưu cấu hình driver nhạy cảm.

**Runtime là thành phần duy nhất có quyền truy cập mạng điều khiển.**

### 2.2. Màn hình HMI là dữ liệu, không phải code

Scene (màn hình) do người dùng thiết kế bị cấm chứa: JavaScript thô, inline event handler, URL `javascript:`, HTML tuỳ ý, `foreignObject`, truy cập file/network từ biểu thức, SVG import chưa sanitize, và C# scripting. **Widget và hàm biểu thức là whitelist-only** — không có cơ chế mở rộng bằng cách nhúng code.

### 2.3. An ninh nền

Không có mật khẩu mặc định. Không phơi ra Internet theo mặc định. Remote access tắt theo mặc định. TLS. Least privilege. Không bao giờ log mật khẩu, private key, hay recipe nhạy cảm. Secret lưu qua OS secret store hoặc mã hoá at-rest. Kênh Runtime↔Web phải được xác thực. Anti-forgery + secure same-site cookie bắt buộc trên mọi endpoint lệnh và cấu hình. Project/SVG/asset import là **untrusted input** và phải sanitize. Restore package phải verify hash/signature.

### 2.4. Audit

Audit log là **append-only**. Operator không có đường nào xoá được audit log.

### 2.5. Zero-Internet (mục tiêu 7)

Không CDN. Không font/icon/JS/CSS tải từ ngoài. Không license check online. Không telemetry ra ngoài theo mặc định. Điều này được **cưỡng chế bằng test**, không bằng thiện chí — xem §11.3.

---

## 3. Envelope quy mô

Thiết kế nhắm đúng dải này. Đây là con số dùng để bác bỏ phương án, không phải kỳ vọng marketing.

| Chiều | Mục tiêu |
|---|---|
| Số tag | ~2.000 |
| Phân bố chu kỳ scan | ~100 tag @250ms · ~900 @1s · ~1.000 @5s |
| Kết nối thiết bị | 4–8 |
| Người dùng đồng thời | 1–20 |
| Tag trên một màn hình | ~300 |
| Tốc độ historian | ~1.000 sample/s trung bình, 5.000 burst |
| Số Runtime hoạt động | 1 |
| Mạng | LAN / mạng OT, không qua Internet |

Hệ quả trực tiếp: **không cần** phân tán, HA, sharding, message broker, hay Kubernetes. Một quyết định kiến trúc nào biện minh bằng "để scale sau" mà không nằm trong dải trên là scope drift.

---

## 4. Kiến trúc tổng thể

### 4.1. Hai process, một boundary

```
┌─────────────────────────────────────────────────────────────┐
│ Browser                                                     │
│  ┌──────────────────────┐    ┌───────────────────────────┐  │
│  │ Trang config/editor  │    │ Trang HMI runtime         │  │
│  │ Blazor Interactive   │    │ Static SSR (ZERO circuit) │  │
│  │ Server (có circuit)  │    │ + JS module render SVG    │  │
│  └──────────────────────┘    └───────────────────────────┘  │
└───────────┬──────────────────────────────┬──────────────────┘
            │ HTTPS + circuit              │ HTTPS + telemetry hub
┌───────────▼──────────────────────────────▼──────────────────┐
│ Scada.Web  (ASP.NET Core Kestrel)                           │
│  · Xác thực người + RBAC + anti-forgery                     │
│  · Editor, quản lý config, publish version                  │
│  · Telemetry hub (fan-out ra client)                        │
│  · GHI: config, audit-của-Web     KHÔNG mở: historian.db     │
└───────────┬─────────────────────────────────────────────────┘
            │ Kênh nội bộ có xác thực (UDS / named pipe)
┌───────────▼─────────────────────────────────────────────────┐
│ Scada.Runtime  (worker process)                             │
│  · Driver (Modbus TCP/RTU, OPC UA, Sim)                     │
│  · Scan scheduler + tag engine + quality                    │
│  · Alarm engine                                             │
│  · Command executor + write policy envelope                 │
│  · GHI: historian.db + audit-của-Runtime                     │
│  · ĐỌC query_only: config                                    │
└───────────┬─────────────────────────────────────────────────┘
            │ Mạng điều khiển (chỉ process này có)
        [ PLC / thiết bị Modbus / OPC UA server ]
```

### 4.2. Vì sao hai process

Không phải để scale. Ba lý do đều là an ninh và vận hành:

1. **Biên giới đặc quyền.** Web nhận request từ người và phơi ra HTTP. Runtime giữ quyền truy cập mạng điều khiển. Web bị RCE không đồng nghĩa với ghi PLC tuỳ ý — nhưng chỉ khi §9.3 (write policy envelope) tồn tại. Không có §9.3, biên giới process là trang trí.
2. **Vòng đời độc lập.** Restart Web để deploy UI không được làm gián đoạn scan hay mất buffer store-and-forward.
3. **Không cùng lịch trình lỗi.** Một truy vấn trend nặng hay memory leak trong UI không được làm jitter chu kỳ scan.

### 4.3. Boundary bây giờ, process split sau

Boundary được định nghĩa bằng **interface C#** và thực thi **in-process** trong các slice đầu. gRPC thay vào sau, không đổi call site.

Đây là điểm hai agent rà soát đánh dấu là mùi over-engineering (hai implementation cho một contract khi chưa có user nào). Tôi giữ, với hai điều kiện làm nó không thành nợ:

- **Policy nằm TRÊN transport**, không nằm trong implementation. Hai implementation có hành vi khác nhau dưới tải — gRPC HTTP/2 flow control sẽ *block*, còn `Channel<T>` in-process sẽ *drop*. Nếu policy backpressure nằm trong implementation thì hai bản chạy khác nhau và test in-process không nói gì về production.
- **Mọi DTO qua boundary là immutable, và implementation in-process phải copy.** Trả reference vào cache nội bộ của Runtime là con đường ngắn nhất tới một bug không debug được.

Nếu tới slice cuối vẫn chưa có nhu cầu tách process thật, xoá bản gRPC — không giữ code không dùng để "cho tương lai".

### 4.4. Quyền sở hữu dữ liệu — ba file DB

Phát biểu "Runtime là writer duy nhất" là **sai** và đã bị loại. Nó tự chống lại chính nó: lệnh bị RBAC ở Web từ chối không bao giờ tới Runtime, nên audit sẽ mất đúng loại sự kiện quan trọng nhất — nỗ lực ghi bị từ chối.

Phát biểu đúng: **chỉ Runtime được append sample và alarm event.**

Nhưng audit thì **cả hai bên** đều phải ghi được, và đây là chỗ dễ thiết kế sai. Web ghi audit của hành động con người (login, publish, lệnh bị từ chối). Runtime cũng có sự kiện phải audit: `tag_load_status` (§6.2), clock re-anchor marker (§7.6), service start/stop, và toàn bộ command event stream (§8.3). Hai writer trên **cùng một hash chain** là một cuộc đua giành `prev_hash` — chain sẽ vỡ, và nó vỡ dưới tải nên test đơn lẻ sẽ không thấy.

Nên: **hai chain độc lập, mỗi chain một writer duy nhất, mỗi chain một file.**

| File | Writer duy nhất | Reader | Cách reader truy cập |
|---|---|---|---|
| `config.db` | **Web** | Runtime | Mở `query_only=1` |
| `audit-web.db` | **Web** | — | Chain riêng, `audit verify` kiểm độc lập |
| `historian.db` + `audit-runtime.db` | **Runtime** | Web | **Không mở file.** Query qua RPC |

`audit verify` (§9.2) verify **từng chain riêng** rồi trình bày hợp nhất theo thời gian. Không có chain thứ ba nào "gộp" hai chain — gộp nghĩa là có một writer thứ ba, tức là quay lại đúng vấn đề vừa loại.

Vì sao Web ghi `config.db`: engineer phải sửa được cấu hình **khi Runtime đang down**, và Web là bên duy nhất xác thực con người.

Vì sao Web **không** mở `historian.db`:

- `mode=ro` **không recover được WAL**. Runtime crash để lại WAL cần replay → reader read-only trả `SQLITE_READONLY_RECOVERY` và không đọc được gì. Đây không phải trường hợp hiếm, đó là đúng lúc bạn cần đọc lịch sử nhất.
- WAL **bắt buộc reader phải ghi được** `-shm` và `-wal`. "ACL read-only + WAL" vốn không chạy được, nên "cưỡng chế Web read-only bằng quyền file" là ảo tưởng ngay từ đầu.
- Trong Docker Compose, hai container **không** share được DB file một cách đáng tin. Đây là phương án duy nhất sống ở cả 3 target của mục tiêu 6.

Các file được join ở **tầng repository**, không ở tầng SQL. Không có `ATTACH` xuyên các file này trong code nghiệp vụ. (`ATTACH` chỉ dùng bên trong `historian.db` để ghép các file partition theo tuần — §7.8.)

### 4.5. Solution layout

```
Scada.Domain          Model, không phụ thuộc gì
Scada.Contracts       Interface + DTO qua boundary (immutable)
Scada.Runtime         Scheduler, tag engine, alarm, command executor
Scada.Drivers.Abstractions
Scada.Drivers.Simulator
Scada.Drivers.ModbusTcp
Scada.Drivers.ModbusRtu
Scada.Drivers.OpcUa
Scada.Infrastructure.Sqlite   Repository impl
Scada.Web             Blazor + hub + REST command endpoint
Scada.Web.Client      JS module: renderer L2, editor L3
Scada.Cli             audit verify, backup, migrate, diag
+ test project tương ứng
```

Ràng buộc kiến trúc được cưỡng chế bằng **NetArchTest** trong CI: `Scada.Domain` không tham chiếu gì; `Scada.Web` không tham chiếu `Scada.Drivers.*`; `Scada.Runtime` không tham chiếu `Scada.Web`; không project nào ngoài `Scada.Infrastructure.Sqlite` được tham chiếu `Microsoft.Data.Sqlite`.

### 4.6. Repository interface phải nói ngôn ngữ nghiệp vụ

"Che DB sau interface" chỉ có giá trị nếu interface không rò rỉ chi tiết lưu trữ. `IHistorianRepository` trả về khái niệm domain (bucket, seed, quality mask), **không** trả `DataTable`, `IQueryable`, chuỗi SQL, hay tên cột. Rò rỉ một lần là "chuyển sang Postgres" thành hư danh.

Kiểm tra cụ thể: nếu một thay đổi trong schema SQLite buộc phải sửa file ngoài `Scada.Infrastructure.Sqlite`, interface đã rò.

---

## 5. Model tag và định danh

### 5.1. Định danh là int64 bất biến, KHÔNG phải path

`TagId` là **int64 surrogate**, sinh khi tạo tag, bất biến vĩnh viễn. Path phân cấp (`Site/Line1/Mixer/Speed`) là **metadata đổi tên được**.

Lý do quyết định — mục tiêu 5. Một audit record dạng *"operator X đã ghi giá trị 1 vào `Site/A1/L2/M3/Speed` lúc T"* trở thành **sai sự thật** nếu path đó sau này được đổi tên hoặc chuyển sang máy khác. Audit phải vừa **bất biến** vừa **giải được**, và không thể xây cả hai trên một chuỗi có thể sửa. Đổi tên là chuyện xảy ra liên tục lúc commissioning, nên đây không phải trường hợp góc.

Chọn int64 thay vì ULID/GUID: mọi hàng historian đều mang khoá này, và đây là bảng lớn nhất trong hệ thống. 8 byte so với 16–26 byte, và index cluster tốt hơn. Hệ thống có **một** writer nên không cần khoá phân tán. "ULID hay int64, quyết sau" là nợ chưa trả — trả bây giờ.

`former_paths` được lưu để giải path lịch sử, với hai luật: mỗi bản ghi **bắt buộc** có `validUntil`, và khi một path bị đổi tên rồi tái sử dụng cho tag khác thì **path hiện tại thắng**. Không có hai luật này, tra cứu path lịch sử trả về sai tag.

Tag bị bỏ khỏi cấu hình phải được **retire**, không bao giờ delete — historian còn tham chiếu tới nó.

### 5.2. Quality là bitfield, không phải enum phẳng

Quality dùng bitfield kiểu OPC UA **từ hàng dữ liệu đầu tiên**. Enum phẳng (`Good/Bad/Uncertain`) sẽ đủ trong ba tháng rồi buộc phải migrate **bảng lớn nhất trong hệ thống** khi cần phân biệt "bad vì comm fail" với "bad vì device báo lỗi" với "uncertain vì giá trị được hold".

```
bit 0-1   severity   Good | Uncertain | Bad
bit 2-7   reason     CommFail | DeviceError | ConfigError | Stale |
                     LastKnownValue | OutOfRange | NotInitialized | Simulated | Forced
bit 8-15  reserved
```

**Quality không phải một giá trị và không bao giờ được coerce.** Xem §7.4 — đây là luật an toàn quan trọng nhất trong tài liệu.

### 5.3. Model tag

```
TagId          int64, bất biến
Path           string, đổi tên được
DataType       Bool | Int16 | Int32 | Int64 | Float32 | Float64 | String | Enum
Unit, Description
ScanGroupId    → §6.1
Address        cấu trúc theo driver, không phải chuỗi tự do
ByteOrder      per-tag — không phải per-device
WordOrder      per-tag
Scaling        raw→eng: linear | none
Deadband       { mode: none|absolute|percentOfSpan, value }
HeartbeatSec   bắt buộc (§7.3)
Writeable      bool, mặc định false (§9.3)
AreaId         cho RBAC theo khu vực
Retired        bool
```

Byte order và word order là **per-tag**, không per-device. Thiết bị thật trộn lẫn quy ước trong cùng một register map, thường ở đúng chỗ đắt nhất — một block float đọc đúng và một block float đọc ngược trên cùng một PLC. Per-device là giả định sẽ vỡ khi gặp hardware thứ ba.

Address là **cấu trúc**, không phải chuỗi. Chuỗi "40001" buộc phải parse ở runtime, không validate được lúc publish, và không diff được giữa hai version.

---

## 6. Scan và driver

### 6.1. Scan group, không phải per-tag scan

Scan được tổ chức theo **scan group**: một nhóm tag cùng connection, cùng chu kỳ, được gom thành các **register block** liên tục.

Đây là thay đổi schema cấu hình, nên **phải có trước version publish đầu tiên** — thêm sau nghĩa là migrate mọi file cấu hình đã ký hash.

Vì sao bắt buộc: 900 tag @1s mà không gom block là 900 request/s. Con số đó bất khả thi trên cả Modbus TCP, chưa nói serial. Gom thành block (một FC3 đọc 100 register phục vụ 40 tag) là khác biệt giữa chạy được và không.

Coalescing có tham số: **gap tối đa được phép** giữa hai tag trong cùng block (đọc thừa vài register rẻ hơn một request nữa) và **số register tối đa** mỗi request (giới hạn protocol: 125 register cho FC3).

### 6.2. Validator ngân sách scan — không có nó là jitter im lặng

**100 tag @250ms trên RS-485 9600 baud là bất khả thi về mặt vật lý.** Một FC3 đọc 20 register ≈ 60–80 ms kể cả silence 3.5 char và turnaround. Chu kỳ 250 ms chứa được 3–4 request. Không có validator, hệ thống sẽ **nhận publish cấu hình đó rồi jitter im lặng** và người dùng sẽ kết luận sản phẩm không đáng tin.

Nên: **validate lúc publish** ngân sách byte-per-cycle so với baud rate cho mỗi scan group trên transport serial, và cảnh báo (không chặn) cho TCP. Runtime báo **tốc độ thực đạt được** cho từng scan group qua system tag (§10.2), để lệch giữa cấu hình và thực tế là con số quan sát được chứ không phải cảm giác.

Validate lúc publish chỉ bắt được **cú pháp**, không bắt được **năng lực** — một địa chỉ hợp lệ về hình thức vẫn có thể không tồn tại trên thiết bị. Nên cần thêm `tag_load_status(version, tagId, status, reason)` do Runtime ghi vào `audit-runtime.db` (§4.4) sau khi load, và Web đọc qua RPC để hiển thị. Nếu không, engineer publish rồi tin là xong.

### 6.3. Scheduler: skip-and-count, không bao giờ catch-up

Khi một chu kỳ trượt deadline, scheduler **bỏ chu kỳ đó và đếm**, không bao giờ chạy bù. Catch-up dồn burst xuống bus serial làm tình hình xấu hơn chính vấn đề nó định chữa.

Transport serial được **serialize** với một **ngân sách request mỗi chu kỳ** ngay từ dòng code đầu tiên — không phải tối ưu về sau. RS-485 là half-duplex single-master; một scheduler cho phép hai request đồng thời trên cùng cổng không phải chậm, nó **sai**.

Thêm: circuit breaker per connection + exponential backoff. Một thiết bị rút cáp không được làm 7 thiết bị còn lại chậm theo.

### 6.4. Contract driver

```
Connect / Disconnect
Browse            (nơi protocol hỗ trợ)
Read(blocks)
Write(items)
Subscribe / Unsubscribe   (OPC UA native; Modbus polling emulation)
GetDiagnostics
```

Bảy trạng thái connection: `Disabled · Connecting · Online · Degraded · Reconnecting · Offline · Faulted`. `Degraded` tồn tại riêng vì "kết nối được nhưng một phần block lỗi" là trạng thái phổ biến nhất trong thực tế và trạng thái mà nhị phân online/offline che mất.

**Chỉ MỘT write outstanding mỗi thiết bị.** Bắt buộc cho RTU về mặt protocol, và ở TCP nó loại bỏ race trong hệ thống. Nó **không** loại bỏ được một HMI panel thứ ba cùng ghi — xem §8.4.

### 6.5. Driver trong phạm vi

Simulator (slice đầu, để làm mọi việc khác không cần hardware) → Modbus TCP → Modbus RTU/RS-485 → OPC UA.

Modbus RTU nằm trong phạm vi và **không** bị bỏ. Nó là giao thức phổ biến nhất ở tầng thiết bị, và nó là lý do tồn tại của §6.2, §6.3, và §11.1.

---

## 7. Dữ liệu real-time và lịch sử

### 7.1. Telemetry lossy, historian và audit lossless

Đây là hai khái niệm khác nhau và tôi đã từng nhập chúng làm một, tạo ra một tiêu chí nghiệm thu bất khả thi ("resync không hở sequence" trên kênh đã tuyên bố lossy).

| Kênh | Bảo đảm | Cách kiểm |
|---|---|---|
| Telemetry → browser | **Convergence**: sau resync, client hiển thị giá trị hiện tại đúng trong ≤ **2.000 ms** | So snapshot sau resync |
| Historian | Không trùng, không hở | `QueryTrend` sau test tải |
| Audit + command journal | Lossless, append-only, fail-closed | `audit verify` (§9.2) |

Cơ chế của telemetry là **epoch per frame**, không phải sequence per sample.

### 7.2. Handle epoch — chế độ lỗi tệ nhất trong SCADA

Telemetry dùng numeric handle theo session thay cho TagId để giảm payload. Không có epoch, đây là bug: SignalR reconnect cấp map handle mới, client vẫn còn cache map cũ → **hiển thị giá trị ĐÚNG của tag SAI.**

Đây là chế độ lỗi tệ nhất có thể vì **nó không trông giống lỗi**. Màn hình vẫn số nhảy, vẫn xanh, và operator hành động trên nó.

Nên:

- Mỗi frame mang `epoch`.
- Snapshot trả kèm bảng `handle → TagId` và epoch tương ứng.
- Client **drop mọi frame khác epoch hiện tại**, không cố diễn giải.
- **Mọi API historian nhận và trả `TagId`, tuyệt đối không nhận handle.** Handle chỉ sống trong một session telemetry.

### 7.3. Ghi historian: store-on-change + deadband + heartbeat

**Deadband so với giá trị đã LƯU lần cuối, không so với scan trước.** So với scan trước thì một tín hiệu trôi chậm — nhiệt độ lò, mức bồn — **không bao giờ** vượt ngưỡng và **không bao giờ được ghi** ngoài heartbeat. Bạn sẽ mất chính xác loại dữ liệu mà người ta mua historian để có.

Deadband **vô nghĩa với bool/enum/string** — những kiểu này store-on-change tuyệt đối.

Mỗi tag cần **max store rate** để một tín hiệu nhiễu không tự mình lấp đĩa.

**Heartbeat là ràng buộc bắt buộc của mô hình đọc, không phải tiện lợi.** Không có nó, một tag bool không đổi trong 3 tháng biến truy vấn as-of thành full scan ngược. Điều này liên kết §7.3 với §7.5: heartbeat là thứ bound truy vấn seed.

### 7.4. Quality không bao giờ được coerce — luật an toàn

Quality Bad / Stale / thiếu dữ liệu phải trả sentinel **`Invalid`**, và `Invalid` phải **poison** toàn bộ biểu thức chứa nó, giống NULL trong SQL.

Lý do, cụ thể: `temp > 100` với `temp` quality Bad, nếu coerce về 0, trả `false`. Alarm **im lặng**. Màn hình trông bình thường. Đó là cách một HMI giết người.

Kèm hai cơ chế bắt buộc:

- **`onInvalid` khai báo per-binding** — mỗi binding phải nói rõ nó làm gì khi dữ liệu chết (hatch pattern, dấu ?, giữ giá trị cuối kèm dấu hiệu stale). Không có mặc định im lặng.
- **Chỉ báo cấp màn hình "N/300 tag invalid"** luôn hiển thị.

Luật, viết ra để không bị lách: **không bao giờ để một màn hình trông bình thường khi dữ liệu của nó đã chết.**

### 7.5. Mô hình đọc: `QueryTrend`, không phải `QueryRange`

`QueryRange(tagId, from, to) → samples[]` bị loại. Nó rò rỉ theo hướng sai và có ba lý do độc lập:

**Khối lượng.** 300 tag × 8h ở 1s = ~8,6 triệu hàng. Không thể đẩy xuống browser. Downsample phải ở **server** theo số pixel, và phải vẽ **envelope min/max, không phải avg** — avg xoá spike, mà với SCADA đó là xoá đúng thứ người ta mở trend để tìm.

**Trung bình cộng là SAI** trên dữ liệu store-on-change. Mỗi sample đại diện một **khoảng thời gian**, không phải một điểm. Phải **time-weighted**. Và **phải loại khoảng Bad khỏi cả tử số và mẫu số** — nếu không, một giá trị bị hold qua 10 phút CommFail sẽ kéo trung bình về nó và bạn có một con số trông rất hợp lý mà hoàn toàn bịa ra. Một con số sai mà trông đúng tệ hơn một khoảng trống.

**Khả năng chuyển Postgres.** TimescaleDB có `time_bucket()`, `locf()`, `interpolate()`. Nếu logic đọc nằm *trên* repository thì chuyển sang Postgres không dùng được chúng — tức "che sau interface" thất bại ở đúng chỗ đắt nhất.

Nên contract là:

```
QueryTrend(tagIds, from, to, maxPoints, aggregate)
  → per tag: {
      seed: sample as-of `from`,        // BẮT BUỘC
      buckets: [{ tsStart, first, last, min, max,
                  twAvg, durGoodMs, durTotalMs, qMask }]
    }
```

- **Seed as-of là bắt buộc.** Không có nó, mọi series bắt đầu bằng một khoảng trống giả ở cạnh trái — người dùng thấy "không có dữ liệu" trong khi dữ liệu vẫn ổn, chỉ là sample cuối nằm trước `from`.
- Truy vấn seed: 300 seek dùng prepared statement tái sử dụng, bound bằng `ts_us >= from − 2 × heartbeat`. **Heartbeat là thứ làm cái bound này khả thi.**
- Aggregate phải **phân rã được**: lưu `tw_sum` + `dur_good_ms`, **không bao giờ** lưu avg đã tính. Avg đã tính không roll-up được.
- `qMask` là **OR bitmask** của quality trong bucket.

### 7.6. Thời gian là một subsystem

**Timestamp phải đơn điệu tăng per tag.** Phát hiện NTP step mà không xử lý là nửa việc — và nửa còn lại mới là phần phá hệ thống. NTP step **lùi** làm sample mới có timestamp nhỏ hơn sample trước cùng tag, phá vỡ giả định mà "last value holds", truy vấn as-of, và primary key `(tagId, ts)` đều dựa vào.

```
ts = anchorWall + (monotonicNow − anchorMonotonic)
```

Re-anchor **chỉ khi** phát hiện step, và ghi một marker vào `audit-runtime.db` (§4.4) khi re-anchor. Enforce cứng `ts > lastTs(tag)`; đếm số lần phải cưỡng chế và phơi qua system tag.

**Độ phân giải là micro giây (`ts_us`), không phải milli.** 1 ms không đủ: burst sinh timestamp trùng trên cùng tag, và primary key là `(tag_id, ts_us)`.

Lưu **hai** timestamp: wall clock và monotonic-since-boot, kèm `boot_id`. Ở site air-gapped không có NTP, monotonic là thứ duy nhất tin được, và cặp này là cách duy nhất tái dựng lại chuỗi sự kiện sau khi phát hiện clock sai.

DST: bucket theo shift/ngày phải tính theo timezone của site, không phải UTC naive — nếu không, ca đêm ngày chuyển DST có 23 hoặc 25 giờ và báo cáo sản lượng sai.

### 7.7. Schema và pragma SQLite

```sql
CREATE TABLE sample (
  tag_id  INTEGER NOT NULL,
  ts_us   INTEGER NOT NULL,
  val     REAL,        -- giới hạn 2^53
  val_i   INTEGER,     -- counter 64-bit
  val_s   TEXT,
  q       INTEGER NOT NULL,
  PRIMARY KEY (tag_id, ts_us)
) STRICT, WITHOUT ROWID;
```

- `WITHOUT ROWID` → cluster theo `(tag_id, ts_us)`, đúng thứ tự mọi truy vấn cần. **Không thêm index thứ hai** — nó chỉ nhân đôi chi phí ghi.
- `STRICT` từ đầu. Type affinity lỏng của SQLite sẽ âm thầm nhận string vào cột số.
- `val REAL` không giữ được counter 64-bit vượt 2^53 → cột `val_i` riêng.
- WAL + `synchronous=NORMAL`.
- Reader dùng **`query_only=1`**, không dùng `mode=ro` (§4.4).
- Reader phải set `busy_timeout` và **giới hạn thời gian sống của read transaction**.

**WAL starvation là chế độ lỗi vận hành thật:** một reader chạy lâu (trend 8h) giữ read-mark → checkpoint không chạy → WAL phình đơn điệu → commit của writer chậm dần. Nên: giám sát kích thước WAL qua system tag, và test nghiệm thu phải có reader song song (§12).

### 7.8. Retention bằng nhiều file, không bằng DELETE

SQLite không có partition. Làm partition ở **tầng repository**: một file mỗi tuần, `ATTACH` các file trong khoảng truy vấn, retention = **xoá file**, O(1).

`DELETE` + `VACUUM` trên bảng vài chục triệu hàng biến mỗi lần retention thành một lần hệ thống đứng. Không làm từ đầu thì ngày thứ 31 là lần đầu bạn phát hiện điều đó — trên máy khách.

---

## 8. Cấu hình, publish, và lệnh

### 8.1. Config là version bất biến

Engineer sửa cấu hình trong **draft**. **Publish** tạo một version **bất biến** kèm **canonical hash**. Runtime **pull** version, không nhận push.

Điều này loại bỏ RPC push-config và cả một class bug đồng bộ. Nhưng nó cần bốn thứ để đúng:

**Canonical JSON phải được định nghĩa, không phải giả định.** "Hash của JSON" là vô định nghĩa cho tới khi chốt: thứ tự khoá, cách round-trip float, encoding, và **có gồm `schemaVersion` hay không**. Không chốt thì hash khác nhau giữa hai máy hoặc hai culture và signature vô dụng. Bảo vệ bằng **property test chạy trên nhiều culture** (`tr-TR` là culture làm vỡ code so sánh chuỗi).

**`ReloadConfig` không được là điểm đơn nhất.** Web publish rồi crash trước khi gọi được → Runtime kẹt ở version cũ vĩnh viễn. Nên Runtime **cũng poll** một con trỏ `latest_published_version` một hàng. RPC là đường nhanh; poll là đường đúng.

**Mốc kích hoạt ở mức tag, không mức version.** `config_activation(version)` với PK = version **vỡ ngay khi rollback** — republish version N−1 là kích hoạt nó lần thứ hai. Cần `activation_id` surrogate. Và mức version tạo mốc giả: thêm một màn hình HMI cũng sinh mốc "cấu hình đã đổi" trên mọi trend, UI đầy cảnh báo vô nghĩa, và người dùng học cách ngó lơ cảnh báo — bao gồm cả cảnh báo thật.

Đúng: `tag_semantic_revision(tag_id, semantic_hash, from_ts_us)` với hash **chỉ trên các field đổi nghĩa** — `dataType`, `byteOrder`, `wordOrder`, `scaling`, `unit`. **Không** gồm `deadband`, `description`, `path`.

**Runtime re-validate khi load, không tin validate của Web.** Xem §9.3 — đây là điều kiện an ninh, không phải phòng thủ chiều sâu cho vui.

### 8.2. Reconciliation dựa trên resource

Áp version mới **không** restart toàn bộ. So sánh theo resource: connection nào đổi (reconnect), scan group nào đổi (rebuild), tag nào đổi nghĩa (ghi semantic revision), tag nào bị bỏ (retire). Tag không đổi **không được gián đoạn** — nếu đổi một màn hình HMI làm mất 2 giây dữ liệu của 2.000 tag thì engineer sẽ tránh chỉnh sửa hệ thống, và một hệ thống người ta sợ chỉnh là một hệ thống chết dần.

### 8.3. Lệnh: vòng đời là chuỗi event

**"Ghi record trước rồi UPDATE kết quả" phá vỡ append-only và phá hash chain.** Đây là sai lầm thiết kế, không phải chi tiết triển khai.

Nên: mỗi giai đoạn là **một event riêng** trỏ về cùng `command_id`.

```
CommandRequested   → CommandAuthorized / CommandRejected
                   → CommandDispatched
                   → CommandOutcome(Verified | Consistent | Failed | Indeterminate)
```

**Chuỗi này nằm trên hai chain** (§4.4), và điều đó là có chủ ý: `CommandRequested` và `CommandRejected` do Web ghi (`audit-web.db`) — vì lệnh bị RBAC từ chối không bao giờ tới Runtime; `CommandAuthorized`, `CommandDispatched`, `CommandOutcome` do Runtime ghi (`audit-runtime.db`). `command_id` do Web sinh và mang xuống Runtime, là thứ duy nhất nối hai bên.

Hệ quả phải chấp nhận: **một lệnh mà Web ghi `CommandRequested` rồi chết trước khi Runtime nhận sẽ để lại một chuỗi treo.** Đây là trạng thái đúng, không phải lỗi — nó nói "có người thử ghi, và ta không biết chuyện gì xảy ra sau đó", và đó chính là sự thật. `audit verify` phải **liệt kê chuỗi treo** như một hạng mục riêng, không im lặng bỏ qua và cũng không coi là chain vỡ.

UI đọc một **projection** của chuỗi này. Projection có thể rebuild; log thì không được sửa.

**Audit ghi thất bại ⇒ REJECT lệnh (fail-closed).** Đĩa đầy mà vẫn cho ghi PLC là đúng thứ sẽ bị chất vấn trong một cuộc điều tra sự cố, và không có câu trả lời tốt.

Pre-write check (ở Runtime, không ở Web): tag tồn tại và không retired · `writeable=true` trong version đang chạy · **và** được write policy envelope cho phép (§9.3) · người dùng có quyền trên area · giá trị trong range · rate limit chưa vượt · connection Online · quality không Bad · một write outstanding mỗi thiết bị · idempotency key chưa dùng · giá trị đúng kiểu · **absolute set-value, không phải toggle/increment** (§8.5).

### 8.4. Readback không chứng minh nhân quả — `Verified` vs `Consistent`

Readback chứng minh **giá trị hiện tại**, không chứng minh **ai gây ra nó**. Đây là insight sản phẩm quan trọng nhất trong toàn bộ phần lệnh.

Cần nói rõ về correlation trên Modbus, vì dễ nhầm: Modbus TCP **có** MBAP Transaction Identifier và RTU tự correlate vì single-master half-duplex. Nên vấn đề **không** phải "response này thuộc request nào". Vấn đề thật là: **"request của tôi có được thực thi trước khi timeout hay không"** — và không protocol nào trả lời được câu đó.

**Tier 1 — `Verified`.** Handshake register: ghi `{cmd_id, opcode, value}`, PLC copy `cmd_id` sang ack register. Đây là bằng chứng nhân quả **thật**, và nó làm retry idempotent một cách tự nhiên. Nhưng nó **cần code phía PLC** → đây là một **yêu cầu tích hợp**, phải tính vào bài toán bán hàng, không phải một tính năng bật lên được.

**Tier 2 — `Consistent`.** Không cần PLC hợp tác: snapshot giá trị cũ, ghi, đọc lại, và **chỉ chấp nhận khi thấy transition từ old kỳ vọng sang new kỳ vọng**. Hai giới hạn cứng: nếu `old == new` thì **không có verification nào cả**; và nếu một master khác đổi giá trị, bạn **nhận công sai**.

**Kỷ luật đặt tên: tier 2 không bao giờ được gọi là `Verified`.** Cái nhãn đó là thứ người ta sẽ đem ra tranh chấp khi có sự cố.

Mỗi thiết bị mang cờ `other_writers_possible`, và cờ đó phải xuất hiện trong audit record. Nếu có một HMI panel thứ ba trên cùng bus, mọi kết luận tier 2 đều có điều kiện, và audit phải nói ra điều kiện đó.

Ưu tiên FC05/FC06 (ghi single). Tránh FC16 và tránh read-modify-write lên word dùng chung — hai master read-modify-write cùng một word sẽ mất bit của nhau mà không bên nào báo lỗi.

### 8.5. `INDETERMINATE` là trạng thái hạng nhất

**Đặt sai tên một trạng thái kết thúc là rủi ro thật duy nhất của một lớp supervisory.** Nói "thất bại" khi thực ra đã thực thi, hay nói "thành công" khi không chắc, đều dẫn tới hành động sai của con người.

- Idempotency key **chỉ bảo vệ hop Web→Runtime.** Ở Runtime, cùng một key trả về **kết quả đã ghi**, không dispatch lại.
- **Model mọi lệnh là absolute set-value, không bao giờ toggle/increment.** Khi đó phần lớn retry an toàn *by construction* — đây là luật thiết kế rẻ nhất và hiệu quả nhất trong cả phần này.
- Deadline phía Web **không bao giờ** được kích retry xuống PLC.
- Lệnh pulse/increment không verify được thì **dừng ở `INDETERMINATE`** cho con người xử lý. Không tự retry.
- UI phải hiện `INDETERMINATE` như trạng thái thứ ba thật sự, không gộp vào lỗi.

**Momentary command:** Runtime crash giữa ON và OFF sẽ **kẹt coil**. Cần reconcile lúc khởi động từ journal, **và** tài liệu phải nói rõ momentary command cần **watchdog phía PLC** — một lớp supervisory không thể đảm bảo OFF sẽ tới. Đây là giới hạn cần nói trước khi bán, không phải sau khi có sự cố.

---

## 9. An ninh và audit

### 9.1. Lỗ định danh gốc — nói thẳng

Web là bên duy nhất xác thực con người, nên Runtime buộc phải tin Web. Ký request **không cứu được**: Web giữ khoá thì Web bị chiếm ký được bất kỳ username.

Sự thật cần nói ra: **mọi cơ chế truyền định danh chỉ bảo vệ audit trước một Web lành mạnh, và có giá trị bằng 0 trước một Web đã bị chiếm.**

Hai cách thu hẹp thật:

- **Runtime phát hành assertion** (không phải Web). Web bị RCE khi đó chỉ **replay** được assertion của user đang online — **không mint** được một admin đang offline. Đây là một thu hẹp thật, không phải trang trí.
- **Re-auth theo lệnh, verifier nằm ở Runtime.** ~2–3 ngày công, và là control mạnh nhất trên mỗi đơn vị công bỏ ra trong toàn bộ tài liệu.

Audit phải mang **cả hai**: `human_subject` và `assertion_path`. Không có RPC impersonation tổng quát. **Cấm truyền username trong gRPC metadata** — bắt ở code review.

### 9.2. Audit đạt được điều gì — và không đạt được điều gì

On-premise với admin có OS root: **non-repudiation là bất khả thi. Không hứa nó.**

Điều đạt được là **tamper-evidence**. Chốt mức **L2 + L3**:

- **L2 — hash chain.** Mỗi audit event chứa hash của event trước. Sửa hay xoá một event làm vỡ chain từ đó về sau, và `audit verify` chỉ ra chính xác vị trí. **Hai chain độc lập, mỗi chain một writer duy nhất** (§4.4) — dùng chung một chain cho hai process là một cuộc đua giành `prev_hash`, và nó vỡ dưới tải nên test đơn lẻ sẽ không thấy.
- **L3 — seal định kỳ.** Chain head được đẩy ra sink ngoài (file trên máy khác, in ra, hay một append-only endpoint). Không có L3, admin có thể rebuild toàn bộ chain. Seal **cả hai** chain head.

**Hash chain phải được dùng từ slice đầu**, cho login / config publish / service start-stop / load error. Slice làm lệnh khi đó chỉ **thêm một loại event** vào một log đã được chứng minh, thay vì phải migrate genesis của chain trên dữ liệu production.

Mỗi event mang `runtime_instance_id` và `boot_id`, cùng cặp timestamp wall + monotonic (§7.6).

Ship CLI **`audit verify`**. Nó vừa là control an ninh vừa là thứ bán được. Nó phải: verify **từng chain riêng**, trình bày hợp nhất theo thời gian, và **liệt kê chuỗi lệnh treo** (§8.3) như một hạng mục riêng — không gộp vào lỗi chain.

**Một giới hạn phải nói ra:** hai chain độc lập nghĩa là **thứ tự giữa hai chain không được bảo vệ bằng mật mã**. Một admin có thể xoá cả một đoạn cuối của `audit-runtime.db` và chain còn lại vẫn hợp lệ. Đây là lý do L3 tồn tại — seal head định kỳ là thứ duy nhất phát hiện việc chặt đuôi (truncation) một chain, và điều đó đúng cho cả trường hợp một chain.

Tài liệu sản phẩm phải viết đúng câu này: **"tamper-evident, không tamper-proof; trước một host administrator, chúng tôi phát hiện, chúng tôi không ngăn chặn."** Điều này khớp IEC 62443 SR 2.8/2.9 và là điều duy nhất trung thực nói được.

### 9.3. Write policy envelope — chuỗi tấn công vượt qua mọi thứ khác

Đây là lỗ an ninh nghiêm trọng nhất được tìm ra, và nó vượt qua boundary process, mTLS, và identity propagation.

**Chuỗi tấn công:** allowlist tag writeable, range, rate limit, RBAC-theo-area — tất cả nằm **trong config version**. Config version do **Web** publish. Vậy **Web bị RCE → publish một version cho mọi tag writeable, mọi range mở, mình là admin → ghi PLC tự do.** Mọi biện pháp khác trở thành vô nghĩa.

Ba sửa chữa, tất cả bắt buộc:

**Write policy envelope do RUNTIME sở hữu.** Một file trên host của Runtime, root-owned, **Web không có quyền ghi**. Đọc lúc khởi động; đổi phải restart. Nội dung: global write kill-switch · danh sách area được phép ghi · max write rate cứng · deny-list tag.

**Luật: một config version chỉ được HẸP HƠN envelope, không bao giờ rộng hơn.** ~1 ngày công, và là thứ **duy nhất** khiến "Web bị RCE" ≠ "ghi PLC tuỳ ý".

**Publish là đặc quyền CAO HƠN command.** Role `ConfigAdmin` riêng biệt với role operator. Audit của publish mang canonical hash và **danh sách resource đã đổi**.

**Runtime re-validate khi load.** Không tin validate của Web, vì Web là bên có thể bị chiếm.

Mặc định của envelope trong slice đầu: **writes disabled globally.**

### 9.4. Ba lỗ nhỏ hơn nhưng thật

- **Blazor circuit sống lâu cache `ClaimsPrincipal`.** Thu hồi quyền của một user không có tác dụng tới khi circuit chết. Nên **RBAC phải được kiểm lại tại thời điểm lệnh, ở Runtime** — không dựa vào principal đã cache trong circuit.
- **Anti-forgery không áp dụng cho WebSocket.** Nên **lệnh phải là POST endpoint**, không phải hub invocation. Đây là lý do kiến trúc, không phải sở thích.
- **Rate limiting phải ở Runtime**, không ở Web. Rate limit ở Web bị bỏ qua nếu Web bị chiếm.

### 9.5. Kênh nội bộ

**Không dùng shared secret.** "Lưu secret ở đâu" là câu hỏi sai, vì Web **phải đọc được** nó — nên Web bị chiếm là đọc được.

| Nền tảng | Cơ chế |
|---|---|
| Linux | Unix domain socket + `SO_PEERCRED` |
| Windows | Named pipe + ACL theo SID + `GetNamedPipeClientProcessId` |
| Buộc phải TCP | mTLS, cert sinh lúc cài |
| Docker | Shared volume cho UDS, **hoặc** TLS thật với cert trong volume |

**Trong Docker, cả ba cơ chế trên đều sụp.** Hai container không có loopback chung, không share được UDS hay named pipe theo mặc định, và `docker inspect` đọc được env var. Đây là chỗ câu chuyện transport auth **âm thầm chết ở đúng target thứ 6**, nên phải quyết bây giờ: shared volume UDS hoặc TLS thật, **tuyệt đối không env var**.

Lưu ý Windows: TCP loopback **mọi local user đều tới được**. Nếu buộc dùng TCP thì mTLS không phải tuỳ chọn.

Kestrel: `CheckCertificateRevocation` mặc định `true` và sẽ **treo** trên host air-gapped khi cố tra CRL. Đặt `RevocationMode = NoCheck` cho kênh nội bộ.

---

## 10. Alarm và tự giám sát

### 10.1. Alarm engine

Alarm được đánh giá ở **Runtime**, không ở client. Client chỉ hiển thị.

Máy trạng thái 4 trạng thái: `Active-Unack · Active-Ack · Cleared-Unack · Cleared-Ack`. Kèm hysteresis, on-delay, off-delay, latching, shelving có expiry, priority.

**Alarm KHÔNG được kích khi quality Bad.** Không có luật này, một lần mất kết nối = 300 alarm cùng lúc, và operator sẽ học cách tắt tiếng hệ thống trong tuần đầu tiên. Sau đó hệ thống alarm không còn giá trị nào.

Kèm: alarm "comm fail" ở cấp cha **suppress** alarm của các tag con.

Alarm event log là **lossless và tách khỏi historian** — nó là bằng chứng, không phải telemetry.

**Alarm là kênh dữ liệu thứ ba** (bên cạnh telemetry và command). Kênh này phải được **reserve trong transport ngay bây giờ**, dù engine làm sau — nếu không, slice làm alarm sẽ viết lại transport.

Blink là **CSS animation**, không bao giờ là JS timer per element. 50 alarm nhấp nháy bằng 50 timer là một cách làm treo tab.

### 10.2. System tag `System.*`

Runtime phơi chính nó qua tag như mọi tag khác. Rẻ, và dùng lại toàn bộ hạ tầng trend + alarm đã có.

```
System.Scan.<group>.JitterP99Ms
System.Scan.<group>.AchievedRateHz      → §6.2
System.Scan.<group>.SkippedCycles       → §6.3
System.Conn.<name>.RequestsPerSec
System.Conn.<name>.State
System.Historian.WalSizeBytes           → §7.7
System.Historian.WritesPerSec
System.Historian.DroppedSamples
System.Clock.StepCount                  → §7.6
System.Clock.MonotonicEnforcedCount
System.Config.ActiveVersion
System.Config.TagLoadErrors             → §6.2
System.Telemetry.MaxClientQueueDepth    → §11.4
```

Đây là thứ biến "8 giờ không tăng memory" từ cảm tính thành số liệu, và biến tiêu chí nghiệm thu rỗng thành đo được (§12).

### 10.3. Store-and-forward

Buffer có **giới hạn**, overflow theo **ưu tiên** (alarm event > sample), và **đếm số bị drop** — phơi qua `System.Historian.DroppedSamples`. Một buffer không giới hạn là một cách hết RAM chậm; một buffer drop im lặng là một cách mất dữ liệu mà không ai biết.

---

## 11. Frontend

### 11.1. Ba tầng, không phải "một renderer hai chế độ"

| Tầng | Chạy ở | Trách nhiệm |
|---|---|---|
| **L1** — Scene model | C# + JS chia sẻ | Schema, validate, migrate, canonical serialize, `computeBounds` |
| **L2** — Renderer | JS | Dựng SVG, `applyValues`, `applyStructural`, `setCamera`, `hitTest` |
| **L3** — Editor | JS | Selection, gesture, transaction, undo, overlay, spatial index |

"Một renderer hai chế độ" bị loại: 60–70% công sức editor là interaction / selection / transaction / overlay, nơi **không chia sẻ gì** với runtime. Phần chia sẻ thật tiết kiệm **15–25%**, không phải 30–40%.

**Hai kênh patch tách biệt** là điểm cốt lõi:

```
applyValues(handleValuePairs)   // HOT — mỗi frame, không đổi cấu trúc
applyStructural(ops)            // COLD — upsert/remove/reorder
setCamera(pan, zoom)
```

Nếu gộp hai kênh, kiểm tra tồn tại và hình học rơi vào hot loop — đó chính là `if (editMode)` được đổi tên. Và `applyStructural` dùng `replaceChild`, thứ **phá node identity**: alarm blink restart, focus mất. Nên **`applyStructural` không bao giờ chạy ở run mode.**

`setCamera` và `applyStructural` phải tồn tại **từ slice 1** — 4–6h bây giờ, ~40h sau, hoặc L2 bị viết lại.

**Camera cần một chủ sở hữu duy nhất** — một module state ở L1 mà cả L2 và L3 subscribe. Hai bản sao camera sẽ desync ở zoom 400% + pan, và đó là bug người ta thấy chứ không phải bug ẩn.

**Luật biên giới:** *editor không bao giờ **mutate** DOM của L2; đọc thì đi qua API truy vấn đã publish, không bao giờ `querySelector`.* Bản gốc của luật này ("editor không chạm DOM của L2") **phải vỡ có kiểm soát**, vì hit-test chính xác trên path và text cần geometry engine. Đây là phiên bản đúng.

**Hit-test hai pha:**
- **Broad-phase ở L3**, phân tích trên spatial index dẫn xuất từ model. Đây là cách **duy nhất** bắt được shape opacity-0, bbox của group, rubber-band trên vùng trống, element bị clip, và element ngoài viewport. `elementFromPoint` mù với **tất cả** những trường hợp đó.
- **Narrow-phase** uỷ quyền cho `L2.hitTest`.

Bounds: **`computeBounds` ở L1 cho mọi loại widget**, cộng cache `measureText` bằng canvas. Không dùng `getBBox`. Và bounds tính trước `document.fonts.ready` là **sai** — phải chờ.

L2 được biết mode ở **đúng một chỗ**: một class ở root + CSS cho `pointer-events`. **Zero branch trong `applyValues`.**

Cưỡng chế bằng ESLint `no-restricted-imports` cộng lệnh cấm `querySelector|children|parentNode` trong `scene-editor`. Không cưỡng chế bằng máy thì luật này chết trong một lần sửa bug gấp.

### 11.2. Schema Scene

**"rect + rotation" không đủ.** Nó vỡ ở năm chỗ, và chỗ tệ nhất là **nested group**: rotate rồi resize không đều đòi **shear**, mà shear **không phân rã được** thành rect + rotation.

Quyết định: **cấm resize không đều trên group đã rotate.** Cho phép matrix tuỳ ý sẽ làm sống lại bệnh accumulated-scale (scale lồng nhau tích luỹ sai số tới khi hình méo).

Geometry là union bốn dạng:

```
box    { x, y, w, h, rotation }
points { vertices[] }            // polyline, pipe
path   { segments[] }            // CẤU TRÚC, không phải chuỗi `d`
link   { fromRef, toRef, routing }  // hình học DẪN XUẤT
```

`path` phải là **segment có cấu trúc, không phải chuỗi `d` thô**: bạn phải parse nó dù sao; chuỗi `d` thô là bề mặt injection; và whitelist-only cấm chuỗi tự do (§2.2). Nó cũng làm SVG import trung thực: chuyển được sang segment thì nhận, không thì **từ chối** — không có đường giữa.

`link` làm **model trở thành graph**: hình học của connector là dẫn xuất, nên lưu điểm sẽ vỡ khi một máy được di chuyển. Cần reverse index và propagate invalidation.

Bbox của group là **dẫn xuất, không bao giờ lưu**.

**`kind: "instance"` + `tagScope` phải được reserve trong schema NGAY BÂY GIỜ.** Không có nó, "symbol tái sử dụng" chỉ là copy-paste, và phần lớn giá trị của mục tiêu 3 mất. Thêm sau nghĩa là migrate mọi file scene đã có.

**Dữ liệu điểm của trend KHÔNG được nằm trong Scene JSON.** Scene JSON được persist, version, và undo — nhúng một stream vào đó biến mỗi frame thành một document change, làm nổ log JSON-Patch, và làm golden test không deterministic. Trend renderer cần decimation riêng: 6h @1s = 21.600 điểm không được đi vào một chuỗi `<polyline points="…">`. Cần **từ slice đầu tiên có trend**, vì slice đó đã vẽ trend.

**Migration là forward-only pure function và không bao giờ được sửa sau khi release.** Bảo vệ bằng **golden corpus**: một file scene cho mỗi schema version đã release. `widgetType` lạ → **hard reject**. Style prop lạ → giữ lại + warning. **`SchemaVersion` và `Revision` là hai thứ khác nhau**, không được gộp.

Golden test dùng **canonical serialization sinh từ L1** (attribute sắp thứ tự, float cố định 3 chữ số), **không bao giờ đọc lại từ DOM**. Không pixel-diff screenshot ở 0.4 FTE — chi phí bảo trì vượt giá trị.

### 11.3. Binding: T1 + T2, KHÔNG có expression engine

| Tier | Dạng | Bao phủ |
|---|---|---|
| **T1 `direct`** | `{ tag, target: "attr:fill" }` | ~70% |
| **T2 `map`** | `{ mode: "range" \| "linear" \| "enum", ... }` | +25% → **~95%** |
| **T3 `expression`** | — | **Không làm.** Chỉ mở khi một màn hình thật chứng minh cần |

T2 có bốn tính chất mà expression **không** có: serializable, phân tích tĩnh được, dependency luôn đúng một tag, và **render được thành UI editor** (một bảng color-stop). Trong một công cụ **kéo-thả**, bạn không thể bắt engineer viết expression — điều đó **phá vỡ mục tiêu 3**. Và thiết kế whitelist trước khi có use case thật là đoán.

Bỏ T3 xoá luôn cả cụm: AST validation, whitelist hàm, giới hạn độ sâu, compile + cache, trích xuất dependency, **cycle detection**, execution budget, và UI soạn expression. **60–100h.**

Schema phải **reserve slot** cho tier binding ngay bây giờ, để mở T3 sau không thành migration.

Ba luật kèm theo:

- **Cấm reference element→element.** Graph trở thành bipartite (tag → element), nên **cycle detection biến mất hoàn toàn** và mọi thứ gom về CSR adjacency + một dirty set per frame. Giá trị dẫn xuất thuộc về Runtime dưới dạng calculated tag — đúng nơi của nó, vì nó cần được historian và alarm nhìn thấy.

  Cần nói rõ hệ quả để không tạo lỗ: calculated tag **chưa được build** (§13 nhóm C), nên trong các slice đầu, một use case cần giá trị dẫn xuất sẽ **không có chỗ nào để đặt nó**. Đây là đánh đổi có chủ ý, không phải sơ suất — nó là cách bắt buộc use case đó phải xuất hiện thật trước khi ta xây engine cho nó. Cách xử lý tạm cho tới khi có calculated tag: giá trị dẫn xuất được tính **ở PLC** (nơi nó thường vốn đã có), hoặc use case bị hoãn. **Tuyệt đối không** mở lại reference element→element như đường tránh — làm vậy là lấy lại toàn bộ cycle detection đã xoá, và lấy nó lại ở đúng chỗ khó nhất.
- **"Referenced nhưng chưa subscribe" phải là bug lúc mount, không phải điều kiện runtime** — tập subscription được **dẫn xuất** từ scene. Và tag của element đang ẩn **vẫn phải subscribe**, nếu không mỗi lần đổi tab là một subscribe-storm.
- **Two-way binding không phải binding** — ghi là một **lệnh** (§8). Cái bẫy thật là **optimistic echo**: khi một field đang có focus hoặc đang có write pending, telemetry **không được** ghi đè nó. Không có luật này, operator gõ số vào ô rồi thấy nó bị nhảy về giá trị cũ.

**Luật "latest-value-wins per frame":** **không logic hiển thị nào được phụ thuộc vào việc thấy đủ mọi sample.** Rising edge, counter, pulse, và "đã từng vượt ngưỡng" tất cả thuộc về server-side. Luật này phải được viết ra, vì nó rất dễ vi phạm một cách vô tình.

### 11.4. Hiệu năng: đang lo sai phía

**1.200 attribute update/s ở client là ~5ms công việc mỗi giây. Nó không phải vấn đề.** Bốn ngưỡng thật, theo thứ tự quan trọng:

**Server fan-out.** 20 client × 100 tag @4Hz = 8.000 change/s. Luật: **message rate là O(clients × ticks), không bao giờ O(clients × tags)** → batch thành ~80 msg/s.

**Payload encoding.** Index-based `[idx, value, qualityByte]` ≈ 10 byte, so với ~60 byte cho dạng có tên. MessagePack giảm thêm ~40%.

**Số node SVG, không phải số attribute.** Chrome bắt đầu đau trên ~5.000 node. 300 element × ~10 node = 3.000 — **đã gần trần mà không nhận ra**. Nên: **budget cứng + counter**, để phát hiện ở slice 2 chứ không phải ở máy khách.

**Text đắt hơn `fill` 10–50×.** Nên **tối ưu giá trị cao nhất trong toàn hệ thống là format-then-compare dedup** bên trong `applyValues`: format giá trị, so với chuỗi đã render, bỏ qua nếu giống. Cắt 60–90% lượng ghi DOM. Nó rẻ và nó là thứ duy nhất trong danh sách này đáng làm sớm.

**Zero allocation trong `applyValues` và trong rAF handler** — đây là kỷ luật code (typed array, handle table dạng array-by-index, không closure trong loop), không phải nhu cầu của 4Hz.

**Z-order nhỏ hơn tưởng:** handle table dạng array-by-index **sống sót** qua việc DOM bị reorder. Điều kiện: đúng **một `<g>` wrapper mỗi element**, và địa chỉ **chỉ qua handle** — không bao giờ `nth-child` hay quan hệ sibling. Thứ phá cache không phải z-order, mà là cache sai thứ.

### 11.5. Biên giới Blazor ↔ JS

Cả hai câu trả lời hiển nhiên đều sai: dùng chung circuit nghĩa là một burst telemetry kill circuit sẽ **giết luôn nav và auth**, không chỉ chart. Hai connection mỗi trang nghĩa là hai reconnect state machine và một chữ "Connected" mơ hồ.

**Layout đúng:**

| Trang | Chế độ |
|---|---|
| **HMI runtime** | **Static SSR, ZERO circuit** + telemetry hub riêng |
| Config / editor / admin | Blazor Interactive Server |

**Vạch đường ở cấp PAGE, không cấp component.**

Hai cái bẫy phải xử lý:

- **Circuit reconnect PHÁ HUỶ DOM do JS tạo**, trừ khi subtree JS-owned nằm dưới một root mà Blazor coi là rỗng. Điều này quan trọng nhất ở **trang editor** — trang buộc phải có circuit.
- **`Dispose` phải cancel rAF loop.** Không làm là "app chậm dần sau 20 phút" — bug mà không ai biết cách tái hiện.

Prerender: quy tắc double-mount phải rõ, hoặc JS module init hai lần.

**Diagnostics HUD từ slice 1** (~1h): FPS, số DOM write, số frame drop, epoch hiện tại, và **send-queue depth per client** — chỉ số **duy nhất đi trước** sự cố (nó tăng trước khi client bắt đầu mất frame).

### 11.6. Đa màn hình, canvas, và cảm ứng

- **Screen có tham số.** Một template "Motor detail" nhận `motorId`, không phải 40 file copy-paste. Đây là nửa còn lại của giá trị mục tiêu 3, cùng với `instance`/`tagScope`.
- Lifecycle teardown → subscribe. **`unmount()` phải được test nghiêm ngặt như `mount()`** — leak nằm ở đó, không nằm ở mount.
- **Canvas cố định + uniform scale-to-fit + letterbox.** Không responsive reflow: một sơ đồ P&ID reflow là một sơ đồ sai.
- **Cảm ứng là mặc định, không phải bổ sung.** Panel PC là màn hình cảm ứng: target ~44px, không có affordance chỉ-hover, dùng Pointer Events. Retrofit cảm ứng nghĩa là viết lại state machine của gesture.
- **Ngôn ngữ thị giác thống nhất cho quality/staleness, và policy khi mất kết nối** — cái sau là **quyết định AN TOÀN, không phải quyết định UI**, và là thứ làm một HMI bị nhà máy từ chối. Phải chốt: sau bao lâu mất kết nối thì màn hình chuyển sang trạng thái "không tin được", và trạng thái đó trông như thế nào.
- **Screen inspector** trong L2 từ slice 1 — rẻ lúc đó, đắt khi retrofit.

### 11.7. Editor

**Gate trước khi viết editor: viết tay 3–5 màn hình thật, tổng ~300 element** (một overview, một detail có input, một trend, một alarm list). Mỗi màn ~4–6h. Đây là cách rẻ nhất để tìm lỗ schema **trước khi** editor đóng cứng nó — nếu không, editor sẽ tự phát hiện chúng và buộc migration.

**Model của editor sống ở đâu: CHỈ trong JS.** Blazor panel là view thụ động, gửi intent vào cùng một transaction log. Một model C# với canvas JS phải round-trip mỗi `pointermove`; và bất kỳ cách chia đôi nào cũng cho một bug distributed-state không debug được.

**MVP editor (~120–150h), cắt tường minh:**

| Có | Không có (schema vẫn hỗ trợ) |
|---|---|
| Kéo-thả, resize | Rotation *(có trong schema, editor không author được)* |
| Grid snap | Smart guide |
| Z-order qua layer list panel | Group |
| Property panel sinh từ metadata | Path/vertex tool |
| Binding T1 + T2 | Multi-select transform |
| Undo per gesture | Sửa text tại chỗ *(sửa qua property panel)* |
| Save / load / validate | Symbol/instance authoring *(reserve trong schema)* |

**Luật: schema đầy đủ ≠ editor đầy đủ.** Rotation, group, instance đều phải **có trong schema từ ngày đầu** dù editor MVP không tạo được chúng. Ngược lại là migration.

Editor đủ tính năng: ~330–420h. Đây là hạng mục bị đánh giá thấp nhất trong dự án.

---

## 12. Tiêu chí nghiệm thu

Năm tiêu chí trong bản nháp trước là **rỗng** — chúng pass được bằng cách không làm gì. Sửa: mỗi tiêu chí phải có ngưỡng số và một cách đo tự động.

Ví dụ tiêu chí rỗng và bản sửa:

| Rỗng | Đo được |
|---|---|
| "8h không tăng memory" | RSS tăng < 5% sau 8h, đo qua `System.*`, so đầu/cuối |
| "gap hiển thị rõ ràng" | Kiểm bằng canonical serialization của scene, không bằng mắt |
| "resync không hở sequence" | **Convergence** < 2.000 ms sau resync (§7.1) |
| "round-trip float" | Test cụ thể `-12345.678` và `1.2345678e-8` — giá trị đối xứng byte pass giả |
| "1.000 sample/s" | **Có reader song song** — xem dưới |

Ba test bắt buộc, mỗi cái nhắm một giả định có thể sai:

**T1 — Ghi và đọc đồng thời.** 1.000 sample/s trung bình + burst 5.000, **cùng lúc 2 reader chạy trend 8h**. Yêu cầu: **0 lần `SQLITE_BUSY`** · p99 commit < 200ms · **WAL trở về dưới ngưỡng trong 30s** sau khi reader kết thúc. Không có reader song song, test này **không kiểm cái mà WAL được chọn để giải quyết** (§7.7).

**T2 — Đúng đắn của mô hình đọc.** As-of seed · time-weighted average **loại khoảng Bad** · timestamp đơn điệu — đối chiếu với đáp án **tính tay**, bao gồm một **NTP step lùi 5 giây** được mô phỏng. Đây là test duy nhất bắt được §7.5 và §7.6 làm sai.

**T3 — Ngân sách scan.** Publish một cấu hình bất khả thi (100 tag @250ms trên 9600 baud) → phải sinh **cảnh báo**, không phải im lặng. Kiểm: tốc độ thực đạt được per group · p99 jitter < 10% · số chu kỳ bị skip được đếm · **xác nhận ở tầng transport là không có catch-up**.

---

## 13. Triage — chống lại chính tài liệu này

Bốn vòng rà soát sinh ra ~40 thay đổi. **Áp dụng cả 40 chính là chế độ chết mà chúng vừa cảnh báo.** Phân loại:

### Nhóm A — Vào slice 0/1 (retrofit đắt 3–10×)

Auth + `[Authorize]` fallback toàn cục · **envelope principal trong MỌI RPC từ RPC đầu tiên** (slice 0 được ignore giá trị; **hình dạng** phải có) · transport có xác thực ngay · audit hash chain dùng ngay cho login/publish/start-stop/load-error · schema command journal · **field `writeable` + area RBAC trong config schema từ version publish đầu tiên** (thêm field vào canonical JSON đã hash sau này tệ hơn migrate DB) · write policy envelope file mặc định writes-disabled · single-instance lock · `setCamera` + `applyStructural` · quality **bitfield** · `ts_us` + anchor monotonic · deadband so giá trị đã lưu · handle epoch · scan group + coalescing trong schema.

### Nhóm B — Luật viết ra, tốn 0h

Telemetry lossy / historian+audit lossless · `Invalid` poison, không coerce · resize mutate geometry, không tích luỹ scale · editor không mutate DOM của L2 · cấm reference element→element · alarm không kích khi quality Bad · lệnh luôn absolute set-value · tier 2 không bao giờ gọi `Verified` · latest-value-wins per frame · không bao giờ để màn hình trông bình thường khi dữ liệu đã chết · **không bao giờ hướng dẫn "copy file này"** để backup (copy WAL đang sống là invalid — ship `VACUUM INTO`) · zero-CDN cưỡng chế bằng CSP không `unsafe-inline` + CI grep `https?://` + **container `--network none` smoke test coi console error là fail** (test duy nhất thật sự chứng minh mục tiêu 7).

### Nhóm C — Reserve slot, build sau

T3 expression · `kind:"instance"` + `tagScope` · rotation/group trong schema · retention multi-file ATTACH · alarm là kênh dữ liệu thứ ba · PostgreSQL · calculated tag.

### Nhóm D — Cắt tường minh

**Editor MVP** 120–150h thay vì 330–420h (§11.7).

**Mục tiêu 6 mỏng đi:** **Windows là platform được release-test đầy đủ** — installer, Windows Service, backup, upgrade path, và test nghiệm thu chạy trên đó. Linux và Docker **chạy được, có tài liệu, có support matrix công khai**, nhưng không có installer riêng. .NET làm "chạy trên 3 OS" gần như miễn phí; cái đắt là **packaging × 3 mỗi lần release, mãi mãi** — và đó là chi phí định kỳ mà một người part-time không trả được.

**Docker: TCP driver only. Modbus RTU KHÔNG được support trong Docker** — Windows container không có serial passthrough, Docker Desktop/WSL2 không pass được COM port, và timing 3.5 char không đáng tin qua abstraction USB-serial. RTU cần native install hoặc gateway serial-to-TCP (NPort / ser2net). Đây là **support matrix công khai**, không phải hạn chế ẩn.

---

## 14. Vận hành

### 14.1. Năm điểm vỡ khi triển khai

**Serial trong container** — xem §13 nhóm D.

**SQLite trên đường mạng.** SMB/NFS/UNC làm **corrupt** DB, và bind mount của Docker Desktop Windows làm WAL không đáng tin. Nên: **refuse to start nếu DB nằm trên đường mạng**, kiểm lúc khởi động. Và **không bao giờ** tài liệu hoá "copy file này để backup" — copy một WAL đang sống cho ra file invalid. Ship backup command dùng `VACUUM INTO` / `.backup`.

**Data Protection key.** Ephemeral với Windows Service (không load user profile), không mã hoá trên Linux, mất mỗi lần container start. Key không được persist nghĩa là cookie forge được → **bypass login**. Nên cấu hình `PersistKeysToFileSystem` + `ProtectKeysWith*` theo từng nền tảng, và kiểm lúc khởi động.

**Certificate.** Self-signed dạy operator bấm qua cảnh báo — thói quen đó là lỗ an ninh thật. Nên: sinh cert lúc first-run, **export root CA** để import vào máy client, và **đưa IP vào SAN** vì người ta sẽ browse bằng IP trần. Cẩn thận HSTS: bật sai sẽ khoá bạn ra khỏi một appliance LAN không sửa được từ xa.

**Upgrade.** Runtime là **migrator duy nhất**, giữ lock. **gRPC contract có version, Web refuse to start nếu mismatch** — chạy lệch version còn tệ hơn không chạy. Migration forward-only, backup trước. **Schema historian chỉ được thêm, không đổi** — rewrite một bảng 6 tháng dữ liệu mất nhiều giờ, trên máy khách, trong giờ sản xuất. Healthcheck readiness thật, vì `depends_on` của Compose **không chờ** service sẵn sàng.

### 14.2. Vận hành thường ngày

Single-instance lock (~20 dòng, chặn một class corrupt DB bạn sẽ gặp trong vòng một tháng). Backup command. CLI: `audit verify`, `backup`, `migrate`, `diag`.

---

## 15. Thứ tự triển khai

Với 0.4 FTE, thứ tự là **risk-first, không phải value-first**: *sai chí tử không phải là làm sai chậm, mà là phát hiện giả định sai quá muộn.*

| Slice | Nội dung | Xong khi |
|---|---|---|
| **0** | Nền + nhóm A. Blazor shell tối thiểu, auth, audit chain, envelope principal, single-instance lock, CI (NetArchTest + `--network none`) | Mở browser, login, audit chain verify được |
| **1** | Sim driver → tag engine → historian → telemetry → một màn hình hard-coded. Camera + `applyStructural` + diagnostics HUD | Một tag sin wave chạy trên browser, ghi và trend lại được |
| **2** | Modbus TCP. Scan group + coalescing + validator ngân sách. `System.*` | Đọc được PLC sim thật, cảnh báo được cấu hình bất khả thi |
| **2.5** | **Ghi MỘT tag.** Sim only. Journal + audit event stream + readback + envelope + một cặp role. Không polish UI | Ghi được một tag, `audit verify` pass, `INDETERMINATE` tái hiện được |
| **3** | Config version + publish + reconciliation + `tag_semantic_revision`. **3–5 màn hình viết tay (~300 element)** | Publish/rollback không gián đoạn tag không đổi |
| **4** | Editor MVP (nhóm D) | Người khác tự vẽ được một màn hình |
| **5** | Alarm engine + `QueryTrend` UI đầy đủ | Alarm không bão khi mất kết nối |
| **6** | Modbus RTU + OPC UA + packaging Windows + support matrix | Chạy trên hardware thật |

**Vì sao Slice 2.5 nằm ở đó, trước editor và alarm:** write path là chỗ **bất định cao nhất**; editor và alarm là **effort cao / bất định thấp**. Phát hiện ở cuối dự án rằng verification tử tế **cần code phía PLC** (§8.4) sẽ invalidate những giả định đã đóng cứng trong config schema và audit semantics. Effort cao mà bất định thấp thì hoãn được an toàn; bất định cao thì không.

Có thể hoãn an toàn: logic readback nâng cao, timer momentary OFF, tuning rate limit, UI PIN re-auth, polish CLI verify, UI sửa role, signed backup package, shelving/latching, PostgreSQL, Docker.

---

## 16. Feasibility và rủi ro dự án

### 16.1. Con số

20h/tuần *nominal* là **13–16h hiệu dụng** — part-time bị phạt **siêu tuyến tính** vì chi phí lấy lại context; hai buổi 10h không bằng một buổi 20h. Ước ~**650–700 giờ hiệu dụng/năm** so với khối lượng **~750–1.100h** → **14–18 tháng** cho cả 7 mục tiêu ở mức mỏng-nhưng-dùng-được.

Ba hạng mục bị đánh giá thấp nhất, theo thứ tự: **editor** → **deployment đa nền tảng** (không phải chi phí một lần mà là 3× chi phí *mỗi lần release, mãi mãi* — đây là lý do nhóm D cắt xuống một platform) → **driver trước khi có hardware thật**.

### 16.2. Rủi ro chết dự án là phi kỹ thuật

**Khúc giữa dài không có thưởng.** Phần lớn thời gian là editor internals, config versioning, deployment plumbing — giai đoạn mà demo không đẹp thêm được gì. Cơ chế cụ thể: một lần bỏ 3 tuần vì việc chính → mất context → chi phí quay lại *cảm giác* rất cao → pause vô thời hạn.

**Scope identity drift.** "R&D → startup" tạo áp lực kiến trúc cho một tương lai giả định. Thiết kế này **đã có dấu hiệu đó**: hai implementation direct/gRPC và repository trên hai loại DB khi chưa có một user nào. §4.3 đặt điều kiện cho nó; nếu tới slice cuối vẫn không có nhu cầu thật, xoá.

Ba đối trọng, tất cả đều là hành động cụ thể:

- **Mỗi lần dừng, để build xanh kèm một ghi chú "bước tiếp theo là gì".** Đây là thứ làm chi phí quay lại thấp.
- **Mỗi ~3 tuần, một artifact demo mà một người thật xem.** Không phải screenshot cho chính mình.
- **Một design partner, dù không trả tiền** — cơ chế forcing function đáng tin duy nhất, và cũng là cách duy nhất biết mục tiêu 3 có thật sự dùng được bởi người không phải lập trình viên.

Time-box: vượt 1.5× thì **cắt scope, không kéo thời gian**. Trục cắt đã xác định sẵn trong nhóm D, theo thứ tự: mục tiêu 6 (rẻ nhất và trung thực nhất để mỏng) → mục tiêu 3 (thứ hai, nhưng cẩn thận vì nó là điểm khác biệt) → mục tiêu 4 (single-tag trend + query thô + alarm list phẳng).

**Mục tiêu 5 chỉ được mỏng theo một trục duy nhất: giảm số loại lệnh và số role. TUYỆT ĐỐI KHÔNG bằng cách bỏ journal, hash chain, allowlist, hay write policy envelope.** Bỏ những thứ đó không phải là mỏng mục tiêu 5, mà là bỏ nó — và một hệ thống ghi được xuống PLC mà không có chúng là một hệ thống không nên tồn tại.

---

## 17. Ngoài phạm vi

Phân tán / HA / clustering. Message broker. Kubernetes. Multi-tenant. Cloud. Mobile app riêng. Báo cáo/BI. MES/ERP integration. Redundant Runtime. Historian đa node. Bất kỳ chức năng safety nào (§1.1).
