# Protocol: Teamobi2026 vs NroUnity

> BattleBall analysis has not started yet.
> Không giả định hai bên khớp hoàn toàn; chỉ ghi những điểm đã đối chiếu từ source.
> Chi tiết: `Teamobi2026.md`, `NroUnity.md` cùng thư mục.

## Tương đồng (CONFIRMED)
| Hạng mục | Server | Client |
|---|---|---|
| Header | `cmd(1) + size(2)` | cùng |
| Mã hoá | XOR vòng với key do server gửi | cùng cơ chế |
| Gói key | `-27`: `len`, `key[0]`, `key[i]^key[i-1]` | `getKey` khôi phục bằng `key[j+1] ^= key[j]` |
| Handshake | nhận `-27` rồi gửi key | gửi `-27` rồi nhận key |
| Kiểu opcode | `byte`, `switch` | `sbyte`, `switch` |
| Sub-command | `-29`, `-28`, `-30` | cùng số |
| Đăng nhập | đọc 2 UTF (user, pass) | gửi UTF user, pass, version + byte type |
| Opcode đối chiếu được | `-7`, `54`, `-60`, `34`, `-43`, `-23`, `-25`, `-12`, `-17`, `-16` | cùng số (xem file từng project) |

## Khác biệt và điểm lệch (CONFIRMED)
1. **Size 3 byte**: server gửi {-32, -66, -74, 11, -67, -87, 66}; client đọc {-32, -66, 11, -67, -74, -87, 66, **12**}. Server không gửi `Message(12)` trong các file đã grep (`S:data\DataGame.java` dùng các cmd còn lại), nên hiện chưa gây lỗi (INFERRED).
2. **Gói key**: client đọc thêm `IP2 (UTF)`, `PORT2 (int)`, `isConnect2 (byte)` sau khi đã đặt `getKeyComplete`; server `KeyHandler.sendKey` không gửi. Exception bị nuốt (`Session_ME.getKey`), `isConnect2` giữ mặc định.
3. **Đòn kết hợp mob+người**: client gửi `-4` hoặc `67`; `Controller.onMessage` của server không có `case -4` hay `67`, nên bị bỏ qua.
4. **Sát thương**: server ghi `int` trong `-60`/`-12`; client gọi `readLong()` nhưng `ModFunc.isReadInt = true` khiến nó đọc `int`. Phụ thuộc cờ này.
5. **`-17` (mình chết)**: server ghi `writeByte((int) pl.id)` rồi 2 short; client đọc byte đầu vào `cPk`. Ý nghĩa trường không khớp (CONFIRMED về code; hệ quả UNKNOWN).
6. **Chiều nhận size**: server luôn đọc size 2 byte; chiều gửi có nhánh 3 byte.
7. **Đọc size khi chưa có key**: client dùng `(num2 & 0xFF00) | (b4 & 0xFF)` với `sbyte` (INFERRED: chỉ ảnh hưởng gói key, nhỏ nên không lỗi).

## Serialization
- Server: `DataOutputStream`/`DataInputStream` của Java (big-endian). CONFIRMED.
- Client: `myWriter`/`myReader` tự cài. UNKNOWN có tương thích hoàn toàn không (INFERRED tương thích vì các gói trao đổi đúng).

## Xác thực
- Cả hai: mật khẩu đi trong payload `-29/0`, chỉ bị XOR (khoá `"NRO"` mặc định) chứ không băm hay TLS. CONFIRMED (code hai phía).

## UNKNOWN
- Bảng opcode đầy đủ; mức khớp payload từng opcode gameplay.
- Định dạng nhị phân dữ liệu tài nguyên (`NRmap`, `NRskill`, `NRitem*` so với `DataGame`).
