using Microsoft.Extensions.DependencyInjection;
using Whispyr.Application.Abstractions;
using Whispyr.Infrastructure.Services;

namespace Whispyr.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services)
        {
            services.AddHttpClient<ISummaryService, SummaryService>();
            return services;
        }
    }
}