using System;
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
        services.AddLogging();
        services.AddPlatformServices();

        _serviceProvider = services.BuildServiceProvider();
    }

    public static T GetRequiredService<T>() where T : notnull
    {
        if (_serviceProvider is null)
            throw new InvalidOperationException("AppServiceProvider.Initialize() must be called before resolving services.");

        return _serviceProvider.GetRequiredService<T>();
    }
}
