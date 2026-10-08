using System.Globalization;
using System.Windows.Data;

namespace TabDeck;

/// <summary>
/// Dal nome di un'icona al suo tracciato nel quadrato di 24, per i Path della
/// finestra: le voci della barra portano il nome nel Tag, e il colore lo decide
/// il Path, cosi' la voce scelta si accende senza un'immagine per ogni stato.
/// </summary>
public sealed class FormaIcona : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Pittogrammi.Forma(value as string ?? parameter as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
