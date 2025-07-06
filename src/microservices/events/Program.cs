using System.Text;
using Confluent.Kafka;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddConsole();

// Конфигурация Kafka
var kafkaBrokers = Environment.GetEnvironmentVariable("KAFKA_BROKERS") ?? "kafka:9092";

builder.Services.AddSingleton<IProducer<Null, string>>(_ =>
    new ProducerBuilder<Null, string>(new ProducerConfig
    {
        BootstrapServers = kafkaBrokers,
        MessageTimeoutMs = 5000,
        RequestTimeoutMs = 3000
    }).Build());

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(10);
});
builder.Services.Configure<HostOptions>(opts => {
    opts.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
    opts.ShutdownTimeout = TimeSpan.FromSeconds(30);
});
builder.WebHost.ConfigureKestrel(serverOptions => {
    serverOptions.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
});

try
{
    using var testConsumer = new ConsumerBuilder<Ignore, string>(
        new ConsumerConfig
        {
            BootstrapServers = kafkaBrokers,
            GroupId = "test-group",
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();

    builder.Services.AddHostedService<KafkaConsumerService>();
}
catch (Exception ex)
{
    Console.WriteLine($"Kafka Consumer unavailable: {ex.Message}");
}

var app = builder.Build();
app.UseRouting();
app.UseEndpoints(endpoints => { });

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Application started with basic health check");

app.UseExceptionHandler("/error");

app.MapGet("/health", () => {
    return Results.Ok(new
    {
        status = "OK",
        timestamp = DateTime.UtcNow
    });
});

app.MapGet("/test-kafka", async (IProducer<Null, string> producer) =>
{
    try
    {
        var result = await producer.ProduceAsync("test-topic",
            new Message<Null, string> { Value = "test" });
        return Results.Ok($"Delivered to {result.TopicPartitionOffset}");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Kafka error: {ex.Message}");
    }
});
app.MapGet("/api/events/health", () =>
    Results.Json(new { status = true }));

app.MapPost("/api/events/movie", async (HttpContext context, IProducer<Null, string> producer) =>
{
    try
    {
        var eventData = await context.Request.ReadFromJsonAsync<MovieEvent>();
        if (eventData == null) return Results.BadRequest();

        var message = new Message<Null, string>
        {
            Value = System.Text.Json.JsonSerializer.Serialize(new
            {
                Type = "Movie",
                Payload = eventData,
                Timestamp = DateTime.UtcNow
            })
        };

        var result = await producer.ProduceAsync("movie-events", message);
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation($"Produced movie event to partition {result.Partition}, offset {result.Offset}");

        return Results.Created($"/api/events/movie/{result.Offset}", new EventResponse
        {
            Status = "success",
            Partition = result.Partition,
            Offset = result.Offset.Value,
            Event = new
            {
                Id = Guid.NewGuid().ToString(),
                Type = "Movie",
                Timestamp = DateTime.UtcNow,
                Payload = eventData
            }
        });
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error producing movie event");
        return Results.Problem("Internal server error", statusCode: 500);
    }
});


app.MapPost("/api/events/user", async (HttpContext context, IProducer<Null, string> producer) =>
{
    try
    {
        var eventData = await context.Request.ReadFromJsonAsync<UserEvent>();
        if (eventData == null) return Results.BadRequest();

        var message = new Message<Null, string>
        {
            Value = System.Text.Json.JsonSerializer.Serialize(new
            {
                Type = "User",
                Payload = eventData,
                Timestamp = DateTime.UtcNow
            })
        };

        var result = await producer.ProduceAsync("user-events", message);
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation($"Produced user event to partition {result.Partition}, offset {result.Offset}");

        return Results.Created($"/api/events/user/{result.Offset}", new EventResponse
        {
            Status = "success",
            Partition = result.Partition,
            Offset = result.Offset.Value,
            Event = new
            {
                Id = Guid.NewGuid().ToString(),
                Type = "User",
                Timestamp = DateTime.UtcNow,
                Payload = eventData
            }
        });
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error producing user event");
        return Results.Problem("Internal server error", statusCode: 500);
    }
});


app.MapPost("/api/events/payment", async (HttpContext context, IProducer<Null, string> producer) =>
{
    try
    {
        var eventData = await context.Request.ReadFromJsonAsync<PaymentEvent>();
        if (eventData == null) return Results.BadRequest();

        var message = new Message<Null, string>
        {
            Value = System.Text.Json.JsonSerializer.Serialize(new
            {
                Type = "Payment",
                Payload = eventData,
                Timestamp = DateTime.UtcNow
            })
        };

        var result = await producer.ProduceAsync("payment-events", message);
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation($"Produced payment event to partition {result.Partition}, offset {result.Offset}");

        return Results.Created($"/api/events/payment/{result.Offset}", new EventResponse
        {
            Status = "success",
            Partition = result.Partition,
            Offset = result.Offset.Value,
            Event = new
            {
                Id = Guid.NewGuid().ToString(),
                Type = "Payment",
                Timestamp = DateTime.UtcNow,
                Payload = eventData
            }
        });
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error producing payment event");
        return Results.Problem("Internal server error", statusCode: 500);
    }
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8082";
app.Urls.Clear();
app.Urls.Add($"http://0.0.0.0:{port}");

logger.LogInformation($"Starting on port {port} with URLs: {string.Join(", ", app.Urls)}");

app.Use(async (context, next) => {
    logger.LogInformation($"Request: {context.Request.Path}");
    await next();
});

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Application terminated unexpectedly");
}
finally
{
    logger.LogInformation("Application shutting down...");
}

app.Run();










public class KafkaConsumerService : BackgroundService
{
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly string _kafkaBrokers;

    public KafkaConsumerService(ILogger<KafkaConsumerService> logger)
    {
        _logger = logger;
        _kafkaBrokers = Environment.GetEnvironmentVariable("KAFKA_BROKERS") ?? "kafka:9092";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // Освобождаем основной поток сразу

        var config = new ConsumerConfig
        {
            BootstrapServers = _kafkaBrokers,
            GroupId = "events-service-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnablePartitionEof = true
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        consumer.Subscribe(new[] { "movie-events", "payment-events", "test-topic", "user-events" });

        _logger.LogInformation("Kafka Consumer started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = consumer.Consume(stoppingToken);

                    if (consumeResult.IsPartitionEOF)
                    {
                        await Task.Delay(100, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation($"Message received: {consumeResult.Message.Value}");

                    // Обработка сообщения в отдельной задаче
                    _ = Task.Run(() => ProcessMessage(consumeResult.Message.Value), stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, $"Kafka consume error: {ex.Error.Reason}");
                    if (ex.Error.IsFatal)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            consumer.Close();
            _logger.LogInformation("Kafka Consumer stopped");
        }
    }

    private async Task ProcessMessage(string message)
    {
        try
        {
            // Асинхронная обработка сообщения
            await Task.Delay(1); // Заглушка для асинхронности
            _logger.LogInformation($"Processing message: {message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
        }
    }
}

// Модели данных
public record MovieEvent(
    int MovieId,
    string Title,
    string Action,
    int? UserId = null,
    double? Rating = null,
    string[]? Genres = null,
    string? Description = null);

public record UserEvent(
    int UserId,
    string Action,
    DateTime Timestamp,
    string? Username = null,
    string? Email = null);

public record PaymentEvent(
    int PaymentId,
    int UserId,
    decimal Amount,
    string Status,
    DateTime Timestamp,
    string? MethodType = null);

public class EventResponse
{
    public string Status { get; set; } = "success";
    public int Partition { get; set; }
    public long Offset { get; set; }
    public object Event { get; set; } = new object();
}
