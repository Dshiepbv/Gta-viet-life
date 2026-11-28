# Kiểm toán mã nguồn M0–M6 (2026-11-28, main tại 16477fb)

## Cách kiểm và giới hạn

- Đã chạy: 63 test engine pass, 2 test bridge pass (Python 3.15.0 trên máy; CI ghim 3.11). Một ca đối chứng cố ý sai trả FAILED.
- Đã chạy lại các ca tái hiện lỗi engine R1–R21 và B1.
- Chưa chạy: build C# (máy không có `dotnet`), GTA V (không thấy cài đặt ở đường dẫn phổ biến và registry), Godot, Launcher, installer. Trạng thái "CI #61 xanh" chưa kiểm được (máy không có `gh`).
- Nhãn bằng chứng: [T] tái hiện bằng script; [Đ] đọc mã và đã đối chiếu lần hai; [S] đọc mã một lần, chưa đối chiếu lại.
- Chưa đọc hết: một số đoạn của `game/GtaVietLifeWorld.cs` (props, vào nhà, vẽ icon), XAML, `android/`, `bridge/stt.py`, `tts.py`, `overlay.py`, `dashboard.html`, một phần `adapters/godot`.
- Viết tắt: Core, World, Eco, Pers, DS là `game/GtaVietLife{Core,World,Economy,Personal,DoiSong}.cs`; Chat là `game/GtaVietLife.cs`; LS là `engine/life_simulation.py`; SOC là `engine/society.py`.

## 1. Bảng trạng thái

| Module | Thực trạng code | Lỗ hổng cốt lõi | Hành động tiếp theo | Sẵn sàng |
|---|---|---|---|---|
| M0 Life Core | Logic thật: LS (nhu cầu, lịch, trí nhớ, SQLite), nhu cầu phía game ở World và DS. `engine/world.py`, `contracts.py`, `adapters/base.py` chỉ có test dùng | Đăng ký lại NPC sau restart bị từ chối; ghi trí nhớ thoại làm mất lệnh đang chờ; migration đứt giữa chừng làm DB không mở được | Sửa E2, E3, E7; bỏ hoặc nối khung `world.py` | Chạy được offline, chưa chạy trong game |
| M1 Bridge/Clock | Logic thật: Core (HTTP, handshake, outbox, đồng hồ), `engine/host.py` | Chặn mất báo cáo nhu cầu và công việc của người chơi; hàng đợi gửi bị kẹt; lùi giờ rồi tính lại; 5 bộ đếm ngày riêng | Sửa E1, E5, E6; một nguồn thời gian | Chưa thông |
| M2 Social | Logic thật: bridge (LLM, `memory.db`), Chat, Pers, SOC quan hệ | Quan hệ lưu 3 nơi, trí nhớ 2 nơi; `news()` rỗng gây treo khóa; một số khóa AppDomain ghi mà không ai đọc | Chọn engine làm chủ quan hệ và trí nhớ; sửa B1 | Chạy rời rạc |
| M3 World/Objects | Logic thật: World, catalog và outbox ở SOC | Lệnh không hết hạn, không lọc theo adapter; engine phát lệnh toilet/shower mà GTA báo không hỗ trợ; đăng ký lại vật thể xóa state | Thêm hạn lệnh; khớp danh sách hoạt động hai phía | Chưa kiểm va chạm, nội thất trong game |
| M4 Economy/Work | Logic thật hai phía: Eco, World; sổ kép SQLite ở SOC | Sổ không phải nguồn chính: số dư đầu kỳ cộng trùng hoặc bị bỏ; trùng ID khác nội dung vẫn báo thành công; không kiểm tiền tệ, tràn số. Mua, thuê, bán, hợp đồng lao động chưa có script GTA nào gọi | Sửa E4, E8; chốt quyền sở hữu số dư | Chưa tin được số liệu |
| M5 Compat/Host | HTTP host thật; adapter Godot là bản tham chiếu hẹp | Mở được 2 host cùng cổng và DB; token ngoài ASCII gây TypeError; protocol chỉ kiểm lúc handshake; tài liệu giao thức lệch code | Khóa một tiến trình; đồng bộ tài liệu | Chưa chạy host thật cùng game |
| M6 Integration/Launcher | Logic thật: `launcher/`, `packaging/`, workflow CI build đủ 6 script | `install.bat` chỉ cài bridge; đóng Launcher có thể để lại tiến trình; checksum không ký; không có unit test C# | Build thử tại máy; test đóng và cập nhật | Chưa chạy |

