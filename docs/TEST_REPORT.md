# Đánh giá theo bản hoàn chỉnh của brief (2026-11-28)

## Kết luận

Chưa đạt bản hoàn chỉnh. Trong 30 yêu cầu dưới đây, 6 yêu cầu đã thấy chạy trong game, 11 đã viết nhưng chưa kiểm trong game, 5 mới đạt một phần, 2 đã thử và không đạt, 6 chưa làm.

Bằng chứng trong game chỉ có một phiên: bản 0.3.3, build 1.0.3889.0, khoảng 19:38–19:56. Bản 0.3.4 và 0.3.5 chưa có log nào.

## 1. Đối chiếu yêu cầu

Ký hiệu: **Đạt** (thấy trong game) · **Chưa kiểm** (có mã và test ngoài game) · **Một phần** · **Không đạt** (thử trong game và hỏng) · **Chưa làm**.

| # | Yêu cầu của bản hoàn chỉnh | Trạng thái | Căn cứ |
|---|---|---|---|
| **M0 Đời sống** | | | |
| 1 | Trạng thái người chơi đi vào Engine | Đạt | T09 |
| 2 | NPC có vòng đời, lịch sinh hoạt, ký ức | Chưa kiểm | T10 chưa đo |
| 3 | **Mọi** NPC có vòng đời | Một phần | Chỉ cư dân được đặt tay, tối đa 8 trong game |
| 4 | Lưu bền, thoát rồi vào lại không mất dữ liệu | Chưa kiểm | Test ghi file và cơ sở dữ liệu đạt; chưa có phiên thứ hai |
| **M1 Nền** | | | |
| 5 | Mọi script nạp và chạy ổn định | Không đạt | T04: module Đời sống sập lúc 19:42. Đã sửa ở 0.3.4, chưa chạy lại |
| 6 | Game nối được Engine | Đạt | T06, T07 |
| 7 | Một đồng hồ ngày chung | Chưa kiểm | T08 chưa đo |
| **M2 Xã hội** | | | |
| 8 | Nói tiếng Việt với NPC, NPC trả lời | Không đạt | T11: thiếu thư viện GPU. Đã sửa ở 0.3.4, chưa chạy lại |
| 9 | NPC nhớ cuộc trò chuyện | Chưa kiểm | T12 chưa đo |
| 10 | Quan hệ và trí nhớ chỉ có một nơi ghi | Chưa làm | Vẫn nằm ở 2–3 nơi |
| **M3 Nhà và đồ vật** | | | |
| 11 | Vào và ra nhà qua cửa | Đạt | T13, T14 |
| 12 | Mọi tòa nhà phù hợp đều vào được | Một phần | 4 cửa có sẵn, cửa tự dò và đánh dấu tay; chưa có danh bạ 500–1000 cửa |
| 13 | Nội thất đúng loại nhà, không lỗi hình | Một phần | Bạn thấy đồ chồng nhau và ít mẫu. 0.3.4 tắt đồ sinh thêm |
| 14 | Tương tác đồ vật có cảm giác thật | Một phần | Chỉ một động tác đứng và cộng chỉ số |
| 15 | Cư dân tự vào nhà sinh hoạt | Chưa kiểm | |
| 16 | Mô hình nhà và quán tự dựng | Chưa làm | Đang dùng mô hình của game theo quyết định thử nghiệm |
| **Quán (yêu cầu thêm)** | | | |
| 17 | Vào quán, gọi món tại quầy, không menu | Chưa kiểm | T17, T19 |
| 18 | Thấy người bán và người dọn | Chưa kiểm | T18 |
| 19 | Quản lý quán của mình không qua menu | Chưa làm | Vẫn dùng menu J |
| **M4 Kinh tế** | | | |
| 20 | Sổ cái làm chủ số dư ngân hàng | Một phần | T20 mới thấy bước nhận quyền; chưa thử tắt Engine rồi gửi bù |
| 21 | Event tiền không bị mất | Đạt | T21 |
| 22 | Sổ cái làm chủ tiền quán, tồn kho, lương | Chưa làm | |
| **M5 Host** | | | |
| 23 | Engine Host nhận đúng yêu cầu, một tiến trình | Đạt | T07 (2791 yêu cầu 2xx) |
| 24 | Tương thích game thứ hai (Godot) | Chưa kiểm | CI chạy thử headless, chưa chạy trong phiên này |
| **M6 Launcher** | | | |
| 25 | Cài một lần, không chép file mod bằng tay | Đạt | T01, T02. ScriptHookV, SHVDN và VC++ vẫn do người dùng cài |
| 26 | Nút cập nhật, có quay lui khi bản mới hỏng | Chưa kiểm | Chưa có bản mới hơn để thử trên máy |
| 27 | Cài đè khi bản cũ đang chạy | Chưa kiểm | Bạn gặp lỗi code 5; đã sửa trên máy, chưa phát hành |
| 28 | Tự mở game, xuất gói log | Chưa kiểm | Gói log đã xuất được; tự mở game chưa thử |
| 29 | Tên file đúng đặc tả, `Setup.exe` | Chưa làm | |
| 30 | Bản cập nhật có chữ ký | Chưa làm | Cần khóa ký |

