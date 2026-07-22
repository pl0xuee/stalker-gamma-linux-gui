using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Stalker.Gamma.Extensions;
using StalkerGamma.Gui.Services;
using StalkerGamma.Gui.ViewModels;
using StalkerGamma.Gui.Views;

namespace StalkerGamma.Gui;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = BuildServices();
        Services.GetRequiredService<SetupUtilitiesService>().Setup();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .RegisterCoreGammaServices()
            .AddSingleton<SettingsService>()
            .AddSingleton<SetupUtilitiesService>()
            .AddSingleton<UtilitiesReadyService>()
            .AddSingleton<OperationRunner>()
            .AddSingleton<LogService>()
            .AddSingleton<GetRemoteGitRepoCommit>()
            .AddSingleton<AnomalyService>()
            .AddSingleton<AppUpdateService>()
            .AddSingleton<Services.Steam.SteamLocator>()
            .AddSingleton<Services.Steam.CompatToolCatalog>()
            .AddSingleton<Services.Steam.ShortcutsVdfService>()
            .AddSingleton<Services.Steam.ConfigVdfService>()
            .AddSingleton<Services.Steam.SteamProcessService>()
            .AddSingleton<Services.Steam.ProtonPrefixService>()
            .AddSingleton<Services.Steam.ProtontricksService>()
            .AddSingleton<Services.Steam.SteamIntegrationService>()
            .AddSingleton<MainViewModel>()
            .AddSingleton<InstallViewModel>()
            .AddSingleton<UpdatesViewModel>()
            .AddSingleton<ModsViewModel>()
            .AddSingleton<Mo2ProfilesViewModel>()
            .AddSingleton<SettingsViewModel>()
            .AddSingleton<SteamSetupViewModel>()
            .BuildServiceProvider();
}
