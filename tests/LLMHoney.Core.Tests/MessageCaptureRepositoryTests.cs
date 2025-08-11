using LLMHoney.Core;
using LLMHoney.Llm.Abstractions;

namespace LLMHoney.Core.Tests;

public class MessageCaptureRepositoryTests
{
    [Fact]
    public async Task CaptureAsync_StoresInteraction_ReturnsId()
    {
        // Arrange
        var repository = new InMemoryMessageCaptureRepository();
        var message = new ProtocolMessage("127.0.0.1:1234", "tcp", System.Text.Encoding.UTF8.GetBytes("Hello"), DateTimeOffset.UtcNow);
        var response = new LlmResponse("TestProvider", "Hi there!", null);

        // Act
        var id = await repository.CaptureAsync(message, response);

        // Assert
        Assert.NotNull(id);
        Assert.NotEmpty(id);
        
        var captured = await repository.GetByIdAsync(id);
        Assert.NotNull(captured);
        Assert.Equal(message.RemoteEndpoint, captured.Message.RemoteEndpoint);
        Assert.Equal("Hi there!", captured.Response.Content);
    }

    [Fact]
    public async Task GetMetrics_ReturnsCorrectStatistics()
    {
        // Arrange
        var repository = new InMemoryMessageCaptureRepository();
        var message1 = new ProtocolMessage("127.0.0.1:1234", "tcp", System.Text.Encoding.UTF8.GetBytes("Hello"), DateTimeOffset.UtcNow);
        var message2 = new ProtocolMessage("192.168.1.1:5678", "tcp", System.Text.Encoding.UTF8.GetBytes("World"), DateTimeOffset.UtcNow);
        var response = new LlmResponse("TestProvider", "Response", null);

        await repository.CaptureAsync(message1, response);
        await repository.CaptureAsync(message2, response);

        // Act
        var metrics = await repository.GetMetricsAsync();

        // Assert
        Assert.Equal(2, metrics.TotalInteractions);
        Assert.Equal(10, metrics.TotalBytesReceived); // "Hello" + "World" = 5 + 5 = 10
        Assert.Equal(2, metrics.ByTransport["tcp"]);
        Assert.Contains("127.0.0.1:1234", metrics.ByRemoteEndpoint.Keys);
        Assert.Contains("192.168.1.1:5678", metrics.ByRemoteEndpoint.Keys);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsInCorrectOrder()
    {
        // Arrange
        var repository = new InMemoryMessageCaptureRepository();
        var response = new LlmResponse("TestProvider", "Response", null);
        
        var ids = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var message = new ProtocolMessage($"127.0.0.{i}:1234", "tcp", System.Text.Encoding.UTF8.GetBytes($"Message{i}"), DateTimeOffset.UtcNow);
            var id = await repository.CaptureAsync(message, response);
            ids.Add(id);
            await Task.Delay(1); // Ensure different timestamps
        }

        // Act
        var recent = await repository.GetRecentAsync(3);

        // Assert
        Assert.Equal(3, recent.Count);
        // Should be in reverse chronological order (most recent first)
        Assert.Contains(ids[4], recent.Select(r => r.Id));
        Assert.Contains(ids[3], recent.Select(r => r.Id));
        Assert.Contains(ids[2], recent.Select(r => r.Id));
    }
}
