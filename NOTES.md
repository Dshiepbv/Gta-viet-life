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
