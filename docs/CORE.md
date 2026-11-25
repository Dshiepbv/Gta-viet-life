# GTA Việt Life: lõi dùng chung (interface cho mọi module)

Thread **Cá nhân** sở hữu tài liệu này và `game/GtaVietLifeCore.cs`. Thread **Đời sống** và **Kinh tế** chỉ cần đọc trang này để cắm module của mình vào, không cần sửa file của nhau.

## Nguyên tắc

1. **Không viết lại.** Mỗi module là một file `.cs` riêng trong `scripts`, cắm vào code đang chạy.
2. **Không đổi định dạng save cũ.** Mỗi module có file `.sav` riêng (dạng `key=value`, số thập phân luôn dùng `CultureInfo.InvariantCulture`, vì máy Sếp dùng dấu phẩy).
3. **Thiếu lõi vẫn chạy.** Mọi lệnh đọc bus phải chịu được giá trị `null`.
4. **Mỗi module bật/tắt được** trong `GtaVietLifeCore.ini` mục `[Modules]`.
5. **SQLite chỉ ở cầu nối Python** (`memory.db`, đã có sẵn). Trong game chỉ dùng file `key=value`.

## Bus: AppDomain data

Mọi script SHVDN chạy chung một AppDomain, nên trao đổi qua `AppDomain.CurrentDomain.GetData/SetData`. Chỉ dùng kiểu có sẵn của .NET (`bool`, `int`, `long`, `float[]`, `string[]`, `List<string>`, `Func<>`, `Action<>`), vì mỗi file `.cs` được biên dịch thành một assembly riêng.

