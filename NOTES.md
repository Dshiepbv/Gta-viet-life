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
