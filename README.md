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

Chép `game\GtaVietLife.cs`, `game\GtaVietLife.ini` (và thư mục `game\GtaVietLifeIcons`) vào thư mục `scripts` trong thư mục game (tạo mới nếu chưa có).

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

### Gói Đời sống 1
- **Cư dân & hàng xóm** (menu `J` > Cư dân & hàng xóm): tối đa 8 người, có tên, lịch cố định
  (6-7h quanh nhà, 7-11h làm, 11-13h ăn trưa gần chỗ làm, 13-17h làm, 17-19h đi dạo, tối ở nhà, 23-6h ngủ trong nhà).
  Được lưu qua các lần chơi: Sếp đi xa thì họ "khuất", tới gần thì hiện lại đúng chỗ theo giờ. Khi nói chuyện (phím N),
  cư dân dùng trí nhớ cố định (`res_<id>`), giữ đúng tên và biết mình đang làm gì (file `GtaVietLifeResidents.txt`).
- **Nhịp phố theo giờ**: khuya vắng người/xe, giờ cao điểm 7-9h và 17-19h đông, trưa đông người đi ăn. Bật/tắt trong menu Cư dân.
- **Vệ sinh cá nhân** (thanh thứ 4 trên HUD): giảm khoảng 50%/ngày game, làm ca hoặc ngủ đường giảm thêm. Dưới 35% NPC thấy Sếp lôi thôi, có mùi.
  Chỉ tính khi đang chơi (tắt game không giảm).
- **Dịch vụ** (menu > Dịch vụ): tắm (ở nhà miễn phí, nhà nghỉ $10), rửa xe (tiệm Strawberry $15, thợ tới tận nơi $40),
  gọi cứu hộ sửa xe tại chỗ (giá theo độ hỏng), ngủ nhà nghỉ $60/đêm (tắm + ngủ 8 tiếng, không bị cướp).
  Toạ độ tiệm rửa xe và nhà nghỉ là ước lượng, cần kiểm tra trong game.

### Đời sống 2 (GtaVietLifeDoiSong.cs)
Chép `game/GtaVietLifeDoiSong.cs` và `GtaVietLifeDoiSong.ini` vào `scripts`. Phím `Y` mở menu (L là menu Kinh tế). Mỗi phần bật/tắt được trong ini mục `[Modules]`.
Mọi chỉ số chỉ chạy khi đang chơi (tắt game thì không ốm thêm, đồ ăn không hỏng, hoá đơn không tới hạn).
- **Căng thẳng 0-100**: tăng khi bị truy nã, máu thấp, đói khát, thức lâu, bẩn, ốm. Giảm khi ngủ, ở nhà, cuối tuần, giải trí. Trên 90% thì nhanh kiệt sức, màn hình rung. NPC thấy Sếp cộc, dễ cáu.
- **Thời tiết**: dầm mưa hoặc đi xe máy dưới mưa thì người ướt dần, ướt lâu dễ bị cảm. Nắng gắt buổi trưa thì khát nhanh.
- **Bệnh và chấn thương**: cảm (2 ngày, không chạy nhanh), ngộ độc khi ăn quán (3,5%, bẩn thì 8%) hoặc ăn đồ hỏng trong tủ lạnh, gãy chân khi tai nạn xe hoặc ngã mạnh (đi tập tễnh tới khi bó bột).
  Chữa ở bệnh viện (Pillbox, Mount Zonah, Sandy, Paleto) hoặc đặt thuốc ship tận nơi. Viện phí trả qua `GVL.Pay` (nhóm `medical`).
- **Say rượu**: uống ở bar thì dáng đi loạng choạng, màn hình nhoè, lái xe gần cảnh sát dễ bị truy nã.
- **Thức quá lâu**: trên 20 tiếng thì mắt mờ, trên 26 tiếng có thể ngất xỉu.
- **Nhà cửa**: hoá đơn điện nước + internet mỗi 7 ngày game, tự trừ nếu đủ tiền. Nợ quá 3 ngày thì bị cắt điện: trong nhà tắt đèn, tủ lạnh hỏng nhanh gấp 3.
  Tủ lạnh có hạn dùng từng món. Đi chợ ở cửa hàng 24/7 hoặc đặt ship (+$5/món). Bấm E ở tủ lạnh thì tự nấu món sắp hết hạn.
