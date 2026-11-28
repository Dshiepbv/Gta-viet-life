# NOTES.md · Tiến độ giữa các phiên

## Phiên 2026-11-28 (đợt hoàn thiện sau 98cc888)

### Công cụ build trên máy này
Gốc: `C:\Users\Admin\AppData\Local\Temp\claude\dc6b571288f5d380\1af1a133-4b2d-440d-af4e-950f605f541e\scratchpad\toolchain`
(thư mục tạm, có thể bị dọn)
- Python đóng gói (3.11, có tkinter + PyInstaller): `pbs\python\python.exe`
- .NET SDK 8: `dotnet\dotnet.exe`
- Inno Setup: `inno\ISCC.exe`

### Kế hoạch
Nền trước khi sửa: 77 engine + 15 contract/C# + 2 bridge test pass.
Sửa lần lượt: E7, E10, E8, E4, E6, G3, B1, E12; sau đó build bộ cài.

### Tiến độ
- [x] E4 · [x] E6 · [x] E7 · [x] E8 · [x] E10 · [x] E12 · [x] B1 · [x] G3 · [x] G5 · [x] G6 · [x] Build exe local (thành công)
- Bản 0.2.0 cũ được chuyển vào `artifacts\0.2.0\`.
- Lệnh build: `packaging\Build-Package.ps1 -InnoCompiler <ISCC> -WorkDir <thư mục tạm> -Dotnet <dotnet> -Python <python>`; đặt `DOTNET_ROOT`, `NUGET_PACKAGES` trỏ vào thư mục toolchain.

Engine: 96 test pass; test mới `engine/tests/test_phase2_regressions.py` đã thấy fail trên mã cũ (18/19).
Đổi một kỳ vọng trong `test_host.py`: giờ game lùi không còn mô phỏng lại (theo hợp đồng mục 4).

- CI #274 (a6ea0ed) fail: test_news_lock in tiếng Việt ra console cp1252. Đã sửa test; trước khi push nên chạy test với `PYTHONIOENCODING=cp1252`.

## Kết quả cuối phiên 2026-11-28

- GitHub: commit `a6ea0ed` + `7755684` trên `main`; CI #275 xanh.
  Tải ở https://github.com/Dshiepbv/Gta-viet-life/actions/runs/46141190825 , mục Artifacts `GtaVietLife-Windows-Installer` (hết hạn 2027-02-26). Chưa tạo tag/Release.
- Exe local (build trước commit sửa test, mã đóng gói giống hệt): `E:\AI\Projects\Gta-vietlife\artifacts\GtaVietLife-Setup-0.3.0.exe`
- Đã qua: 97 engine, 18 contract/C#, 5 bridge, test Launcher/Updater, chạy thử exe Engine và Bridge đã đóng gói. Chưa chạy trong GTA V.

### Việc tiếp theo
1. Chủ dự án test trong GTA V Legacy, gửi `GtaVietLife*.log`, `ScriptHookVDotNet.log`.
2. Sổ cái làm chủ ghi ngân hàng và quán (script C# đang tự giữ số dư).
3. Lỗi còn mở: E9, E11, E14, G4, G7, G8–G13.
4. M6: đổi tên exe theo đặc tả, `config.json` của Launcher, ký checksum, tự chạy game.
5. Chưa đạt so yêu cầu UIS: danh bạ 500–1000 cửa, offset nội thất theo tòa nhà.

## Đợt 2 trong cùng phiên (làm tiếp phần còn mở)
- [x] E9 · [x] E11 · [x] E14 · [x] G4 · [x] G7 · [x] G8 · [x] G9 · [x] G10 · [x] G11 · [x] G12 · [x] G13
- [x] M6 (phần không cần quyết định) · [ ] Sổ cái làm chủ ngân hàng/quán (chờ chốt) · [ ] Build exe 0.3.1

## Đợt 3 trong cùng phiên
- [x] M1 · [x] M3 · [x] M5 · [ ] Build exe 0.3.2
- [ ] M2 gom quan hệ và trí nhớ về engine (chưa làm)
- [ ] M4: chờ chủ dự án chốt khi Engine tắt thì khóa giao dịch hay xếp hàng gửi bù
- Chưa commit từ sau 7755684. Push `bridge/dashboard.html` sẽ kích hoạt workflow phát hành APK công khai: hỏi trước.

## Đợt 4: M4
- [x] Build 0.3.2 · [x] Chốt M4: chơi tiếp rồi gửi bù · [x] Sổ cái làm chủ ngân hàng · [ ] Build 0.3.3
- [ ] Chuyển tiền quán, tồn kho, lương sang giao dịch sổ cái

## Điểm dừng hiện tại
- [x] Build 0.3.3: `E:\AI\Projects\Gta-vietlife\artifacts\GtaVietLife-Setup-0.3.3.exe` (114 engine, 19 contract/C#, 9 bridge, test Launcher/Updater/exe đóng gói đạt; chưa chạy trong GTA V)
- Bản cũ: `artifacts\0.2.0`, `0.3.0`, `0.3.1`, `0.3.2`
- Chưa commit, chưa push. GitHub đang ở 7755684 (0.3.0).
- Còn lại: tiền quán/tồn kho/lương sang sổ cái; M2 gom quan hệ và trí nhớ; khóa ký bản cập nhật; đổi tên exe; danh bạ cửa; test trong game.

## Cập nhật cuối: 0.3.3 đã lên GitHub
- Commit 9be2a43; CI Windows #276 xanh: https://github.com/Dshiepbv/Gta-viet-life/actions/runs/46144377564 (artifact `GtaVietLife-Windows-Installer`, hết hạn 2027-02-26)
- Workflow APK đã phát hành release `dash-13`.
- Dòng "Chưa commit, chưa push" ở trên không còn đúng. Chỉ NOTES.md và CLAUDE.md đang sửa trên máy.

## 0.3.4: sửa lỗi sau lần test trong game
- [x] World sập khi vẽ chữ, cờ menu · [x] STT chuyển CPU · [x] F7 rơi · [x] đồ chồng nhau · [ ] Build 0.3.4 · chưa commit
- Log người dùng gửi: GtaVietLife-Diagnostics.zip (2026-11-28 20:02)
- Chờ người dùng test lại; ini cũ trong scripts không bị ghi đè, mặc định SpawnFurniture=false nằm trong mã.

- [x] Build 0.3.4: `E:\AI\Projects\Gta-vietlife\artifacts\GtaVietLife-Setup-0.3.4.exe`. Chưa commit, chưa push (GitHub đang ở 9be2a43, bản 0.3.3).

## 0.3.5
- [x] ShopFloor · [x] Nút cập nhật · [ ] Build 0.3.5 · chưa commit
- Người dùng muốn: không menu game, tương tác trực tiếp trong quán; nhà/quán thử dùng mô hình có sẵn, sau tự dựng.
- Nút cập nhật cần release tag v* (CI phát hành khi push tag): hỏi người dùng trước.

## Phát hành v0.3.5
- Commit 47671a7, tag v0.3.5, CI #277 và #278 xanh. Release: https://github.com/Dshiepbv/Gta-viet-life/releases/tag/v0.3.5
- Workflow APK cũng chạy theo tag (bộ lọc paths không áp cho tag). Cần giới hạn nó vào nhánh main.
- Dòng 'Build 0.3.5 / chưa commit' ở trên đã xong.

- Người dùng gặp 'DeleteFile code 5' khi cài 0.3.5 đè bản đang chạy. Đã thêm bước đóng tiến trình vào packaging/GtaVietLife.iss (biên dịch được, chưa thử cài đè, chưa commit, đưa vào bản sau).

## Chấm log đối chứng
- [x] `tools/evaluate_logs.py` (21 tiêu chí), `tests/test_log_evaluation.py` (29 test contract/C#/log pass), `docs/TEST_REPORT.md` (30 yêu cầu).
- Bản đối chứng: gói log 0.3.3 người dùng gửi (không commit vì có thông tin máy).
- Thêm dòng log gọi món trong ShopFloor. Chưa commit: các file trên, ShopFloor.cs, GtaVietLife.iss. Bản phát hành đang là 0.3.5.

## 0.3.6
- [x] Nội thất nạp trước khi chuyển · [x] F7 đánh dấu bản đồ · [x] Cập nhật trong app Android (chưa biên dịch: máy không có Java; chỉ CI build được) · [ ] Build 0.3.6
- Android cần secret ANDROID_KEYSTORE_BASE64 và ANDROID_KEYSTORE_PASSWORD do chủ dự án tạo.
- Brief gốc: docs/private/BRIEF_V2.txt.

- [x] Build 0.3.6: `artifacts\GtaVietLife-Setup-0.3.6.exe`. Chưa commit, chưa push; chờ chủ dự án cho push (sẽ phát hành APK).

## Brief V4 (nhận 2026-11-28)
- Trích tại `docs/private/BRIEF_V4.txt` (gitignore). Được push không cần hỏi; không gắn tag khi chưa được yêu cầu.
- [x] Test 1.000 giao dịch · [ ] Đợt 2: quan hệ và ký ức một nguồn; test giết tiến trình khi đang lưu
- [ ] Đợt 3: Procedural Catalog, MLO, Modular, NPC tới Activity Anchor
- Đề xuất bài học chờ duyệt (lessons.md): "Không đưa nhân vật đi trước khi nơi đến đã nạp xong"; "Log phải có dòng chứng minh thành công thì mới chấm được".

## Kế hoạch V4
- docs/PLAN_V4.md. Bước tiếp: A (quan hệ/ký ức về Engine). Được push và gắn tag không cần hỏi.

- Khóa ký APK: %LOCALAPPDATA%\GtaVietLife\signing (ngoài repo). Workflow chỉ phát hành APK khi có secret. Chờ chủ dự án thêm 2 secret.
- Chủ dự án chốt: bằng chứng runtime do script tự ghi log (bài tự kiểm).

- 2026-11-28: chủ dự án để APK tính sau, giữ khóa ký tạm; khóa PKCS12 đã tạo vẫn ở %LOCALAPPDATA%\GtaVietLife\signing. Workflow hiện không phát hành APK khi thiếu secret.
- MLO: dùng gói cộng đồng + khung tương tác tự nhận đồ vật (PLAN_V4 mục 5).

## 0.4.0 / 0.4.1
- 0.4.0 phát hành (93d38a8). 0.4.1: Data Pack vi-VN. Lệnh test: python -m pytest engine/tests tests (cần pytest).
- Chưa làm: bài tự kiểm tự động trong game; giá VND chưa dùng trong script C#; đổi tên exe.

- Tag v0.4.1 và v0.4.2 trỏ vào commit thiếu .gitattributes: build trên GitHub sẽ hỏng vì Windows đổi xuống dòng làm sai SHA-256 của Data Pack, nên không có Release. Bản dùng được: v0.4.3.

- Tag v0.4.1, v0.4.2, v0.4.3 trỏ vào commit thiếu .gitattributes nên build hỏng, không có Release; xóa tag bị hệ thống chặn, để nguyên. Bản phát hành đúng: v0.4.4 (commit 3cbaf38).

## 0.4.5 (2026-11-28): tự lưu khi thoát, cập nhật Launcher, chấm vào nhà
- Mất tiền kinh doanh khi thoát: ví GTA chỉ bền khi GTA lưu. Core gọi `DO_AUTO_SAVE` khi tiền thay đổi (đứng yên 6 s, cách nhau ≥90 s, ngoài trời, không truy nã/nhiệm vụ/xe đang chạy) và mỗi ngày game. Tự lưu tắt trong cài đặt thì báo một lần.
- World ghi tiền thu/chi chưa nằm trong file lưu GTA (`cashguard=` trong file lưu mod, ghi mỗi 5 s). Nạp lại đúng file GTA đó (ví bằng ví lúc lưu, cùng nhân vật) thì trả lại; nạp file khác thì không đụng.
- Bù offline: quán hết hàng thì đóng cửa, không trả lương; không thu hồi mặt bằng khi tắt máy (ghi nợ); chủ nợ không tính lãi, không lấy tiền ví khi tắt máy.
- Updater: WinExe (hết cửa sổ cmd đen), log ở `%LOCALAPPDATA%\GtaVietLife\updater.log`, rời thư mục cài trước khi đổi tên (nguyên nhân cập nhật thất bại: thư mục làm việc nằm trong thư mục cài), thử lại khi file còn khóa, kiểm exe PE. Test `cwd-inside` fail trên Updater cũ, pass trên bản mới.
- Chấm vào nhà: dò mặt tiền bằng tia quét (tường có mái, gần đường, cách nhau 18 m), vòng tròn xanh lá (quán: vàng) thấy từ 40 m.
- Chưa chạy trong game. Bước tiếp: người dùng test 0.4.5, gửi `GtaVietLifeCore.log`, `GtaVietLifeWorld.log`, `updater.log`.

## 0.4.7 (2026-11-28): cửa hàng mua bán trực tiếp, người dân ra vào cửa, NPC trả lời không cần AI
- Cửa hàng (ShopFloor): mỗi kệ một loại hàng (đồ uống, ăn vặt, tạp hóa, đồ ăn sẵn, tráng miệng), 55 mặt hàng từ Data Pack (giá VND đổi 5.000 VND = $1). Đứng trước kệ: `,` `.` xem, E lấy vào giỏ; ra quầy E trả tiền cả giỏ; đồ ăn uống dùng tại chỗ, tạp hóa vào tủ lạnh (bus `GVL.FridgeAdd`). Rời quán thì giỏ trả lại kệ.
- Xã hội trên phố (Interiors): theo giờ người dân bước ra từ cửa (hiện dần), lên xe đi làm; chiều lái xe về đỗ, đi vào cửa (mờ dần); trưa vào/ra cửa hàng. Tối đa 6 người cùng lúc giờ cao điểm. Tắt: `StreetLife=false`.
- Người trong nhà không đứng đơ khi thiếu chỗ sinh hoạt; bị tường chặn thì đặt thẳng tới chỗ, không đi xuyên tường.
- NPC chat: không có Groq/Gemini/Ollama thì đáp bằng mẫu thoại Data Pack theo ý định câu nói (Bridge đóng gói kèm Data Pack).
- Engine: không gửi lối vào đã loại (tier -1) nữa, hết HTTP 400.
- Yêu cầu mới chưa làm: cửa thật đẩy được, chủ nhà mở cửa, mua nhà rồi tự trang trí. Cần MLO thật ở đúng chỗ; đang chờ chủ dự án chọn hướng.

## 0.4.8 (2026-11-28): bản thử khả thi, cửa hàng thật với menu kiểu game gốc
- Chủ dự án chốt: làm theo thứ tự (1) cửa hàng thật kèm menu kiểu Rockstar, (2) làm chủ cửa hàng thật, (3) thành phố đông, (4) nhà có cửa thật. Nếu bản test này không khả thi thì xem lại cơ chế game gốc.
- Mua bán chỉ ở cửa hàng thật của game (nhận ra bằng máy tính tiền có sẵn). Vỏ nhà kho do mod dựng không còn là quán; lối vào "quán" kiểu dịch chuyển bị ẩn (`ShopShells=false`).
- Tại quầy: E mở menu LemonUI 2.2.0 (MIT, ghim SHA-256 trong Build-Package) có banner 24/7, danh mục theo nhóm, giá $; mua thì trừ tiền, âm thanh tính tiền; đồ ăn uống dùng ngay, tạp hóa vào tủ lạnh; có mục thanh toán giỏ hàng lấy từ kệ. Lời nhắc dùng hộp gợi ý góc trên trái như game gốc.
- Launcher cài `LemonUI.SHVDN3.dll` (không hạ cấp bản mới hơn của mod khác) và thư mục `GtaVietLifeData`.
- Người dân trên phố: tối đa 8 giờ cao điểm, 5 giờ thường; xe đi làm gồm cả xe Online có trong file game.
- Chưa làm: bước 2 (mua và vận hành cửa hàng thật), bước 4 (cửa thật). Chưa chạy trong game.
