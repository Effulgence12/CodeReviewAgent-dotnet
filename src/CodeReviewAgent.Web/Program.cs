using CodeReviewAgent.Core;
using CodeReviewAgent.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// 配置（.NET 惯用分层）：默认 builder 已装配 appsettings.json → 环境变量，
// 这里追加 appsettings.Local.json（本地机密覆盖，gitignore，不进版本库），
// 末尾再追加环境变量，使其保持最高优先级。机密填到 appsettings.Local.json 的 Llm:ApiKey。
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Blazor Server（交互式服务端渲染）。
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 注册代码审查 Agent 的全部核心服务（Core 提供）。
builder.Services.AddCodeReviewAgent(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
