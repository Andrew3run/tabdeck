using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TabDeck;

/// <summary>
/// Come raggiungere OBS e l'account Twitch. Sta in un file suo,
/// config/streaming.json, e non in tabdeck.json: dentro ci sono una password
/// e i gettoni di Twitch, e pacchetto.ps1 lo lascia fuori come le chiavi
/// delle lampade, a meno di -ConChiavi.
/// </summary>
public sealed class StreamingConfig
{
    public string ObsHost { get; set; } = "localhost";
    public int ObsPorta { get; set; } = 4455;
    public string ObsPassword { get; set; } = "";

    /// <summary>
    /// L'applicazione registrata su dev.twitch.tv, di tipo « Public »: e' quella
    /// che permette il collegamento col codice senza nessun segreto sul PC.
    /// </summary>
    public string TwitchClientId { get; set; } = "";
    public string TwitchToken { get; set; } = "";
    public string TwitchRinnovo { get; set; } = "";
    public string TwitchUtente { get; set; } = "";
    public string TwitchId { get; set; } = "";
}

/// <summary>
/// OBS e Twitch per i pulsanti del deck. Niente si collega da solo: OBS si
/// apre alla prima pressione e resta aperto, Twitch si collega solo col
/// pulsante « Collega » dell'editor.
/// </summary>
public static class Streaming
{
    private static readonly string Percorso = Path.Combine(ConfigFile.Directory(), "streaming.json");

    public static StreamingConfig Config { get; private set; } = ConfigFile.Load<StreamingConfig>(Percorso);

    public static readonly ObsClient Obs = new();
    public static readonly TwitchClient Twitch = new();

    public static void Salva() => ConfigFile.Save(Percorso, Config);

    // ---- i comandi, cosi' come compaiono nell'elenco dell'editor ----

    public const string ObsScena = "Cambia scena";
    public const string ObsDiretta = "Avvia/ferma diretta";
    public const string ObsAvviaDiretta = "Avvia diretta";
    public const string ObsFermaDiretta = "Ferma diretta";
    public const string ObsRegistra = "Avvia/ferma registrazione";
    public const string ObsPausa = "Pausa/riprendi registrazione";
    public const string ObsMuto = "Muto on/off";
    public const string ObsFonte = "Mostra/nascondi fonte";
    public const string ObsReplay = "Avvia/ferma replay";
    public const string ObsSalvaReplay = "Salva replay";
    public const string ObsCamera = "Camera virtuale on/off";
    public const string ObsTransizione = "Transizione (studio)";

    public static readonly string[] ComandiObs =
    {
        ObsScena, ObsDiretta, ObsAvviaDiretta, ObsFermaDiretta, ObsRegistra, ObsPausa,
        ObsMuto, ObsFonte, ObsReplay, ObsSalvaReplay, ObsCamera, ObsTransizione,
    };

    public const string TwClip = "Crea clip";
    public const string TwMarker = "Segna il momento";
    public const string TwChat = "Messaggio in chat";
    public const string TwPubblicita = "Pubblicita'";
    public const string TwTitolo = "Cambia titolo";
    public const string TwCategoria = "Cambia categoria";
    public const string TwEmote = "Solo emote on/off";
    public const string TwFollower = "Solo follower on/off";
    public const string TwLenta = "Modalita' lenta on/off";
    public const string TwAbbonati = "Solo abbonati on/off";
    public const string TwShoutout = "Shoutout";
    public const string TwRaid = "Raid";
    public const string TwAnnullaRaid = "Annulla raid";

    public static readonly string[] ComandiTwitch =
    {
        TwClip, TwMarker, TwChat, TwPubblicita, TwTitolo, TwCategoria,
        TwEmote, TwFollower, TwLenta, TwAbbonati, TwShoutout, TwRaid, TwAnnullaRaid,
    };