## 2. Lỗi theo mức ưu tiên

Bảng mô tả mã tại `16477fb`. Trạng thái sửa xem mục 6.

### P0: mất tiền, mất dữ liệu, treo script, đường M0–M1–M4 không thông

| Mã | Lỗi | Hậu quả | Vị trí | Bằng chứng |
|---|---|---|---|---|
| G1 | `FadeWait` tự xếp lại vào hàng đợi đang được `while` rút | Chọn viện phí hoặc giải trí: đã trừ tiền, vòng lặp không thoát trong một tick | DS:173-177, 951-953 | [Đ] |
| G2 | `RestoreWallet` đặt `Game.Player.Money` bằng số đã lưu khi gặp nhân vật | Mất tiền GTA cộng vào trong lúc không điều khiển nhân vật đó | Pers:303-325 | [Đ] |
| G3 | World đọc save trong một `try` chung, ghi đè trực tiếp, không bản sao | Một dòng lỗi làm mất phần sau; lần lưu kế tiếp ghi đè file | World:4710, 4760-4841 | [Đ] |
| E1 | `PostWorldRequest` chỉ nhận đường dẫn `/engine/v1/world/` | `actors/observe` và `work-completed` bị bỏ im lặng | Core:646-647; World:374, 2793 | [Đ] |
| E2 | Sau restart, đăng ký lại hồ sơ y hệt bị `npc_identity_conflict` | Dừng đồng bộ cư dân và số dư ví | LS:186, 350; Core:538-542, 561-594 | [T] R1 |
| E3 | `record_dialogue_memory` gửi lại danh sách lệnh cũ | Mọi lệnh NPC đang chờ bị đánh dấu superseded | LS:587-621 | [T] R2 |
| E4 | Số dư đầu kỳ và event cùng đổ vào một tài khoản | Ví thật 1500 thành 2000 hoặc 500 | SOC:329-335, 644-661; Core:261-279 | [T] R11 |
| E5 | Lỗi HTTP vĩnh viễn bị đưa lại đầu hàng; hàng chỉ ở RAM | Mọi gửi sau đó bị chặn; ngoại tuyến thì mất | Core:644-691 | [Đ], R21 |
| E6 | Giờ lùi thì đặt lại mốc, không có định danh save | Tải save cũ làm mô phỏng và có thể trả lương lại | host.py:59-85 | [T] R3, R4 |
| E7 | DDL commit trước khi ghi `schema_version` | Đứt giữa chừng thì lần sau không mở được DB | LS:143-170 | [T] R14 |

### P1: sai số liệu hoặc khai thác được

