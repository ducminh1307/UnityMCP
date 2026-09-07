<!-- UnityMCP Antigravity context: start -->
## UnityMCP live-Unity workflow

For every task that queries, diagnoses, verifies, or changes Unity, invoke the appropriate project-scoped UnityMCP tool (`unity_*`) before using terminal commands, searching serialized project files, or inferring live Editor state.

Use terminal or project-file inspection only when the UnityMCP gateway is unreachable, the required tool is unavailable, or that tool returns an error. State the reason before falling back. After a Unity-related change, use UnityMCP to check compilation, console output, or the relevant test/live state.
<!-- UnityMCP Antigravity context: end -->

# Lean & Clean Execution Rules

1. **Targeted Scope**: Khi yêu cầu đã rõ mục tiêu (file, dòng, tên hàm cụ thể), thao tác cục bộ trong 1-2 bước. CẤM quét diện rộng, không tự ý kiểm tra `git diff`/`git log`, không viết script phụ.
2. **Exploratory Scope**: Chỉ quét diện rộng và trace luồng khi điều tra bug chưa rõ nguyên nhân.
3. **In-Place & No Dead Code**: Sửa tại chỗ; nếu thay hàm thì bắt buộc dọn dẹp hàm/biến cũ không còn dùng.
4. **KISS & YAGNI**: Logic đơn giản, trực tiếp, không chêm abstraction hay điều kiện rườm rà.