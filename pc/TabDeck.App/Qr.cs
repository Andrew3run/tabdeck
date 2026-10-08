namespace TabDeck;

/// <summary>
/// Disegna un codice QR. Serve solo per il pulsante « Rileva chiavi »: si
/// mostra il codice, lo si inquadra con l'app Smart Life, e le chiavi delle
/// lampade arrivano senza aprire nessun account da sviluppatore.
///
/// E' una versione ridotta all'osso, non una libreria: **versione 5, correzione
/// L, modo byte**. Sono 37x37 moduli e 106 caratteri di capienza, contro i ~60
/// che servono qui. La scelta non e' pigrizia: la versione 5 e' l'ultima in cui
/// i dati stanno in un blocco solo e non c'e' l'informazione di versione da
/// scrivere nella matrice, quindi spariscono le due parti piu' facili da
/// sbagliare - l'interfogliatura dei blocchi e le tabelle per quaranta formati.
///
/// Il resto del progetto non ha un pacchetto NuGet e non lo prende adesso per
/// disegnare un quadrato di puntini.
/// </summary>
public static class Qr
{
    private const int Versione = 5;
    private const int Lato = 17 + 4 * Versione;      // 37
    private const int DatiCodeword = 108;            // v5, correzione L
    private const int EccCodeword = 26;
    private const int Capienza = DatiCodeword - 3;   // modo, lunghezza, chiusura

    /// <summary>
    /// La matrice dei moduli: vero = nero. Null se il testo non ci sta, che
    /// qui non deve succedere ma e' meglio di un codice illeggibile.
    /// </summary>
    public static bool[,]? Genera(string testo)
    {
        byte[] dati = System.Text.Encoding.UTF8.GetBytes(testo);
        if (dati.Length > Capienza) return null;

        byte[] codeword = Codifica(dati);
        byte[] finale = new byte[DatiCodeword + EccCodeword];
        Array.Copy(codeword, finale, DatiCodeword);
        Array.Copy(Ecc(codeword), 0, finale, DatiCodeword, EccCodeword);

        // Si prova ogni maschera e si tiene quella che rende il codice piu'
        // regolare: sono le stesse regole di penalita' dello standard, e
        // servono a non lasciare macchie che confondono chi legge.
        bool[,]? migliore = null;
        int minimo = int.MaxValue;
        for (int maschera = 0; maschera < 8; maschera++)
        {
            var m = Disegna(finale, maschera);
            int p = Penalita(m);
            if (p < minimo) { minimo = p; migliore = m; }
        }
        return migliore;
    }

    // ---- dati ----

    /// <summary>Modo byte, lunghezza, contenuto, chiusura e riempimento.</summary>
    private static byte[] Codifica(byte[] dati)
    {
        var bit = new List<bool>();
        Aggiungi(bit, 0b0100, 4);          // modo byte
        Aggiungi(bit, dati.Length, 8);     // lunghezza (8 bit fino alla versione 9)
        foreach (byte b in dati) Aggiungi(bit, b, 8);

        // Chiusura: fino a quattro zeri, ma non oltre la fine.
        for (int i = 0; i < 4 && bit.Count < DatiCodeword * 8; i++) bit.Add(false);
        while (bit.Count % 8 != 0) bit.Add(false);

        var fuori = new byte[DatiCodeword];
        for (int i = 0; i < bit.Count; i++)
            if (bit[i]) fuori[i / 8] |= (byte)(0x80 >> (i % 8));

        // Riempimento con i due valori previsti, alternati.
        for (int i = bit.Count / 8, giro = 0; i < DatiCodeword; i++, giro++)
            fuori[i] = (byte)(giro % 2 == 0 ? 0xEC : 0x11);

        return fuori;
    }

    private static void Aggiungi(List<bool> bit, int valore, int quanti)
    {
        for (int i = quanti - 1; i >= 0; i--) bit.Add(((valore >> i) & 1) != 0);
    }