| Mã | Lỗi | Vị trí | Bằng chứng |
|---|---|---|---|
| E8 | Cùng `transaction_id` khác nội dung báo thành công; không kiểm tiền tệ; tràn số thành REAL; đặt trước ID `payroll:` làm lương đánh dấu đã trả mà không có tiền; bán lại cùng lệnh báo thiếu hàng | SOC:285-299, 552-558, 742-751 | [T] R5–R9 |
| E9 | Lưu thất bại vẫn đổi bộ nhớ; ack commit trước hiệu ứng; `request_id` trùng chạy society hai lần | LS:560-569; SOC:237-258; host.py:27-33 | [T] R13, R15, R16 |
| E10 | Mở được 2 host cùng cổng và DB; không bật WAL, foreign_keys, busy_timeout, backup | host.py:298-322; LS:134-135 | [T] R17 |
| E11 | Dùng chung không gian dedup giữa các endpoint world | SOC:177, 481 | [T] R10 |
| E12 | Ngữ cảnh quá 16000 ký tự trả prompt rỗng | `bridge/engine_context.py:7` | [T] R19 |
| E13 | Một event lỗi làm cả lô bị từ chối và gửi lại mãi | SOC:618-661; Core:290-342 | [T] R12 phía engine; chưa biết C# có sinh ra không |
| E14 | Dashboard nghe `0.0.0.0:5588`, GET `/data` không cần PIN, PIN rỗng thì POST mở | `bridge/dashboard.py:108-133, 173` | [S] |
| B1 | `news()` nhận kết quả rỗng thì không nhả khóa, `stop()` treo | `bridge/main.py:146-148, 175` | [T] B1 |
| G4 | Bán bất động sản đang thế chấp không kiểm khoản vay; phần nợ thiếu khi phát mãi bị bỏ | World:969-981, 998-1013; Eco:304 | [Đ] |
| G5 | `Core.sav` mất hoặc reset thì seq quay lại, engine bỏ event mới vì coi là trùng | Core:395-405; SOC:634-642 | [T] R11 phần engine |
| G6 | Gọi `Game.Player.Money` trong `Task.Run` | Core:265, 519-540 | [Đ], hậu quả trong game chưa đo |
| G7 | Hợp đồng B2B giữ chỉ số quán; đóng quán thì trỏ sang quán khác | World:726, 1240-1251 | [S] |

### P2: hành vi game

| Mã | Lỗi | Vị trí | Bằng chứng |
|---|---|---|---|
| G8 | NPC đứng đơ khi `/stop` lỗi hoặc nói chuyện người thứ hai trước khi thả người đầu | Chat:201-205, 229, 633-637 | [S] |
| G9 | `Residents.txt` cũ sống qua phiên, ped khác có thể nhận danh tính cư dân | World:3764-3765 | [S] |
| G10 | Xe bị tịch thu vẫn gọi lại được từ garage với giá thường | Eco:543-548; Pers:544 | [S] |
| G11 | Menu của các script chồng nhau, một phím số chạy hai hành động tốn tiền | World:4326 | [S] |
| G12 | Cư dân do engine điều khiển không được trả về lịch gốc khi engine tắt | World:3624, 3707-3711 | [S] |
| G13 | Gọi tài xế thất bại chỉ hoàn một phần phí | World:2937-3018 | [S] |

## 3. Nguồn dữ liệu đang lệch

| Dữ kiện | Nơi đang giữ |
|---|---|
| Tiền mặt, ngân hàng | Ví GTA; `GtaVietLifeEconomy.sav`; `wallet_SPx` trong Personal.sav; tài khoản `legacy:*` trong SQLite |
| Tiền quán | World.sav; tài khoản ledger; `native_cash_balance` |
| Quan hệ | `memory.db` (affinity); `NPCState.affinity`; `world_social_relationships` |
| Trí nhớ | `memory.db`; `NPCState.memories` (tối đa 100) |
| Ngày, giờ | Core, Economy, Personal, DoiSong, World đếm riêng; engine giữ `simulation_minute` |
| Danh tính NPC | bridge gán nghề ngẫu nhiên; engine ghi "resident" |
| Nơi lưu | 6 file `.sav`, `Residents.txt`, `Dash.json`, 2 file SQLite, Apps Script properties |

Không nơi nào quay lui khi người chơi tải save GTA cũ. Ngân hàng, nợ và quán dùng chung cho cả 3 nhân vật; chỉ ví tiền mặt tách riêng.

## 4. Kế hoạch đưa M0 + M1 + M4 chạy thông

| Đợt | Việc | Tiêu chí nghiệm thu |
|---|---|---|
| 1. Thông đường ống (làm được trên máy này) | E1, E2, E3, E5, G1; thêm test đối chiếu mọi đường dẫn C# gửi với bộ lọc và route của host | Test mới fail trên code hiện tại, pass sau sửa; CI build 6 script pass |
| 2. Lưu bền | E7, E10, G3, G2: migration một giao dịch, khóa một tiến trình, WAL, backup, ghi file nguyên tử | Ngắt tiến trình giữa lúc lưu và migration vẫn mở lại được |
| 3. Sổ cái và thời gian | E4, E6, E8, G5, G6 theo `SHARED_STATE_CONTRACT.md` | Phát lại cùng event không đổi số dư; tải save cũ không mô phỏng lại; số dư bằng tổng bút toán |
| 4. Chạy thật | Máy có GTA V, ghi build game và phiên bản SHVDN; ngủ, đi làm, mua bán, thoát, vào lại | Log script hook không lỗi; ví trong game khớp sổ cái sau mỗi bước |

