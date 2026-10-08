using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TabDeck;

/// <summary>
/// Prende le chiavi locali delle lampade entrando con un QR, senza nessun
/// account da sviluppatore.
///
/// E' la strada che usa Home Assistant dal 2024: si mostra un codice, lo si
/// inquadra con l'app Smart Life, si conferma, e Tuya consegna l'elenco dei
/// dispositivi con dentro la chiave di ognuno. Al posto di Access ID e Access
/// Secret - progetto cloud, data center giusto, prova che scade dopo un mese -
/// serve solo il **codice utente**, che sta scritto nell'app.
///
/// Ci si presenta con l'identificativo pubblico dell'integrazione di Home
/// Assistant: e' quello che rende superfluo l'account da sviluppatore. Non e'
/// un'interfaccia documentata da Tuya, quindi un giorno potrebbe smettere di
/// funzionare - ma le chiavi servono una volta sola, e quelle gia' prese
/// restano buone.
///
/// Le chiamate all'elenco viaggiano cifrate in AES-GCM con una chiave derivata
/// dal token e da un numero casuale per richiesta, e firmate in HMAC-SHA256.
/// </summary>
public static class TuyaSharing
{
    private const string Identificativo = "HA_3y9q4ak7g4ephrvke";
    private const string Schema = "haauthorize";
    private const string Portale = "https://apigw.iotbing.com";

    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public sealed record Trovata(string Id, string Nome, string Chiave, string Ip, string Tipo);

    /// <summary>Quel che serve sapere fra un passo e l'altro dell'accesso.</summary>
    public sealed class Sessione
    {
        public string Codice = "";          // il token dietro il QR
        public string AccessToken = "";
        public string RefreshToken = "";
        public string Portale = "";
        public string Utente = "";
    }

    /// <summary>Il testo da mettere nel QR: l'app riconosce questo indirizzo.</summary>
    public static string TestoQr(string codice) => "tuyaSmart--qrLogin?token=" + codice;

    // ---- accesso ----

    /// <summary>Chiede a Tuya un codice da mostrare. Scade in pochi minuti.</summary>
    public static async Task<(string Codice, string Errore)> ChiediCodice(string codiceUtente)
    {
        try
        {
            string url = $"{Portale}/v1.0/m/life/home-assistant/qrcode/tokens"
                         + $"?clientid={Identificativo}&usercode={Uri.EscapeDataString(codiceUtente)}&schema={Schema}";
            using var risposta = await http.PostAsync(url, null);
            using var doc = JsonDocument.Parse(await risposta.Content.ReadAsStringAsync());

            if (!Riuscito(doc.RootElement, out string errore)) return ("", errore);
            return (Testo(doc.RootElement.GetProperty("result"), "qrcode"), "");
        }
        catch (Exception e)
        {
            return ("", e.Message);
        }
    }

