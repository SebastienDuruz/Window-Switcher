using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using WindowSwitcher.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Commands;
using WindowSwitcher.Lib.Data.Platform.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Factories;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Factories.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Services;
using WindowSwitcher.Lib.Data.Platform.Policies;
using WindowSwitcher.Lib.Data.Platform.SystemInfo;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Updates;
using WindowSwitcher.Lib.Data.Updates.Abstractions;
using WindowSwitcher.ViewModels.Abstractions;
using WindowSwitcher.Windows.Services;

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
            services.AddSingleton<IFloatingPreviewPolicy, WindowsFloatingPreviewPolicy>();
            services.AddSingleton<INativeThumbnailRenderer, WindowsDwmNativeThumbnailRenderer>();
            services.AddSingleton<
                IFloatingWindowHandleConfigurator,
                WindowsFloatingWindowHandleConfigurator
            >();
            services.AddSingleton<IPlatformAppInfoProvider, WindowsPlatformAppInfoProvider>();
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<ICommandRunner, LinuxCommandRunner>();
            services.AddSingleton<IFloatingPreviewPolicy, LinuxFloatingPreviewPolicy>();
            services.AddSingleton<INativeThumbnailRenderer, NoOpNativeThumbnailRenderer>();
            services.AddSingleton<
                IFloatingWindowHandleConfigurator,
                NoOpFloatingWindowHandleConfigurator
            >();
            services.AddSingleton<IPlatformAppInfoProvider, LinuxPlatformAppInfoProvider>();
        }

        else
        {
            throw new PlatformNotSupportedException(
                "Only Windows and Linux are currently supported."
            );
        }

        services.AddSingleton<PlatformCapabilityStatus>();
        services.AddSingleton<WinAccessorBase>(_ => new RuntimeWinAccessorFactory().Create());
        services.AddSingleton<IPreviewFrameProvider>(serviceProvider =>
            new RuntimePreviewFrameProviderFactory(
                capabilityStatus: serviceProvider.GetRequiredService<PlatformCapabilityStatus>()
            ).Create(serviceProvider.GetRequiredService<WinAccessorBase>())
        );

        services.AddSingleton<
            IGlobalKeyboardListenerFactory,
            RuntimeGlobalKeyboardListenerFactory
        >();
        services.AddSingleton<IGlobalKeyboardListener>(serviceProvider =>
            serviceProvider.GetRequiredService<IGlobalKeyboardListenerFactory>().Create()
        );
        services.AddSingleton<HttpClient>();
        if (IsWindowsStoreBuild())
            services.AddSingleton<IAppUpdateService, StoreManagedAppUpdateService>();
        else
            services.AddSingleton<IAppUpdateService, GitHubAppUpdateService>();
        services.AddSingleton<
            IGlobalKeyboardStartupStatusService,
            GlobalKeyboardStartupStatusService
        >();
        services.AddSingleton<IGlobalKeyboardService, GlobalKeyboardService>();
        services.AddSingleton<IWindowKeybindManager, WindowKeybindManager>();
        services.AddSingleton<
            IWindowKeybindTargetCatalogService,
            WindowKeybindTargetCatalogService
        >();
        services.AddSingleton<IWindowKeybindActivator, WindowKeybindActivator>();
        services.AddSingleton<
            IGlobalWindowKeybindRuntimeService,
            GlobalWindowKeybindRuntimeService
        >();
#if SENTRY_TELEMETRY
        services.AddSingleton<ISentrySdkAdapter, SentrySdkAdapter>();
        services.AddSingleton<ITelemetrySettingsProvider, ConfigFileTelemetrySettingsProvider>();
        services.AddSingleton<IAppTelemetry, SentryAppTelemetry>();
#else
        services.AddSingleton<IAppTelemetry, NoOpAppTelemetry>();
#endif
        services.AddSingleton<
            IFloatingWindowSettingsService,
            ConfigFileFloatingWindowSettingsService
        >();
        services.AddSingleton<
            IMainWindowConfigurationService,
            ConfigFileMainWindowConfigurationService
        >();

        services.AddSingleton<SystemInfoService>();
        return services;
    }

    private static bool IsWindowsStoreBuild()
    {
        string? distributionChannel = typeof(PlatformServiceCollectionExtensions)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                string.Equals(
                    attribute.Key,
                    "TelemetryDistributionChannel",
                    StringComparison.Ordinal
                )
            )
            ?.Value;
        return string.Equals(
            distributionChannel,
            "windows_store",
            StringComparison.OrdinalIgnoreCase
        );
    }
}