- **Giải trí**: bar, rạp phim, gym (phát `life.gym` để file Cá nhân tăng cơ bắp), biển, công viên, câu cá (có thể câu được cá bỏ tủ lạnh).
- **Sự kiện ngẫu nhiên** (6-12 phút một lần): cướp giật (đuổi, xô ngã tên cướp được thưởng, tăng uy tín), người ngã quỵ (lại gần bấm `Y` để sơ cứu), cháy, mất điện cả khu ban đêm.
- **Bản tin radio** lúc 7h và 18h (hoặc chọn trong menu): cầu nối `POST /news` nhờ AI tổng hợp sự kiện gần đây thành bản tin có giọng đọc.
- Sự kiện phát lên bus: `life.sick` (kind), `life.injury`, `life.hospital` (amount), `life.bill` (amount, paid), `life.powercut`, `life.eat` (src, food, spoiled),
  `life.leisure` / `life.gym` (minutes, place), `life.event` (kind = snatch/help/fire/blackout).
- Toạ độ bar, rạp, gym, biển, cửa hàng 24/7, bệnh viện là ước lượng, cần kiểm tra trong game.

## Cá nhân (GtaVietLifePersonal.cs)
Chép `game/GtaVietLifePersonal.cs` (và `GtaVietLifePersonal.ini` nếu muốn đổi phím) vào `scripts`. Phím `I` mở menu Cá nhân, `O` ăn/uống nhanh từ túi.

- **Hồ sơ**: đặt tên, chọn quê. Tên, quê, bộ đồ đang mặc và kỹ năng giỏi được gửi cho cầu nối để NPC gọi đúng tên và nhận xét đúng.
- **Kỹ năng 0-100** tự tăng khi chơi: Thể lực (chạy, bơi, đạp xe → đói/khát/mệt chậm hơn tới 30%), Lái xe, Bắn súng, Sức mạnh (ghi vào chỉ số gốc của Michael/Franklin/Trevor, chỉ nâng không hạ), Giao tiếp (giữ N nói với người gần → mua đồ rẻ hơn tới 15%).
- **Túi đồ**: bánh mì, cơm hộp, phở, nước suối, trà đá, cà phê sữa đá, nước tăng lực, băng cứu thương. Mua ship tận nơi (+$3/đơn). Túi 10 món, ba lô $250 lên 25 món.
- **Trang phục**: lưu 4 bộ (Đi làm, Đi chơi, Thể thao, Lịch sự) từ đồ đang mặc, đổi nhanh.
- **Garage xe riêng**: ghi tối đa 6 xe (giữ màu, biển số), gọi xe tới chỗ Sếp ($50), chấm xanh trên bản đồ.
- **Sổ thu chi**: thu/chi trong ngày và 7 ngày gần nhất của nhân vật đang chơi.
- Đói/khát/năng lượng chỉ giảm khi đang chơi, không tính lúc game tắt.
- Hai file nói với nhau qua AppDomain data `GVL.*` (cùng một AppDomain SHVDN). Thiếu file Đời sống thì đồ ăn trong túi không cộng được no/khát.

## Mo hinh kinh doanh (GT / MT / B2C / B2B)

- **Loai hinh** (chon khi mo quan): Quan nuoc via he, Tap hoa, Quan an (kenh GT, von nho); Cua hang tien loi, Sieu thi mini, Nha hang/ca phe chuoi (kenh MT, von lon, dong khach, nop VAT, can mat bang thue tu "Thue ki-ot").
- **San pham**: danh muc F&B va FMCG co gia von / gia ban rieng; moi quan chi ban nhom mat hang cua loai hinh. Loi nhuan moi ngay = doanh thu - gia von - luong - thue - tien thue mat bang.
- **Nhap hang tu nha phan phoi**: lo 1 / 5 / 10, chiet khau 0 / 7% / 12%, MT duoc them 5%.
- **B2B**: menu Kinh doanh > "Ban si B2B": ky hop dong cung ung cho cua hang/quan khac, giao hang moi ngay tu kho mot quan, gia si thap hon gia le, thu tien sau 5-45 ngay, co rui ro vo no.

