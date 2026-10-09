package dev.tabdeck;

/**
 * Protocollo binario, identico sui due lati. Ogni frame e':
 *
 *     [u8 tipo][u32 lunghezza big-endian][payload]
 *
 * Il tablet e' sempre il server, su tutti e due i trasporti:
 *
 *   USB   il PC si collega con "adb forward tcp:8765 localabstract:tabdeck".
 *         Su Android 4.4 "adb reverse" non esiste, quindi la direzione e'
 *         obbligata.
 *   WiFi  il tablet apre una ServerSocket sulla stessa porta e risponde ai
 *         messaggi di scoperta, cosi' il PC lo trova senza sapere l'IP.
 */
public final class Proto {
    private Proto() {}

    /** Nome della socket astratta UNIX esposta dal tablet (trasporto USB). */
    public static final String SOCKET_NAME = "tabdeck";

    /** Porta TCP del trasporto WiFi. La stessa che adb inoltra lato PC. */
    public static final int TCP_PORT = 8765;

    /** Porta UDP su cui il tablet risponde a chi lo cerca in rete locale. */
    public static final int DISCOVERY_PORT = 8766;

    /** Datagramma che il PC spedisce in broadcast per trovare il tablet. */
    public static final String DISCOVERY_PROBE = "TABDECK?";

    /** Prefisso della risposta: "TABDECK <porta> <modello>". */
    public static final String DISCOVERY_REPLY = "TABDECK";

    /**
     * Porta UDP su cui il PC ascolta gli annunci del tablet, cioe' la ricerca
     * al contrario.
     *
     * La 8766 va bene finche' e' il PC a cercare, ma quel giro funziona solo se
     * il tablet risponde, e un tablet fermo non risponde: non risponde nemmeno
     * all'ARP, che e' broadcast, e senza ARP il PC non riesce nemmeno a provare
     * a connettersi. Misurato: primo ping "Host di destinazione non
     * raggiungibile", e subito dopo che il tablet aveva trasmesso qualcosa,
     * tutto funzionante — ping, TCP 8765 e ricerca sei volte su sei.
     *
     * Da qui la direzione in piu': chi ha appena premuto un pulsante sul tablet
     * ha in mano un tablet sveglio, e un tablet sveglio parla. Il PC non deve
     * piu' indovinare quando l'altro c'e'.
     */
    public static final int ANNOUNCE_PORT = 8767;

    /** Annuncio del tablet al PC: "TABDECK! <porta TCP> <modello>". */
    public static final String ANNOUNCE = "TABDECK!";

    // ---- PC -> tablet ----
    /** Payload JSON: geometria della cattura e modo dei gesti. */
    public static final int HELLO   = 0x01;
    /**
     * Payload JSON: {"mode":"screen"|"deck","full":true|false} — il PC dice al
     * tablet cosa mostrare. Serve perche' la scelta di guardare lo schermo si
     * fa quasi sempre stando al computer, non allungando la mano sul tablet.
     */
    public static final int MODE    = 0x02;
    /**
     * Payload JSON: come deve comportarsi il tablet — schermo sempre acceso,
     * luminosita'. Sono scelte dell'utente, e si fanno dalla finestra sul PC
     * perche' e' li' che si sta quando le si vuole cambiare.
     */
    public static final int CONFIG  = 0x03;
    // CONFIG va anche all'indietro, tablet -> PC: {"sezioni":{...}} scelte nelle Impostazioni del tablet.
    /** Payload JSON: {"do":"..."} — un comando singolo, eseguito e finito li'. */
    public static final int CMD     = 0x04;
    /** [u16 x][u16 y][u16 w][u16 h][JPEG] — porzione di schermo cambiata. */
    public static final int TILE    = 0x10;
    /** Nessun payload: le tile ricevute finora formano un frame completo. */
    public static final int PRESENT = 0x11;
    /** Payload JSON: griglia dello stream deck da disegnare. */
    public static final int DECK    = 0x21;
    /**
     * Payload JSON: {"luci":[{nome, id, ip, chiave, versione}, ...]} — le luci
     * di casa.
     *
     * A differenza del DECK, che va rimandato a ogni collegamento perche' le
     * azioni restano sul PC, questo elenco il tablet **se lo salva**: le luci
     * le comanda da solo, parlando alle lampade sulla rete, e deve poterlo fare
     * a PC spento e a cavo staccato. Il PC lo rimanda solo quando l'elenco
     * cambia.
     */
    public static final int LUCI    = 0x30;
    /**
     * [u16 lunghezza nome][nome][PNG] — l'immagine di un pulsante del deck.
     *
     * Come le luci, e per la stessa ragione, il tablet **se la salva**: i
     * pulsanti devono avere la loro faccia appena acceso, prima ancora che il
     * PC risponda, e continuare ad averla a PC spento. Il PC la manda una volta
     * per collegamento, prima del frame DECK che la nomina.
     *
     * L'immagine arriva gia' ridotta a 128 pixel: ridimensionare una fotografia
     * intera qui costerebbe piu' che disegnare tutto il deck.
     */
    public static final int ICON    = 0x40;

