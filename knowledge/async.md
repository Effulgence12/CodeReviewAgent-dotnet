# C# 异步编程规范

异步用于在等待 I/O 时释放线程，不等同于把同步工作包装进 `Task.Run`。调用链应尽量保持 async all the way。

## async 与 await

- 返回值优先使用 `Task`/`Task<T>`；只有事件处理器等框架签名才使用 `async void`。
- `async` 方法通常应包含直属 `await`。没有 await 时移除 `async`，直接返回已有任务或重新审视设计。
- 不要忽略返回的任务。需要并发时显式保存任务并用 `Task.WhenAll` 等待。
- 异步方法使用 `Async` 后缀，让调用者能从 API 名称识别调度语义。

## 取消、超时与生命周期

- 可取消操作接受 `CancellationToken`，并把同一个 token 继续传给下游 I/O、锁等待和延迟。
- 取消通常应传播 `OperationCanceledException`，不要转换成普通失败或静默成功。
- 超时策略应位于拥有业务期限的边界，并与调用方取消区分。
- 长生命周期任务要有明确所有者；对象关闭或请求结束时取消并等待其收尾。

## 避免同步阻塞

- 在异步调用链中避免 `.Result`、`.Wait()` 和 `GetAwaiter().GetResult()`，它们可能导致线程饥饿或死锁。
- 不要在锁内 await；先复制所需状态，或改用适合异步的协调原语。
- 高频代码避免无意义的 `Task.Run` 和不必要状态机；优化前先测量。

## 库代码与上下文

通用库可根据目标环境考虑 `ConfigureAwait(false)`；ASP.NET Core 通常没有传统同步上下文，不应机械添加。`ValueTask` 仅在分析证明能降低分配且调用契约清晰时使用。