    /// <summary>
    /// Guarda se il codice e' stato inquadrato e confermato. Finche' non lo e'
    /// risponde di no senza che sia un errore: e' cosi' che si aspetta.
    /// </summary>
    public static async Task<(bool Entrato, string Errore)> Esito(Sessione s, string codiceUtente)
    {
        try
        {
            string url = $"{Portale}/v1.0/m/life/home-assistant/qrcode/tokens/{s.Codice}"
                         + $"?clientid={Identificativo}&usercode={Uri.EscapeDataString(codiceUtente)}";
            using var risposta = await http.GetAsync(url);
            using var doc = JsonDocument.Parse(await risposta.Content.ReadAsStringAsync());

            var root = doc.RootElement;
            if (!root.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True)
                return (false, "");                 // non ancora: si riprova

            var r = root.GetProperty("result");
            s.AccessToken = Testo(r, "access_token");
            s.RefreshToken = Testo(r, "refresh_token");
            s.Portale = Testo(r, "endpoint").TrimEnd('/');
            s.Utente = Testo(r, "username");
            return s.AccessToken.Length > 0
                ? (true, "")
                : (false, "");
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    // ---- dispositivi ----

    /// <summary>Le lampade dell'account, con nome, chiave, indirizzo e categoria.</summary>
    public static async Task<(List<Trovata> Luci, string Errore)> Dispositivi(Sessione s)
    {
        var luci = new List<Trovata>();
        try
        {
            var (case_, errore) = await Firmata(s, "/v1.0/m/life/users/homes", null);
            if (errore.Length > 0) return (luci, errore);

            foreach (var casa in case_!.Value.EnumerateArray())
            {
                string id = casa.TryGetProperty("ownerId", out var o) ? o.ToString() : "";
                if (id.Length == 0) continue;

                var (dispositivi, e2) = await Firmata(s, "/v1.0/m/life/ha/home/devices",
                    new Dictionary<string, string> { ["homeId"] = id });
                if (e2.Length > 0) return (luci, e2);

                foreach (var d in dispositivi!.Value.EnumerateArray())
                {
                    string devId = Testo(d, "id");
                    if (devId.Length == 0) continue;
                    luci.Add(new Trovata(devId, Parola(Testo(d, "name")), Testo(d, "local_key"),
                                         Testo(d, "ip"), Testo(d, "category")));
                }
            }
            return luci.Count == 0 ? (luci, "l'account non ha dispositivi") : (luci, "");
        }
        catch (Exception e)
        {
            return (luci, e.Message);
        }
    }

    /// <summary>
    /// Una chiamata firmata e cifrata. La chiave di cifratura nasce dal numero
    /// casuale della richiesta e dal token di rinnovo, quindi cambia ogni volta
    /// e non si puo' registrare una richiesta per rimandarla dopo.
    /// </summary>
    private static async Task<(JsonElement? Risultato, string Errore)> Firmata(
        Sessione s, string percorso, Dictionary<string, string>? parametri)
    {
        string rid = Guid.NewGuid().ToString();
        string hashKey = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(rid + s.RefreshToken))).ToLowerInvariant();
        string segreto = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(rid), Encoding.UTF8.GetBytes(hashKey)))
            .ToLowerInvariant()[..16];

        string encdata = "";
        string url = s.Portale + percorso;
        if (parametri is { Count: > 0 })
        {
            string json = "{" + string.Join(",", parametri.Select(
                p => $"\"{p.Key}\":\"{p.Value}\"")) + "}";
            encdata = Cifra(json, segreto);
            url += "?encdata=" + Uri.EscapeDataString(encdata);
        }

        long adesso = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-appKey", Identificativo);
        req.Headers.Add("X-requestId", rid);
        req.Headers.Add("X-time", adesso.ToString());
        req.Headers.Add("X-token", s.AccessToken);

        // La firma copre le intestazioni non vuote, in quest'ordine, piu' il
        // contenuto cifrato. X-sid qui e' sempre vuoto e quindi non entra.
        string daFirmare = $"X-appKey={Identificativo}||X-requestId={rid}"
                           + $"||X-time={adesso}||X-token={s.AccessToken}" + encdata;
        req.Headers.Add("X-sign", Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(hashKey), Encoding.UTF8.GetBytes(daFirmare))).ToLowerInvariant());

        using var risposta = await http.SendAsync(req);
        using var doc = JsonDocument.Parse(await risposta.Content.ReadAsStringAsync());
        if (!Riuscito(doc.RootElement, out string errore)) return (null, errore);

        if (!doc.RootElement.TryGetProperty("result", out var cifrato))
            return (null, "risposta senza risultato");

        using var chiaro = JsonDocument.Parse(Decifra(cifrato.GetString() ?? "", segreto));
        return (chiaro.RootElement.Clone(), "");
    }

    // ---- cifratura ----

    /// <summary>
    /// L'alfabeto del numero usa-e-getta: niente lettere che si confondono fra
    /// loro, perche' viaggia come testo e non come byte.
    /// </summary>
    private const string AlfabetoNonce = "ABCDEFGHJKMNPQRSTWXYZabcdefhijkmnprstwxyz2345678";

    private static string Cifra(string chiaro, string segreto)
    {
        var nonce = new byte[12];
        for (int i = 0; i < 12; i++)
            nonce[i] = (byte)AlfabetoNonce[RandomNumberGenerator.GetInt32(AlfabetoNonce.Length)];

        byte[] dati = Encoding.UTF8.GetBytes(chiaro);
        var cifrato = new byte[dati.Length];
        var firma = new byte[16];
        using var gcm = new AesGcm(Encoding.UTF8.GetBytes(segreto), 16);
        gcm.Encrypt(nonce, dati, cifrato, firma);

        // Due base64 attaccati, non uno solo: il primo e' sempre di 16
        // caratteri, ed e' cosi' che l'altro capo ritrova il confine.
        return Convert.ToBase64String(nonce) + Convert.ToBase64String(cifrato.Concat(firma).ToArray());
    }

    private static string Decifra(string base64, string segreto)
    {
        byte[] tutto = Convert.FromBase64String(base64);
        var nonce = tutto[..12];
        var cifrato = tutto[12..^16];
        var firma = tutto[^16..];

        var chiaro = new byte[cifrato.Length];
        using var gcm = new AesGcm(Encoding.UTF8.GetBytes(segreto), 16);
        gcm.Decrypt(nonce, cifrato, firma, chiaro);
        return Encoding.UTF8.GetString(chiaro);
    }

    // ---- utilita' ----

    private static bool Riuscito(JsonElement root, out string errore)
    {
        if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True)
        {
            errore = "";
            return true;
        }
        string msg = Testo(root, "msg");
        errore = msg.Length > 0 ? msg : "il servizio Tuya ha rifiutato la richiesta";
        return false;
    }

    private static string Testo(JsonElement o, string nome) =>
        o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Il nome dato nell'app, ridotto a una parola comoda da scrivere.</summary>
    private static string Parola(string grezzo)
    {
        var sb = new StringBuilder();
        foreach (char c in grezzo.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        return sb.ToString().Trim('-');
    }
}
