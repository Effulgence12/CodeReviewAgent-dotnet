# 编码规范知识库（RAG 语料）

本目录存放供 RAG 检索的编码规范文档（Markdown）。运行时由 `KnowledgeBase` 读取、分块、向量化并建立索引，供 `search_standards` 工具检索。

当前语料按主题拆分为：

- `naming.md` 命名规范
- `exceptions.md` 异常处理与资源管理（IDisposable）
- `async.md` 异步编程（async/await）
- `security.md` 安全（输入校验、硬编码密钥、空引用）
- `performance.md` 性能（集合、LINQ、循环内分配）
- `solid.md` SOLID 与架构

每篇均使用清晰的小标题组织条款，便于分块后被精准检索。新增语料时应同步增加离线可检索性测试。