## 5. Đặc tả M6: Launcher độc lập (yêu cầu chốt 2026-11-28)

Launcher chạy độc lập với GTA V và Life Simulator Runtime. Người dùng không bao giờ phải chép file `.cs` hay chạy `install.bat`.

| Yêu cầu | Hiện trạng mã (đọc 2026-11-28, chưa chạy) | Khoảng cách |
|---|---|---|
| Bộ gồm `GTA Viet Life Launcher.exe`, `Setup.exe`, Updater, `manifest.json`, `config.json` | Có `GtaVietLife.Launcher.exe`, `GtaVietLife-Setup-<ver>.exe`, `GtaVietLife.Updater.exe`; cấu hình Launcher là `launcher.json`; `config.json` chỉ dành cho bridge | Chưa có `manifest.json`; tên file chưa đúng yêu cầu |
| Tự kiểm môi trường và tương thích GTA V | `RuntimeManager.InstallAdapter` kiểm DLL script hook, .NET 4.8, VC++, log Script Hook V | Người dùng phải tự chọn file exe; chưa có bảng build được hỗ trợ; log chưa khớp build vẫn báo sẵn sàng |
| Cập nhật qua staging và checksum | `UpdateService` tải zip, so SHA-256; Updater giải nén vào thư mục tạm rồi đổi chỗ | Checksum lấy cùng release, không ký; không kiểm từng file theo manifest |
| Rollback nguyên tử | Chỉ khôi phục khi đổi thư mục bị lỗi; script trong game chép từng file, có backup | Bản mới khởi động hỏng không được quay lui; bộ script có thể cài dở; thư mục gỡ cài đặt của Inno có thể mất sau cập nhật (chưa kiểm) |
| Không ghi đè save người dùng | Chỉ chép `.cs`, icon và `.ini` còn thiếu; dữ liệu engine ở `%LOCALAPPDATA%\GtaVietLife\Data` | Có sửa cổng trong `GtaVietLifeCore.ini` (có backup); chưa có test chứng minh không đụng `.sav` và SQLite |
| Quản lý tiến trình và heartbeat | Poll 3 giây, tự khởi động lại host và bridge | Đóng cửa sổ không chờ dừng xong; kill theo tên tiến trình |
| Xuất gói log | Gộp log thành một file `.txt` trên Desktop, che khóa bí mật | Chưa phải gói nén; thiếu `GtaVietLifeCore.deadletter.log`; vị trí log game chưa thống nhất |
| Khởi chạy game tự động | Nút Play chạy thẳng exe đã chọn | Chưa tự chạy sau khi sẵn sàng; chưa kiểm với Steam, Epic, Rockstar Launcher |

Nghiệm thu M6: máy sạch cài bằng Setup, một thao tác là vào game; ngắt giữa lúc cập nhật vẫn còn bản chạy được; mã băm mọi file save trước và sau cập nhật không đổi.

## 6. Tiến độ sửa lỗi

| Mã | Trạng thái 2026-11-28 | Test |
|---|---|---|
| E1 | Đã sửa, chưa chạy trong game. Báo cáo ca làm được xếp hàng cả khi ngoại tuyến | `tests/test_adapter_wire_contract.py` |
| E2 | Đã sửa; một hồ sơ bị từ chối không còn chặn hồ sơ khác và số dư ví | `engine/tests/test_phase1_regressions.py`, wire contract |
| E3 | Đã sửa | `test_phase1_regressions.py` |
| E5 | Đã sửa cho cả ba hàng gửi (world, event engine, event bridge). Yêu cầu bị loại ghi vào `GtaVietLifeCore.deadletter.log` | `tests/test_csharp_logic.py` cho lớp thuần; đoạn gọi HTTP trong Core chưa có test chạy |
| E13 | Đã sửa (cùng loại E5): engine trả danh sách `rejected` thay vì từ chối cả lô | `test_phase1_regressions.py` |
| G1 | Đã sửa, chưa chạy trong game | `test_csharp_logic.py` |