- **Dich vu** (kho = vat tu, tinh theo luot; can mat bang thue; khong ban si B2B): Tiem rua xe, Sua xe - thay nhot (lai xe toi tiem cua minh duoc rua/sua mien phi, tru 1 luot vat tu), Tiem toc - goi dau, Giat ui. Mat bang that: tiem rua xe Strawberry / Little Seoul, LS Customs La Mesa, tiem toc Davis / Rockford Hills / Hawick.

## Bat dong san (menu Kinh doanh > Bat dong san)

- 19 toa nha that tren ban do (toa do gan dung): can ho (Eclipse Towers, Tinsel Towers, Del Perro Heights, Richards Majestic, Weazel Plaza, 4 Integrity Way, 3 Alta Street), nha pho (Grove Street, Mirror Park, Kenh Vespucci, Paleto Bay), van phong (Maze Bank Tower, Arcadius, Lombank West, Maze Bank West), nha nghi (Pink Cage Motel, Motor Motel Harmony), kho (cang Elysian, Cypress Flats).
- Gia mua ~180 ngay tien thue day phong, theo khu. Den tan noi (trong 150 m) mua gia goc; mua tu xa qua moi gioi +3%.
- **Dai han**: khach vao dan, o theo hop dong, moi ngay mot so khach het hop dong chuyen di, it khi bung tien. **Ngan han**: gia dem x1.7, so phong co khach ngau nhien moi dem, ton 18% don phong.
- 3 muc gia (thap: lap day nhanh; cao: lau hon), quan ly toa nha $60/ngay (de cho thue hon, it bung tien), sua sang cap 2-3 (+15% gia thue moi cap), bao tri + thue nha dat ~0,11% gia tri/ngay, gia tri bien dong nhe moi ngay, ban lai nhan 92%.
- Offline: dai han thu du, ngan han bang 60%.

## Kinh te: ngan hang, vay, xe (GtaVietLifeEconomy.cs, phim L)

Chep `game/GtaVietLifeEconomy.cs` va `game/GtaVietLifeEconomy.ini` vao `scripts`. File doc lap, tat tung phan trong ini (`[Modules] Bank/Fuel/Wear/Impound`).
- **Ngan hang**: tai khoan tach khoi tien mat. Gui/rut o cay ATM (dung sat cay, rut mat $2) hoac chi nhanh (cham xanh la, mien phi). Lai tiet kiem 0,03%/ngay game. Tra o quay (xang, cuu ho, bai xe) dung tien mat truoc, thieu thi tu quet the.
- **So cai**: moi khoan thu chi cua file nay va cac khoan phat / le phi tu file Ca nhan (qua `GVL.Pay`/`GVL.Earn` cua loi) deu ghi so, xem trong menu. Khoan phai tra ma khong du tien thanh no ngan hang, lai 2%/ngay, tu tru khi tai khoan co tien.
- **Vay** (tai chi nhanh): tin chap theo diem tin dung (300-850), 4 ky; the chap bat dong san toi da 60% gia tri, 8 ky. Tra gop moi 7 ngay game, tu tru tai khoan. Tre 1 ky -35 diem va phat 5%, 3 ky thi bi siet: tai khoan, tien mat, roi phat mai BDS the chap (80% gia), con lai thanh no xau, cam vay 30 ngay. Co tien an thi khong vay duoc.
- **Xang**: moi xe co binh rieng theo loai (xe may 12 L ... xe tai 150 L), chay nhanh ton hon, het xang chet may. Do xang khi dung canh cot bom (bam L). Binh du phong 10 L, goi cuu ho do xang $120.
- **Nhot va may**: nhot het sau ~140 km game, het nhot thi may hong dan. May yeu thi thinh thoang chet may; hong han thi goi xe keo ve gara gan nhat. Thay nhot / sua may o cac gara LS Customs.
- **Do sai**: xe do giua long duong trong thanh pho, Sep di xa qua 3 phut thi bi cau ve bai giu xe Davis (cham do). Lay lai $250 + $25/ngay.
- Bu offline: lai tiet kiem va tra gop van chay luc tat may (toi da 14 ngay game).
- Toa do chi nhanh ngan hang, gara, bai giu xe la gan dung; ATM va cot bom duoc nhan bang mau vat the trong game.

## Chay 24/24 (offline)