| Khoá | Kiểu | Ai ghi | Ý nghĩa |
|---|---|---|---|
| `GVL.Clock` | `long[6]` | Core | {giây thật epoch, ngày game, phút trong ngày 0-1439, thứ 0=T2..6=CN, cuối tuần 0/1, giờ} |
| `GVL.Events` / `GVL.EventSeq` | `List<string>` / `long` | ai cũng ghi | Bus sự kiện, xem bên dưới |
| `GVL.Mod.<Ten>` | `bool` | Core | `Personal`, `Legal`, `Body`, `Social`, `Life`, `Economy`. `null` nghĩa là bật |
| `GVL.Pay` | `Func<int,string,string,bool>` | Core (mặc định), **Kinh tế ghi đè** | Trả tiền `(soTien, nhom, ghiChu)` trả `true` nếu trả được |
| `GVL.Earn` | `Action<int,string,string>` | Core (mặc định), Kinh tế ghi đè | Nhận tiền |
| `GVL.Needs` | `float[3]` | Đời sống | {đói, khát, năng lượng} 0-100 |
| `GVL.NeedsAdd` | `float[3]` | ai cũng cộng dồn | World đọc mỗi tick, cộng vào nhu cầu rồi đặt `null` |
| `GVL.DrainMul` | `float` | Cá nhân | Hệ số tốc độ giảm nhu cầu (thể lực, cân nặng) |
| `GVL.WorldMenu` / `GVL.PersonalMenu` | `bool` | Đời sống / Cá nhân | Menu đang mở, để script kia bỏ qua phím (Kinh tế dùng `GVL.EconMenu`). Mở menu này thì menu kia tự đóng |
| `GVL.Profile` | `string[]` | Cá nhân | {tên, quê, tuổi, đồ đang mặc} |
| `GVL.Body` | `float[5]` | Cá nhân | {cân nặng kg, cơ bắp 0-100, thể lực 0-100, số ngày chưa cắt tóc, số ngày chưa cạo râu} |
| `GVL.Licenses` | `string` | Cá nhân | Danh sách giấy tờ, ngăn bằng dấu phẩy: `ID,A1,B2,C,GUN` |
| `GVL.Record` | `int` | Cá nhân | Số tiền án. Kinh tế dùng để từ chối việc nhà nước hoặc vay vốn khi `> 0` |
| `GVL.Jailed` | `bool` | Cá nhân | Đang ngồi tù. Các module tạm dừng thu nhập, việc làm |
| `GVL.Spouse` | `string` | Cá nhân | Khoá NPC vợ/chồng (`npc_...`), rỗng nếu độc thân |
| `GVL.NearestNamed` | `string[2]` | Nói chuyện | {khoá, tên} của NPC đã ghi nhớ gần nhất trong 5 m |
| `GVL.PhoneCall` | `string` | Cá nhân | Khoá NPC đang gọi điện. Phím N nói vào điện thoại thay vì người đứng cạnh |
| `GVL.Hygiene` | `float` | Đời sống | Vệ sinh 0-100 |
| `GVL.Home` | `bool[2]` | Đời sống | {có nhà, đang ở trong nhà} |
| `GVL.Slept` | `int` | Đời sống | Đếm số lần ngủ |
| `GVL.HygAdd` / `GVL.RepAdd` | `float` / `int` | ai cũng cộng dồn | Đời sống đọc rồi xoá (cộng vệ sinh, uy tín) |
| `GVL.PayOwner` | `string` | Core / Kinh tế | `core` hoặc `economy`: ai đang giữ cổng trả tiền |
| `GVL.Bank` | `int` | Kinh tế | Số dư tài khoản ngân hàng |
| `GVL.AddDebt` | `Action<int,string,string>` | Kinh tế | Ghi nợ `(soTien, nhom, ghiChu)` khi không trả nổi phạt/án phí. Cá nhân gọi, không tự ghi tiền án thay nợ nếu khoá này có |
| `GVL.ImpoundCar` | `Action<int,int,int,string>` | Kinh tế | Giao xe vào bãi Davis `(hashModel, mau1, mau2, bienSo)`. Cá nhân gọi khi CSGT giữ xe; lấy xe ở menu L (phí `GVL.Fee.Impound` + `GVL.Fee.ImpoundDay`/ngày). Thiếu khoá này thì Cá nhân tự giữ xe 1 ngày, lấy lại $200 ở menu Giấy tờ |
| `GVL.Debt` | `int` | Kinh tế | Tổng nợ đang ghi |
| `GVL.Fee.Impound` / `GVL.Fee.ImpoundDay` | `int` | Kinh tế | Phí lấy xe ($250) và phí mỗi ngày ($25) |
| `GVL.Credit` | `int` | Kinh tế | Điểm tín dụng 300-850 |
| `GVL.EconMenu` | `bool` | Kinh tế | Menu Kinh tế (phím L) đang mở |
| `GVL.Assets` | `int` | Đời sống | Tổng giá trị bất động sản |
| `GVL.Seize` | `int` | Kinh tế | Giá trị cần siết nợ; Đời sống phát mại bất động sản thế chấp |

### Trả tiền và sổ cái

Không ai trừ `Game.Player.Money` trực tiếp cho các khoản có tính chất kinh tế (phạt, viện phí, hoá đơn). Hãy gọi:

```csharp
static bool Pay(int amount, string cat, string note)
{
    Func<int, string, string, bool> f = AppDomain.CurrentDomain.GetData("GVL.Pay") as Func<int, string, string, bool>;
    if (f != null) return f(amount, cat, note);
    if (Game.Player.Money < amount) return false;          // khong co Core: tru tien mat
    Game.Player.Money = Game.Player.Money - amount; return true;
}
```

Nhóm (`cat`) đang dùng: `fine` (phạt), `court` (bồi thường, án phí), `license` (lệ phí giấy tờ), `bail` (tiền bảo lãnh), `impound` (bãi giữ xe), `gift`, `date`, `pet`, `barber`, `shop`, `car`. Kinh tế (`GtaVietLifeEconomy.cs`, phím L) đăng ký `GVL.Pay` của mình: trừ ngân hàng trước rồi tới tiền mặt, không đủ thì trả `false` và không trừ gì; mọi khoản ghi sổ cái kèm `acct=bank|cash`. Nhóm Kinh tế dùng thêm: `xang`, `sua xe`, `cuu ho`, `vay`, `lai`, `no`, `siet no`, `gui`, `rut`. Khi `Pay` trả `false`, module gọi tự quyết (Cá nhân: ghi tiền án hoặc chuyển thành vụ kiện). Bản mặc định của Core trừ tiền mặt và phát `ledger.expense`.

