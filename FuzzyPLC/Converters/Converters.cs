using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FuzzyPLC.Models;

namespace FuzzyPLC.Converters
{
    public class BoolToVisConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;
        public object Convert(object v, Type t, object p, CultureInfo c)
        { bool b = v is true; if (Invert) b=!b; return b?Visibility.Visible:Visibility.Collapsed; }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is Visibility.Visible;
    }

    public class BehaviorLabelConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is FuzzyBehavior b ? b switch {
                FuzzyBehavior.Otimo   => "Otimo - ambos os lados",
                FuzzyBehavior.Direto  => "Direto - abaixo do ideal",
                FuzzyBehavior.Inverso => "Inverso - acima do ideal", _=>"" } : "";
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class ZoneToColorConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v?.ToString() switch {
                "IDEAL"  => new SolidColorBrush(Color.FromRgb(0x23,0x86,0x36)),
                "ABAIXO" => new SolidColorBrush(Color.FromRgb(0xC4,0x55,0x00)),
                "ACIMA"  => new SolidColorBrush(Color.FromRgb(0xB9,0x1C,0x1C)),
                _        => new SolidColorBrush(Colors.Gray) };
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class SeverityToColorConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (v is double d)
                return d < 0.25 ? new SolidColorBrush(Color.FromRgb(0x23,0x86,0x36))
                     : d < 0.60 ? new SolidColorBrush(Color.FromRgb(0xE8,0x64,0x0C))
                                : new SolidColorBrush(Color.FromRgb(0xB9,0x1C,0x1C));
            return new SolidColorBrush(Colors.Gray);
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class IsMainToColorConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is true
                ? new SolidColorBrush(Color.FromRgb(0xE8,0x64,0x0C))
                : new SolidColorBrush(Color.FromRgb(0x55,0x55,0x55));
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // Converter para NPoints — permite digitar livremente sem bloquear a cada tecla
    // Permite digitar livremente inteiros sem bloquear durante digitação
    public class IntInputConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v?.ToString() ?? "";
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            if (int.TryParse(v?.ToString(), out int result))
                return result;
            return DependencyProperty.UnsetValue;
        }
    }

    // Converter decimal robusto
    // - Exibe com ponto decimal sempre
    // - Aceita ponto OU virgula ao digitar
    // - Usa LostFocus para não travar durante digitação
    public class DoubleInputConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (v is double d)
            {
                // Exibe sem casas desnecessárias: 3 → "3", 3.5 → "3.5"
                return d == Math.Floor(d)
                    ? ((long)d).ToString()
                    : d.ToString("G6", CultureInfo.InvariantCulture);
            }
            return v?.ToString() ?? "";
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            string s = (v?.ToString() ?? "").Trim();

            // Normaliza: vírgula → ponto
            s = s.Replace(",", ".");

            // Durante digitação: não rejeitar entradas incompletas
            if (s == "" || s == "-" || s == "." || s == "-.")
                return DependencyProperty.UnsetValue;

            if (double.TryParse(s, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double result))
                return result;

            return DependencyProperty.UnsetValue;
        }
    }

    // Converter para ActionType — exibe label descritivo no ComboBox
    public class ActionTypeLabelConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is FuzzyPLC.Models.ActionType a ? a switch {
                FuzzyPLC.Models.ActionType.Direta  => "Direta  — PV↑ → CMD↑",
                FuzzyPLC.Models.ActionType.Reversa => "Reversa — PV↑ → CMD↓",
                _ => "" } : "";
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // Converter ActionType para cor indicadora
    public class ActionTypeColorConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is FuzzyPLC.Models.ActionType a
                ? a == FuzzyPLC.Models.ActionType.Direta
                    ? new SolidColorBrush(Color.FromRgb(0x23,0x86,0x36))  // verde = direta
                    : new SolidColorBrush(Color.FromRgb(0xE8,0x64,0x0C))  // laranja = reversa
                : new SolidColorBrush(Colors.Gray);
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }
}