## 2. Kết quả chấm log phiên 0.3.3

Đạt 12 · Không đạt 2 · Chưa đo 7, trên 21 tiêu chí.

| Mã | Kỳ vọng | Kết quả |
|---|---|---|
| T01 T02 T03 | Bộ cài đúng checksum, cài module, nạp đủ 7 script | Đạt |
| T04 | Không script nào bị sập | **Không đạt** |
| T05 T06 T07 | Log module không lỗi, bắt tay, Engine nhận mọi yêu cầu | Đạt |
| T08 | Sang ngày đúng một lần | Chưa đo |
| T09 | Trạng thái người chơi tới Engine | Đạt |
| T10 | Cư dân được đăng ký | Chưa đo |
| T11 | Nói chuyện ra chữ | **Không đạt** |
| T12 | Ký ức hội thoại được lưu | Chưa đo |
| T13 T14 T15 | Vào, ra nhà, nhà có chỗ tương tác | Đạt |
| T16 | Hoàn tất tương tác đồ vật | Chưa đo |
| T17 T18 T19 | Quầy, nhân viên, gọi món (chưa có ở 0.3.3) | Chưa đo |
| T20 T21 | Sổ cái nhận quyền, không mất event | Đạt |

Log không đo được cảm giác chơi. T15 đạt vì mỗi nhà có 5 chỗ tương tác, nhưng ở 0.3.3 một phần là đồ sinh thêm bị chồng nhau (yêu cầu 13).

## 3. Cách đối chứng

- Mỗi tiêu chí chỉ **Đạt** khi có dòng log chứng minh. Không có dòng nào thì là **Chưa đo**.
- `tests/test_log_evaluation.py` giữ ba ca đối chứng cho bộ chấm: log mẫu đúng phải đạt cả 21, log mẫu sai phải không đạt, gói rỗng phải chưa đo cả 21. Riêng T16 không có ca không đạt, vì tương tác không hoàn tất thì không để lại dòng log nào.
- Log module chỉ ghi giờ, không ghi ngày. Bộ chấm chỉ lấy các dòng của phiên mới nhất.
- So hai phiên bản: dùng gói 0.3.3 làm bản đối chứng. Báo cáo sẽ nêu tiêu chí hỏng thêm và tiêu chí đã sửa được.

```powershell
python tools\evaluate_logs.py <goi-moi.zip> --baseline <goi-0.3.3.zip> --output ket-qua.md
```

## 4. Kịch bản test lần tới (bản 0.3.5)

Chơi liền một phiên, theo thứ tự, rồi bấm "Xuất nhật ký".

| Bước | Làm | Đo |
|---|---|---|
| 1 | Mở Launcher, bấm "Kiểm tra & khởi động", mở game | T01–T03, T06 |
| 2 | Mở J, xem kho và quán, chơi 10 phút. J, F7, H vẫn phải ăn | T04, T05 |
| 3 | Trong J, đặt một người làm cư dân | T10 |
| 4 | Đến gần cư dân đó, giữ N nói một câu, chờ trả lời | T11, T12 |
| 5 | F7 tới cửa hàng 24/7, vào trong, gọi một món và chờ dùng xong | T17–T19 |
| 6 | Vào một nhà, dùng một đồ vật đến khi báo "Xong", rồi ra | T13–T16 |
| 7 | Ngủ hoặc chơi qua nửa đêm | T08 |
| 8 | Gửi tiền ở ATM. Thoát Launcher, gửi thêm lần nữa, mở lại Launcher, đợi 30 giây | T20, T21, yêu cầu 20 |
| 9 | Thoát game, vào lại, so số dư, cư dân và nhà đã đánh dấu | Yêu cầu 4 |

Bước 8 và 9 cần bạn ghi lại số dư trước và sau. Bộ chấm chưa tự so được hai số này.

## 5. Chấm theo ma trận nghiệm thu của brief V2 (ngưỡng 85/100)