## Bus sự kiện

Mỗi dòng có dạng `seq \t loai \t k=v;k=v`. Giá trị không được chứa `;`, `=` hoặc tab. Chép hai hàm này vào module:

```csharp
static void Emit(string type, string data)
{
    AppDomain d = AppDomain.CurrentDomain;
    List<string> list = d.GetData("GVL.Events") as List<string>;
    if (list == null) { list = new List<string>(); d.SetData("GVL.Events", list); }
    lock (list)
    {
        object o = d.GetData("GVL.EventSeq");
        long seq = (o is long ? (long)o : 0) + 1;
        d.SetData("GVL.EventSeq", seq);
        list.Add(seq + "\t" + type + "\t" + (data ?? ""));
    }
}

// goi moi tick; last = seq cuoi da doc (luu trong field cua module)
static List<string[]> Poll(ref long last)
{
    List<string[]> res = new List<string[]>();
    List<string> list = AppDomain.CurrentDomain.GetData("GVL.Events") as List<string>;
    if (list == null) return res;
    lock (list)
        foreach (string line in list)
        {
            string[] p = line.Split('\t');
            long seq;
            if (p.Length == 3 && long.TryParse(p[0], out seq) && seq > last) { res.Add(p); last = seq; }
        }
    return res;
}
```

Core giữ 300 dòng cuối, ghi `GtaVietLifeEvents.log` và gửi lô sự kiện tới cầu nối `POST /gvl_event`. Cầu nối dùng chúng làm tin đồn và bảng tin mạng xã hội trong điện thoại.

### Danh mục sự kiện

| Loại | Dữ liệu | Ai phát |
|---|---|---|
| `core.day` | `day`, `weekday` | Core, lúc qua nửa đêm |
| `ledger.expense` / `ledger.income` | `amount`, `cat`, `note` | Core (Pay/Earn mặc định) hoặc Kinh tế |
| `legal.license` | `type` (`ID`/`A1`/`B2`/`C`/`GUN`) | Cá nhân |
| `legal.fine` | `amount`, `reason`, `paid` 0/1 | Cá nhân |
| `legal.stop` | `result` (`ok`/`fine`/`impound`) | Cá nhân (cảnh sát kiểm tra giấy tờ) |
| `legal.arrest` / `legal.jail` / `legal.release` | `stars` / `minutes` / — | Cá nhân |
| `legal.crash` / `legal.court` | `amount`, `victim` | Cá nhân (va chạm xe NPC, toà xử) |
| `body.haircut` / `body.weight` / `body.look` | `kind` / `kg` / `kind=custom` | Cá nhân |
| `social.affinity` | `key`, `name`, `value`, `level` | Cá nhân (khi đổi cấp quan hệ) |
| `social.date` / `social.marry` / `social.breakup` | `key`, `name` | Cá nhân |
| `social.gift` / `social.pet` / `social.sms` / `social.call` | `key`, `name`, ... | Cá nhân |
| `econ.loan` / `econ.repay` / `econ.late` / `econ.foreclose` | `amount`,`kind` / `amount`,`left` / `late`,`penalty` / `amount`,`kind` | Kinh tế |
| `econ.impound` / `econ.release` | `plate`,`fee` / `fee` | Kinh tế (bãi giữ xe Davis) |
| `econ.fuel` / `econ.repair` | `liters`,`cost` / `kind`,`cost` | Kinh tế |
| `life.sick` / `life.injury` / `life.hospital` | `kind` (`cold`/`poison`) / — / `amount` | Đời sống (`GtaVietLifeDoiSong.cs`, phím Y) |
| `life.bill` / `life.powercut` | `amount`, `paid` / — | Đời sống |
| `life.eat` | `src`, `food`, `spoiled` | Đời sống |
| `life.leisure` / `life.gym` | `minutes`, `place` | Đời sống. Cá nhân nghe `life.gym` để tăng cơ bắp |
| `life.event` | `kind` (`snatch`/`help`/`fire`/`blackout`) | Đời sống |
| `econ.*` | (Kinh tế tự định nghĩa: lương, vay, siết nợ, cửa hàng) | Kinh tế |

