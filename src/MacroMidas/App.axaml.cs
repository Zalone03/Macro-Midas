using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MacroMidas.Services;
using MacroMidas.ViewModels;
using MacroMidas.Views;

namespace MacroMidas;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            CrashLog.Write("UI", e.Exception);
            e.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { Args: ["--selftest", ..] args } selfTest)
        {
            selfTest.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var report = args.Length > 1 ? args[1] : Path.Combine(AppPaths.Root, "autoteste.txt");
            Dispatcher.UIThread.Post(async () => selfTest.Shutdown(await Diagnostics.SelfTest.RunAsync(report)));
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppPaths.Ensure();
            var settings = new SettingsService();
            settings.Load();
            var window = new MainWindow();
            var vm = new MainWindowViewModel(settings, new FlowLibrary(), window);
            window.DataContext = vm;
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
