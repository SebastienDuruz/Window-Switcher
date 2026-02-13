using System;
using Microsoft.Extensions.DependencyInjection;
using WindowSwitcherLib.Application.Platform;
using WindowSwitcherLib.Application.Platform.Diagnostics;
using WindowSwitcherLib.Application.Services;
using WindowSwitcherLib.Data.Platform.WindowAccess.Factories;
using WindowSwitcherLib.Infrastructure.Platform.Commands;
using WindowSwitcherLib.Infrastructure.Platform.Diagnostics;
using WindowSwitcherLib.Infrastructure.Platform.Policies;

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
            AccessorFactory.Current = new WindowsWinAccessorFactory();
            PreviewFactory.Current = new WindowsPreviewFrameProviderFactory();
            services.AddSingleton<ICommandRunner, WindowsCommandRunner>();
            services.AddSingleton<IFloatingPreviewPolicy, WindowsFloatingPreviewPolicy>();
            services.AddSingleton<IFloatingWindowHandleConfigurator, WindowsFloatingWindowHandleConfigurator>();
            services.AddSingleton<IDependencyNotificationService, NoOpDependencyNotificationService>();
            services.AddSingleton<ISettingsPlatformPolicy, WindowsSettingsPlatformPolicy>();
            services.AddSingleton<IPlatformAppInfoProvider, WindowsPlatformAppInfoProvider>();
        }
        else if (OperatingSystem.IsLinux())
        {
            AccessorFactory.Current = new LinuxWinAccessorFactory();
            PreviewFactory.Current = new LinuxPreviewFrameProviderFactory();
            services.AddSingleton<ICommandRunner, LinuxCommandRunner>();
            services.AddSingleton<IFloatingPreviewPolicy, LinuxFloatingPreviewPolicy>();
            services.AddSingleton<IFloatingWindowHandleConfigurator, NoOpFloatingWindowHandleConfigurator>();
            services.AddSingleton<IDependencyNotificationService, LinuxDependencyNotificationService>();
            services.AddSingleton<ISettingsPlatformPolicy, LinuxSettingsPlatformPolicy>();
            services.AddSingleton<IPlatformAppInfoProvider, LinuxPlatformAppInfoProvider>();
        }
        else
        {
            throw new PlatformNotSupportedException("Only Windows and Linux are currently supported.");
        }

        services.AddSingleton<SystemInfoService>();
        return services;
    }
}
