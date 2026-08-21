using System;
using System.IO;
using System.Windows;

namespace NyxarConcord;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        string log = Path.Combine(AppContext.BaseDirectory, "erro.txt");

        DispatcherUnhandledException += (_, ev) =>
        {
            try { File.WriteAllText(log, ev.Exception.ToString()); } catch { }
            MessageBox.Show("Erro ao iniciar. Detalhes salvos em erro.txt:\n\n" + ev.Exception.Message,
                "Nyxar Concord", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            try { File.WriteAllText(log, (ev.ExceptionObject as Exception)?.ToString() ?? "Erro desconhecido"); } catch { }
        };

        base.OnStartup(e);
    }
}
