using AssetMap.Comms.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AssetMap.Comms;

public static class CommsServiceExtensions
{
    public static IServiceCollection AddCommsServices(this IServiceCollection services)
    {
        // FxRates — singleton (sdílený cache)
        services.AddHttpClient<IFxRateService, FxRateService>(c =>
            c.BaseAddress = new Uri("https://api.frankfurter.app/"));
        services.AddSingleton<IFxRateService>(sp =>
            new FxRateService(sp.GetRequiredService<IHttpClientFactory>()
                               .CreateClient(nameof(FxRateService))));

        // PriceService — potřebuje dva pojmenované HttpClienty
        services.AddHttpClient("CoinGecko", c =>
            c.BaseAddress = new Uri("https://api.coingecko.com/api/v3/"));
        services.AddHttpClient("YahooFinance", c =>
            c.BaseAddress = new Uri("https://query1.finance.yahoo.com/"));

        services.AddScoped<IPriceService>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new PriceService(
                sp.GetRequiredService<IFxRateService>(),
                factory.CreateClient("CoinGecko"),
                factory.CreateClient("YahooFinance"));
        });

        return services;
    }
}
