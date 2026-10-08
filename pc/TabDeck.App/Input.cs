namespace TabDeck;

/// <summary>
/// Traduce i gesti del tablet e le azioni del deck in eventi di input reali.
///
/// Si usa SendInput e non mouse_event/keybd_event perche' e' l'unica via che
/// alimenta la coda di input a basso livello: i giochi e le applicazioni che
/// leggono lo stato grezzo della tastiera vedono questi eventi come veri.
/// </summary>
public sealed class InputInjector
{
    /// <summary>
    /// Dove finiscono gli avvisi: la finestra li mostra nel registro. Statico
    /// perche' lo usano anche i metodi di traduzione dei tasti, che non hanno
    /// niente di legato all'istanza — e di istanze ce n'e' comunque una sola.
    /// </summary>
    public static Action<string>? Log { get; set; }

    /// <summary>Area di schermo che il tablet sta mostrando, in pixel fisici.</summary>
    public Rect Source { get; set; }

    /// <summary>
    /// Dimensioni del frame inviato al tablet. Quando la cattura viene ridotta,
    /// i tocchi arrivano in questo spazio e vanno riportati a quello dello
    /// schermo, altrimenti su un monitor 1080p il dito colpirebbe l'angolo in
    /// alto a sinistra.
    /// </summary>
    public int OutputWidth { get; set; } = 1;
    public int OutputHeight { get; set; } = 1;

    private Rect virtualScreen = ScreenCapture.VirtualScreen();

    public void RefreshScreenGeometry() => virtualScreen = ScreenCapture.VirtualScreen();

    // ---- mouse ----

    /// <param name="x">Coordinata nello spazio del frame inviato, non dello schermo.</param>
    public void MoveTo(int x, int y)
    {
        int screenX = Source.X + (OutputWidth <= 1 ? x : (int)Math.Round(x * (double)Source.W / OutputWidth));
        int screenY = Source.Y + (OutputHeight <= 1 ? y : (int)Math.Round(y * (double)Source.H / OutputHeight));
        Send(MouseInput(Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK,
            Normalize(screenX, virtualScreen.X, virtualScreen.W),
            Normalize(screenY, virtualScreen.Y, virtualScreen.H)));
    }

    private static int Normalize(int value, int origin, int extent) =>
        extent <= 1 ? 0 : (int)Math.Round((value - origin) * 65535.0 / (extent - 1));

    public void LeftDown() => Send(MouseInput(Native.MOUSEEVENTF_LEFTDOWN));
    public void LeftUp() => Send(MouseInput(Native.MOUSEEVENTF_LEFTUP));

    public void LeftClick()
    {
        Send(MouseInput(Native.MOUSEEVENTF_LEFTDOWN), MouseInput(Native.MOUSEEVENTF_LEFTUP));
    }

    public void RightClick()
    {
        Send(MouseInput(Native.MOUSEEVENTF_RIGHTDOWN), MouseInput(Native.MOUSEEVENTF_RIGHTUP));
    }

    public void MiddleClick()
    {
        Send(MouseInput(Native.MOUSEEVENTF_MIDDLEDOWN), MouseInput(Native.MOUSEEVENTF_MIDDLEUP));
    }

    /// <summary>
    /// Doppio clic in un colpo solo. I quattro eventi partono insieme dentro la
    /// stessa SendInput: cosi' hanno lo stesso istante di sistema e Windows li
    /// riconosce come doppio clic anche quando la macchina e' carica, mentre
    /// due chiamate separate ogni tanto arrivavano troppo distanti.
    /// </summary>
    public void DoubleClick()
    {
        Send(MouseInput(Native.MOUSEEVENTF_LEFTDOWN), MouseInput(Native.MOUSEEVENTF_LEFTUP),
             MouseInput(Native.MOUSEEVENTF_LEFTDOWN), MouseInput(Native.MOUSEEVENTF_LEFTUP));
    }

    public void Wheel(int delta) => Send(MouseInput(Native.MOUSEEVENTF_WHEEL, 0, 0, (uint)delta));

