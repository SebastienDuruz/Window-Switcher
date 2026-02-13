using System;
using Microsoft.Extensions.DependencyInjection;
using WindowSwitcherLib.Application.Platform;
using WindowSwitcherLib.Application.Services;
using WindowSwitcherLib.Infrastructure.Platform.Commands;

namespace WindowSwitcher.Hosting;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers OS-specific adapters in the composition root.
    /// </summary>
    public static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ICommandRunner, WindowsCommandRunner>();
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<ICommandRunner, LinuxCommandRunner>();
        }
        else
        {
            throw new PlatformNotSupportedException("Only Windows and Linux are currently supported.");
        }

        services.AddSingleton<SystemInfoService>();
        return services;
    }
}
