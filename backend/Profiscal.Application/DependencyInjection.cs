using Microsoft.Extensions.DependencyInjection;

namespace Profiscal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register MediatR, FluentValidation, AutoMapper here when needed
        return services;
    }
}
