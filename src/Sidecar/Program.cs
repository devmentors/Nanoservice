using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sidecar
{
    public class Program
    {
        public static Task Main(string[] args)
            => Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder => webBuilder
                    .ConfigureServices(services => services.AddMvcCore())
                    .Configure(app => app
                        .UseRouting()
                        .UseEndpoints(endpoints => endpoints
                            .MapGet("", ctx => ctx.Response.WriteAsync("sidecar")))))
                .Build()
                .RunAsync();
    }
}