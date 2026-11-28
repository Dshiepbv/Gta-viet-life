# lessons.md · Sổ bài học

Chỉ ghi khi tôi đã duyệt. Mỗi dòng: `- L### · <bài học> (yyyy-mm-dd)`.

## Quy trình ghi (3 câu hỏi định tuyến)
1. Xóa hết tên riêng mà câu vẫn có nghĩa tổng quát: ghi mục CHUNG. Mất nghĩa: ghi mục DỰ ÁN.
2. Vừa là nguyên tắc vừa gắn ca cụ thể: tách đôi, hai dòng cùng mã ID (ví dụ L004 ở CHUNG và L004 ở DỰ ÁN).
3. Ca mới trùng nguyên tắc cũ: gộp vào dòng cũ (thêm ngày), không tạo mục mới.
Sổ phình to thì tách theo mảng (`lessons-build.md`, `lessons-ui.md`, `lessons-security.md`...) và liệt kê các file ở đây; không cắt bớt bài học.

## CHUNG
- L001 · Build không báo lỗi chưa phải là xong; phải chạy thật và xem đúng hành vi. (khởi tạo)
- L002 · Sửa 2 lần vẫn cùng một lỗi thì dừng, ghi trạng thái, mở phiên mới với brief sạch. (khởi tạo)
- L003 · Trước khi nói "không hỗ trợ" hay "không có", kiểm tra tài liệu chính chủ mới nhất và cấu hình đang dùng. (khởi tạo)
- L004 · Trước khi gỡ xung đột, so merge-base và cây file: nhánh đã được squash vào đích thì lấy bản đích, không trộn tay. (2026-11-28)
- L005 · Test từng thành phần pass không chứng minh hai đầu nối đúng; phải có test đối chiếu đường dẫn và trường dữ liệu giữa bên gửi và bên nhận. (2026-11-28)
- L006 · Mỗi dữ kiện (tiền, giờ, quan hệ, trí nhớ) chỉ có một chủ ghi; nơi khác đọc bản sao. (2026-11-28)
- L007 · ID chống trùng phải sinh một lần, lưu bền và kèm mã băm nội dung; cùng ID khác nội dung là lỗi. (2026-11-28)
- L008 · Hàng đợi gửi lại phải phân biệt lỗi vĩnh viễn với lỗi tạm; lỗi vĩnh viễn tách riêng để không chặn phía sau. (2026-11-28)
- L009 · Ghi file lưu phải nguyên tử, có phiên bản và bản sao; tải lỗi thì cấm ghi đè. (2026-11-28)
- L010 · Test lưu bền phải gồm khởi động lại, đăng ký lại, phát lại và đứt giữa chừng. (2026-11-28)
- L012 · Test hồi quy phải được chạy trên mã cũ và thấy fail trước khi tin nó. (2026-11-28)
- L013 · Logic nằm trong môi trường khó chạy thử thì tách thành lớp thuần để biên dịch và chạy thật. (2026-11-28)
- L014 · Chính sách bỏ yêu cầu tùy giá trị dữ liệu: dữ liệu tiền không bị bỏ vì lỗi máy chủ; nhịp thử lại không được tăng theo số yêu cầu mới. (2026-11-28)
- L015 · Sửa cho một luồng đang bị chặn chạy lại thì kiểm tác dụng phụ về tải và dung lượng của luồng đó. (2026-11-28)

## DỰ ÁN
- L004 · `4819d89` có cây file trùng đỉnh `codex/gta-viet-life-architecture-hardening` (d5ab107); merge 12 file xung đột giải bằng bản main. (2026-11-28)
- L005 · `PostWorldRequest` chỉ nhận `/engine/v1/world/` nên `actors/observe` và `work-completed` bị bỏ im lặng dù 63 test pass. (2026-11-28)
- L006 · Quan hệ nằm ở `memory.db`, `NPCState.affinity`, `world_social_relationships`; 5 nơi đếm ngày riêng; tiền ở ví GTA, file `.sav` và SQLite. (2026-11-28)
- L007 · `society.py` coi trùng `transaction_id` khác nội dung là thành công. (2026-11-28)
- L008 · World post gặp 400/404 bị đưa lại đầu hàng mãi (`GtaVietLifeCore.cs`). (2026-11-28)
- L009 · World đọc save trong một `try` chung và ghi đè trực tiếp; Personal ghi đè ví GTA khi khôi phục. (2026-11-28)
- L010 · Đăng ký lại NPC sau restart bị `npc_identity_conflict` do `knowledge` là list so với tuple. (2026-11-28)
- L011 · `DoiSong.FadeWait` tự xếp lại vào hàng đang được `while` rút, không thoát được trong một tick: việc chờ theo thời gian phải để sang tick sau. (2026-11-28)
- L012 · Test đợt 1 chạy trên `16477fb` bắt được E1 E2 E3 G1 và lỗi lô event; test hành vi C# của E5 fail trên mã cũ chỉ vì chưa có lớp để biên dịch. (2026-11-28)
- L013 · `GvlPostQueue`, `GvlSendRules`, `GvlBatchIsolation`, `GvlFade` được test bằng `csc.exe`; phần gọi HTTP và API GTA vẫn phải thử trong game. (2026-11-28)
- L014 · Bản sửa E5 đầu tiên cho phép loại event ledger khi host trả 500 và để mỗi quan sát mới kích hoạt thử lại; đã sửa trước khi bàn giao. (2026-11-28)
- L015 · Mở lại `actors/observe` khiến `request_dedup` ghi định kỳ. (2026-11-28)
