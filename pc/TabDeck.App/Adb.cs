using System.Diagnostics;

namespace TabDeck;

/// <summary>
/// adb, per la finestra: vedere i tablet attaccati, installare, togliere, mettere a
/// riposo. Tutto bloccante: si chiama da un task, mai dal thread della finestra.
/// </summary>
public static class Adb
{
    public sealed record Dispositivo(string Seriale, string Nome, string Android, int Sdk);

    public static (int Uscita, string Testo) Esegui(string adb, string argomenti, int attesaMs = 10_000)
    {
        try
        {
            var psi = new ProcessStartInfo(adb, argomenti)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return (-1, "impossibile avviare adb");
            // Le due uscite insieme: lette una dopo l'altra, adb si blocca se riempie per
            // prima quella che si legge dopo — succede con l'avanzamento di « install ».
            var fuori = p.StandardOutput.ReadToEndAsync();
            var errori = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(attesaMs))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return (-1, "adb non ha risposto in tempo");
            }
            return (p.ExitCode, fuori.Result + errori.Result);
        }
        catch (Exception e)
        {
            return (-1, e.Message);
        }
    }

    /// <summary>I dispositivi che adb vede, con lo stato: "device", "unauthorized", "offline".</summary>
    public static List<(string Seriale, string Stato)> Seriali(string adb)
    {
        var elenco = new List<(string, string)>();
        var (uscita, testo) = Esegui(adb, "devices");
        if (uscita != 0) return elenco;
        // Solo le righe col tabulatore: prima dell'elenco adb puo' scrivere che sta avviando il suo servizio.
        foreach (var riga in testo.Split('\n'))
        {
            var parti = riga.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parti.Length == 2) elenco.Add((parti[0], parti[1]));
        }
        return elenco;
    }

    /// <summary>Marca, modello e versione di Android, con una shell sola.</summary>
    public static Dispositivo Descrivi(string adb, string seriale)
    {
        var righe = Shell(adb, seriale,
                "getprop ro.product.manufacturer; getprop ro.product.model; getprop ro.build.version.release; getprop ro.build.version.sdk")
            .Split('\n').Select(r => r.Trim()).ToArray();
        string Riga(int i) => i < righe.Length ? righe[i] : "";
        int.TryParse(Riga(3), out int sdk);
        string marca = Riga(0), modello = Riga(1);
        string nome = modello.StartsWith(marca, StringComparison.OrdinalIgnoreCase) || marca.Length == 0
            ? modello
            : char.ToUpperInvariant(marca[0]) + marca[1..] + " " + modello;
        return new Dispositivo(seriale, nome.Length > 0 ? nome : seriale, Riga(2), sdk);
    }

    public static string Shell(string adb, string seriale, string comando, int attesaMs = 15_000) =>
        Esegui(adb, $"-s {seriale} shell \"{comando}\"", attesaMs).Testo.Trim();

    /// <summary>I pacchetti del tablet. « -u » comprende quelli a riposo, « -d » da' solo quelli spenti.</summary>
    public static HashSet<string> Pacchetti(string adb, string seriale, string filtro = "") =>
        Shell(adb, seriale, "pm list packages " + filtro, 30_000)
            .Split('\n')
            .Select(r => r.Trim())
            .Where(r => r.StartsWith("package:", StringComparison.Ordinal))
            .Select(r => r["package:".Length..])
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Le app dell'elenco che su questo tablet ci sono e sono in servizio
    /// (<paramref name="inServizio"/>), oppure ci sono e sono a riposo.
    /// </summary>
    public static List<string> Riposabili(string adb, Dispositivo d, IEnumerable<string> elenco, bool inServizio)
    {
        var tutte = Pacchetti(adb, d.Seriale, "-u");
        // Su Android 4.4 una app bloccata sparisce dall'elenco senza « -u »; dal 5 in poi
        // resta, e la si riconosce dall'elenco di quelle spente.
        var visibili = Pacchetti(adb, d.Seriale);
        var spente = d.Sdk <= 20 ? new HashSet<string>() : Pacchetti(adb, d.Seriale, "-d");
        return elenco
            .Where(p => tutte.Contains(p) && inServizio == (visibili.Contains(p) && !spente.Contains(p)))
            .ToList();
    }

    /// <summary>
    /// Mette a riposo o rimette in servizio. « pm block » su Android 4.4, dove « disable-user »
    /// fa cadere il package manager; dal 5 « block » non c'e' piu' e si spegne per l'utente.
    /// Niente si disinstalla: e' tutto reversibile.
    /// </summary>
    public static void Riposo(string adb, Dispositivo d, IEnumerable<string> pacchetti, bool metti)
    {
        string verbo = d.Sdk <= 20
            ? (metti ? "pm block" : "pm unblock")
            : (metti ? "pm disable-user --user 0" : "pm enable");
        // A gruppi: una shell per pacchetto sono duecento giri di adb, una sola sfonda la riga di comando.
        foreach (var gruppo in pacchetti.Chunk(25))
            Shell(adb, d.Seriale, string.Join("; ", gruppo.Select(p => $"{verbo} {p}")), 120_000);
    }
}
