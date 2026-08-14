using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace WindowSwitcher.Hosting;

internal static class AppServiceProvider
{
    private static ServiceProvider? _serviceProvider;

    public static void Initialize()
    {
        if (_serviceProvider is not null)
            return;

        var services = new ServiceCollection();
        services.AddPlatformServices();

        _serviceProvider = services.BuildServiceProvider();
    }

    public static T GetRequiredService<T>()
        where T : notnull
    {
        if (_serviceProvider is null)
            throw new InvalidOperationException(
                "AppServiceProvider.Initialize() must be called before resolving services."
            );

        return _serviceProvider.GetRequiredService<T>();
    }

    public static async ValueTask DisposeAsync()
    {
        ServiceProvider? serviceProvider = Interlocked.Exchange(ref _serviceProvider, null);
        if (serviceProvider is not null)
            await serviceProvider.DisposeAsync().ConfigureAwait(false);
    }
}