    /// <summary>
    /// Che cosa chiede il secondo campo per quel comando: l'etichetta, se e'
    /// obbligatorio, e se i valori si possono prendere da OBS. Null se il
    /// comando non ha niente da chiedere.
    /// </summary>
    public static (string Etichetta, bool Obbligatorio, string? DaObs)? Argomento(string tipo, string comando)
    {
        string c = comando.Trim();
        if (tipo == "obs")
        {
            if (Uguale(c, ObsScena)) return ("Scena", true, "scene");
            if (Uguale(c, ObsMuto)) return ("Fonte audio", false, "fonti");   // vuoto: il microfono di OBS
            if (Uguale(c, ObsFonte)) return ("Fonte", true, "fonti");
            return null;
        }
        if (tipo == "twitch")
        {
            if (Uguale(c, TwMarker)) return ("Descrizione", false, null);
            if (Uguale(c, TwChat)) return ("Messaggio", true, null);
            if (Uguale(c, TwPubblicita)) return ("Secondi", false, null);
            if (Uguale(c, TwTitolo)) return ("Titolo", true, null);
            if (Uguale(c, TwCategoria)) return ("Categoria", true, null);
            if (Uguale(c, TwFollower)) return ("Minuti", false, null);
            if (Uguale(c, TwLenta)) return ("Secondi", false, null);
            if (Uguale(c, TwShoutout) || Uguale(c, TwRaid)) return ("Canale", true, null);
            return null;
        }
        return null;
    }

    public static string Controlla(ActionSpec azione)
    {
        string tipo = azione.Type.ToLowerInvariant();
        var comandi = tipo == "obs" ? ComandiObs : ComandiTwitch;
        string c = azione.Value.Trim();
        if (c.Length == 0) return "Manca il comando.";
        if (!comandi.Any(x => Uguale(x, c))) return $"'{c}' non e' un comando di {(tipo == "obs" ? "OBS" : "Twitch")}.";

        var arg = Argomento(tipo, c);
        if (arg is { Obbligatorio: true } && azione.Args.Trim().Length == 0) return $"Manca: {arg.Value.Etichetta.ToLowerInvariant()}.";
        if (tipo == "twitch" && !Twitch.Collegato) return "Twitch non e' collegato.";
        return "";
    }

    /// <summary>Esegue il comando; ritorna la riga da scrivere nel registro.</summary>
    public static string Esegui(ActionSpec azione)
    {
        string tipo = azione.Type.ToLowerInvariant();
        return tipo == "obs"
            ? Obs.Esegui(azione.Value.Trim(), azione.Args.Trim()).GetAwaiter().GetResult()
            : Twitch.Esegui(azione.Value.Trim(), azione.Args.Trim()).GetAwaiter().GetResult();
    }