    // ---- plugin, nei due versi ----
    /**
     * Da 0x50 a 0x5F i frame sono delle estensioni. Il nucleo legge PLUGIN
     * (annunci e risposte, vedi MainActivity) e PLUGIN_CODICE, e passa il resto
     * alle estensioni caricate: senza, i frame si scartano. I nomi dicono un uso
     * possibile, non obbligato.
     */
    public static final int PLUGIN          = 0x50;
    /** PC -> tablet: [u8 lunghezza id][id][pacchetto Android dell'estensione]. */
    public static final int PLUGIN_CODICE   = 0x5E;

    // ---- salvaschermo ----
    /**
     * PC -> tablet, JSON: le scelte e l'elenco delle foto. Tablet -> PC, JSON:
     * {"mancano":[nomi]}, le foto che non ha ancora.
     */
    public static final int SALVASCHERMO      = 0x60;
    /** PC -> tablet: [u16 lunghezza nome][nome][JPEG 1024x600]. */
    public static final int SALVASCHERMO_FOTO = 0x61;
    /** PC -> tablet, JSON: {"id":n,"testo":"..."} o {"id":n,"errore":"..."}. Vedi {@link Web}. */
    public static final int WEB_RISPOSTA      = 0x62;

    /**
     * Il secondo canale del cavo, per « Internet col cavo: tutto il tablet ». Il PC ci
     * arriva con adb forward come sul primo, e ci passano pacchetti IP: [u16 lunghezza][pacchetto].
     * Vedi {@link Tunnel}.
     */
    public static final String SOCKET_RETE = "tabdeck-rete";
    /** PC -> tablet, JSON: quale strumento e' davanti dall'altra parte. */
    public static final int PLUGIN_CONTESTO = 0x51;
    /** PC -> tablet, JSON: come sta lo strumento. */
    public static final int PLUGIN_STATO    = 0x52;
    /** Tablet -> PC, JSON: {"azione":"...","args":{...}}. */
    public static final int PLUGIN_COMANDO  = 0x53;
    /** Tablet -> PC: [u16 punti][i16 x][i16 y]... in 0..10000 per lato. */
    public static final int PLUGIN_TRATTO   = 0x54;
    /** Tablet -> PC: [u8 fase][PCM 16 kHz mono 16 bit]. */
    public static final int PLUGIN_VOCE     = 0x55;
    /** PC -> tablet, JSON: com'e' andata un'azione chiesta dal tablet {azione, ok, messaggio, dati}. */
    public static final int PLUGIN_RISPOSTA = 0x56;
    /** PC -> tablet, JSON: l'icona di un programma del PC {nome, png in base64}. */
    public static final int PLUGIN_ICONA    = 0x57;
    /** PC -> tablet, JSON: l'immagine del documento aperto nello Studio {larghezza, altezza, png}. */
    public static final int PLUGIN_ANTEPRIMA = 0x58;
    public static final int PLUGIN_ULTIMO   = 0x5F;

    // ---- tablet -> PC ----
    /** Payload JSON: {"id":"..."} pulsante dello stream deck premuto. */
    public static final int PRESS   = 0x20;
    /** [u8 azione][i16 x][i16 y] — coordinate nello spazio dell'area catturata. */
    public static final int TOUCH   = 0x22;
    /** [i16 delta] — scroll a due dita, positivo = verso l'alto. */
    public static final int WHEEL   = 0x23;
    /** Payload JSON: risoluzione del pannello, inviato appena connessi. */
    public static final int READY   = 0x24;
    /**
     * Nessun payload: il frame precedente e' stato disegnato, il PC puo'
     * mandarne un altro. Senza questa conferma il PC riempie la socket piu' in
     * fretta di quanto il PXA986 decodifichi, la coda cresce senza limite e lo
     * schermo resta indietro di secondi: sembra bloccato.
     */
    public static final int ACK     = 0x25;
    /** Payload JSON: {"id":n,"url":"http://..."} — una pagina che il PC scarica per il tablet. Vedi {@link Web}. */
    public static final int WEB     = 0x26;

    // Azioni del frame TOUCH.
    public static final int TOUCH_DOWN  = 0;
    public static final int TOUCH_MOVE  = 1;
    public static final int TOUCH_UP    = 2;
    public static final int TOUCH_RIGHT = 3;
}
