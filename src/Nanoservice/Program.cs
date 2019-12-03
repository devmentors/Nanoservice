using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nanoservice
{
    public class Program
    {
        public static Task Main(string[] args)
            => Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder => webBuilder.ConfigureServices(services =>
                    {
                        IConfiguration configuration;
                        using (var serviceProvider = services.BuildServiceProvider())
                        {
                            configuration = serviceProvider.GetService<IConfiguration>();
                        }

                        services.AddRouting()
                            .AddHttpClient()
                            .Configure<AppOptions>(configuration.GetSection("app"))
                            .AddHealthChecks().AddCheck<DelayedHealthCheck>("delayed");
                    })
                    .Configure(app =>
                    {
                        var logger = app.ApplicationServices.GetService<ILogger<Program>>();
                        var options = app.ApplicationServices.GetService<IOptions<AppOptions>>().Value;

                        var id = GetOption(nameof(options.Id), options.Id);
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            id = Guid.NewGuid().ToString("N");
                        }

                        logger.LogInformation($"Nanoservice ID: {id}");
                        var message = GetOption(nameof(options.Message), options.Message);
                        var file = GetOption(nameof(options.File), options.File);
                        var nextServiceUrl = GetOption(nameof(options.NextServiceUrl),
                            options.NextServiceUrl);

                        app.UseDeveloperExceptionPage();
                        app.UseHealthChecks("/health");

                        if (options.LogRequestHeaders)
                        {
                            logger.LogInformation("Logging request headers enabled.");
                            app.Use(async (ctx, next) =>
                            {
                                var builder = new StringBuilder(Environment.NewLine);
                                foreach (var (key, value) in ctx.Request.Headers)
                                {
                                    builder.AppendLine($"{key}:{value}");
                                }

                                logger.LogInformation(builder.ToString());
                                await next();
                            });
                        }

                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("", ctx => ctx.Response.WriteAsync($"{message} [ID: {id}]"));
                            endpoints.MapGet("id", ctx => ctx.Response.WriteAsync(id));
                            endpoints.MapGet("ready", async ctx =>
                            {
                                await Task.Delay(TimeSpan.FromSeconds(options.ReadinessCheckDelay));
                                await ctx.Response.WriteAsync("ready");
                            });
                            endpoints.MapGet("file", ctx =>
                                ctx.Response.WriteAsync(File.Exists(file)
                                    ? File.ReadAllText(file)
                                    : $"File: '{file}' was not found."));
                            endpoints.MapGet("next", async ctx =>
                            {
                                var httpClient = ctx.RequestServices.GetService<IHttpClientFactory>()
                                    .CreateClient();
                                var nextMessage = await httpClient.GetStringAsync(nextServiceUrl);
                                await ctx.Response.WriteAsync($"Received a message: {nextMessage}");
                            });
                        });
                    }))
                .Build()
                .RunAsync();

        private static string GetOption(string property, string value)
            => Environment.GetEnvironmentVariable($"NANO_{property.ToUpperInvariant()}") ?? value;

        private class AppOptions
        {
            public string Id { get; set; }
            public string Message { get; set; }
            public string File { get; set; }
            public string NextServiceUrl { get; set; }
            public int HealthCheckDelay { get; set; }
            public int ReadinessCheckDelay { get; set; }
            public bool LogRequestHeaders { get; set; }
        }

        private class DelayedHealthCheck : IHealthCheck
        {
            private readonly TimeSpan _delay;

            public DelayedHealthCheck(IOptions<AppOptions> appOptions)
            {
                _delay = TimeSpan.FromSeconds(appOptions.Value.HealthCheckDelay);
            }

            public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
                CancellationToken cancellationToken = new CancellationToken())
            {
                await Task.Delay(_delay, cancellationToken);

                return HealthCheckResult.Healthy("ok");
            }
        }
    }
}