Moi 2 phut mod ghi dau thoi gian vao file luu. Khi mo game lai, mod tinh thoi gian da tat (1 ngay game = 48 phut thuc, toi da 14 ngay): quan co nhan vien van ban hang (cham hon luc choi: 60% so khach), van tra luong, thue, tien thue mat bang, giao hang B2B, thu cong no, thu tien thue bat dong san; neu dang ky giao hang thi moi ngay chay 2-5 don (tru tien xang). Ket qua hien thanh mot thong bao tong ket, chi tiet nam trong GtaVietLifeWorld.log (dong "[offline]"). Neu thieu tien tra thue mat bang 3 ngay se mat mat bang nhu luc dang choi.

## Toa nha that

Muc "Tim mat bang" co them cac cua hang that cua game goc (24/7, LTD, Los Santos Customs) danh dau bang bieu tuong cua hang tren ban do. Toa do gan dung: hay doi chieu voi cham tren ban do roi bao lai neu lech.

## Dashboard tren dien thoai (xem va giao dich 24/24)

1. Tren PC chay `start_dashboard.bat` (khong can mo game, khong can bridge AI). Lan dau Windows hoi tuong lua thi chon Allow (mang rieng).
2. Dien thoai cung Wi-Fi mo dia chi in ra (dang `http://192.168.x.x:5588`). Bam vao trang de bat che do man hinh luon sang/toan man hinh; dung "Them vao man hinh chinh" cua Chrome de co icon.
3. Trang hien tien, no, uy tin, loi nhuan 14 ngay, tung quan. Khi game tat, so lieu duoc UOC TINH (7 ngay gan nhat x 60%); so that chot khi mo game.
4. Giao dich tren dien thoai: nhap hang 1/5/10 lo, thue/sa thai, nang cap, rut quy. Lenh xep hang doi trong `GtaVietLifeCmd.txt`; game ap dung ngay khi mo (truoc luc bu offline) hoac trong 10 giay neu dang choi. Thieu tien thi lenh that bai va hien thong bao tren trang.
5. De dien thoai truy cap duoc, bat buoc dat ma PIN: them `"dash_pin": "<ma cua Sep>"` vao `bridge/config.json` (chua dat thi trang chi mo duoc tren chinh may tinh). APK cu hon ban nay can cai lai ban moi. Truy cap tu ngoai nha (4G) can Tailscale hoac tuong tu — chua lam.

### APK cho dien thoai Android
GitHub tu build `GtaDashboard-N.apk` (muc Releases, tag `dash-N`). Cai xong mo len, nhap `http://IP-may-tinh:5588` mot lan. Doi dia chi: giu nut Back. Man hinh luon sang. APK ky debug nen cai thang, can cho phep "cai tu nguon khong ro".

### Xem khi o ngoai (4G, di lam)
Cai Tailscale (mien phi) tren PC va dien thoai, dang nhap cung mot tai khoan. `start_dashboard.bat` se in them dong "qua Tailscale" voi dia chi dang `http://100.x.x.x:5588` — nhap dia chi do vao APK. Nen dat `dash_pin`. PC phai bat va chay `start_dashboard.bat`; luon cho phep cong 5588 trong tuong lua Windows.

## Xem 24/24 ca khi PC tat (Google Apps Script — dung tai khoan Google cua Sep)
1. Vao https://script.google.com > New project > dan noi dung `apps_script/Code.gs`. Doi `PIN` (ma tren dien thoai) va `KEY` (khoa cua PC).
2. Deploy > New deployment > loai Web app > Execute as: Me, Who has access: Anyone > Deploy. Cap quyen khi duoc hoi. Copy link `.../exec`.
3. Tren PC, trong `bridge/config.json` them `"cloud_url": "<link exec>"` va `"cloud_key": "<KEY>"`. Chay `start_dashboard.bat` (chi can bat khi vua choi xong / vua mo game; no tu day so lieu len va keo lenh ve).
4. Mo APK, dan link `.../exec` vao o dia chi, nhap PIN. Xem va giao dich duoc tu bat ky dau, PC tat van duoc.
Khi PC tat: so lieu la UOC TINH tu so lieu cuoi, lenh xep hang cho; bat PC + `start_dashboard.bat` thi lenh duoc keo ve, mo game thi ap dung.