Tác dụng phụ cần xử lý ở đợt 2: chỉ số cơ thể người chơi nay tới engine định kỳ nên `request_dedup` tăng liên tục (chưa đo).

Các mã khác vẫn mở, gồm G2 (Personal ghi đè ví GTA, trái hợp đồng đã chốt) và E4, E6 (đợt 3).

## 7. Đợt GTA V Legacy và M3 (2026-11-28, chưa chạy trong game)

| Hạng mục | Trạng thái |
|---|---|
| G2 | Đã sửa: Personal không ghi ví; gửi `wallet.observed`, sổ cái đối soát bằng bút toán `reconcile` |
| `request_dedup` | Xóa biên nhận quan sát và đồng bộ quán cũ hơn 1 giờ; biên nhận ca làm được giữ |
| Mới phát hiện: script `.cs` không nạp được | SHVDN không tham chiếu `System.Web.Extensions`; chuyển sang phát hành DLL |
| Mới phát hiện: API Legacy khác | `Task.Combat`, `Model.GetDimensions` không có ở 3.6.0; đã thay |
| Mới phát hiện: Engine đóng gói không khởi động | Thiếu module khi đóng gói; đã sửa, pipeline chạy thử exe |
| Mới phát hiện: bridge sập khi không có loa | Đã sửa |
| M3 UIS | `GtaVietLifeInteriors.cs`: vùng 120/150 m, danh bạ cửa (4 cửa gốc + dò cửa + Shift+H, tối đa 5000), 26 mẫu nội thất, đóng băng tối thiểu 200 ms, hết hạn 8 giây, sức chứa 3, gỡ kẹt 8 giây |
| M3 còn thiếu | Không có bộ 500–1000 tọa độ cửa; nội thất gốc không dịch chuyển được nên nhiều nhà dùng chung mẫu (mỗi lúc một nhà), `building_id` chỉ đổi vị trí đồ sinh thêm; cư dân chỉ vào nhà khi ở trong 30 m quanh cửa đã ghi |
| M6 | Có `manifest.json`, tự kiểm lúc mở, Updater quay lui khi bản mới không xác nhận. Còn thiếu: đổi tên exe theo yêu cầu, `config.json` của Launcher, ký checksum, tự chạy game |

## 8. Đợt 0.3.0: lưu bền, sổ cái, thời gian (2026-11-28, chưa chạy trong game)

| Mã | Trạng thái | Test |
|---|---|---|
| E4 | Adapter gửi `as_of_source` + `as_of_sequence`; số dư đầu kỳ mở một lần, event đã nằm trong số dư không cộng lại, event đến trước không bị mất; quan sát ví được ưu tiên | `test_phase2_regressions.py` |
| E6 | Engine giữ mốc giờ cao nhất đã mô phỏng; giờ lùi tăng `clock_epoch`, không mô phỏng lại; nhảy giờ lớn chạy theo khúc 1440 phút; bộ đếm mới (`clock_id` khác) đặt lại mốc | như trên, `test_host.py` |
| E7 | Migration chạy trong một giao dịch; DB dở dang do bản cũ mở lại được; backup `.v<n>.bak` trước khi nâng cấp | như trên |
| E8 | Trùng ID khác nội dung bị từ chối; không trộn tiền tệ; giới hạn số tiền kèm trigger ở DB; khóa tiền tố của engine; phát lại bán và thuê trả `duplicate`; một khoản lương lỗi không chặn khoản khác; kiểm số dư lúc khởi động | như trên |
| E10 | Khóa một tiến trình trên thư mục dữ liệu và cổng; WAL, `foreign_keys`, `busy_timeout` | như trên |
| E12 | Ngữ cảnh dài được cắt bớt, giữ giới hạn tri thức | `bridge/test_engine_context.py` |
| B1 | `news()` luôn nhả khóa | `bridge/test_news_lock.py` |
| G3 | World, Personal, Economy, DoiSong, Chat, Interiors, Core ghi qua `GvlSaveFile`: file tạm, `.bak`, dòng đầu và cuối; tải lỗi cả hai thì không ghi đè | `test_csharp_logic.py` (lớp thuần) |
| G5 | `Core.sav` mới sinh `clock_id`, nguồn event `gta5.shvdn:<id>`; file cũ giữ nguồn cũ | như trên |
| G6 | Số dư ví đọc trên luồng script | `test_csharp_logic.py` |

