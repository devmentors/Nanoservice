using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sidecar
{
    public class Program
    {
        private const string TraceHeader = "Trace";
        private static readonly string Id = Guid.NewGuid().ToString("N");

        public static Task Main(string[] args)
            => Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder => webBuilder
                    .ConfigureServices(services =>
                    {
                        IConfiguration configuration;
                        using (var serviceProvider = services.BuildServiceProvider())
                        {
                            configuration = serviceProvider.GetService<IConfiguration>();
                        }

                        services
                            .Configure<AppOptions>(configuration.GetSection("app"))
                            .AddHttpClient()
                            .AddMvcCore();
                    })
                    .Configure(app => app
                        .UseRouting()
                        .UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/_sidecar", ctx => ctx.Response.WriteAsync($"Sidecar [ID: {Id}]"));
                            endpoints.MapGet("{*url}", ctx => Handle(HttpMethod.Get, ctx));
                            endpoints.MapPost("{*url}", ctx => Handle(HttpMethod.Post, ctx));
                            endpoints.MapPut("{*url}", ctx => Handle(HttpMethod.Put, ctx));
                            endpoints.MapDelete("{*url}", ctx => Handle(HttpMethod.Delete, ctx));
                        })))
                .Build()
                .RunAsync();

        private static async Task Handle(HttpMethod method, HttpContext context)
        {
            var logger = context.RequestServices.GetService<ILogger<Program>>();
            var options = context.RequestServices.GetRequiredService<IOptions<AppOptions>>().Value;
            var downstream = options.Downstream;
            var path = context.Request.Path.HasValue ? context.Request.Path.Value : string.Empty;
            var url = $"{downstream}{path}";
            var requestId = Guid.NewGuid().ToString("N");
            var trace = context.TraceIdentifier;
            var requestHeaders = options.RequestHeaders;
            var responseHeaders = options.ResponseHeaders;

            logger.LogInformation($"Sending a request [ID: {requestId}]: {context.Request.Method} {url}");
            var httpClient = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(url),
                Method = method
            };

            request.Headers.TryAddWithoutValidation(TraceHeader, trace);
            if (requestHeaders is {})
            {
                foreach (var (key, value) in requestHeaders)
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }

            HttpResponseMessage response;
            if (context.Request.Method is "POST" || context.Request.Method is "PUT")
            {
                using var content = new StreamContent(context.Request.Body);
                request.Content = content;
                response = await httpClient.SendAsync(request);
            }
            else
            {
                response = await httpClient.SendAsync(request);
            }

            logger.LogInformation($"Received a response [ID: {requestId}]: {response}");
            context.Response.StatusCode = (int) response.StatusCode;
            context.Response.Headers.TryAdd(TraceHeader, trace);
            if (responseHeaders is {})
            {
                foreach (var (key, value) in responseHeaders)
                {
                    context.Response.Headers.TryAdd(key, value);
                }
            }

            await context.Response.WriteAsync(await response.Content.ReadAsStringAsync());
        }

        private class AppOptions
        {
            public string Downstream { get; set; }
            public IDictionary<string, string> RequestHeaders { get; set; }
            public IDictionary<string, string> ResponseHeaders { get; set; }
        }
    }
}