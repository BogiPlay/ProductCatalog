using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace ProductCatalog;

public partial class App : Application
{
    public static readonly CultureInfo Bulgarian = new("bg-BG");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Български формат на числа и дати (49,90 €, 16.11.2026) навсякъде в приложението.
        CultureInfo.DefaultThreadCurrentCulture = Bulgarian;
        CultureInfo.DefaultThreadCurrentUICulture = Bulgarian;
        Thread.CurrentThread.CurrentCulture = Bulgarian;
        Thread.CurrentThread.CurrentUICulture = Bulgarian;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(Bulgarian.IetfLanguageTag)));

        // Последна защита: при неочаквана грешка – съобщение, а не срив.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Възникна неочаквана грешка:\n\n{e.Exception.Message}",
            "Каталог на продукти",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>Сменя цветовата тема ("Dark" или "Light") без рестартиране.</summary>
    public static void ApplyTheme(string theme)
    {
        var name = theme == "Light" ? "Light" : "Dark";
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/ProductCatalog;component/Themes/{name}.xaml", UriKind.Absolute)
        };
        Current.Resources.MergedDictionaries[0] = dictionary;
    }
}
