# 编码规范知识库（RAG 语料）

本目录存放供 RAG 检索的编码规范文档（Markdown）。运行时由 `KnowledgeBase` 读取、分块、向量化并建立索引，供 `search_standards` 工具检索。

TODO(组员B)：编写 5–6 篇主题分明的规范文档，确保不同查询命中不同文档。建议拆分主题：

- `naming.md` 命名规范
- `exceptions.md` 异常处理与资源管理（IDisposable）
- `async.md` 异步编程（async/await）
- `security.md` 安全（输入校验、硬编码密钥、空引用）
- `performance.md` 性能（集合、LINQ、循环内分配）
- `solid.md` SOLID 与架构

每篇用清晰的小标题组织条款，便于分块后被精准检索。
