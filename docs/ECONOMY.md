# Kinh tế GTA Việt Life: bảng giá chuẩn và hệ phí (thread Kinh tế giữ)

Mọi thread lấy giá, phí và quy tắc tiền từ trang này, không tự đặt riêng. Giao diện bus chi tiết: `docs/CORE.md`.

## 1. Cổng tiền (bắt buộc)
- Trả tiền: `GVL.Pay(soTien, nhom, ghiChu)`. Nhận tiền: `GVL.Earn(...)`. Kinh tế đăng ký hai cổng này: trừ/cộng tài khoản trước, rồi tới tiền mặt, ghi sổ cái, phát `ledger.*`.
- Không file nào cộng/trừ thẳng `Game.Player.Money` cho khoản kinh tế, trừ khi bus không có cổng (khi đó dùng tiền mặt như cũ).
- Riêng tiền mặt tại quầy (rút ATM, nộp tiền) do Kinh tế tự xử lý.

## 2. Một sổ nợ duy nhất
- Khoản phải trả mà không đủ tiền: gọi `GVL.AddDebt(soTien, nhom, ghiChu)` (`Action<int,string,string>`). Nợ ngân hàng lãi **2%/ngày game**, tự trừ khi tài khoản có tiền, làm giảm điểm tín dụng.
- Bỏ các biến `debt` riêng trong file khác. Nợ cũ đang lưu thì chuyển một lần vào `GVL.AddDebt` lúc nạp game rồi đặt về 0.
- Tổng nợ đọc ở `GVL.Debt` (int).

## 3. Một bãi giữ xe duy nhất
- Bãi Davis (Kinh tế). Phí: `GVL.Fee.Impound` = **$250** + `GVL.Fee.ImpoundDay` = **$25/ngày game**.
- Cảnh sát giữ xe (Cá nhân, `legal.stop result=impound`): gọi `GVL.ImpoundCar(hash, mau1, mau2, bien)` (`Action<int,int,int,string>`). Sếp lấy xe ở bãi Davis qua menu Kinh tế (phím L).
- Đỗ sai bị cẩu: Kinh tế tự xử lý.

## 4. Phạt
- **Bị bắt**: chỉ tính tiền bảo lãnh (Cá nhân). World KHÔNG phạt thêm $/sao khi truy nã kết thúc vì bị bắt.
- **Thoát truy nã không bị bắt**: phạt nguội $150/sao (World), qua `GVL.Pay` nhóm `fine`, thiếu thì `GVL.AddDebt`.
- Giấy tờ, va chạm, toà án: Cá nhân, qua `GVL.Pay`.

## 5. Nhóm tiền (cat) dùng chung
`food`, `shop`, `bills`, `leisure`, `medical`, `car`, `xang`, `sua xe`, `cuu ho`, `impound`, `fine`, `court`, `license`, `bail`, `barber`, `gift`, `date`, `pet`, `rent`, `wage`, `tax`, `sales`, `b2b`, `estate`, `delivery`, `vay`, `lai`, `no`.

## 6. Bảng giá (chờ Sếp chốt mốc)
Đề xuất mốc **1$ ≈ 5.000đ** (giá Cá nhân/Đời sống đã theo mốc này). 1 ngày game = 48 phút thực.

| Món / khoản | Đề xuất | Hiện tại |
|---|---|---|
| Trà đá | $2 | Cá nhân $2, quán bán $10 |
| Cà phê sữa | $5 | Cá nhân $5, quán bán $22 |
| Bánh mì | $6 | Cá nhân $6, quán bán $18 |
| Cơm | $10–12 | Cá nhân $12, menu J $15, quán bán $38 |
| Phở | $15 | Cá nhân $15, quán bán $36 |
| Xăng | $2/L | $2/L |
| Điện nước internet | $90–150 / 7 ngày | như cũ |

Thu nhập đề xuất: giao hàng $20–40/đơn (đang $135–250), ca thợ phụ 4 giờ $60 (đang $130), lương nhân viên quán $50/ngày (đang $100). Giá bán của quán = giá tiêu dùng × 1,0–1,4 theo cấp; tiền thuê mặt bằng, vốn mở quán, nâng cấp hạ cùng tỉ lệ để biên lãi giữ nguyên.

Mục 6 chỉ áp dụng sau khi Sếp chốt, vì PR #6 vừa đưa giá quán vỉa hè cấp 4 về khoảng $40 theo ý Sếp.
