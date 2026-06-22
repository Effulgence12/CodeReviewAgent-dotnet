using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Rag;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Tests;

public sealed class EmbeddingServiceTests
{
    [Fact]
    public async Task EmbedAsync_BuildsCompatibleRequest()
    {
        string? url = null;
        string? authorization = null;
        string? body = null;
        var handler = SuccessHandler(async request =>
        {
            url = request.RequestUri!.ToString();
            authorization = request.Headers.Authorization?.ToString();
            body = await request.Content!.ReadAsStringAsync();
        }, count: 2);
        var service = CreateService(handler);

        await service.EmbedAsync(new[] { "first", "second" });

        Assert.Equal("https://example.test/v1/embeddings", url);
        Assert.Equal("Bearer embedding-key", authorization);
        Assert.Contains("\"model\":\"embedding-model\"", body);
        Assert.Contains("\"input\":[\"first\",\"second\"]", body);
    }

    [Fact]
    public async Task EmbedAsync_UsesEmbeddingConfigBeforeLlmFallback()
    {
        string? url = null;
        string? authorization = null;
        var handler = SuccessHandler(request =>
        {
            url = request.RequestUri!.ToString();
            authorization = request.Headers.Authorization?.ToString();
            return Task.CompletedTask;
        });
        var config = CreateConfig();
        config.Embedding.Endpoint = "https://embedding.test/api";
        config.Embedding.ApiKey = "preferred-key";
        var service = new EmbeddingService(new StubHttpClientFactory(handler), Options.Create(config));

        await service.EmbedAsync(new[] { "text" });

        Assert.Equal("https://embedding.test/api/embeddings", url);
        Assert.Equal("Bearer preferred-key", authorization);
    }

    [Theory]
    [InlineData("https://example.test/v1")]
    [InlineData("https://example.test/v1/")]
    public async Task EmbedAsync_NormalizesEndpoint(string endpoint)
    {
        string? url = null;
        var handler = SuccessHandler(request =>
        {
            url = request.RequestUri!.ToString();
            return Task.CompletedTask;
        });
        var config = CreateConfig();
        config.Embedding.Endpoint = endpoint;
        var service = new EmbeddingService(new StubHttpClientFactory(handler), Options.Create(config));

        await service.EmbedAsync(new[] { "text" });

        Assert.Equal("https://example.test/v1/embeddings", url);
    }

    [Fact]
    public async Task EmbedAsync_Cancellation_IsPropagated()
    {
        var handler = new StubHttpMessageHandler((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(StubHttpMessageHandler.Json("{}"));
        });
        var service = CreateService(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EmbedAsync(new[] { "text" }, cts.Token));
    }

    [Fact]
    public async Task EmbedAsync_OutOfOrderData_RestoresInputOrder()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("{\"data\":[{\"index\":1,\"embedding\":[2]},{\"index\":0,\"embedding\":[1]}]}")));
        var result = await CreateService(handler).EmbedAsync(new[] { "first", "second" });

        Assert.Equal(1f, result[0][0]);
        Assert.Equal(2f, result[1][0]);
    }

    [Fact]
    public async Task EmbedAsync_EmptyInput_DoesNotSendRequest()
    {
        var handler = SuccessHandler(_ => Task.CompletedTask);
        var result = await CreateService(handler).EmbedAsync(Array.Empty<string>());

        Assert.Empty(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task EmbedAsync_MissingKey_ThrowsBeforeRequest()
    {
        var handler = SuccessHandler(_ => Task.CompletedTask);
        var config = CreateConfig();
        config.Embedding.ApiKey = "";
        config.Llm.ApiKey = "";
        var service = new EmbeddingService(new StubHttpClientFactory(handler), Options.Create(config));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EmbedAsync(new[] { "text" }));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task EmbedAsync_NonSuccess_ThrowsWithoutSecret()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("server failure", System.Net.HttpStatusCode.BadRequest)));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateService(handler).EmbedAsync(new[] { "text" }));

        Assert.Contains("400", exception.Message);
        Assert.DoesNotContain("embedding-key", exception.ToString());
    }

    [Fact]
    public async Task EmbedAsync_MalformedPayload_Throws()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("{not-json")));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(handler).EmbedAsync(new[] { "text" }));
    }

    [Fact]
    public async Task EmbedAsync_InconsistentDimensions_Throws()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("{\"data\":[{\"index\":0,\"embedding\":[1]},{\"index\":1,\"embedding\":[2,3]}]}")));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(handler).EmbedAsync(new[] { "first", "second" }));
    }

    private static StubHttpMessageHandler SuccessHandler(Func<HttpRequestMessage, Task> inspect, int count = 1) =>
        new(async (request, _) =>
        {
            await inspect(request);
            var data = string.Join(",", Enumerable.Range(0, count)
                .Select(index => $"{{\"index\":{index},\"embedding\":[{index + 1},2]}}"));
            return StubHttpMessageHandler.Json($"{{\"data\":[{data}]}}");
        });

    private static EmbeddingService CreateService(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), Options.Create(CreateConfig()));

    private static AgentConfig CreateConfig() => new()
    {
        Llm = new LlmOptions { Endpoint = "https://llm.test/v1", ApiKey = "llm-key" },
        Embedding = new EmbeddingOptions
        {
            Endpoint = "https://example.test/v1/",
            ApiKey = "embedding-key",
            Model = "embedding-model",
        },
    };
}
