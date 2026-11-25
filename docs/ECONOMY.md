# Kinh tế GTA Việt Life: bảng giá chuẩn và hệ phí (thread Kinh tế giữ)

Mọi thread lấy giá, phí và quy tắc tiền từ trang này, không tự đặt riêng. Giao diện bus chi tiết: `docs/CORE.md`.

## 1. Cổng tiền (bắt buộc)
- Trả tiền: `GVL.Pay(soTien, nhom, ghiChu)`. Nhận tiền: `GVL.Earn(...)`. Kinh tế đăng ký hai cổng này: trừ/cộng tài khoản trước, rồi tới tiền mặt, ghi sổ cái, phát `ledger.*`.
- Không file nào cộng/trừ thẳng `Game.Player.Money` cho khoản kinh tế, trừ khi bus không có cổng (khi đó dùng tiền mặt như cũ).
- Nhận tiền mặt có ghi sổ: `GVL.EarnCash(...)`. Quy tắc thu của World: kênh GT, giao hàng, làm công, rút quỹ quán thu **tiền mặt** (`GVL.EarnCash`); kênh MT, B2B, bất động sản thu **vào tài khoản** (`GVL.Earn`); mọi khoản chi qua `GVL.Pay`.
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

## 6. Bảng giá chuẩn (Sếp đã chốt 25/11: thống nhất theo đời thực)

**Mốc: 1$ ≈ 5.000đ.** 1 ngày game = 48 phút thực, các khoản "theo ngày" tính theo ngày game.

**Giá theo khu:** giá thực tế = giá chuẩn × `GVL.PriceZone(x,y,z)` = 0,7 + 0,3 × hệ số khu.
- Hệ số khu cũng là hệ số khách và tiền thuê mặt bằng: Trung tâm/Legion/bãi biển 1,5 → giá ×1,15. Vinewood/Rockford 1,4 → ×1,12. Strawberry 1,0 → ×1,0. Davis 0,9 → ×0,97. Sandy/Paleto 0,6 → ×0,88. Grapeseed 0,5 → ×0,85.
- Áp cho mọi mặt hàng: quán của Sếp bán ra, đồ Sếp mua (túi đồ, đi chợ, ăn ngoài, giải trí), xăng.
- Ở xa (ship) thì lấy hệ số chỗ của Sếp.

### Danh mục hàng (giá vốn / giá chuẩn, $)
| F&B | | FMCG | | Dịch vụ | |
|---|---|---|---|---|---|
| Trà đá 1/2 | | Mì gói 1/2 | | Rửa xe máy 1/6 | |
| Cà phê sữa 2/5 | | Nước ngọt lon 2/3 | | Rửa ô tô 4/20 | |
| Nước ép 3/6 | | Nước suối 1/2 | | Thay nhớt 20/30 | |
| Bánh mì 3/6 | | Snack 1/2 | | Vá/bơm lốp 1/4 | |
| Phở bò 7/15 | | Sữa hộp 1/2 | | Cắt tóc 2/10 | |
| Cơm tấm 5/12 | | Dầu gội 15/20 | | Gội đầu 3/12 | |
| Gà rán 7/14 | | Giấy vệ sinh 7/10 | | Giặt sấy 1/4 | |
| Burger 6/12 | | Xà phòng 3/5 | | Ủi đồ 1/2 | |
| Bia hơi 2/4 | | Gạo 5kg 20/25 | | | |
| Bánh ngọt 2/5 | | Dầu ăn 1L 8/10 | | | |

Hệ số loại hình (PriceMul) và bước cấp quán (+27%/cấp) giữ nguyên. Ví dụ quán nước vỉa hè cấp 4 ở trung tâm: trung bình ~$11/phần ≈ 55.000đ.

### Chi phí kinh doanh (nhân 0,3 so với cũ để biên lãi giữ nguyên)
| Khoản | Mới | Cũ |
|---|---|---|
| Vốn mở quán (vỉa hè / ki-ốt / HĐ 30 ngày / mua đứt) | 150 / 300 / 700 / 8.000 | 400 / 800 / 2.000 / 25.000 |
| Tiền thuê mặt bằng/ngày (trước hệ số khu) | 6 / 27 / 18 / 0 | 20 / 90 / 60 / 0 |
| Nâng cấp quán | $800 × cấp | $2.500 × cấp |
| Lương nhân viên | $30/ngày | $100 |
| Thuế khoán GT | $3/ngày | $8 |
| Quản lý toà nhà | $20/ngày | $60 |
| Tiền thuê BĐS/đơn vị/ngày (Base) | ×0,3 (căn hộ ~20, văn phòng ~45, phòng motel ~9, kho ~40) | 30–150 |
| Hệ số sinh lời cho thuê dài hạn (`LongTermYield`) | ×3 nhân riêng vào kết quả `UnitRent()` (không áp vào `EstatePrice`, không áp cho thuê ngắn hạn) — để đầy khách dài hạn đạt ~25–35% lãi một quán MT max cấp cùng vốn | — |

### Sinh hoạt
| Khoản | Mới | Cũ |
|---|---|---|
| Ăn cơm (menu J) / uống nước | $10 / $2 | $15 / $5 |
| Thuê nhà/ngày: trailer / Strawberry / Vinewood / Rockford | 25 / 60 / 135 / 270 | 80 / 200 / 450 / 900 |
| Điện nước internet | $40–70 / 7 ngày | $90–150 |
| Nhà nghỉ / tắm nhà nghỉ | $40 / $5 | $60 / $10 |
| Rửa xe tiệm / thợ tới nơi | $6 (xe máy) – $20 (ô tô) / $30 | $15 / $40 |
| Gọi xe riêng tới (Cá nhân) / ba lô | $15 / $60 | $50 / $250 |
| Taxi mở cửa / tài xế riêng / gọi xe về | $5 / $20 / $50 | $15 / $50 / $150 |

### Thu nhập
| Nguồn | Mới | Cũ |
|---|---|---|
| Giao hàng (online) | 8 + quãng đường/80 → ~$15–27/đơn | 60 + quãng đường/8 → $135–250 |
| Giao hàng offline | 2–5 đơn × $12–22, trừ xăng $2/đơn | $75–145, xăng $8 |
| Ca thợ phụ LS Customs (4 giờ) | $40 | $130 |

### Xe và ngân hàng (file Kinh tế, đã áp dụng)
Xăng $1/L × hệ số khu. Can 10 L $10 + xăng. Cứu hộ $40, kéo xe $60 + $5/100 m. Thay nhớt $30, sửa máy $10 + mức hỏng × 0,2. Bãi giữ xe $80 + $10/ngày. Rút ATM mất $1. Hạn mức vay tín chấp (điểm − 500) × $20, khoản vay mẫu $300 / $1.000 / $3.000, thế chấp $3.000 / $10.000 / $30.000.

### Phạt và giấy tờ (giữ nguyên, đã sát thực tế)
Phạt nguội $150/sao. Lệ phí giấy tờ $20–500. Tiền bảo lãnh 400 + 300 × sao.
