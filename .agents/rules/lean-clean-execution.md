---
description: Nguyên tắc điều tra tương xứng, thực thi tinh gọn và giữ code sạch (không điều tra thừa, không để code chết, không chêm logic rườm rà).
always_on: true
---

# Lean & Clean Execution Rules

Áp dụng cho **TẤT CẢ** các tác vụ (coding, refactoring, bugfix, animation, config...):

---

### PHẦN 1: ĐIỀU TRA TƯƠNG XỨNG (PROPORTIONAL INVESTIGATION)

Trước khi thực hiện, phân loại yêu cầu vào 1 trong 2 nhóm:

1. **Nhóm đã rõ mục tiêu (Targeted Scope):**
   *(Chỉ rõ file, dòng code, tên hàm, update animation, sửa thông số, fix warning cụ thể...)*
   - **Thao tác cục bộ tuyệt đối**: Chỉ mở đúng file mục tiêu, sửa ngay trong 1–2 bước.
   - **CẤM**: Không quét diện rộng, không tự ý check `git diff`/`git log`, không viết script reflection/thăm dò phụ trợ.

2. **Nhóm điều tra lỗi ẩn (Exploratory Scope):**
   *(Hỏi nguyên nhân bug, điều tra crash, lỗi luồng logic nhiều script...)*
   - **Được phép đào sâu**: Quét diện rộng, trace luồng gọi hàm qua các script liên quan trong codebase để tìm nguyên nhân gốc rễ.
   - Không quét vào các thư mục editor/cache khổng lồ (Unity Hub, AppData, ShaderCache...).

---

### PHẦN 2: THỰC THI TINH GỌN & GIỮ CODE SẠCH (CLEAN REFACTORING & NO BLOAT)

1. **Sửa tại chỗ, không để lại code chết (In-Place & No Dead Code):**
   - Ưu tiên sửa đổi trực tiếp trên hàm/logic hiện có thay vì tạo thêm hàm wrapper/helper mới không cần thiết.
   - Nếu bắt buộc phải thay thế bằng hàm mới, **BẮT BUỘC xóa bỏ** hàm/biến cũ không còn sử dụng. Tuyệt đối không để lại dead code.

2. **Logic đơn giản, tránh rườm rà (KISS & YAGNI - No Over-Engineering):**
   - Viết code ngắn gọn, tự nhiên, đúng trọng tâm vấn đề cần giải quyết.
   - Tuyệt đối không tự ý chêm thêm các tầng trung gian, abstractions phức tạp, hay kiểm tra điều kiện lòng vòng khi một giải pháp trực tiếp 2–3 dòng đã đủ đáp ứng.
