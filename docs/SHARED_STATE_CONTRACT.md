# Hợp đồng dữ liệu chung M0–M6 (bản 1, 2026-11-28)

Trạng thái: quyền sở hữu tiền và cách xử lý khi tải save đã được chủ dự án chốt ngày 2026-11-28, cài đặt ở đợt 3. Mục 5, 6 và quy tắc giờ lùi ở mục 4 đã cài phần lớn trong bản 0.3.0 (xem audit mục 8). Các phần còn lại là đề xuất kỹ thuật. Hiện trạng và mã lỗi xem `AUDIT_M0_M6.md`.

## 1. Quyền sở hữu dữ liệu

Mỗi dữ kiện có đúng một chủ ghi. Nơi khác chỉ đọc bản sao và gửi event.

| Dữ kiện | Chủ ghi | Ghi chú |
|---|---|---|
| Đồng hồ thế giới | M1 (GTA qua Core) | Engine chỉ tiến khi nhận giờ từ adapter đang giữ quyền |
| Nhu cầu, lịch, vòng đời NPC | M0 engine | |
| Chỉ số cơ thể người chơi | GTA, gửi quan sát lên engine | |
| Quan hệ, trí nhớ | M2 trong engine | Bridge đọc ngữ cảnh và gửi event thoại; `memory.db` chuyển thành bộ đệm |
| Tiền mặt người chơi | Ví GTA (đã chốt) | Game gốc tự cộng trừ; sổ cái ghi lại và đối soát có mã lý do. Cấm `Personal` hay script khác ghi đè ví bằng số đã lưu |
| Ngân hàng, nợ, doanh thu và chi phí quán, lương | Sổ cái SQLite M4 (đã chốt) | Script C# hiển thị bản sao, đổi số dư bằng lệnh giao dịch |
| Tồn kho quán | SQLite M4 (đã chốt) | Đổi tồn kho bằng lệnh giao dịch, không ghi đè bằng ảnh chụp từ game |
| Hành động vật lý | Adapter | Engine ra lệnh, adapter báo kết quả |

Sổ cái nằm trong engine (`life-simulation.sqlite3`). Script trong game không mở SQLite; chúng chỉ giữ hàng đợi gửi.

## 2. Định danh

| Đối tượng | Quy tắc |
|---|---|
| Người chơi | `player.sp0`, `player.sp1`, `player.sp2`; `player.main` hiện tại được ánh xạ khi migration |
| NPC | `npc.<guid>`, sinh một lần và lưu; `res_<n>` cũ giữ làm bí danh |
| Quán, nhà, xe | GUID lưu bền; tọa độ và chỉ số mảng chỉ là thuộc tính |
| Nguồn event | `gta5.shvdn:<install_guid>`; `install_guid` đổi khi hàng đợi bị tạo lại, nên seq không đụng nhau |
| Dòng thời gian | `epoch` tăng mỗi lần giờ game lùi hoặc phát hiện tải save |

## 3. Phong bì event và lệnh

Mọi POST làm đổi state mang:

`protocol`, `request_id`, `source`, `sequence`, `epoch`, `world_minute`, `actor_id`, `type`, `payload`

- `request_id` sinh một lần khi tạo event, lưu cùng hàng đợi, giữ nguyên khi gửi lại.
- Engine lưu `(endpoint, request_id, payload_hash, response)`. Cùng ID cùng hash trả lại kết quả cũ. Cùng ID khác hash trả 409.
- Ghi dedup, đổi state và journal nằm trong một giao dịch SQLite.
- Lỗi 4xx: chuyển event vào hàng lỗi, ghi log, gửi tiếp event sau. Lỗi mạng và 5xx: thử lại có giãn cách.
- Lệnh từ engine có `command_id`, `adapter_id`, `expires_at_world_minute`; trạng thái cuối là completed, rejected, unsupported, failed hoặc expired. Gửi lại lệnh đã xong trả đúng trạng thái đã ghi.

## 4. Thời gian

- Đơn vị chuẩn: `world_minute` nguyên, không giảm trong một epoch. Ngày = `world_minute // 1440`.
- Chỉ Core tính qua ngày và công bố `GVL.Clock`. Economy, Personal, DoiSong, World đọc giá trị đó, bỏ bộ đếm riêng.
- Giờ lùi: tăng epoch, không mô phỏng đoạn đã chạy; engine tiếp tục khi vượt mốc cao nhất.
- Nhảy giờ lớn được mô phỏng theo từng khúc, không cắt bỏ phần dư.
- Adapter giữ quyền đồng hồ được ghi trong phiên; `/simulation/advance` bị từ chối khi quyền thuộc adapter khác.

## 5. Sổ cái

Dùng các bảng `world_accounts`, `world_transactions`, `world_ledger_entries` đang có, bổ sung:

- Số tiền là số nguyên trong giới hạn khai báo; ràng buộc `CHECK` ở DB.
- Mọi bút toán trong một giao dịch cùng một tiền tệ, tổng bằng 0.
- Cùng `transaction_id` khác `payload_hash` bị từ chối.
- Tiền tố `payroll:`, `lease:`, `sale:` chỉ engine được tạo.
- Số dư đầu kỳ là một bút toán `opening` duy nhất, kèm `sequence` cao nhất đã nằm trong số dư đó; event có seq nhỏ hơn hoặc bằng không cộng lần nữa.
- Đối soát ví tạo bút toán `reconcile` có mã lý do và độ lệch; lệch quá ngưỡng thì ghi cảnh báo.
- Kiểm tra lúc khởi động: số dư mỗi tài khoản bằng tổng bút toán.

### Ngân hàng khi Engine tắt (đã chốt 2026-11-28)

Game cho chơi tiếp, ghi mỗi thay đổi thành event lưu bền và gửi bù khi Engine bật lại.

1. Lần đầu một bộ đếm game gặp Engine, game gửi `bank.takeover` (số dư và tổng nợ); sổ cái đối soát theo đó.
2. Thay đổi ngân hàng đi qua `ledger.income`/`ledger.expense`, thay đổi nợ qua `ledger.debt`.
3. `GET /engine/v1/world/ledger/player` (header `X-GVL-Source`) trả `owned`, `bank`, `debt`, `applied_sequence`.
4. Chỉ khi hàng gửi trống và `applied_sequence` đã bao gồm thay đổi ngân hàng mới nhất, game mới lấy số dư của sổ cái.

Chưa áp dụng cho tiền quán, tồn kho và lương: các số này vẫn đi bằng ảnh chụp `businesses/sync`. Tổng nợ được ghi vào sổ cái, lịch trả góp vẫn do script giữ.

## 6. Lưu và tải

- Engine: WAL, `foreign_keys=ON`, `busy_timeout`, một `schema_version` cho toàn bộ bảng, migration trong một giao dịch, backup trước migration, khóa một tiến trình trên thư mục dữ liệu và cổng.
- Script C#: ghi file tạm, `File.Replace` có `.bak`, dòng đầu là phiên bản schema, dòng cuối đánh dấu đủ file. Tải lỗi thì dùng `.bak`; cả hai lỗi thì không ghi đè.
- Tải save GTA cũ (đã chốt): giữ nguyên đời sống và sổ cái hiện hành, không quay lui, không mô phỏng lại; chỉ đối soát tiền mặt trong ví GTA bằng bút toán `reconcile`.
