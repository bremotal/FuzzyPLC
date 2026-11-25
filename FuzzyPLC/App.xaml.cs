using System.Globalization;
using System.Windows;

namespace FuzzyPLC
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── Configura cultura ANTES de abrir qualquer janela ──────────
            // Garante que ponto decimal funciona independente do Windows regional
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ".";
            culture.NumberFormat.NumberGroupSeparator   = ",";
            culture.NumberFormat.CurrencyDecimalSeparator = ".";

            CultureInfo.DefaultThreadCurrentCulture   = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            System.Threading.Thread.CurrentThread.CurrentCulture   = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;

            // ── Abre a janela principal ───────────────────────────────────
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}