Brief đòi bằng chứng bắt buộc cho từng nhóm. Chưa nhóm nào đủ bằng chứng, nên **điểm chấm chặt hiện là 0/100**. Mã của nhiều nhóm đã có, nhưng chưa được chứng minh trong game.

| Nhóm | Trọng số | Đã có bằng chứng | Còn thiếu để được tính điểm |
|---|---|---|---|
| M0 Vòng đời con người | 20% | Chỉ số người chơi tới Engine | Log người chơi **và NPC** đủ thể chất, tinh thần, lịch, tự chủ. NPC thường trên phố chưa có vòng đời |
| M3 Tương tác vật lý, không gian sống | 15% | Vào ra nội thất (0.3.3) | NPC vào ra cửa, dùng vật thể có ngữ cảnh; MLO quán, nhà trọ chưa có |
| M2 Xã hội, ký ức | 15% | Không | Hội thoại, quan hệ, ký ức tạo hệ quả ở phiên sau |
| M4 Kinh tế, sổ cái | 15% | Sổ cái nhận quyền ngân hàng | Đối soát số dư qua nhiều phiên; 1.000 giao dịch ngẫu nhiên; tiền quán, lương, tồn kho |
| M1 Thời gian, lưu liên tục | 10% | Bắt tay Engine | Qua ngày, thoát vào lại không reset, không lặp sự kiện |
| M5 Tương thích, ranh giới Engine | 10% | Engine nhận yêu cầu | Chạy ổn định một phiên dài không sập (0.3.3 có sập) |
| M6 Launcher, cập nhật, quay lui | 5% | Cài mới, nhận phiên bản game | Cập nhật checksum, quay lui, giữ save trên máy thật |
| Ổn định, hiệu năng, log | 10% | Xuất được gói log | Chạy liên tục nhiều NPC, đo FPS |

Phản hồi sau phiên 0.3.5: nội thất hết chồng chéo, menu J hoạt động, còn rơi tự do khi dịch chuyển, bấm H ở một cửa bị màn đen. Người chơi không muốn chơi bằng menu, đúng với mục 1 của brief. Hai lỗi dịch chuyển được sửa trong 0.3.6, chưa kiểm trong game.

## 6. Cập nhật theo Master Brief V4.0

Ma trận và ngưỡng 85/100 giữ như mục 5. Nhóm M3 nay đòi đủ 4 lớp UIS và NPC đi qua cửa tới Activity Anchor. **Điểm theo bằng chứng runtime vẫn là 0/100.**

### Tiêu chí nghiệm thu từng đợt

| Đợt | Tiêu chí của brief | Trạng thái | Bằng chứng |
|---|---|---|---|
| 1 | Test fail trên mã cũ, pass sau sửa; toàn bộ test xanh | Đạt (không cần game) | Đợt 1 và CI #272; số test hiện tại xem CI |
| 2 | Tắt ngang khi đang lưu và nâng cấp DB không hỏng file, không mất dữ liệu | Một phần | Có test migration bị cắt giữa chừng và file lưu bị cắt; chưa có test giết tiến trình thật |
| 2 | Quan hệ, ký ức chỉ một nguồn | Chưa làm | Vẫn 2–3 nơi |
| 3 | 1.000 giao dịch ngẫu nhiên lệch 0; trùng ID khác nội dung bị từ chối | Đạt (không cần game) | `engine/tests/test_ledger_stress.py` |
| 3 | Mở 2 host bị chặn | Đạt (không cần game) | `test_phase2_regressions.py` |
| 3 | Catalog phủ các lớp IPL/Template/MLO; NPC tới đúng Activity Anchor | Chưa đạt | Chỉ có 26 mẫu và cửa dò tại chỗ; chưa có bộ sinh danh mục, MLO, Modular |
| 4 | Chạy trong GTA V, 85/100, rollback và giữ save | Chưa đạt | Mục 5 |

Theo brief, đợt 2 chưa nghiệm thu xong (nguồn quan hệ và ký ức). Việc đã làm ở đợt 3 và 4 được giữ lại; phần mới tiếp theo quay về đợt 2.

### Điểm brief V4 khác thực tế dự án

- Brief ghi môi trường GTA V **Enhanced**. Quyết định ngày 2026-11-28 và máy test là **Legacy** build 1.0.3889.0. Mã đang theo Legacy.
- Brief ghi tiến độ 35–40% tại commit 16477fb; các lỗi E1–E8, B1, G1–G3 trong bảng đã sửa từ đó (xem audit mục 6–13).
- Brief nhắc UIS Godot mới chỉ chạy trên adapter tham chiếu: phần chuyển sang C# mới có vào/ra cửa và cư dân dùng đồ vật, chưa kiểm trong game.

