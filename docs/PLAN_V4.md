# Phương án hoàn thành theo Master Brief V4.0 (2026-11-28)

## Kết luận

- Mã nguồn: phần lớn lỗi trong brief đã sửa. Những phần cần nội dung lớn (catalog toàn thành phố, MLO, Modular, vòng đời mọi NPC trên phố) **chưa làm**.
- Bằng chứng runtime theo ma trận 85: **0/100**. Mới có một phiên chơi (0.3.3) và phản hồi miệng cho 0.3.5.
- Ước tính thực chất toàn dự án: **khoảng 45%**. Tính theo hạng mục brief, chưa có đo đạc.
- Cách làm từ nay: làm trọn đợt 2 → 3 → 4 trên máy, rồi giao **một bản tích hợp duy nhất** kèm **bài tự kiểm trong game**. Bạn chỉ chạy bài tự kiểm và gửi gói log, không thử từng phần bằng tay.

## 1. Đã làm và chưa làm

Ký hiệu: Xong (có test tự động) · Viết rồi, chưa chạy game · Một phần · Chưa làm.

| Hạng mục brief | Trạng thái | Ghi chú |
|---|---|---|
| **Đợt 1**: E1 E2 E3 E5 G1 | Xong | Test fail trên mã cũ, pass sau sửa |
| **Đợt 2**: E6 E7 B1 G3 | Xong | Migration trong giao dịch, file lưu nguyên tử |
| Đợt 2: thử tắt ngang tiến trình khi đang lưu | Một phần | Có test file bị cắt; chưa giết tiến trình thật |
| Đợt 2: quan hệ và ký ức một nguồn (M0) | **Chưa làm** | Vẫn ở `memory.db`, `NPCState`, bảng quan hệ |
| **Đợt 3**: E4 E8 G2, khóa một tiến trình | Xong | 1.000 giao dịch ngẫu nhiên lệch 0 |
| Đợt 3: tiền quán, lương, tồn kho thuộc sổ cái | **Chưa làm** | Mới chuyển được ngân hàng |
| Đợt 3: luồng mua, thuê, bán từ game vào sổ cái | Một phần | Có API; game chỉ gửi ảnh chụp quán |
| Đợt 3: hạn lệnh (TTL) | Xong | |
| Đợt 3: Procedural Catalog Generator toàn thành phố | **Chưa làm** | Hiện là dò cửa quanh người chơi và đánh dấu tay |
| UIS lớp 1 Native IPL (~100+ nội thất) | Một phần | 26 mẫu; chưa nạp toàn bộ IPL |
| UIS lớp 2 Custom MLO | **Chưa làm** | Cần gói tài nguyên (xem mục 4) |
| UIS lớp 3 Reusable Template | Một phần | Gán cửa vào 26 mẫu, mỗi lúc một nhà |
| UIS lớp 4 Modular Interior | **Chưa làm** | |
| NPC đi bộ qua cửa tới Activity Anchor | Viết rồi, chưa chạy game | Chỉ cư dân đặt tay, tối đa 3 người một nhà |
| Vòng đời cho **mọi** NPC trên phố | **Chưa làm** | Engine chỉ mô phỏng cư dân đã đăng ký |
| Không dùng menu chữ | Một phần | Gọi món tại quầy đã có; quán, ngân hàng, nhà vẫn dùng menu J L I Y |
| **Đợt 4**: Launcher (cài, kiểm môi trường, cập nhật, rollback, log zip) | Viết rồi, một phần đã chạy | Cập nhật lần đầu thử với v0.3.6 |
| Launcher: 7 trạng thái hiển thị, tên `GTA Viet Life Launcher.exe` và `Setup.exe` | Chưa làm | |
| Save Manager: sao lưu định kỳ có đánh phiên bản | Một phần | Có `.bak` mỗi file; chưa có bản sao định kỳ |
| Chạy trong GTA V, 85/100, video/log | **Chưa đạt** | |

## 2. Phương án: một bản tích hợp, một bài tự kiểm

### Bài tự kiểm trong game (thay cho thử tay)
Thêm script `GtaVietLifeSelfTest`. Bạn bấm một phím; script tự chạy chuỗi kịch bản và ghi kết quả từng tiêu chí của ma trận vào log:
dịch chuyển an toàn, vào và ra nội thất ở mỗi lớp UIS, sinh NPC cho đi qua cửa tới anchor, gọi món, giao dịch ngân hàng, qua ngày, lưu rồi nạp lại, đo FPS khi có nhiều NPC.
Bộ chấm `tools/evaluate_logs.py` đọc log đó và cho điểm theo ma trận. Phần cảm nhận (đẹp, tự nhiên) vẫn cần bạn nhận xét một lần.

### Thứ tự làm (không nhảy đợt)