    private static Native.INPUT MouseInput(uint flags, int dx = 0, int dy = 0, uint data = 0) => new()
    {
        type = Native.INPUT_MOUSE,
        u = new Native.INPUTUNION
        {
            mi = new Native.MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags }
        }
    };

    // ---- tastiera ----

    /// <summary>Esegue una combinazione tipo "ctrl+shift+m" o un tasto singolo "f13".</summary>
    public bool Hotkey(string combo)
    {
        if (!TryReadCombo(combo, out var keys, out string unknown))
        {
            if (unknown.Length > 0) Log?.Invoke($"Tasto sconosciuto nella combinazione: '{unknown}'");
            return false;
        }

        var inputs = new List<Native.INPUT>(keys.Count * 2);
        foreach (var vk in keys) inputs.Add(KeyInput(vk, false));
        // Rilascio in ordine inverso: i modificatori vanno mollati per ultimi.
        for (int i = keys.Count - 1; i >= 0; i--) inputs.Add(KeyInput(keys[i], true));
        Send(inputs.ToArray());
        return true;
    }

    /// <summary>
    /// Legge una combinazione senza eseguirla. Serve alla finestra per dire
    /// subito che "ctrl+shitf+m" non funzionera', invece di lasciarlo scoprire
    /// al primo tocco sul tablet quando non succede niente.
    /// </summary>
    /// <param name="unknown">Il primo nome di tasto che non esiste, se c'e'.</param>
    public static bool TryReadCombo(string combo, out List<ushort> keys, out string unknown)
    {
        keys = new List<ushort>();
        unknown = "";

        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (var part in parts)
        {
            if (!TryMapKey(part, out ushort vk))
            {
                unknown = part;
                keys.Clear();
                return false;
            }
            keys.Add(vk);
        }
        return true;
    }

    /// <summary>I nomi che si possono scrivere in una combinazione, in ordine.</summary>
    public static IEnumerable<string> NomiTasti => Keys.Keys.OrderBy(k => k, StringComparer.Ordinal);

    /// <summary>I tasti multimediali, gli unici che valgono anche senza fuoco.</summary>
    public static readonly string[] Multimediali =
        { "playpause", "next", "prev", "stopmedia", "mute", "volup", "voldown" };

    public void TapKey(ushort vk) => Send(KeyInput(vk, false), KeyInput(vk, true));

    /// <summary>
    /// Preme, o molla, dei tasti senza fare l'altra meta' del gesto.
    ///
    /// E' quello che serve alle macro che devono tenere un tasto premuto mentre
    /// succede altro — shift durante una serie di frecce, alt mentre si preme
    /// tab piu' volte. Il rilascio va all'incontrario perche' i modificatori
    /// devono essere gli ultimi a cadere.
    /// </summary>
    public void PressKeys(IReadOnlyList<ushort> keys, bool up)
    {
        if (keys.Count == 0) return;
        var inputs = new Native.INPUT[keys.Count];
        for (int i = 0; i < keys.Count; i++)
            inputs[i] = KeyInput(keys[up ? keys.Count - 1 - i : i], up);
        Send(inputs);
    }

    /// <summary>
    /// Digita testo con KEYEVENTF_UNICODE: passa qualsiasi carattere senza
    /// dipendere dal layout di tastiera attivo.
    /// </summary>
    public void TypeText(string text)
    {
        var inputs = new List<Native.INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            inputs.Add(UnicodeInput(c, false));
            inputs.Add(UnicodeInput(c, true));
        }
        if (inputs.Count > 0) Send(inputs.ToArray());
    }

    private static Native.INPUT KeyInput(ushort vk, bool up)
    {
        uint flags = up ? Native.KEYEVENTF_KEYUP : 0;
        if (IsExtended(vk)) flags |= Native.KEYEVENTF_EXTENDEDKEY;
        return new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            u = new Native.INPUTUNION { ki = new Native.KEYBDINPUT { wVk = vk, dwFlags = flags } }
        };
    }

    private static Native.INPUT UnicodeInput(char c, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.INPUTUNION
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0)
            }
        }
    };

    /// <summary>
    /// I tasti del blocco navigazione e quelli multimediali vivono nella parte
    /// estesa dello scancode: senza il flag alcune applicazioni li ignorano.
    /// </summary>
    private static bool IsExtended(ushort vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or          // pagsu pagdown fine inizio
        0x25 or 0x26 or 0x27 or 0x28 or          // frecce
        0x2D or 0x2E or                          // ins canc
        0x5B or 0x5C or 0x5D or                  // win sinistro/destro, menu
        0xA1 or 0xA3 or 0xA5 or                  // shift/ctrl/alt destri
        >= 0xA6 and <= 0xB7;                     // navigazione browser e multimediali

    private static void Send(params Native.INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        uint sent = Native.SendInput((uint)inputs.Length, inputs,
            System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());
        if (sent != inputs.Length)
        {
            // Succede quando ha il fuoco una finestra a integrita' piu' alta
            // (UAC, Task Manager): non e' recuperabile senza privilegi di
            // amministratore, ma non deve far cadere la trasmissione.
            Log?.Invoke("Input rifiutato dalla finestra in primo piano: e' una finestra di amministratore, e per toccarla va avviato come amministratore anche TabDeck.");
        }
    }

    // ---- mappa dei nomi ----

    private static readonly Dictionary<string, ushort> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0x11, ["control"] = 0x11, ["alt"] = 0x12, ["shift"] = 0x10,
        ["win"] = 0x5B, ["super"] = 0x5B, ["meta"] = 0x5B,
        ["enter"] = 0x0D, ["invio"] = 0x0D, ["return"] = 0x0D,
        ["esc"] = 0x1B, ["escape"] = 0x1B, ["tab"] = 0x09, ["space"] = 0x20, ["spazio"] = 0x20,
        ["backspace"] = 0x08, ["delete"] = 0x2E, ["canc"] = 0x2E, ["insert"] = 0x2D,
        ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["printscreen"] = 0x2C, ["stamp"] = 0x2C, ["capslock"] = 0x14,
        // Multimediali: sono i piu' utili su uno stream deck perche' funzionano
        // anche quando l'applicazione bersaglio non ha il fuoco.
        ["volup"] = 0xAF, ["voldown"] = 0xAE, ["mute"] = 0xAD,
        ["playpause"] = 0xB3, ["next"] = 0xB0, ["prev"] = 0xB1, ["stopmedia"] = 0xB2,
    };

    private static bool TryMapKey(string name, out ushort vk)
    {
        if (Keys.TryGetValue(name, out vk)) return true;

        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }

        if (name.Length is 2 or 3 && (name[0] is 'f' or 'F')
            && int.TryParse(name.AsSpan(1), out int n) && n is >= 1 and <= 24)
        {
            vk = (ushort)(0x6F + n);   // VK_F1 = 0x70
            return true;
        }

        vk = 0;
        return false;
    }
}
