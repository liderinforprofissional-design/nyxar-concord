using System;
using System.IO;
using System.Windows;
using NyxarConcord.Services;

namespace NyxarConcord;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        string log = Path.Combine(AppContext.BaseDirectory, "erro.txt");

        DispatcherUnhandledException += (_, ev) =>
        {
            try { File.WriteAllText(log, ev.Exception.ToString()); } catch { }
            Diag.Log("ERRO", "DispatcherUnhandled: " + ev.Exception);
            MessageBox.Show("Erro ao iniciar. Detalhes salvos em erro.txt:\n\n" + ev.Exception.Message,
                "Nyxar Concord", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            var ex = (ev.ExceptionObject as Exception)?.ToString() ?? "Erro desconhecido";
            try { File.WriteAllText(log, ex); } catch { }
            Diag.Log("ERRO", "Unhandled: " + ex);
        };

        Diag.Log("APP", "OnStartup");
        base.OnStartup(e);
    }
}