    internal static bool Uguale(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public sealed class StreamingErrore(string messaggio) : Exception(messaggio);

/// <summary>
/// Il server WebSocket di OBS, protocollo 5 (OBS 28 e successivi, gia' dentro
/// OBS: Strumenti › Impostazioni del server WebSocket). Una richiesta alla
/// volta e nessun evento sottoscritto: si legge solo la risposta a quel che
/// si e' chiesto.
/// </summary>
public sealed class ObsClient
{
    private readonly SemaphoreSlim turno = new(1, 1);
    private ClientWebSocket? ws;

    public async Task<string> Esegui(string comando, string arg)
    {
        string c = comando;
        if (Streaming.Uguale(c, Streaming.ObsScena))
        {
            await Chiedi("SetCurrentProgramScene", new JsonObject { ["sceneName"] = arg });
            return $"OBS: scena « {arg} »";
        }
        if (Streaming.Uguale(c, Streaming.ObsDiretta)) return Stato("diretta", await Chiedi("ToggleStream"));
        if (Streaming.Uguale(c, Streaming.ObsAvviaDiretta)) { await Chiedi("StartStream"); return "OBS: diretta avviata"; }
        if (Streaming.Uguale(c, Streaming.ObsFermaDiretta)) { await Chiedi("StopStream"); return "OBS: diretta fermata"; }
        if (Streaming.Uguale(c, Streaming.ObsRegistra)) return Stato("registrazione", await Chiedi("ToggleRecord"));
        if (Streaming.Uguale(c, Streaming.ObsPausa)) { await Chiedi("ToggleRecordPause"); return "OBS: registrazione in pausa o ripresa"; }
        if (Streaming.Uguale(c, Streaming.ObsReplay)) return Stato("replay", await Chiedi("ToggleReplayBuffer"));
        if (Streaming.Uguale(c, Streaming.ObsSalvaReplay)) { await Chiedi("SaveReplayBuffer"); return "OBS: replay salvato"; }
        if (Streaming.Uguale(c, Streaming.ObsCamera)) return Stato("camera virtuale", await Chiedi("ToggleVirtualCam"));
        if (Streaming.Uguale(c, Streaming.ObsTransizione)) { await Chiedi("TriggerStudioModeTransition"); return "OBS: transizione"; }
        if (Streaming.Uguale(c, Streaming.ObsMuto))
        {
            // Senza nome, il microfono impostato in OBS (Audio › Mic/Aux): il
            // nome di quella fonte cambia con la lingua di OBS, il ruolo no.
            if (arg.Length == 0)
            {
                var speciali = await Chiedi("GetSpecialInputs");
                arg = speciali.TryGetProperty("mic1", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                if (arg.Length == 0) throw new StreamingErrore("in OBS non c'e' un microfono impostato");
            }
            var r = await Chiedi("ToggleInputMute", new JsonObject { ["inputName"] = arg });
            return $"OBS: « {arg} » {(r.GetProperty("inputMuted").GetBoolean() ? "muto" : "con audio")}";
        }
        if (Streaming.Uguale(c, Streaming.ObsFonte)) return await AlternaFonte(arg);
        throw new StreamingErrore($"comando OBS sconosciuto: {comando}");
    }

    private static string Stato(string cosa, JsonElement r) =>
        $"OBS: {cosa} {(r.TryGetProperty("outputActive", out var a) && a.GetBoolean() ? "accesa" : "spenta")}";

    /// <summary>
    /// Cerca la fonte prima nella scena in onda, poi nelle altre: chi preme
    /// « mostra la webcam » pensa a quella che si vede adesso.
    /// </summary>
    private async Task<string> AlternaFonte(string fonte)
    {
        var inOnda = (await Chiedi("GetCurrentProgramScene")).GetProperty("currentProgramSceneName").GetString() ?? "";
        var scene = new List<string> { inOnda };
        scene.AddRange((await Scene()).Where(s => s != inOnda));

        foreach (var scena in scene)
        {
            JsonElement id;
            try
            {
                id = (await Chiedi("GetSceneItemId", new JsonObject { ["sceneName"] = scena, ["sourceName"] = fonte }))
                    .GetProperty("sceneItemId");
            }
            catch (StreamingErrore)
            {
                continue;   // non sta in questa scena
            }

            int item = id.GetInt32();
            bool acceso = (await Chiedi("GetSceneItemEnabled", new JsonObject { ["sceneName"] = scena, ["sceneItemId"] = item }))
                .GetProperty("sceneItemEnabled").GetBoolean();
            await Chiedi("SetSceneItemEnabled", new JsonObject
            {
                ["sceneName"] = scena, ["sceneItemId"] = item, ["sceneItemEnabled"] = !acceso,
            });
            return $"OBS: « {fonte} » {(acceso ? "nascosta" : "mostrata")} in « {scena} »";
        }
        throw new StreamingErrore($"la fonte « {fonte} » non sta in nessuna scena");
    }

    /// <summary>Le scene, nell'ordine in cui OBS le elenca a schermo.</summary>
    public async Task<List<string>> Scene()
    {
        var r = await Chiedi("GetSceneList");
        var nomi = r.GetProperty("scenes").EnumerateArray()
            .Select(s => s.GetProperty("sceneName").GetString() ?? "").ToList();
        nomi.Reverse();   // OBS le da' dal basso verso l'alto
        return nomi;
    }

    public async Task<List<string>> Fonti()
    {
        var r = await Chiedi("GetInputList");
        return r.GetProperty("inputs").EnumerateArray()
            .Select(s => s.GetProperty("inputName").GetString() ?? "").OrderBy(s => s).ToList();
    }

    /// <summary>Si collega da capo, per il « Prova » dell'editor: ritorna la versione di OBS.</summary>
    public async Task<string> Prova()
    {
        await turno.WaitAsync();
        try { Chiudi(); } finally { turno.Release(); }
        var r = await Chiedi("GetVersion");
        return r.GetProperty("obsVersion").GetString() ?? "?";
    }

    public void Dimentica()
    {
        turno.Wait();
        try { Chiudi(); } finally { turno.Release(); }
    }

    private async Task<JsonElement> Chiedi(string richiesta, JsonObject? dati = null)
    {
        await turno.WaitAsync();
        try
        {
            // Un secondo tentativo, uno solo: OBS chiuso e riaperto lascia qui un
            // collegamento che sembra vivo finche' non ci si scrive dentro.
            for (int tentativo = 0; ; tentativo++)
            {
                try
                {
                    if (ws is not { State: WebSocketState.Open }) await Collega();
                    return await Richiesta(richiesta, dati);
                }
                catch (Exception e) when (tentativo == 0 && e is WebSocketException or IOException)
                {
                    Chiudi();
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException)
        {
            Chiudi();
            var cfg = Streaming.Config;
            throw new StreamingErrore($"OBS non risponde su {cfg.ObsHost}:{cfg.ObsPorta}");
        }
        finally
        {
            turno.Release();
        }
    }

    private async Task Collega()
    {
        Chiudi();
        var cfg = Streaming.Config;
        var nuovo = new ClientWebSocket();
        using var tempo = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await nuovo.ConnectAsync(new Uri($"ws://{cfg.ObsHost.Trim()}:{cfg.ObsPorta}"), tempo.Token);
        ws = nuovo;

        var hello = await Leggi();
        var identify = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
        if (hello.GetProperty("d").TryGetProperty("authentication", out var auth))
        {
            if (cfg.ObsPassword.Length == 0) throw new StreamingErrore("OBS vuole una password");
            string segreto = Sha(cfg.ObsPassword + auth.GetProperty("salt").GetString());
            identify["authentication"] = Sha(segreto + auth.GetProperty("challenge").GetString());
        }
        await Scrivi(new JsonObject { ["op"] = 1, ["d"] = identify });

        var risposta = await Leggi();
        if (risposta.GetProperty("op").GetInt32() != 2) throw new StreamingErrore("OBS ha rifiutato il collegamento");
    }

    private static string Sha(string testo) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(testo)));

    private async Task<JsonElement> Richiesta(string richiesta, JsonObject? dati)
    {
        string id = Guid.NewGuid().ToString("N");
        var d = new JsonObject { ["requestType"] = richiesta, ["requestId"] = id };
        if (dati is not null) d["requestData"] = dati;
        await Scrivi(new JsonObject { ["op"] = 6, ["d"] = d });

        while (true)
        {
            var m = await Leggi();
            if (m.GetProperty("op").GetInt32() != 7) continue;
            var r = m.GetProperty("d");
            if (r.GetProperty("requestId").GetString() != id) continue;

            var stato = r.GetProperty("requestStatus");
            if (!stato.GetProperty("result").GetBoolean())
            {
                string perche = stato.TryGetProperty("comment", out var c) ? c.GetString() ?? "" : "";
                throw new StreamingErrore(perche.Length > 0 ? $"OBS: {perche}" : $"OBS ha rifiutato {richiesta} (codice {stato.GetProperty("code").GetInt32()})");
            }
            return r.TryGetProperty("responseData", out var risposta) ? risposta.Clone() : default;
        }
    }

    private async Task Scrivi(JsonObject messaggio)
    {
        using var tempo = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await ws!.SendAsync(Encoding.UTF8.GetBytes(messaggio.ToJsonString()), WebSocketMessageType.Text, true, tempo.Token);
    }

    private async Task<JsonElement> Leggi()
    {
        using var tempo = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var buffer = new byte[16 * 1024];
        using var tutto = new MemoryStream();
        while (true)
        {
            var r = await ws!.ReceiveAsync(buffer, tempo.Token);
            if (r.MessageType == WebSocketMessageType.Close)
            {
                // 4009: autenticazione fallita. Gli altri codici di chiusura
                // dicono cose che a chi preme un pulsante non servono.
                var codice = (int?)ws.CloseStatus;
                Chiudi();
                throw new StreamingErrore(codice == 4009 ? "password di OBS sbagliata" : "OBS ha chiuso il collegamento");
            }
            tutto.Write(buffer, 0, r.Count);
            if (r.EndOfMessage) break;
        }
        using var doc = JsonDocument.Parse(tutto.ToArray());
        return doc.RootElement.Clone();
    }

    private void Chiudi()
    {
        try { ws?.Abort(); ws?.Dispose(); } catch { /* sta gia' chiuso */ }
        ws = null;
    }
}

/// <summary>
/// L'API Helix di Twitch, con un gettone dell'utente preso col codice del
/// dispositivo: si apre twitch.tv/activate, si conferma, e il PC non vede mai
/// la password. Il gettone scade dopo qualche ora e si rinnova da solo con
/// quello di rinnovo, senza segreto perche' l'applicazione e' « Public ».
/// </summary>
public sealed class TwitchClient
{
    private const string Ambiti =
        "clips:edit channel:manage:broadcast user:write:chat channel:edit:commercial " +
        "moderator:manage:chat_settings channel:manage:raids moderator:manage:shoutouts";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public bool Collegato => Streaming.Config.TwitchToken.Length > 0 && Streaming.Config.TwitchId.Length > 0;

    /// <summary>
    /// Primo mezzo del collegamento: Twitch da' un codice e l'indirizzo dove
    /// confermarlo. Il secondo mezzo e' <see cref="Attendi"/>.
    /// </summary>
    public async Task<(string Codice, string Indirizzo, string Dispositivo, int Ogni, int Scade)> Inizia()
    {
        var cfg = Streaming.Config;
        if (cfg.TwitchClientId.Trim().Length == 0) throw new StreamingErrore("manca il Client ID");
        var r = await Modulo("https://id.twitch.tv/oauth2/device", new()
        {
            ["client_id"] = cfg.TwitchClientId.Trim(),
            ["scopes"] = Ambiti,
        });
        return (r.GetProperty("user_code").GetString()!, r.GetProperty("verification_uri").GetString()!,
                r.GetProperty("device_code").GetString()!, r.GetProperty("interval").GetInt32(),
                r.GetProperty("expires_in").GetInt32());
    }

    public async Task<string> Attendi(string dispositivo, int ogni, int scade, CancellationToken stop)
    {
        var cfg = Streaming.Config;
        var fine = DateTime.UtcNow.AddSeconds(scade);
        while (DateTime.UtcNow < fine)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, ogni)), stop);
            try
            {
                var r = await Modulo("https://id.twitch.tv/oauth2/token", new()
                {
                    ["client_id"] = cfg.TwitchClientId.Trim(),
                    ["scopes"] = Ambiti,
                    ["device_code"] = dispositivo,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                });
                cfg.TwitchToken = r.GetProperty("access_token").GetString() ?? "";
                cfg.TwitchRinnovo = r.GetProperty("refresh_token").GetString() ?? "";
                await Chi();
                Streaming.Salva();
                return cfg.TwitchUtente;
            }
            catch (StreamingErrore e) when (e.Message.Contains("authorization_pending", StringComparison.Ordinal))
            {
                // non ha ancora confermato
            }
        }
        throw new StreamingErrore("il codice e' scaduto");
    }

    public void Scollega()
    {
        var cfg = Streaming.Config;
        cfg.TwitchToken = cfg.TwitchRinnovo = cfg.TwitchUtente = cfg.TwitchId = "";
        Streaming.Salva();
    }

    private async Task Chi()
    {
        var cfg = Streaming.Config;
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        req.Headers.TryAddWithoutValidation("Authorization", "OAuth " + cfg.TwitchToken);
        var r = await Manda(req);
        cfg.TwitchUtente = r.GetProperty("login").GetString() ?? "";
        cfg.TwitchId = r.GetProperty("user_id").GetString() ?? "";
    }

    private async Task Rinnova()
    {
        var cfg = Streaming.Config;
        if (cfg.TwitchRinnovo.Length == 0) throw new StreamingErrore("Twitch va ricollegato");
        try
        {
            var r = await Modulo("https://id.twitch.tv/oauth2/token", new()
            {
                ["client_id"] = cfg.TwitchClientId.Trim(),
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = cfg.TwitchRinnovo,
            });
            cfg.TwitchToken = r.GetProperty("access_token").GetString() ?? "";
            cfg.TwitchRinnovo = r.GetProperty("refresh_token").GetString() ?? cfg.TwitchRinnovo;
            Streaming.Salva();
        }
        catch (StreamingErrore)
        {
            throw new StreamingErrore("Twitch va ricollegato");
        }
    }

    public async Task<string> Esegui(string comando, string arg)
    {
        var cfg = Streaming.Config;
        if (!Collegato) throw new StreamingErrore("Twitch non e' collegato");
        string io = cfg.TwitchId;
        string c = comando;

        if (Streaming.Uguale(c, Streaming.TwClip))
        {
            var r = await Helix(HttpMethod.Post, $"clips?broadcaster_id={io}");
            return "Twitch: clip creata · " + r.GetProperty("data")[0].GetProperty("edit_url").GetString();
        }
        if (Streaming.Uguale(c, Streaming.TwMarker))
        {
            var corpo = new JsonObject { ["user_id"] = io };
            if (arg.Length > 0) corpo["description"] = arg.Length > 140 ? arg[..140] : arg;
            await Helix(HttpMethod.Post, "streams/markers", corpo);
            return "Twitch: momento segnato";
        }
        if (Streaming.Uguale(c, Streaming.TwChat))
        {
            await Helix(HttpMethod.Post, "chat/messages", new JsonObject
            {
                ["broadcaster_id"] = io, ["sender_id"] = io, ["message"] = arg,
            });
            return "Twitch: messaggio mandato";
        }
        if (Streaming.Uguale(c, Streaming.TwPubblicita))
        {
            int secondi = int.TryParse(arg, out int s) ? Math.Clamp(s, 30, 180) : 30;
            await Helix(HttpMethod.Post, "channels/commercial", new JsonObject { ["broadcaster_id"] = io, ["length"] = secondi });
            return $"Twitch: pubblicita' di {secondi} secondi";
        }
        if (Streaming.Uguale(c, Streaming.TwTitolo))
        {
            await Helix(HttpMethod.Patch, $"channels?broadcaster_id={io}", new JsonObject { ["title"] = arg });
            return $"Twitch: titolo « {arg} »";
        }
        if (Streaming.Uguale(c, Streaming.TwCategoria))
        {
            var giochi = (await Helix(HttpMethod.Get, "games?name=" + Uri.EscapeDataString(arg))).GetProperty("data");
            if (giochi.GetArrayLength() == 0)
                giochi = (await Helix(HttpMethod.Get, "search/categories?first=1&query=" + Uri.EscapeDataString(arg))).GetProperty("data");
            if (giochi.GetArrayLength() == 0) throw new StreamingErrore($"nessuna categoria « {arg} »");
            var gioco = giochi[0];
            await Helix(HttpMethod.Patch, $"channels?broadcaster_id={io}",
                new JsonObject { ["game_id"] = gioco.GetProperty("id").GetString() });
            return $"Twitch: categoria « {gioco.GetProperty("name").GetString()} »";
        }
        if (Streaming.Uguale(c, Streaming.TwEmote)) return await Alterna("emote_mode", "solo emote", null);
        if (Streaming.Uguale(c, Streaming.TwAbbonati)) return await Alterna("subscriber_mode", "solo abbonati", null);
        if (Streaming.Uguale(c, Streaming.TwFollower))
            return await Alterna("follower_mode", "solo follower",
                ("follower_mode_duration", int.TryParse(arg, out int m) ? Math.Clamp(m, 0, 129600) : 0));
        if (Streaming.Uguale(c, Streaming.TwLenta))
            return await Alterna("slow_mode", "modalita' lenta",
                ("slow_mode_wait_time", int.TryParse(arg, out int w) ? Math.Clamp(w, 3, 120) : 30));
        if (Streaming.Uguale(c, Streaming.TwShoutout))
        {
            string altro = await IdDi(arg);
            await Helix(HttpMethod.Post, $"chat/shoutouts?from_broadcaster_id={io}&to_broadcaster_id={altro}&moderator_id={io}");
            return $"Twitch: shoutout a {arg}";
        }
        if (Streaming.Uguale(c, Streaming.TwRaid))
        {
            string altro = await IdDi(arg);
            await Helix(HttpMethod.Post, $"raids?from_broadcaster_id={io}&to_broadcaster_id={altro}");
            return $"Twitch: raid verso {arg} in preparazione";
        }
        if (Streaming.Uguale(c, Streaming.TwAnnullaRaid))
        {
            await Helix(HttpMethod.Delete, $"raids?broadcaster_id={io}");
            return "Twitch: raid annullato";
        }
        throw new StreamingErrore($"comando Twitch sconosciuto: {comando}");
    }

    private async Task<string> Alterna(string campo, string nome, (string Campo, int Valore)? extra)
    {
        string io = Streaming.Config.TwitchId;
        string dove = $"chat/settings?broadcaster_id={io}&moderator_id={io}";
        bool acceso = (await Helix(HttpMethod.Get, dove)).GetProperty("data")[0].GetProperty(campo).GetBoolean();
        var corpo = new JsonObject { [campo] = !acceso };
        if (!acceso && extra is { } e) corpo[e.Campo] = e.Valore;
        await Helix(HttpMethod.Patch, dove, corpo);
        return $"Twitch: {nome} {(acceso ? "spento" : "acceso")}";
    }

    private async Task<string> IdDi(string canale)
    {
        string login = canale.Trim().TrimStart('@').ToLowerInvariant();
        var d = (await Helix(HttpMethod.Get, "users?login=" + Uri.EscapeDataString(login))).GetProperty("data");
        if (d.GetArrayLength() == 0) throw new StreamingErrore($"il canale « {canale} » non esiste");
        return d[0].GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> Helix(HttpMethod metodo, string percorso, JsonObject? corpo = null)
    {
        for (int tentativo = 0; ; tentativo++)
        {
            var cfg = Streaming.Config;
            using var req = new HttpRequestMessage(metodo, "https://api.twitch.tv/helix/" + percorso);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + cfg.TwitchToken);
            req.Headers.TryAddWithoutValidation("Client-Id", cfg.TwitchClientId.Trim());
            if (corpo is not null) req.Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json");
            try
            {
                return await Manda(req);
            }
            catch (NonAutorizzato) when (tentativo == 0)
            {
                await Rinnova();
            }
            catch (NonAutorizzato)
            {
                throw new StreamingErrore("Twitch va ricollegato");
            }
        }
    }

    private sealed class NonAutorizzato() : Exception;

    private static async Task<JsonElement> Modulo(string indirizzo, Dictionary<string, string> campi)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, indirizzo) { Content = new FormUrlEncodedContent(campi) };
        return await Manda(req);
    }

    private static async Task<JsonElement> Manda(HttpRequestMessage req)
    {
        HttpResponseMessage r;
        try
        {
            r = await Http.SendAsync(req);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new StreamingErrore("Twitch non risponde");
        }
        using (r)
        {
            string testo = await r.Content.ReadAsStringAsync();
            if (r.StatusCode == HttpStatusCode.Unauthorized && req.RequestUri!.Host == "api.twitch.tv") throw new NonAutorizzato();
            if (!r.IsSuccessStatusCode)
            {
                string perche = testo;
                try
                {
                    using var d = JsonDocument.Parse(testo);
                    if (d.RootElement.TryGetProperty("message", out var m)) perche = m.GetString() ?? testo;
                }
                catch (JsonException) { }
                throw new StreamingErrore($"Twitch: {perche}");
            }
            if (testo.Length == 0) return default;
            using var doc = JsonDocument.Parse(testo);
            return doc.RootElement.Clone();
        }
    }
}
