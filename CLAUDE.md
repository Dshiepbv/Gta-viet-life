# CLAUDE.md · GTA VietLife (mod GTA V)

File này được Claude Code tự nạp mỗi phiên. Phần 0 riêng cho dự án; phần 1–8 là luật làm việc chung; phần 9 là trạng thái.

## 0. Dự án: GTA VietLife (mod GTA V, thư mục `E:\AI\Projects\Gta-vietlife`)
- Tình trạng (2026-11-28): đích là GTA V Legacy; đã có bộ cài 0.3.3, chưa chạy trong GTA V. Tiến độ phiên gần nhất: `NOTES.md`. Kiểm toán: `docs/AUDIT_M0_M6.md`. Hợp đồng dữ liệu: `docs/SHARED_STATE_CONTRACT.md`.
- Mục tiêu mod (1 câu): Life Simulator, người chơi và mọi NPC có vòng đời, sinh hoạt, kinh tế, quan hệ, tương tác vật lý, ký ức và lưu bền.
- Loại mod và nền tảng: 7 script C# SHVDN v3 (`game/`, phát hành dạng DLL biên dịch sẵn), engine Python + SQLite (`engine/`), bridge hội thoại (`bridge/`), Launcher/Updater .NET (`launcher/`), adapter Godot tham chiếu, app Android dashboard.
- Phiên bản game và thư viện: GTA V Legacy; biên dịch theo SHVDN API 3.6.0 (ghim SHA-256), khi chơi dùng SHVDN nightly cho build từ 1.0.3258; .NET 8 (launcher), net48 (script), Python 3.11. Build game đích: chưa ghi nhận.
- Test: `python -B -m unittest discover -s engine/tests` · `python -B -m unittest discover -s tests` (đối chiếu C#/host, biên dịch logic C# bằng `csc.exe` của .NET Framework) · trong `bridge/`: `python -B -m unittest test_engine_context`
- Đóng gói (test, 7 DLL, Launcher, exe dịch vụ, chạy thử, bộ cài): `packaging/Build-Package.ps1 -InnoCompiler <ISCC.exe> -WorkDir <thư mục ngoài repo>`; chỉ DLL: `packaging/Build-Scripts.ps1`. Python để đóng gói phải có tkinter. Kết quả ở `artifacts/`.
- Thư mục cài mod: `<GTA V>/scripts` (Launcher cài DLL theo giao dịch, có backup, cất file `.cs` cũ) · Cách nạp lại khi đang chơi: chưa xác minh.
- Định nghĩa "xong": build pass, đã vào game chế độ Story kiểm tra đúng hành vi, xem log của script hook không có lỗi.

### Bản đồ module và trạng thái kiểm toán
| Module | Chính | Trạng thái 2026-11-28 | Lỗi mở |
|---|---|---|---|
| M0 Life Core | `engine/life_simulation.py` | Logic thật, chưa chạy game | |
| M1 Bridge/Clock | `GtaVietLifeCore.cs`, `engine/host.py` | E6 đã sửa, chưa chạy game | |
| M2 Social | `bridge/`, Chat, Personal | Dữ liệu tách 2–3 nơi; G8 E14 đã sửa | |
| M3 World/Objects | World, `GtaVietLifeInteriors.cs`, `society.py` | UIS đã viết, logic thuần có test, chưa chạy game | |
| M4 Economy/Work | Economy, World, `society.py` | E4 E8 đã sửa; sổ cái chưa là chủ ngân hàng/quán | |
| M5 Compat/Host | `engine/host.py`, Godot | Đã khóa một tiến trình | |
| M6 Launcher | `launcher/`, `packaging/` | Bộ cài chạy thử được; còn thiếu so đặc tả | xem audit mục 5 |

### Đặc tả M6: Launcher độc lập
- Gồm `GTA Viet Life Launcher.exe`, `Setup.exe`, Updater, `manifest.json`, `config.json`; độc lập với GTA V và Life Simulator Runtime.
- Tự kiểm môi trường và tương thích GTA V; cập nhật qua staging + checksum; rollback nguyên tử; tuyệt đối không ghi đè save người dùng.
- Quản lý tiến trình và heartbeat; xuất gói log; tự khởi chạy game. Người dùng không phải chép `.cs` hay chạy `install.bat`.
- Khoảng cách với mã hiện tại và tiêu chí nghiệm thu: `docs/AUDIT_M0_M6.md` mục 5.

### Luật kỹ thuật rút từ kiểm toán
- Logic C# cần test đặt trong khối `// <gvl-testable:tên>`, không gọi API GTA, viết theo C# 5.
- File lưu chỉ ghi qua `GvlSaveFile` (`game/GtaVietLifeSaveFile.cs`); script mới có file lưu phải thêm file này vào `.Compile.csproj`.
- Event tiền không bị bỏ vì lỗi 5xx; yêu cầu bị loại phải vào `GtaVietLifeCore.deadletter.log`.
- Thêm đường dẫn engine mới trong C#: kiểm bộ lọc ở `PostWorldRequest` và route ở `host.py`.
- Không gọi native GTA trong `Task.Run`. Không tự xếp lại việc vào hàng đang được `while` rút trong cùng tick.

### Luật riêng của dự án mod GTA V
- Chỉ dùng ở chế độ Story / offline. Không viết hay đề xuất gì để chạy trong GTA Online (vi phạm điều khoản, có thể bị cấm tài khoản).
- Không sửa file gốc của game. Thay đổi tài nguyên đặt trong thư mục `mods` (OpenIV), và sao lưu trước mọi thay đổi `.rpf`.
- Không chép đè, không xóa file trong thư mục game khi chưa hỏi; cài đặt bằng script phải có bước sao lưu và cờ xác nhận.
- Ghi rõ phụ thuộc vào phiên bản game: bản cập nhật game có thể làm hỏng script hook; khi game cập nhật, kiểm tra tương thích trước khi sửa code.
- Không phân phối lại tài nguyên có bản quyền của game hay của tác giả mod khác.

### Đã chốt
- Phát triển trực tiếp trên `main`, không tách nhánh. · 2026-11-28
- Không để code giữ chỗ; không coi build/CI xanh là hoàn thành. · 2026-11-28
- Sổ cái kinh tế thống nhất dùng SQLite. · 2026-11-28
- Ví GTA V là chủ tiền mặt. Sổ cái SQLite (M4) là chủ tuyệt đối ngân hàng, nợ, doanh thu/chi phí quán, lương, tồn kho. · 2026-11-28
- Tải save GTA cũ: giữ đời sống và sổ cái hiện hành, chỉ đối soát tiền mặt; cấm `Personal` ghi đè ví GTA, cấm mô phỏng lại. Cài đặt ở đợt 3. · 2026-11-28
- Launcher độc lập theo đặc tả M6. · 2026-11-28
- M4 khi Engine tắt: cho chơi tiếp, lưu bền rồi gửi bù; khi sổ cái đã nhận đủ thì sổ cái là chuẩn. · 2026-11-28

### Đã bác bỏ (lý do · từ khóa kích hoạt)
- (phương án) · vì (lý do) · từ khóa: (...)

## 1. Cách làm việc với tôi
- Trả lời tiếng Việt, ngắn, kết luận lên đầu. Tin nhắn qua điện thoại: 1–4 câu.
- Tôi là BA/QA (nắm bài toán, nghiệm thu), bạn là người thực thi. Đề bài mơ hồ hoặc việc khó đảo ngược thì hỏi; còn lại phác hướng đi vài dòng rồi làm luôn.
- Việc có đường lùi (sửa code, tạo file, chạy build/test, cài thư viện thiếu): tự quyết, tự làm, báo ngắn. Không kết thúc bằng một loạt câu hỏi.
- Làm đúng động từ được giao rồi dừng. Bảo "commit" thì chỉ commit; push, tạo PR, gửi bất cứ thứ gì ra ngoài: hỏi trước.
- Tôi đã chốt thì làm theo. Thấy sai chỉ nhắc 1 câu rồi vẫn làm (trừ khi lỗi nằm ngay trong thứ sắp gửi ra ngoài).
- Tôi kể ý tưởng mà chưa chốt thời điểm: ghi nhận và hỏi đúng 1 câu "Làm luôn hay để sau?", không tự dựng file.
- Không hỏi lại điều đã có trong file này, lessons.md hoặc đã kể trong phiên.
- Kết quả trước, công cụ sau: khi có hạn, hoàn thành trọn bộ kết quả trước, tối ưu sau.

## 2. Brief: nhận việc và giao việc
Mọi việc lớn bắt đầu bằng brief đủ 6 ý. Thiếu ý nào thì tự điền giả định hợp lý nhất, ghi rõ "Giả định", rồi làm.
1. Mục tiêu (1 câu) · 2. Nút thắt cần giải · 3. Bối cảnh tối thiểu (file, module liên quan)
4. Ràng buộc và điều đã bác bỏ · 5. Đầu ra (file nào, định dạng nào) · 6. Tiêu chí nghiệm thu đo được (lệnh test/build nào phải pass, hành vi nào phải thấy)
Khi giao việc cho subagent: viết brief tự đủ theo khung trên; subagent chỉ trả về kết luận.

## 3. Code
- Viết đầy đủ, chạy được. Cấm `// TODO`, `...`, "phần còn lại tương tự", hàm rỗng giữ chỗ, cắt bớt code khi trả lời.
- Sửa file thì sửa trúng chỗ; không viết lại cả file khi chỉ cần sửa vài dòng.
- Làm xong tự chạy build/test liên quan (lệnh ở mục Dự án). Chưa chạy được thì nói rõ "chưa chạy, vì...".
- Việc lặp quá 2 lần: đề xuất đóng thành script hoặc skill.

## 4. Kiểm chứng, chống ảo giác
- Kiểm tra sản phẩm thật, không kiểm tra ý định: đọc lại file từ đĩa, chạy app/test thật. Build không lỗi chưa phải là xong.
- Ghi chú cũ (kể cả mục Trạng thái dưới đây) là ảnh chụp quá khứ: kiểm lại trên code/thiết bị thật trước khi dựa vào.
- Trước khi kết luận "không có / không được / không hỗ trợ": rà hết file, cấu hình, nhánh, tài liệu chính chủ mới nhất.
- Test báo pass toàn bộ hoặc fail toàn bộ: nghi ngờ chính bộ test trước; luôn có một ca đối chứng bắt buộc phải fail.
- Chưa đo thì nói "chưa đo". Không bịa số liệu, API, tên hàm, phiên bản thư viện.
- Từ dễ hiểu nhầm trước khi làm hàng loạt: làm một mẫu nhỏ theo nghĩa đoán được rồi hỏi xác nhận.
- Tôi chỉ ra một lỗi: rà toàn bộ dự án sửa mọi lỗi cùng loại, không vá một chỗ.
- Sửa 2 lần vẫn cùng một lỗi: dừng, ghi trạng thái, đề xuất mở phiên mới với brief sạch.

## 5. Khóa an toàn cứng (kể cả khi bật bypass)
- Không xóa file, không chạy lệnh có thể mất dữ liệu chưa lưu (`rm -rf`, `git reset --hard`, `git clean`, ghi đè hàng loạt) khi chưa hỏi.
- Không force push. Không commit mật khẩu, API key, keystore, file `.env`.
- Không hành động ra bên ngoài dưới danh nghĩa tôi (đăng, gửi, phát hành) và không đụng tới tiền.
- Không dùng connector gắn thẳng vào mail, ổ đĩa, lịch; nếu cần thì qua script trung gian chỉ đọc, script có ghi phải chạy thử và có cờ xác nhận.

## 6. Bộ nhớ ngoài và kỷ luật ngữ cảnh
- Đầu phiên đọc file này và `lessons.md`. Gặp tên module/người/việc đã có hồ sơ thì mở hồ sơ đọc trước khi trả lời.
- Mục Trạng thái chỉ giữ 5 mục gần nhất; mục cũ chuyển sang `docs/history.md`. Cuối mỗi việc lớn cập nhật Trạng thái.
- Đọc quá 5 file để tìm hiểu: giao subagent, chỉ nhận kết luận. Tối đa 2 agent chạy song song.
- Không chạy lại bước đã có kết quả chỉ để "kiểm tra lại cho chắc".
- Gần đầy ngữ cảnh: ghi trạng thái vào file này, đóng phiên, mở phiên mới. Không dựa vào tự nén (compact).
- Effort mặc định `high`; chỉ dùng `max` cho việc thực sự khó (tốn gấp 1,5–2 lần).

## 7. Sổ bài học (lessons.md)
Khi tôi chỉ ra lỗi: đề xuất 1 dòng rút kinh nghiệm; tôi duyệt mới ghi. Định tuyến:
- Xóa hết tên riêng mà câu vẫn có nghĩa: ghi mục CHUNG; mất nghĩa: ghi mục DỰ ÁN.
- Vừa là nguyên tắc vừa gắn ca cụ thể: tách đôi, nối bằng cùng mã ID.
- Ca mới trùng nguyên tắc cũ: gộp vào bài cũ, không thêm mục mới.
- Sổ phình to: tách theo mảng (build, UI, bảo mật...), không cắt bớt.

## 8. Văn bản giao người khác
Văn phong con người: không mở bài "Không phải X mà là Y", không câu dài 4 mệnh đề, không kết bằng châm ngôn, không rải emoji và in đậm. Không để lộ trao đổi nội bộ giữa tôi và AI. Commit message và changelog viết rõ việc đã làm.

## 9. Trạng thái (giữ 5 mục gần nhất, mới nhất ở dưới)
- [2026-11-28] Đợt 1 đã push (28ae051), CI #272 xanh. G2 + dọn dedup + M3 UIS + bộ cài Legacy 0.2.0 xong trên máy; 77 engine + 15 contract/C# + 2 bridge + test Launcher/Updater/exe đóng gói pass. | Bước tiếp: chủ dự án test trong GTA V Legacy tối nay, gửi `GtaVietLife*.log`, `ScriptHookVDotNet.log`; sau đó E4 E6 E7 E8 G3. | Chưa đạt so yêu cầu: danh bạ 500–1000 cửa, offset nội thất theo tòa nhà.
- [2026-11-28] Bản 0.3.0 (đã push, 7755684, CI #275 xanh): E4 E6 E7 E8 E10 E12 B1 G3 G5 G6; bộ cài build lại. Chi tiết `docs/AUDIT_M0_M6.md` mục 8. | Bước tiếp: test trong GTA V Legacy, gửi log; kiểm CI; sổ cái làm chủ ngân hàng/quán, G4 G7, M6.
- [2026-11-28] Bản 0.3.1 trên máy, chưa commit: E9 E11 E14 G4 G7–G13, một phần M6. Chi tiết audit mục 9. | Bước tiếp: test trong GTA V Legacy; chốt sổ cái khi Engine tắt, khóa ký, tên exe; hỏi trước khi push (đổi `bridge/dashboard.html` sẽ phát hành APK công khai).
- [2026-11-28] Bản 0.3.2 trên máy, chưa commit: thêm M1 (đếm ngày theo Core), M3 (hạn lệnh, giữ state vật thể), M5 (token, protocol). | Bước tiếp: chốt M4; làm M2; test trong game; hỏi trước khi push.
- [2026-11-28] Bản 0.3.3 trên máy, chưa commit: sổ cái làm chủ số dư ngân hàng (chơi khi Engine tắt, gửi bù); nợ được ghi vào sổ cái. Quán, tồn kho, lương chưa chuyển. | Bước tiếp: chuyển quán và lương; M2; test trong game; hỏi trước khi push.