| Bước | Việc | Nghiệm thu không cần game |
|---|---|---|
| A. Hoàn tất đợt 2 | Gom quan hệ và ký ức về Engine, `memory.db` chỉ làm bộ đệm; test giết tiến trình thật khi đang lưu và nâng cấp DB | Test mới fail trên mã cũ; dữ liệu đọc từ một nguồn |
| B. Hoàn tất đợt 3, kinh tế | Tiền quán, lương, tồn kho, mua, thuê, bán thành giao dịch sổ cái; quán chơi không menu: nhập hàng ở kho, thuê người ở quầy, thu tiền ở máy tính tiền | Đối soát lệch 0 sau chuỗi giao dịch quán |
| C. Đợt 3, UIS | Bộ sinh danh mục: quét bản đồ offline thành file catalog (nhà, cửa, loại công trình); nạp toàn bộ IPL có sẵn; gán Template theo loại nhà; Modular lắp từ đồ vật game cho mặt bằng trống; Activity Anchor cho từng mẫu | Báo cáo phủ: số công trình theo lớp; NPC tìm được đường tới anchor trong mô phỏng |
| D. Đợt 3, NPC | NPC thường trên phố được Engine cấp vòng đời nhẹ khi tới gần người chơi; cư dân đi bộ qua cửa theo lịch | Test Engine với vài trăm NPC |
| E. Đợt 4 | Bài tự kiểm; Launcher 7 trạng thái, đổi tên file, sao lưu định kỳ; đo FPS | Bộ chấm đạt trên log mẫu |
| F. Giao bản | Phát hành một bản, bạn chạy bài tự kiểm, gửi log; sửa tối đa 3 vòng | Bộ chấm ≥ 85 trên log của bạn |

Các bản phát hành trung gian vẫn lên GitHub để giữ nút Cập nhật hoạt động, nhưng không cần bạn thử cho tới bước F.

## 3. Rủi ro

- **Catalog "mọi công trình"** là việc nội dung lớn. Bộ quét tìm được vỏ nhà và cửa, nhưng nội thất vẫn là số mẫu có hạn dùng lại. Brief cho phép điều đó (lớp Template), miễn mỗi nhà có định danh và trạng thái riêng.
- **Vòng đời mọi NPC** phải giới hạn theo khoảng cách để giữ FPS. Mức giới hạn chỉ chốt được sau khi đo trên máy bạn.
- Brief ghi GTA V **Enhanced**; dự án và máy test là **Legacy** 1.0.3889.0. Giữ Legacy.

## 4. Cần bạn quyết

1. **Gói MLO (lớp 2):** dùng gói miễn phí có giấy phép cho phép phân phối kèm (bạn chọn hoặc duyệt từng gói), hay để Launcher tải từ trang tác giả mà không đóng gói lại. Tôi không đóng gói tài nguyên của người khác khi giấy phép không cho phép.
2. **Khóa ký APK:** tạo theo README mục "Cập nhật trong app" và đưa vào GitHub Secrets. Chưa có thì mỗi bản APK mới phải gỡ bản cũ.

Hai việc này không chặn các bước A–E.

## 5. Nội thất MLO và tương tác kiểu Big Ambitions (chốt 2026-11-28: phải làm MLO, dùng cách miễn phí)

Mục tiêu: mọi đồ vật trong nhà và quán đều dùng được (ngồi, nằm, nấu, lấy hàng trên kệ, tính tiền, dọn), cả người chơi lẫn NPC.

| Lớp | Cách làm miễn phí | Ai làm | Ghi chú |
|---|---|---|---|
| Khung tương tác | Danh mục loại đồ vật (giường, ghế, bàn, bếp, tủ lạnh, kệ hàng, quầy, máy tính tiền, bồn cầu, vòi sen, tivi, máy tính) gắn hành động, động tác và hệ quả; áp cho mọi nội thất | Tôi | Dùng chung cho người chơi và NPC |
| Modular (ghép từ đồ vật gốc) | Bố cục lưu dạng JSON; script đặt tường, sàn, đồ đạc bằng đồ vật có sẵn của game tại bãi đất trống hoặc vỉa hè. Đi bộ thẳng vào, không dịch chuyển | Tôi | Không phân phối tài nguyên của ai. Thô hơn MLO thật |
| MLO thật | Bố cục JSON → file XML của CodeWalker → gói DLC trong `mods`, Launcher cài và gỡ có sao lưu. Công cụ: CodeWalker, Blender + Sollumz (miễn phí) | Tôi làm đường ống và gói mẫu; mô hình đẹp cần người dựng | Bước chuyển XML sang file game tự động hay phải mở CodeWalker bằng tay: chưa thử |
| MLO cộng đồng | Người dùng tự tải gói miễn phí từ trang tác giả; Launcher nhận ra gói đã cài và gắn Activity Anchor theo danh mục đi kèm | Người dùng tải; tôi viết danh mục | Phần lớn gói miễn phí không cho đăng lại, nên không đóng gói kèm |

Thứ tự: khung tương tác → Modular → đường ống MLO thật và gói mẫu → danh mục cho gói cộng đồng. Theo brief, việc này thuộc đợt 3 (M3), làm sau bước A.
### Cập nhật 2026-11-28: đã có MLO cộng đồng miễn phí

Trên gta5-mods.com có nhiều MLO chơi đơn miễn phí (quán ăn, cà phê, 24/7, bar, motel, căn hộ). Vì vậy đổi thứ tự:
1. **Dùng MLO cộng đồng** cho không gian: đi bộ thẳng qua cửa thật, không dịch chuyển. Người dùng tự tải theo điều khoản từng gói, mod không đóng gói lại.
2. **Khung tương tác (lớp 1)** tự nhận đồ vật theo mẫu mô hình trong bất kỳ nội thất nào (gốc hay MLO), nên không cần danh mục riêng cho từng gói.
3. Lớp 2 (Modular) và lớp 3 (đường ống MLO tự dựng) hoãn, chỉ làm khi gói cộng đồng không đủ.

Cài gói MLO cần thêm dòng vào `dlclist.xml` (trong `mods/update/update.rpf`). Hiện có công cụ cộng đồng làm việc này: Simple Mods Loader, DLCForge. Để Launcher tự làm mà người dùng không mở OpenIV: chưa thử, cần kiểm.
