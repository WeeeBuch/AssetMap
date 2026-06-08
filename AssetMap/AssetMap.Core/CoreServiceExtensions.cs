using AssetMap.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AssetMap.Core;

public static class CoreServiceExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IPortfolioService, PortfolioService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IImportService, ImportService>();
        return services;
    }
}