    // ---- correzione d'errore ----

    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    static Qr()
    {
        // Campo di Galois a 256 elementi, il solito dei QR.
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if (x >= 256) x ^= 0x11D;
        }
        for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
    }

    private static byte Moltiplica(byte a, byte b) =>
        a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

    private static byte[] Ecc(byte[] dati)
    {
        // Polinomio generatore di grado 26.
        var gen = new byte[EccCodeword + 1];
        gen[0] = 1;
        for (int i = 0; i < EccCodeword; i++)
        {
            for (int j = i + 1; j > 0; j--)
                gen[j] = (byte)(gen[j - 1] ^ Moltiplica(gen[j], Exp[i]));
            gen[0] = Moltiplica(gen[0], Exp[i]);
        }

        var resto = new byte[EccCodeword];
        foreach (byte d in dati)
        {
            byte fattore = (byte)(d ^ resto[0]);
            Array.Copy(resto, 1, resto, 0, EccCodeword - 1);
            resto[EccCodeword - 1] = 0;
            for (int i = 0; i < EccCodeword; i++)
                resto[i] ^= Moltiplica(gen[EccCodeword - 1 - i], fattore);
        }
        return resto;
    }

    // ---- matrice ----

    private static bool[,] Disegna(byte[] codeword, int maschera)
    {
        var m = new bool[Lato, Lato];
        var fisso = new bool[Lato, Lato];   // moduli non toccabili dai dati

        Mirino(m, fisso, 0, 0);
        Mirino(m, fisso, 0, Lato - 7);
        Mirino(m, fisso, Lato - 7, 0);

        // Righe di sincronia: alternate, partendo da nero.
        for (int i = 8; i < Lato - 8; i++)
        {
            m[6, i] = m[i, 6] = i % 2 == 0;
            fisso[6, i] = fisso[i, 6] = true;
        }

        // Alla versione 5 c'e' un solo riquadro di allineamento: gli altri tre
        // punti previsti cadono sopra i mirini.
        Allineamento(m, fisso, Lato - 7, Lato - 7);

        // Il modulo sempre nero, e lo spazio riservato al formato.
        m[Lato - 8, 8] = true;
        fisso[Lato - 8, 8] = true;
        foreach (var (r, c) in PostiFormato())
            fisso[r, c] = true;

        Dati(m, fisso, codeword, maschera);
        Formato(m, maschera);
        return m;
    }

    private static void Mirino(bool[,] m, bool[,] fisso, int riga, int colonna)
    {
        // Sette per sette piu' il bordo bianco intorno: si segna fisso anche il
        // bordo, altrimenti i dati ci finirebbero dentro.
        for (int r = -1; r <= 7; r++)
            for (int c = -1; c <= 7; c++)
            {
                int rr = riga + r, cc = colonna + c;
                if (rr < 0 || cc < 0 || rr >= Lato || cc >= Lato) continue;
                bool nero = r >= 0 && r <= 6 && c >= 0 && c <= 6
                            && (r == 0 || r == 6 || c == 0 || c == 6
                                || (r >= 2 && r <= 4 && c >= 2 && c <= 4));
                m[rr, cc] = nero;
                fisso[rr, cc] = true;
            }
    }

    private static void Allineamento(bool[,] m, bool[,] fisso, int riga, int colonna)
    {
        for (int r = -2; r <= 2; r++)
            for (int c = -2; c <= 2; c++)
            {
                m[riga + r, colonna + c] =
                    Math.Abs(r) == 2 || Math.Abs(c) == 2 || (r == 0 && c == 0);
                fisso[riga + r, colonna + c] = true;
            }
    }

    /// <summary>
    /// I dati salgono e scendono a serpentina, due colonne per volta, da destra
    /// a sinistra. La colonna 6 e' della sincronia e si salta.
    /// </summary>
    private static void Dati(bool[,] m, bool[,] fisso, byte[] codeword, int maschera)
    {
        int bit = 0;
        bool su = true;
        for (int destra = Lato - 1; destra > 0; destra -= 2)
        {
            if (destra == 6) destra = 5;
            for (int passo = 0; passo < Lato; passo++)
            {
                int riga = su ? Lato - 1 - passo : passo;
                for (int d = 0; d < 2; d++)
                {
                    int colonna = destra - d;
                    if (fisso[riga, colonna]) continue;

                    bool acceso = false;
                    if (bit < codeword.Length * 8)
                        acceso = ((codeword[bit / 8] >> (7 - bit % 8)) & 1) != 0;
                    bit++;

                    m[riga, colonna] = acceso ^ Maschera(maschera, riga, colonna);
                }
            }
            su = !su;
        }
    }

    private static bool Maschera(int quale, int r, int c) => quale switch
    {
        0 => (r + c) % 2 == 0,
        1 => r % 2 == 0,
        2 => c % 3 == 0,
        3 => (r + c) % 3 == 0,
        4 => (r / 2 + c / 3) % 2 == 0,
        5 => (r * c) % 2 + (r * c) % 3 == 0,
        6 => ((r * c) % 2 + (r * c) % 3) % 2 == 0,
        _ => ((r + c) % 2 + (r * c) % 3) % 2 == 0,
    };

    /// <summary>Le due copie dell'informazione di formato: correzione L e maschera.</summary>
    private static void Formato(bool[,] m, int maschera)
    {
        int dati = (0b01 << 3) | maschera;          // 01 = correzione L
        int resto = dati << 10;
        for (int i = 4; i >= 0; i--)
            if (((resto >> (i + 10)) & 1) != 0) resto ^= 0x537 << i;
        int bits = ((dati << 10) | resto) ^ 0x5412;

        var posti = PostiFormato();
        for (int i = 0; i < 15; i++)
        {
            // Il primo posto prende il bit piu' significativo, non il meno:
            // invertendo l'ordine il codice resta bello da vedere e nessun
            // lettore lo riconosce.
            bool acceso = ((bits >> (14 - i)) & 1) != 0;
            m[posti[i].r, posti[i].c] = acceso;
            m[posti[i + 15].r, posti[i + 15].c] = acceso;
        }
    }

    /// <summary>I trenta moduli del formato, in ordine di bit: prima copia, poi seconda.</summary>
    private static (int r, int c)[] PostiFormato()
    {
        var p = new (int r, int c)[30];
        for (int i = 0; i < 15; i++)
        {
            // Prima copia: intorno al mirino in alto a sinistra.
            p[i] = i < 6 ? (8, i)
                 : i == 6 ? (8, 7)
                 : i == 7 ? (8, 8)
                 : i == 8 ? (7, 8)
                 : (14 - i, 8);
            // Seconda copia: sotto quello in basso a sinistra e a destra di quello in alto.
            p[i + 15] = i < 7 ? (Lato - 1 - i, 8) : (8, Lato - 15 + i);
        }
        return p;
    }

    // ---- scelta della maschera ----

    private static int Penalita(bool[,] m)
    {
        int totale = 0;

        // Serie di cinque o piu' moduli uguali in fila.
        for (int i = 0; i < Lato; i++)
        {
            totale += SerieInFila(m, i, true) + SerieInFila(m, i, false);
        }

        // Quadrati due per due dello stesso colore.
        for (int r = 0; r < Lato - 1; r++)
            for (int c = 0; c < Lato - 1; c++)
                if (m[r, c] == m[r, c + 1] && m[r, c] == m[r + 1, c] && m[r, c] == m[r + 1, c + 1])
                    totale += 3;

        // Sagome che assomigliano a un mirino: sono quelle che fanno sbagliare
        // il lettore su dove comincia il codice.
        bool[] sagoma = { true, false, true, true, true, false, true, false, false, false, false };
        for (int r = 0; r < Lato; r++)
            for (int c = 0; c < Lato; c++)
            {
                if (Combacia(m, r, c, sagoma, true) || Combacia(m, r, c, sagoma, false)) totale += 40;
            }

        // Sbilanciamento fra nero e bianco.
        int neri = 0;
        foreach (bool b in m) if (b) neri++;
        int percento = neri * 100 / (Lato * Lato);
        totale += Math.Abs(percento - 50) / 5 * 10;

        return totale;
    }

    private static int SerieInFila(bool[,] m, int indice, bool orizzontale)
    {
        int totale = 0, lunghezza = 1;
        for (int i = 1; i < Lato; i++)
        {
            bool ora = orizzontale ? m[indice, i] : m[i, indice];
            bool prima = orizzontale ? m[indice, i - 1] : m[i - 1, indice];
            if (ora == prima) lunghezza++;
            else { if (lunghezza >= 5) totale += 3 + (lunghezza - 5); lunghezza = 1; }
        }
        if (lunghezza >= 5) totale += 3 + (lunghezza - 5);
        return totale;
    }

    private static bool Combacia(bool[,] m, int r, int c, bool[] sagoma, bool orizzontale)
    {
        if (orizzontale ? c + sagoma.Length > Lato : r + sagoma.Length > Lato) return false;
        for (int i = 0; i < sagoma.Length; i++)
            if ((orizzontale ? m[r, c + i] : m[r + i, c]) != sagoma[i]) return false;
        return true;
    }
}