Đời sống và Kinh tế thêm loại mới thì ghi tiếp vào bảng này trong PR của mình, đặt tiền tố `life.` hoặc `econ.`.

## Lược đồ dữ liệu

### Nhân vật (game, `GtaVietLifePersonal.sav`)
`name`, `hometown`, `age`, `skill0..4` (thể lực, lái xe, bắn súng, sức mạnh, giao tiếp: 0-100), `item=id,soLuong`, `backpack`, `outfit0..3`, `wearing`, `car=hash,mau1,mau2,bien,ten,giuXe`, `kg`, `muscle`, `hairDays`, `beardDays`, `custom=` (nhân vật tuỳ biến freemode: model cũ, cha/mẹ, tóc, mắt, râu), `face=` (20 thanh khuôn mặt), `lic=ID,A1,...`, `record`, `jailUntil`, `pending=...` (vụ kiện chờ xử), `pet=model,ten`.

### NPC (cầu nối, `memory.db` SQLite, bảng `npc`)
| Cột | Ý nghĩa |
|---|---|
| `key` | `npc_<ticks>` cho NPC đã ghi nhớ (K), `tmp_<handle>` cho người qua đường (xoá khi khởi động) |
| `name`, `gender`, `job`, `trait`, `pitch`, `rate` | Danh tính và giọng nói |
| `affinity` | Thiện cảm **-100..+100** (bản cũ -10..10 được nhân 10 khi nâng cấp) |
| `status` | `''`, `hen_ho`, `vo_chong`, `chia_tay` |
| `summary` | Tóm tắt trí nhớ dài hạn, LLM viết lại sau mỗi 10 câu |
| `msgs`, `last_talk` | Số câu đã nói, thời điểm nói gần nhất |
| `phone` | 1 nếu đã có số điện thoại (thiện cảm ≥ 10 hoặc đã ghi nhớ) |

Cấp quan hệ theo thiện cảm: ≤ -40 Kẻ thù · ≤ -15 Không ưa · < 10 Người lạ · < 30 Người quen · < 60 Bạn bè · ≥ 60 Bạn thân. Hẹn hò cần ≥ 60, kết hôn cần ≥ 85 và đang hẹn hò.

Trong game, NPC đã ghi nhớ được lưu ở `GtaVietLifeNamed.sav` (khoá, model, vị trí) và xuất hiện lại gần chỗ cũ khi Sếp tới gần.

### HTTP cầu nối (127.0.0.1:5577)
Có sẵn: `/ping`, `/start`, `/stop`, `/state`, `/event`, `/banter`.
Thêm cho Cá nhân: `POST /gvl_event` (lô sự kiện), `GET /contacts`, `POST /sms` (`key`, `text`, trả lời 1 dòng), `POST /relation` (`key`, `action` = `date`/`marry`/`breakup`/`gift`, `value`), `GET /feed` (bảng tin mạng xã hội), `GET /npc?key=`.

## Phím của các module

| Phím | Module |
|---|---|
| N (giữ), K | Nói chuyện (`GtaVietLife.cs`) |
| J, U, E, F8, F9 | Đời sống cũ (`GtaVietLifeWorld.cs`) |
| Y | Đời sống mới (`GtaVietLifeDoiSong.cs`) |
| L | Kinh tế (`GtaVietLifeEconomy.cs`) |
| I, O, B | Cá nhân (`GtaVietLifePersonal.cs`): menu, ăn nhanh, bồi thường va chạm |

Thêm phím mới thì ghi vào bảng này để không trùng.
