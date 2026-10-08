using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TabDeck;

/// <summary>
/// La ricerca al contrario: qui non si cerca il tablet, lo si aspetta.
///
/// <para><b>Perche' serve.</b> <see cref="Link.Discover"/> funziona solo se il
/// tablet risponde, e un tablet fermo non risponde. Non risponde nemmeno
/// all'ARP, che e' broadcast, e senza ARP Windows non riesce nemmeno a mandare
/// il primo pacchetto: il ping torna « Host di destinazione non raggiungibile »
/// e la connessione TCP scade senza mai partire. Appena il tablet trasmette
/// qualcosa di suo, la voce ARP si popola e da quel momento va tutto — ping,
/// porta 8765, ricerca in rete. Le voci ARP di Windows pero' scadono in un paio
/// di minuti, ed e' li' che nasce il « a volte si a volte no ».</para>
///
/// <para>Nessuna insistenza dal lato PC puo' chiudere quel buco, perche' il
/// problema e' che l'altro non parla. La direzione giusta e' l'opposta: chi ha
/// appena premuto un pulsante sul tablet ha in mano un tablet sveglio, e un
/// tablet sveglio trasmette. Il datagramma esce, questa socket lo riceve, e il
/// collegamento si apre come si e' sempre aperto.</para>
///
/// <para><b>Cosa NON cambia.</b> Il tablet resta il server sulla 8765 e il
/// protocollo e' identico: cambia solo chi parla per primo. La ricerca dritta
/// resta dov'era, e le due si coprono a vicenda.</para>
///
/// <para><b>Il firewall.</b> Questa e' l'unica socket del programma che riceve
/// traffico non richiesto: le risposte alla ricerca dritta rientrano da sole
/// perche' Windows le riconosce come ritorno di un pacchetto uscito, un annuncio
/// no. Alla prima accensione Windows chiede se permettere a TabDeck di
/// comunicare sulle reti private: senza quel permesso l'annuncio non arriva mai,
/// e il tablet direbbe « chiamata mandata » a un PC che non sente niente.</para>
/// </summary>
public sealed class Richiamo : IDisposable
{
    private UdpClient? socket;
    private Thread? thread;
    private volatile bool running;

    /// <summary>Un tablet si e' annunciato. Arriva sul thread di rete.</summary>
    public event Action<FoundTablet>? Annuncio;

    /// <summary>Righe per il registro. Thread di rete.</summary>
    public event Action<string>? Log;

    public bool Attivo => running;

    /// <summary>
    /// Apre l'ascolto. Idempotente: chiamarla due volte non apre due socket.
    ///
    /// Se la porta e' occupata non si insiste e non si solleva niente: il
    /// programma continua a funzionare con la ricerca dritta, che e' quella di
    /// prima. Perdere l'annuncio e' una comodita' in meno, non un guasto.
    /// </summary>
    public void Ascolta()
    {
        if (running) return;

        try
        {
            var s = new UdpClient(AddressFamily.InterNetwork);
            s.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            s.Client.Bind(new IPEndPoint(IPAddress.Any, Proto.AnnouncePort));
            socket = s;
        }
        catch (SocketException e)
        {
            Log?.Invoke($"Il richiamo del tablet non si apre sulla {Proto.AnnouncePort}: {e.Message}");
            return;
        }

        running = true;
        thread = new Thread(Ciclo) { IsBackground = true, Name = "tabdeck-richiamo" };
        thread.Start();
        Log?.Invoke($"In ascolto del tablet sulla udp:{Proto.AnnouncePort}. "
                  + "Se il tablet dice « chiamata mandata » e qui non succede niente, "
                  + "manca la regola del firewall: vedi docs/procedura.md.");
    }

    private void Ciclo()
    {
        while (running)
        {
            try
            {
                IPEndPoint from = new(IPAddress.Any, 0);
                byte[] data = socket!.Receive(ref from);
                string text = Encoding.ASCII.GetString(data).Trim();
                if (!text.StartsWith(Proto.Announce, StringComparison.Ordinal)) continue;

                // "TABDECK! <porta> <modello>": il modello e' l'unica parte che
                // puo' contenere spazi, quindi si taglia in tre e si tiene la coda.
                var parts = text.Split(' ', 3);
                Annuncio?.Invoke(new FoundTablet(
                        from.Address.ToString(),
                        parts.Length >= 3 ? parts[2] : "tablet"));
            }
            catch (SocketException)
            {
                // Socket chiusa da Dispose, oppure la scheda di rete e' sparita
                // sotto: nel primo caso running e' gia' falso e si esce, nel
                // secondo non c'e' niente da fare qui.
                if (!running) return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        running = false;
        // Chiudere la socket e' l'unico modo di svegliare la Receive bloccata:
        // non c'e' un timeout, e metterne uno vorrebbe dire girare a vuoto ogni
        // tot per tutta la vita della finestra.
        socket?.Close();
        socket = null;
        thread = null;
    }
}
