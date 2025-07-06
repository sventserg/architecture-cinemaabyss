
namespace proxy
{
    public class ProxyService
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddHttpClient();

            var app = builder.Build();
            app.Urls.Add("http://0.0.0.0:8000");
            app.UseExceptionHandler("/error");
            app.MapGet("/health", () => Results.Json(new
            {
                status = "OK",
                service = "Proxy"
            }));
            app.MapGet("/error", () => Results.Problem("Internal Server Error"));

            int migrationPercent = int.Parse(Environment.GetEnvironmentVariable("MOVIES_MIGRATION_PERCENT") ?? "0");
            bool gradualMigration = bool.Parse(Environment.GetEnvironmentVariable("GRADUAL_MIGRATION") ?? "false");
            var random = new Random();

            app.Use(async (context, next) =>
            {
                try
                {
                    var httpClient = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
                    string targetUrl;

                    if (context.Request.Path.StartsWithSegments("/api/movies") && gradualMigration)
                    {
                        var randomPercent = random.Next(0, 100);
                        Console.WriteLine($"Request to {context.Request.Path}: random {randomPercent} vs threshold {migrationPercent}");
                        if (randomPercent <= migrationPercent)
                        {
                            Console.WriteLine("Routing to movies-service");
                            targetUrl = "http://movies-service:8081";
                        }
                        else
                        {
                            Console.WriteLine("Routing to monolith");
                            targetUrl = "http://monolith:8080";
                        }
                    }
                    else
                    {
                        if (context.Request.Path.StartsWithSegments("/api/events"))
                        {
                            targetUrl = "http://events-service:8082";
                        }
                        else
                        {
                            targetUrl = "http://monolith:8080";
                        }
                    }

                    await ProxyTo(context, httpClient, targetUrl);
                    await next();
                }
                catch (Exception ex)
                {
                    app.Logger.LogError(ex, "Proxy error");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync("Proxy error occurred");
                }
                
            });
            
            app.Logger.LogInformation($"Service started on http://0.0.0.0:8000");
            app.Run();

            async Task ProxyTo(HttpContext context, HttpClient httpClient, string targetUrl)
            {
                var targetUri = new Uri(targetUrl + context.Request.Path + context.Request.QueryString);

                var requestMessage = new HttpRequestMessage
                {
                    RequestUri = targetUri,
                    Method = new HttpMethod(context.Request.Method)
                };

                if (!string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(context.Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    requestMessage.Content = new StreamContent(context.Request.Body);
                }

                foreach (var header in context.Request.Headers)
                {
                    if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    {
                        requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                    }
                }

                using var responseMessage = await httpClient.SendAsync(requestMessage, context.RequestAborted);

                context.Response.StatusCode = (int)responseMessage.StatusCode;

                foreach (var header in responseMessage.Headers)
                {
                    if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                        continue;

                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }

                foreach (var header in responseMessage.Content.Headers)
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }

                await responseMessage.Content.CopyToAsync(context.Response.Body);
            }
        }
    }
}
