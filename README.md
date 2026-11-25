# GTA Việt Life

Mod cho GTA V (chơi đơn) giúp **nói chuyện bằng giọng tiếng Việt với NPC**. Mỗi NPC có tên, nghề, tính cách và trí nhớ riêng, trả lời bằng giọng Việt. Toàn bộ chạy **miễn phí** trên máy của Sếp.

```
[Giữ N, nói] → script trong game ──HTTP──► cầu nối Python
                                           ├─ Nghe:  faster-whisper (GPU) → chữ tiếng Việt
                                           ├─ Nghĩ:  Gemini Flash (gói free) / Ollama Qwen (dự phòng, local)
                                           ├─ Nói:   Edge-TTS (NamMinh / HoaiMy, mỗi NPC một chất giọng)
                                           ├─ Nhớ:   SQLite (memory.db)
                                           └─ Phụ đề tiếng Việt nổi trên game
NPC quay mặt, mấp máy miệng, làm theo hành động ◄── (đi theo / bỏ đi / bỏ chạy / đánh nhau)
```

> ⚠️ Chỉ dùng ở chế độ chơi đơn. **Không bao giờ vào GTA Online** từ bản game đã cài mod.

## 1. Chuẩn bị game

1. Cài GTA V Enhanced (Epic), **bỏ tích Auto-update**, cài ngoài `C:\Program Files` (ví dụ `E:\GTA-Viet_Life`).
2. Cài **ScriptHookV** bản hỗ trợ Enhanced, rồi **[ScriptHookVDotNet Enhanced](https://github.com/Chiheb-Bacha/scripthookvdotnetenhanced)** theo hướng dẫn trên trang đó (chép các file vào thư mục chứa `GTA5_Enhanced.exe`).
3. Trong game: Settings → Graphics → Screen Type = **Windowed Borderless** (để phụ đề tiếng Việt nổi lên trên game).

## 2. Cài cầu nối AI (làm 1 lần)

1. Cài **Python 3.11** từ python.org (tích "Add python.exe to PATH").
2. Lấy **Gemini API key miễn phí**: vào https://aistudio.google.com/apikey → Create API key.
3. Chạy `install.bat`. Xong thì mở `bridge\config.json`, dán key vào `"gemini_api_key"`.
4. (Tuỳ chọn, dự phòng khi hết lượt Gemini hoặc mất mạng) cài [Ollama](https://ollama.com) rồi chạy `ollama pull qwen2.5:3b`.

## 3. Cài script vào game

Chép `game\GtaVietLife.cs` và `game\GtaVietLife.ini` vào thư mục `scripts` trong thư mục game (tạo mới nếu chưa có).

## 4. Chơi

1. Chạy `start_bridge.bat` **trước** khi vào game. Lần đầu sẽ tải mô hình nghe (~1.6 GB).
2. Vào game, góc màn hình báo "da ket noi cau noi AI" là được.
3. Đến gần một người (≤ 5 m), **giữ N**, nói tiếng Việt, **thả N** để gửi. NPC trả lời sau khoảng 2–3 giây.
4. Bấm **K** để "ghi nhớ" người gần nhất: họ có chấm xanh trên bản đồ, không biến mất, và nhớ mọi cuộc trò chuyện với Sếp.

Ví dụ thử: "Chào anh, quanh đây có quán cà phê nào không?", "Đi theo tôi làm vệ sĩ nhé, trả anh 500 đô", "Thôi anh về đi".

## Cấu hình (`bridge\config.json`)

| Mục | Ý nghĩa |
|---|---|
| `gemini_model` | Mô hình Gemini, mặc định `gemini-2.5-flash` |
| `ollama_model` | Mô hình local dự phòng. Card 8GB đang chạy game: nên dùng `qwen2.5:3b` |
| `whisper_model` | Mô hình nghe. `large-v3-turbo` (chính xác) hoặc `medium` (nhẹ hơn) |
| `mic_device` | Số thứ tự micro nếu máy có nhiều micro (`null` = mặc định của Windows) |
| `overlay_font_size` | Cỡ chữ phụ đề |

Đổi phím trong `GtaVietLife.ini` (`Talk=N`, `Remember=K`).

## Lỗi thường gặp

- **"chua bat cau noi AI"**: chưa chạy `start_bridge.bat`, hoặc cửa sổ đó báo lỗi.
- **"Không chạy được GPU"**: thiếu thư viện CUDA, cầu nối tự chuyển sang CPU (chậm hơn). Chạy lại `install.bat`.
- **Không thấy phụ đề**: game đang ở Fullscreen, chuyển sang Windowed Borderless.
- **NPC không trả lời / "Xin lỗi, tôi đang vội"**: sai API key hoặc hết lượt Gemini miễn phí trong ngày; cài Ollama để có dự phòng.

## Đời sống (GtaVietLifeWorld.cs)
Chép `game/GtaVietLifeWorld.cs` vào `scripts` cùng file nói chuyện. Phím `M` mở menu (số 1-9 chọn):
ăn/uống/ngủ, thuê nhà (tiền nhà mỗi ngày), việc giao hàng (ca 6h-22h), kinh doanh (mở quán, nhập hàng, thuê nhân viên, rút tiền),
đặt NPC gần nhất làm "cư dân" có lịch (ban ngày ở chỗ làm, tối về nhà), thông tin, trả nợ.
Phạt khi bị truy nã rồi thoát, nợ có lãi, uy tín tăng/giảm. Trạng thái được gửi cho cầu nối (`/state`, `/event`)
để NPC biết hoàn cảnh và có tin đồn trong khu.

## Mo hinh kinh doanh (GT / MT / B2C / B2B)

- **Loai hinh** (chon khi mo quan): Quan nuoc via he, Tap hoa, Quan an (kenh GT, von nho); Cua hang tien loi, Sieu thi mini, Nha hang/ca phe chuoi (kenh MT, von lon, dong khach, nop VAT, can mat bang thue tu "Thue ki-ot").
- **San pham**: danh muc F&B va FMCG co gia von / gia ban rieng; moi quan chi ban nhom mat hang cua loai hinh. Loi nhuan moi ngay = doanh thu - gia von - luong - thue - tien thue mat bang.
- **Nhap hang tu nha phan phoi**: lo 1 / 5 / 10, chiet khau 0 / 7% / 12%, MT duoc them 5%.
- **B2B**: menu Kinh doanh > "Ban si B2B": ky hop dong cung ung cho cua hang/quan khac, giao hang moi ngay tu kho mot quan, gia si thap hon gia le, thu tien sau 5-45 ngay, co rui ro vo no.

## Chay 24/24 (offline)

Moi 2 phut mod ghi dau thoi gian vao file luu. Khi mo game lai, mod tinh thoi gian da tat (1 ngay game = 48 phut thuc, toi da 14 ngay): quan co nhan vien van ban hang (cham hon luc choi: 40% so khach), van tra luong, thue, tien thue mat bang, giao hang B2B va thu cong no. Ket qua hien thanh mot thong bao tong ket, chi tiet nam trong GtaVietLifeWorld.log (dong "[offline]"). Neu thieu tien tra thue mat bang 3 ngay se mat mat bang nhu luc dang choi.

## Toa nha that

Muc "Tim mat bang" co them cac cua hang that cua game goc (24/7, LTD, Los Santos Customs) danh dau bang bieu tuong cua hang tren ban do. Toa do gan dung: hay doi chieu voi cham tren ban do roi bao lai neu lech.
