using System;
using Microsoft.Extensions.DependencyInjection;
using WindowSwitcher.Lib.Data.Platform.Commands;
using WindowSwitcher.Lib.Data.Platform.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Factories;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Factories.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Services;
using WindowSwitcher.Lib.Data.Platform.Policies;
using WindowSwitcher.Lib.Data.Platform.SystemInfo;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

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
            services.AddSingleton<
                IFloatingWindowHandleConfigurator,
                WindowsFloatingWindowHandleConfigurator
            >();
            services.AddSingleton<
                IDependencyNotificationService,
                NoOpDependencyNotificationService
            >();
            services.AddSingleton<ISettingsPlatformPolicy, WindowsSettingsPlatformPolicy>();
            services.AddSingleton<IPlatformAppInfoProvider, WindowsPlatformAppInfoProvider>();
        }
        else if (OperatingSystem.IsLinux())
        {
            AccessorFactory.Current = new LinuxWinAccessorFactory();
            PreviewFactory.Current = new LinuxPreviewFrameProviderFactory();
            services.AddSingleton<ICommandRunner, LinuxCommandRunner>();
            services.AddSingleton<IFloatingPreviewPolicy, LinuxFloatingPreviewPolicy>();
            services.AddSingleton<
                IFloatingWindowHandleConfigurator,
                NoOpFloatingWindowHandleConfigurator
            >();
            services.AddSingleton<
                IDependencyNotificationService,
                LinuxDependencyNotificationService
            >();
            services.AddSingleton<ISettingsPlatformPolicy, LinuxSettingsPlatformPolicy>();
            services.AddSingleton<IPlatformAppInfoProvider, LinuxPlatformAppInfoProvider>();
        }
        else
        {
            throw new PlatformNotSupportedException(
                "Only Windows and Linux are currently supported."
            );
        }

        services.AddSingleton<IGlobalKeyboardListenerFactory, RuntimeGlobalKeyboardListenerFactory>();
        services.AddSingleton<IGlobalKeyboardListener>(serviceProvider =>
            serviceProvider.GetRequiredService<IGlobalKeyboardListenerFactory>().Create()
        );
        services.AddSingleton<IGlobalKeyboardService, GlobalKeyboardService>();
        services.AddSingleton<IWindowKeybindManager, WindowKeybindManager>();
        services.AddSingleton<IWindowKeybindTargetCatalogService, WindowKeybindTargetCatalogService>();
        services.AddSingleton<IWindowKeybindActivator, WindowKeybindActivator>();
        services.AddSingleton<IGlobalWindowKeybindRuntimeService, GlobalWindowKeybindRuntimeService>();

        services.AddSingleton<SystemInfoService>();
        return services;
    }
}
