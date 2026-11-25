<!-- UnityMCP Antigravity context: start -->
## UnityMCP live-Unity workflow

For every task that queries, diagnoses, verifies, or changes Unity:

- **When a shell is available**, prefer the official **Unity CLI** (`unity status`, `unity command <group> <name>`, `unity recompile`, `unity test`, `unity build`) backed by `com.unity.pipeline` and `com.ducminh.unity-mcp` extension commands before inspecting raw serialized files or inferring live Editor state.
- **Discover before calling**: run `unity command <group>` (or `--query <term>`) before calling an unfamiliar command — never guess names (unknown commands log `CommandNotFound` errors in the Unity Console). Common built-in read commands: `scene list_open`, `scene get_hierarchy` (includes each GameObject's `components`), `gameobject find`, `component get_fields`, `settings.<domain> get`, `console get`, `asset find`.
- **When using MCP** (or when shell access is unavailable), invoke the project-scoped `unity` MCP server (`unity mcp --tool-groups`).
- Use project-file fallback only when no Editor/Pipeline server is running, the CLI/MCP command is unavailable, or the invocation returns an error. State the reason before falling back. After Unity changes, verify compilation (`unity recompile`), console output (`unity command console get`), or tests (`unity test`) via Unity CLI or MCP.
<!-- UnityMCP Antigravity context: end -->

# Lean & Clean Execution Rules

1. **Targeted Scope**: Khi yêu cầu đã rõ mục tiêu (file, dòng, tên hàm cụ thể), thao tác cục bộ trong 1-2 bước. CẤM quét diện rộng, không tự ý kiểm tra `git diff`/`git log`, không viết script phụ.
2. **Exploratory Scope**: Chỉ quét diện rộng và trace luồng khi điều tra bug chưa rõ nguyên nhân.
3. **In-Place & No Dead Code**: Sửa tại chỗ; nếu thay hàm thì bắt buộc dọn dẹp hàm/biến cũ không còn dùng.
4. **KISS & YAGNI**: Logic đơn giản, trực tiếp, không chêm abstraction hay điều kiện rườm rà.