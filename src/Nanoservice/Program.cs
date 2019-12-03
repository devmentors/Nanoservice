using System;
using System.IO;
using System.Net.Http;
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
                        var appOptions = app.ApplicationServices.GetService<IOptions<AppOptions>>().Value;

                        var id = GetOption(nameof(appOptions.Id), appOptions.Id);
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            id = Guid.NewGuid().ToString("N");
                        }

                        logger.LogInformation($"Nanoservice ID: {id}");
                        var message = GetOption(nameof(appOptions.Message), appOptions.Message);
                        var file = GetOption(nameof(appOptions.File), appOptions.File);
                        var nextServiceUrl = GetOption(nameof(appOptions.NextServiceUrl),
                            appOptions.NextServiceUrl);

                        app.UseDeveloperExceptionPage();
                        app.UseHealthChecks("/health");
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("", ctx => ctx.Response.WriteAsync($"{message} [ID: {id}]"));
                            endpoints.MapGet("id", ctx => ctx.Response.WriteAsync(id));
                            endpoints.MapGet("ready", async ctx =>
                            {
                                await Task.Delay(TimeSpan.FromSeconds(appOptions.ReadinessCheckDelay));
                                await ctx.Response.WriteAsync("ready");
                            });
                            endpoints.MapGet("file", ctx =>
                                ctx.Response.WriteAsync(File.Exists(file)
                                    ? File.ReadAllText(file)
                                    : $"File: '{file}' was not found."));
                            endpoints.MapGet("next", async ctx =>
                            {
                                var httpClient = app.ApplicationServices.GetService<IHttpClientFactory>()
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
