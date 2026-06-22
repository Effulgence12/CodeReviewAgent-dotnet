# 架构设计文档

TODO(组员C)：撰写架构设计文档，作为课程交付物之一。建议包含：

1. 总体架构图：对话式编排 Agent（顶层，LLM 动态决策） + 多 Agent 深度审查（底层，固定工作流，三阶段）。
2. 两层编排说明：顶层动态规划 vs 底层固定工作流的职责区分。
3. 工具设计：6 个工具（list_files / read_file / analyze_code / search_standards / propose_fix / compile_check）的职责与输入输出。
4. 推理流程图：ReAct 循环 Thought → Action → Observation 的迭代与终止。
5. 概念辨析：工具 vs Agent vs MCP 工具的区别与嵌套关系。
6. 配置与依赖注入、可观测性、错误处理策略。

> 详细的分工、接口契约与里程碑见 [`开发计划.md`](./开发计划.md)。