Còn mở: E9, E11, E14, G4, G7, G8–G13. Sổ cái chưa là chủ ghi của ngân hàng và quán: script C# vẫn tự giữ số dư (đợt tiếp theo).
Chưa làm: `epoch`, `expires_at_world_minute` trong phong bì event và lệnh; Core là nơi duy nhất đếm ngày.

## 9. Đợt 0.3.1: lỗi còn mở và Launcher (2026-11-28, chưa chạy trong game)

| Mã | Trạng thái | Test |
|---|---|---|
| E9 | Lưu lỗi thì bộ nhớ trở về trạng thái đã lưu; ack lệnh và hiệu ứng trong một giao dịch; quan hệ và trạng thái NPC ghi cùng nhau; `advance` trùng ID chỉ chạy một lần | `test_phase3_regressions.py` |
| E11 | Biên nhận chống trùng tách theo endpoint, vẫn đọc biên nhận cũ | như trên |
| E14 | Chưa đặt `dash_pin` thì dashboard chỉ nghe trên máy tính; có PIN thì đọc dữ liệu cũng cần PIN; khóa 1 phút sau 5 lần sai. APK cũ phải cài lại | `bridge/test_dashboard_access.py` |
| G4 | Bán nhà đang thế chấp trừ phần vượt hạn mức 60% vào tiền bán; phát mãi thiếu thành nợ | `test_csharp_logic.py` (phép tính) |
| G7 | Đóng quán hủy hợp đồng của quán đó và dời chỉ số quán phía sau | như trên |
| G8 | Thả NPC trước khi giữ người mới và khi `/stop` lỗi | chưa có test chạy |
| G9 | `Residents.txt` được xóa trắng lúc nạp và thoát | chưa có test chạy |
| G10 | Xe đang ở bãi giữ không gọi được từ garage | chưa có test chạy |
| G11 | Chỉ menu mở sau cùng nhận phím số (`GVL.MenuOwner`) | chưa có test chạy |
| G12 | Engine mất kết nối thì cư dân về lịch gốc; thêm hoạt động toilet, shower | chưa có test chạy |
| G13 | Chuyến xe không tới được thì hoàn toàn bộ phí | chưa có test chạy |
| M6 | Tự dò thư mục game (Rockstar, Steam); tùy chọn tự mở GTA V; `config.json` thay `launcher.json`; gói log dạng zip che khóa; đóng Launcher chờ dịch vụ dừng; chỉ dừng tiến trình của bản cài này | `launcher/tests/Logic` (dò game, gói log) |

Còn mở, cần chủ dự án quyết hoặc cần vào game:
- Sổ cái làm chủ ghi ngân hàng và quán: script C# vẫn tự giữ số dư. Cần chốt cách chơi khi Engine tắt (khóa giao dịch hay xếp hàng chờ).
- Ký checksum bản cập nhật: cần khóa ký và nơi cất khóa.
- Đổi tên exe theo đặc tả (`GTA Viet Life Launcher.exe`, `Setup.exe`): ảnh hưởng đường cập nhật từ bản đã cài.
- Danh bạ 500–1000 cửa: phải thu tọa độ trong game. Offset nội thất theo tòa nhà: nội thất gốc không dịch chuyển được.
- Lệnh trên dashboard dùng chỉ số quán, đóng quán thì lệnh đang chờ có thể trỏ sang quán khác.

## 10. Đợt 0.3.2: M1, M3, M5 (2026-11-28, chưa chạy trong game)

