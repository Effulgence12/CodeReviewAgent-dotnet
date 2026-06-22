# C# 命名规范

清晰、一致的命名应表达业务意图，而不是实现细节。项目内约定优先于个人偏好；引入新约定时应同步更新分析规则和团队文档。

## 类型与成员

- 类、结构体、枚举、委托、方法、属性和事件使用 **PascalCase**，例如 `OrderService`、`CalculateTotal`。
- 接口使用 PascalCase，并采用 `I` 前缀，例如 `IOrderRepository`。
- 返回 `Task` 或 `Task<T>` 的异步方法建议使用 `Async` 后缀，例如 `SaveAsync`；事件处理器等框架约定可例外。
- 布尔成员应表达可判断的状态，如 `IsEnabled`、`HasItems`、`CanRetry`。

## 参数与局部变量

- 参数和局部变量使用 **camelCase**，例如 `orderId`、`retryCount`。
- 名称应体现角色和单位；优先使用 `timeoutMilliseconds`，避免含糊的 `value`、`data`、`tmp`。
- 循环索引 `i`、坐标 `x/y`、丢弃变量 `_` 等短名称只适用于作用域很小且含义明确的场景。

## 常量、字段与缩写

- 常量使用 PascalCase，例如 `DefaultTimeout`；不要用全大写加下划线模拟其他语言风格。
- 私有字段采用团队统一风格；本项目推荐 `_camelCase`，但自动分析不应在没有项目约定时武断报告。
- 缩写按单词处理，例如 `HttpClient`、`JsonParser`、`GetUserId`，避免 `HTTPClient`。

## 审查提示

重命名公共 API 前应评估兼容性。审查意见应指出具体符号和建议名称，不应只写“命名不好”。
