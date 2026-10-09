# BattleBallServer: Protocol Design (Proposed)

> ARCHITECTURE DESIGN ONLY. **Không định nghĩa packet ID cụ thể.** Packet format chi tiết: **UNKNOWN / TO BE DESIGNED**.
> Giao thức NRO trong `docs\protocol\Teamobi2026.md` **không** phải giao thức của BattleBall; chỉ dùng làm bài học.
> Nhãn: **CONFIRMED** / **PROPOSED** / **UNKNOWN / OPEN**.

> **Cập nhật:** tài liệu này viết khi chưa biết game chơi đơn. **Nguồn sự thật cho message và luồng protocol nay là `docs\protocol\BattleBall.md`**; mục 2 (nhóm message) bên dưới đã lỗi thời (còn các mục về framing, reuse từ Teamobi2026 vẫn dùng được).

> **Đã chốt (2026-10-07):** serialization = **Protobuf** (mục 1, dòng Serialization không còn OPEN); mã hóa = **TLS**; không có người chơi khác nên các message đồng bộ nhiều người không còn. Xem `docs\protocol\BattleBall.md`.

## 1. Abstraction (PROPOSED)
```
Message
├── Header
│   ├── Length          (độ dài frame, kiểm tra giới hạn tối đa)
│   ├── Message ID      (định danh loại message; độ rộng: TO BE DESIGNED)
│   └── (tuỳ chọn) Sequence / Request ID, Flags   (TO BE DESIGNED)
├── Payload             (các trường của message)
└── Serialization       (Codec: encode/decode; định dạng: OPEN)
```

| Thành phần | Đề xuất | Trạng thái |
|---|---|---|
| Framing | Length-prefixed, có giới hạn kích thước tối đa | PROPOSED |
| Message ID | Một bảng định nghĩa duy nhất, có tên, dùng chung cho client và server (không số trần rải trong code) | PROPOSED; giá trị: TO BE DESIGNED |
| Version | Version giao thức trao đổi trong handshake | PROPOSED |
| Endianness, kiểu số | Cố định và ghi rõ trong định nghĩa; kiểu số đủ rộng cho damage/tiền (reference ép `int`) | PROPOSED; chi tiết TO BE DESIGNED |
| Chuỗi | UTF-8 có length prefix | PROPOSED |
| Serialization | Một trong: binary tự viết theo schema; thư viện schema (Protobuf/MessagePack/FlatBuffers…) | **OPEN** |
| Request/response | Request ID cho các lệnh cần phản hồi (login, enter world); event không cần | PROPOSED |
| Lỗi | Message lỗi chuẩn (mã lỗi + thông điệp) thay vì im lặng | PROPOSED |
| Nén | Không cần ở giai đoạn đầu | PROPOSED |
| Mã hoá | Không dùng XOR tĩnh; TLS/transport an toàn: OPEN | PROPOSED/OPEN |

## 2. Nhóm message (khái niệm, không có ID)
| Nhóm | Ví dụ khái niệm | Chiều | Trạng thái |
|---|---|---|---|
| Connection | Handshake/version, Heartbeat, Disconnect reason | hai chiều | PROPOSED |
| Auth/Session | Login request/result, Session error | C→S, S→C | LIKELY |
| World | Enter world, Spawn entity, Despawn entity, Map changed | S→C (và C→S cho enter) | LIKELY |
| Movement | Move intent (C→S), Position update (S→C) | hai chiều | LIKELY |
| Appearance | Đổi costume (C→S), Appearance update (S→C) | hai chiều | LIKELY |
| Combat/Skill/Item | Attack intent, result, … | | UNKNOWN (chỉ khi gameplay xác nhận) |

## 3. Reuse từ Teamobi2026 (đánh giá)
| Reference (CONFIRMED) | Quyết định | Lý do |
|---|---|---|
| Khung `cmd(1)+size(2)+data` | ADAPT (ý tưởng length-prefix) | 1 byte id và 2 byte size quá hẹp; phải thêm nhánh 3 byte cho gói lớn |
| Sub-command trong byte đầu payload (`-29/-28/-30`) | DROP | Thay bằng ID rõ ràng và state check ở dispatcher |
| XOR key | DROP | Không phải bảo mật |
| `DataOutputStream` big-endian + modified UTF-8 | DROP/OPEN | Định dạng phụ thuộc Java; chọn định dạng trung lập ngôn ngữ |
| Server ghi `int`, client cờ `isReadInt` để tương thích | DROP | Dấu hiệu thiếu schema chung |
| Không có version/handshake rõ | REPLACE | Thêm version |

## 4. Kiểm thử giao thức (PROPOSED)
- Test round-trip encode/decode cho mọi message ở cả client và server.
- Test từ chối frame quá lớn, message không đúng state, version sai.