| Module | Trạng thái | Test |
|---|---|---|
| M1 | World, Economy, Personal đếm ngày theo Core (`GvlDayWatch`); không có Core thì tự nhận biết qua nửa đêm. Ngủ hoặc tua giờ trong game nay cũng được Đời sống tính ngày | `test_csharp_logic.py` |
| M3 | Đăng ký lại vật thể giữ nguyên state; lệnh quá 30 phút game thành `expired`; lọc lệnh theo adapter | `test_phase4_regressions.py` |
| M5 | Token ngoài ASCII trả 401; POST mang protocol khác bị từ chối; tài liệu giao thức cập nhật | như trên |

Chưa làm: M2 gom quan hệ và trí nhớ về engine; M4 sổ cái làm chủ ghi (chờ chốt cách chơi khi Engine tắt).

## 11. Đợt 0.3.3: M4 ngân hàng (2026-11-28, chưa chạy trong game)

- Chốt: Engine tắt vẫn chơi, event lưu bền và gửi bù. Lần đầu gửi `bank.takeover`; sau đó khi hàng gửi trống và sổ cái đã nhận đủ thì số dư ngân hàng theo sổ cái.
- Bộ đếm game tạo lại, hoặc phải đọc `Core.sav.bak`, thì dùng nguồn event mới và đối soát lại theo game.
- Test: `test_bank_ownership.py`, `test_csharp_logic.py`. Phần gọi HTTP và áp số dư trong game chưa có test chạy.
- Chưa chuyển: tiền quán, tồn kho, lương; lịch trả góp vẫn ở script (sổ cái chỉ giữ tổng nợ). Event bị Engine từ chối (ghi ở deadletter) sẽ bị số dư sổ cái ghi đè.

## 12. Lần chạy đầu trong GTA V (2026-11-28, bản 0.3.3, build 1.0.3889.0, SHVDN 3.7.0-nightly.206)

Cả 7 script nạp được, Engine Host trả 200 cho mọi yêu cầu trong log. Lỗi thấy được và sửa trong 0.3.4 (chưa chạy lại trong game):

| Hiện tượng | Nguyên nhân | Sửa |
|---|---|---|
| Phím J ngừng ăn, F7 và H ngừng theo | `GtaVietLifeWorld` bị hủy: `Collection was modified` trong `TextElement.InternalDraw`. Suy đoán: bộ dọn rác hủy phần tử chữ khi đang vẽ. Cờ `GVL.WorldMenu` không được xóa | `GC.KeepAlive`, bắt lỗi khi vẽ, xóa cờ menu khi script bị hủy |
| Giữ N không ra chữ | `cublas64_12.dll` thiếu, lỗi chỉ lộ khi nhận dạng | Chuyển sang CPU và nhận dạng lại |
| F7 rơi tự do | Thả nhân vật khi địa hình chưa nạp | Quay về chỗ cũ, chờ tối đa 10 giây |
| Đồ đạc chồng lên nhau | Mod sinh thêm đồ trong nội thất đã có đồ | Mặc định không sinh (`SpawnFurniture=false`) |

Còn mở: tương tác với đồ vật chỉ là một hoạt cảnh đứng tại chỗ và cộng chỉ số; số mẫu nội thất bị giới hạn bởi nội thất gốc.

## 13. Đợt 0.3.5: phục vụ tại quán và nút cập nhật (chưa chạy trong game)

- `GtaVietLifeShopFloor.cs`: trong nội thất có máy tính tiền của game, đến quầy bấm E, đổi món bằng `,` `.`, trả tiền qua `GVL.PayCash`, nhận chỉ số qua `GVL.NeedsAdd`. Người bán: dùng người đang đứng ở quầy, không có thì sinh thêm. Một người dọn đi giữa tối đa 3 điểm. Vị trí đứng và tên mô hình đồ ăn chưa kiểm trong game. Quản lý quán sở hữu vẫn dùng menu J.
- Launcher: nút Cập nhật đọc danh sách release, chỉ nhận tag `v*` có đủ gói và checksum (bỏ qua release APK `dash-*`); dừng dịch vụ trước khi trình cập nhật thay thư mục. Repo chưa có release `v*` nào.
