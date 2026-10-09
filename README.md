# TabDeck — Samsung Galaxy Tab 3 7.0 (SM-T210) come schermo e console da scrivania

Trasforma un SM-T210 (Android 4.4.2, 1024x600, 1GB RAM, SoC Marvell PXA986) in
un dispositivo dedicato a due cose, collegato al PC Windows via cavo o via rete:

1. **Un monitor in piu', vero.** Non il rispecchiamento di una finestra: uno
   schermo che Windows vede per conto suo, con la sua risoluzione e la sua
   posizione nel desktop. Ci trascini dentro le finestre come su qualsiasi altro
   monitor, e le tocchi con il dito.
2. **Uno stream deck.** Una griglia di pulsanti disegnata sul tablet; premendone
   uno, il PC esegue una scorciatoia, un comando multimediale, un programma, una
   sequenza.

Sul PC c'e' **un'applicazione con una finestra**, e ogni cosa si comanda a mano
da li': quale schermo mandare, a che risoluzione, con che collegamento, che
pulsanti mettere nel deck. Non c'e' nessun servizio, nessuna intelligenza
artificiale e niente che decida al posto tuo: quello che TabDeck sa fare da
solo — aprirsi con Windows, collegarsi, riprovare, avvisare — lo fa solo se lo
si sceglie con una spunta in **Gestione › Impostazioni**, e di partenza e'
spento.

La croce della finestra non spegne: **ritira la finestra accanto all'orologio**,
e il tablet resta collegato. Chiudere davvero staccava il tablet, spegneva lo
schermo virtuale e lasciava il deck a comandare niente — il gesto piu' facile
della finestra faceva la cosa piu' cara. Per spegnere davvero c'e' « Esci » nel
menu dell'icona, che e' un gesto in piu' apposta. Uscendo, sul PC non resta
niente in esecuzione.

Dalla stessa ragione viene che **ne gira una sola**. Premendo il collegamento
sul desktop mentre TabDeck sta nascosto nell'area di notifica, una seconda copia
non sarebbe una seconda finestra della stessa cosa: sarebbe un secondo
programma che prova a prendersi la porta gia' occupata, a mandare il suo deck
allo stesso tablet e ad accendere un altro schermo virtuale. Quindi non parte:
dice a quella che c'e' gia' di farsi vedere, e se ne va. Il collegamento sul
desktop e l'icona accanto all'orologio fanno la stessa cosa.

## Le impostazioni

In **Gestione › Impostazioni**, e dal menu dell'icona accanto all'orologio.
Ogni cosa e' una spunta, e le spunte che fanno partire qualcosa da sole sono
spente finche' non le si accende.

| | |
|---|---|
| **Avvio con Windows** | un'attivita' pianificata, « TabDeck Accesso », con i permessi pieni e dieci secondi di ritardo: la chiave Run non basterebbe, perche' all'accesso Windows salta i programmi che chiedono l'amministratore. Partito cosi', puo' restare accanto all'orologio senza aprire la finestra. La spunta legge l'Utilita' di pianificazione, non un file: toglierla a mano da li' si vede anche qui |
| **All'apertura** | non collegare, col cavo, in rete, oppure prima il cavo e poi la rete |
| **Al cavo attaccato** | se ne accorge Windows, e solo in quel momento si chiede ad adb se c'e' il tablet: niente gira a vuoto |
| **Se cade** | riprova ogni 15 secondi finche' non torna; sulla dashboard « Smetti di riprovare » ferma tutto |
| **Alla chiamata del tablet** | « Connetti al PC » sul tablet collega subito, come prima; la si puo' spegnere |
| **Notifiche di Windows** | collegato, caduto, tentativo automatico non riuscito; di norma solo a finestra nascosta. Premendone una si apre la finestra |

L'attivita' « TabDeck » del collegamento sul desktop resta quella di prima,
senza orario: sono due scelte diverse, e togliere l'una non porta via l'altra.
Il collegamento a mano — cavo, rete, porta — sta in fondo alla stessa pagina.

## Perche' chiede i permessi di amministratore

Servono per due cose sole, e sono tutt'e due dentro il mestiere del programma:
accendere e spegnere il **monitor virtuale** — `Enable-PnpDevice` e
`Disable-PnpDevice`, che senza permessi non si eseguono — e mandare i **tocchi
del tablet** anche alle finestre che girano elevate, cosa che Windows vieta a
un programma normale.

Chiederli all'apertura e' il modo con **meno** richieste, non con piu': senza
elevazione TabDeck saprebbe cavarsela lo stesso, ma il consenso lo chiederebbe
a ogni « Manda lo schermo » e a ogni « Ferma », cioe' due volte per sessione
invece di una.

A zero si arriva dicendolo a Windows una volta sola invece che ogni volta:

    .\avvio.ps1              il collegamento passa da un'attivita' pianificata
    .\avvio.ps1 -Togli       si torna alla domanda a ogni apertura

L'attivita' si chiama « TabDeck », **non ha nessun orario e nessun avvio
all'accesso**: esiste solo per portare con se' il livello dei permessi, e si
muove quando la chiami tu premendo il collegamento. Niente parte da solo, che
e' la regola di tutto il resto. Sugli altri PC la crea `Installa.ps1`, e
`-SenzaAttivita` dice di non farlo.

Va detto per intero: da quel momento chiunque possa premere quel collegamento
fa partire un programma elevato senza vedere nessuna domanda. Su una macchina
propria e' quello che si vuole; su una condivisa e' una cosa da sapere.

## La finestra, in due

A sinistra c'e' una barra, e la barra divide l'applicazione in due meta' che
non si somigliano.

**USO** sono tre pagine, e sono quelle che si aprono tutti i giorni.

La **dashboard** dice a colpo d'occhio se il tablet risponde e per che strada,
quanti byte stanno passando, se lo schermo virtuale c'e' e con che pixel; da li'
si collega, si manda lo schermo, si sceglie cosa far vedere al pannello.

Il **deck** e' la stessa griglia che sta sul tablet, disegnata con le misure del
pannello - 1024x600 a densita' uno - e poi ingrandita: quel che si vede qui e'
quel che esce di la'. Premendo un pulsante l'azione parte sul PC dopo tre
secondi, il tempo di andare sulla finestra a cui serve; senza l'attesa la
combinazione di tasti finirebbe dentro TabDeck, che e' la finestra col fuoco.
Le cartelle invece si aprono subito, come sul tablet. Un pulsante in alto
scambia il premere col **disporre**: da li' i pulsanti si trascinano dove li si
vuole, su una griglia grande quanto la finestra. Accanto c'e' il **profilo**,
perche' e' guardando questa pagina che viene voglia di passare al deck
dell'altra cosa.

**Casa** e' il pannello delle lampade: si preme e la luce cambia, senza niente
da riempire. **Timer** e **Sveglia** sono le altre due cose che il tablet fa da
solo a PC spento: un conto alla rovescia di qualunque durata e una sveglia coi
suoi giorni della settimana.

**GESTIONE** e' quello che si tocca una volta e poi si dimentica: montare i
**pulsanti** del deck, dare un nome e una chiave alle **luci**, cercare il
driver di **schermo** virtuale, scegliere risoluzione e qualita', decidere nelle
**impostazioni** come TabDeck si apre, si collega e avvisa, dire al **tablet**
quando spegnere il pannello, leggere il **registro**.

E' la stessa applicazione di prima, con le stesse cose dentro. Erano pero'
schede in fila allo stesso peso, e la finestra sembrava un pannello di
controllo dove il gesto piu' comune stava accanto a quello che si fa una volta
l'anno.

## I profili

Un deck solo basta finche' il tablet serve a una cosa sola. Lavorare, montare un
video e guardare un film vogliono pulsanti diversi - non gli stessi in ordine
diverso - e il **profilo** e' la griglia intera che si cambia in blocco: nomi,
colori, cartelle, righe e colonne. Ne convive quanti se ne vogliono, ma sul
tablet ce n'e' sempre uno solo, ed e' quello che il PC gli ha mandato.

Stanno tutti dentro `config/deck.json`. Un file scritto prima che i profili
esistessero si apre lo stesso: quello che c'e' diventa il profilo « Deck ».

Per portarli altrove c'e' **« Esporta »**, nella barra del profilo. Copiare
`deck.json` non basta e non e' preciso: non basta perche' i pulsanti con
un'immagine nominano file che stanno in `config/icone`, e di la' quei file non
ci sono — arriverebbe un deck di quadratini vuoti; non e' preciso perche' il
file li contiene tutti, e chi vuole passare *un* profilo si porterebbe dietro
anche gli altri. L'esportazione e' un file solo, di testo, con dentro i profili
scelti e le immagini che nominano. **« Importa »** li rimette, e non sovrascrive
niente: un profilo che si chiama come uno che c'e' gia' entra col nome libero
accanto — « Lavoro 2 » — e i pulsanti « Cambia profilo » che lo nominavano
vengono corretti di conseguenza.

Si cambiano da due posti - la pagina **Pulsanti**, mentre si monta, e la pagina
**Deck**, mentre si lavora - e da un terzo che conta piu' degli altri due: **un
pulsante del deck**. L'azione « Cambia profilo del deck » nomina un altro
profilo, e premendola sul tablet arriva quella griglia. E' cosi' che un tablet
da sette pollici tiene sessanta pulsanti senza diventare un elenco: tre decks di
venti, e un pulsante in ognuno che porta agli altri.

Cambiare profilo **salva**. Non e' una scorciatoia: il file li contiene tutti,
quindi passare da uno all'altro lo riscrive comunque, e allora tanto vale che
quello che si stava montando finisca su disco invece di restare per aria.

## Le postazioni

Il profilo cambia i pulsanti; la **postazione** cambia il posto. A casa il
tablet sta sulla rete di casa, mostra la sezione Casa e apre il deck di tutti i
giorni; in ufficio ha un altro indirizzo, niente lampade e il deck del lavoro.
Una postazione si ricorda:

- il profilo del deck;
- l'indirizzo del tablet in rete e come collegarsi all'apertura;
- le sezioni della barra del tablet, col PC e senza;
- quando tenere acceso il pannello e la luminosita';
- le luci di casa: spente, il tablet riceve un elenco vuoto e niente sezione
  Casa, e sul PC spariscono la pagina Casa e le routine della dashboard;
- le estensioni accese.

Si sceglie nelle pagine di sempre, e quella attiva se lo ricorda a ogni
salvataggio; in **Gestione › Postazioni**, sotto l'elenco, deck, luci, sezioni
ed estensioni si scelgono anche a mano per la postazione selezionata, anche
per una in cui non si sta. Si creano, rinominano e tolgono in **Gestione ›
Postazioni**, oppure da « Nuova postazione… » nell'elenco in cima alla
finestra; una nuova parte da com'e' tutto adesso. Dallo stesso elenco, accanto
al segnaposto, si passa dall'una all'altra: le scelte tornano
nelle pagine e, col tablet collegato, gli arrivano subito. Il collegamento in
corso non si tocca. Stanno in `config/tabdeck.json`; un file di prima ne
prende una sola, « Casa », con le scelte che aveva.

## Le cartelle

Quindici celle su una pagina, e le pagine si sfogliano di lato: finche' i
pulsanti sono venti va bene, a quaranta si scorre tre schermate per trovarne
uno. Un pulsante puo' allora essere una **cartella**: dentro ha altri pulsanti,
e premendola si apre il suo elenco, con una fascia in cima che dice dove si e' e
riporta indietro. Si annidano fino a quattro piani, che e' il punto oltre il
quale trovare un pulsante costa piu' che premerlo.

Le apre il tablet, non il PC. E' voluto, ed e' la stessa ragione per cui le
pagine si sfogliano da sole: **il deck si usa anche a computer spento**, e una
cartella che per aprirsi dovesse chiedere il permesso a una macchina spenta
sarebbe una cartella che non si apre. Il prezzo e' che l'albero intero viaggia
nel frame DECK, ed e' un prezzo di qualche chilobyte.

Sul PC si montano dalla pagina **Pulsanti**: si sta sempre dentro una cartella
sola - la radice e' una cartella come le altre - e a dire dove si e' ci pensano
le briciole sopra l'elenco e la fascia dell'anteprima, che e' la stessa che
disegna il tablet. Un pulsante ci finisce dentro trascinandocelo sopra
nell'anteprima, oppure con « Sposta in » del tasto destro; fermandosi sopra una
cartella mentre la si trascina, quella si apre e si scende di un piano. E c'e'
**Annulla**, perche' una cartella tolta si porta via quello che aveva dentro.

## Il punto: uno schermo vero, non un ritaglio

Windows non lascia trascinare una finestra dove non c'e' un monitor. Perche' il
tablet sia un secondo schermo e non una fotografia del primo, serve che esista
davvero un monitor in piu': lo crea un **driver di display virtuale**.

Su questa macchina e' installato **Virtual Display Driver**, configurato a
1024x600 — gli stessi pixel del pannello, quindi un pixel del PC finisce
esattamente su un pixel del tablet. I dettagli, e perche' Parsec VDD e' stato
scartato, sono in [docs/procedura.md](docs/procedura.md).

**Lo schermo virtuale esiste solo mentre serve.** Non c'e' quando il PC e'
acceso e basta: nasce quando apri l'applicazione e il tablet risponde, e sparisce
quando premi Ferma, quando chiudi la finestra o quando stacchi il tablet. Un
monitor fantasma che resta li' e si mangia le finestre e' peggio che non averlo.
TabDeck non installa driver: trova quello presente e lo accende.

Perche' il tablet riceva i 1024 pixel pieni, la barra laterale del tablet deve
ritirarsi: lo fa da sola quando lo schermo viene chiesto dal PC a schermo
intero, oppure a mano col tasto « chiudi » in fondo alla barra. Si riapre
trascinando il dito dal bordo sinistro verso l'interno: non c'e' nessun tasto
che resti sopra le sezioni. Con la barra aperta restano 952 pixel utili e
l'immagine viene ridotta.

La voce **Schermo** nella barra del tablet c'e' solo mentre il PC e' collegato:
senza, sarebbe un rettangolo nero. Se il collegamento cade mentre la si
guarda, il tablet torna al deck e riapre la barra.

### Oppure un monitor vero

In **Gestione › Schermo** si sceglie che cosa mandare: lo schermo virtuale, che
e' quello di tutti i giorni, oppure uno dei monitor attaccati. Mandare il
proprio monitor grande e' un'altra cosa dall'averlo per ripiego: serve a
guardare da lontano quello che sta succedendo sulla scrivania — una
compilazione, una resa, una partita — senza portarsi via un pezzo di desktop.

Le due strade non si somigliano, e la finestra lo dice sotto l'elenco:

|  | Schermo virtuale | Monitor vero |
|---|---|---|
| Esiste | nasce quando premi « Manda lo schermo », sparisce quando fermi | c'e' gia', o non c'e' |
| Risoluzione | la porta TabDeck a 1024x600, un pixel su un pixel | **non si tocca mai**: sta davanti a qualcuno |
| Sul pannello | nitido | ridotto per starci, con le bande nere |
| Le finestre | ce le trascini dentro | restano dove sono |

Quello che non si fa e' **ripiegare**. Se la cosa scelta non c'e' — il driver
che manca, il monitor staccato — non parte niente e si dice perche'. Prima il
ripiego c'era, e mandava il desktop grande rimpicciolito: un risultato inutile
che per giunta nascondeva il guasto vero.

## Perche' NON una ROM custom

Il PXA986 non ha sorgenti kernel complete rilasciate da Marvell; i port AOSP per
`lt02wifi` sono fermi al 2018, con mirror morti e driver instabili. Una ROM
custom avrebbe aggiunto peso e rischio di brick senza migliorare nessuno dei due
obiettivi. La scelta e': Android 4.4.2 di fabbrica + factory reset + **una sola
app nativa** registrata come Home. All'accensione il tablet entra direttamente
in TabDeck: niente TouchWiz, niente cassetto delle applicazioni, niente barre di
sistema.

## Vincoli tecnici che hanno guidato il progetto

| Vincolo | Conseguenza sul progetto |
|---|---|
| `adb reverse` non esiste su Android 4.4 (serve API 21+) | Il **tablet fa da server**, il PC da client, su `adb forward tcp:18765 localabstract:tabdeck` (la 8765 del PC e' gia' occupata da un altro programma). Il WiFi fa lo stesso per coerenza, sulla 8765 del tablet |
| AndroidX richiede minSdk 21 | **Zero dipendenze**: solo framework Android, Java 7. APK di 29 KB |
| 1GB RAM, GPU Vivante GC1000 | Frame in RGB_565, bitmap riusate, aggiornamento a tessere invece che a schermo intero. Il deck e' una sola View disegnata a mano, non quindici widget |
| Font di KitKat | I glifi sono stati provati uno per uno sul pannello. Ci sono: ▲ ▼ ◀ ▶ ■ □ ▢ ▣ ▤ ▥ ▦ ▧ ▨ ▩ ▪ ▫ ◆ ◇ ○ ● ◉ ◎ ◐ × ÷ ± – + ↻. Non ci sono ▬ ▭ ▮ ▯: escono come celle vuote. Per questo i pulsanti del deck non usano il font ma le **icone Lucide** disegnate come tracciati (`Pittogrammi.java` e `Pittogrammi.cs`, generati insieme da `tools/pittogrammi/genera.py`); i glifi restano per le lampade di Casa e per i pulsanti di prima |
| Nessuna uscita video (no MHL/SlimPort) | L'immagine passa per forza dal cavo dati o dalla rete |

## C'e' un secondo tablet, ed e' un altro progetto

Da settembre 2026 sulla scrivania c'e' anche un **DUODUOGO E960** (MT6580,
Android 7.0, 1280x800), e ha una cartella tutta sua: `..\Tablet DUODUOGO E960`.
Non condivide codice con questo. TabDeck resta di qua e non gira di la'.

I due tablet stanno pero' sullo stesso PC e sullo stesso `adb`, e "il primo
dispositivo autorizzato" ha smesso di essere una risposta buona: con tutti e
due attaccati, `build.ps1 -Install` avrebbe potuto installare TabDeck sul
tablet sbagliato senza dire niente. A scegliere e' ora
`tools/dispositivo.ps1`, che cerca un `ro.product.model` uguale a `SM-T210` e,
se non lo trova, si ferma dicendo quale tablet ha visto al suo posto.

## Struttura

    tablet/    app Android nativa (Java, minSdk 19, nessuna dipendenza)
               cinque sezioni: deck, schermo, casa, timer, sveglia — le ultime
               tre funzionano a PC spento
    tablet/sistema/  i pacchetti Samsung e Google messi a riposo, con
               alleggerisci.ps1 per spegnerli e ripristina.ps1 per riaccenderli,
               e batteria.ps1 per vedere dove se ne va la corrente. Viaggia
               anche nel pacchetto, con lo stesso percorso relativo, cosi' sono
               questi file e non una copia da tenere allineata
    pc/        applicazione Windows (WPF, .NET 10, nessun pacchetto NuGet)
    pc/luci/   luce.py, per comandare le lampade dalla riga di comando
    pc/pacchetto/  quel che finisce nel pacchetto per un altro PC: Installa.ps1,
               Disinstalla.ps1, Avvio.ps1, Tablet.ps1, i cinque .bat che li
               aprono col doppio clic (Installa, Tablet, Alleggerisci,
               Ripristina, Disinstalla), LEGGIMI.txt e GUIDA.md — li mette
               insieme pacchetto.ps1, in cima alla cartella
    config/    tabdeck.json, deck.json (tutti i profili del deck), luci.json —
               scritti dall'applicazione
    config/icone/  le immagini dei pulsanti, ridotte a 128 pixel e ribattezzate
               con l'impronta del loro contenuto
    config/estensioni/  le estensioni installate, una cartella per id: toglierne
               una cartella e' toglierla
    config/salvaschermo/  le foto del salvaschermo gia' tagliate a 1024x600:
               bing/ scaricate da Bing, galleria/ scelte a mano
    tools/     platform-tools (adb), e pittogrammi/: gli SVG Lucide delle icone del
               deck e lo script che ne fa i due file, per il tablet e per il PC
    docs/      note operative

## Come si mette in funzione

### Da un clone di questo repository

Nel repository c'e' solo il sorgente. Il resto si procura a parte, una volta:

| Cosa | Dove va |
|---|---|
| .NET 10 SDK | per `pc\TabDeck.App` |
| Android SDK con `platforms\android-19` e un `build-tools` | `%LOCALAPPDATA%\Android\Sdk`, o `ANDROID_HOME`, per `tablet\build.ps1` |
| [platform-tools](https://developer.android.com/tools/releases/platform-tools) di Google | scompattati in `tools\platform-tools` (adb) |
| [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) | lo scarica `pacchetto.ps1` (o `Installa.ps1`) se `C:\VirtualDisplayDriver` non c'e' |

Restano fuori, apposta: `config\` (deck, chiavi delle lampade, OBS e Twitch:
l'applicazione la ricrea vuota) e `tablet\keystore\` — la chiave che firma
l'APK e le estensioni. `build.ps1` ne crea una nuova al primo giro; un APK
firmato con un'altra chiave non si installa sopra quello vecchio, va prima
disinstallato.

La procedura completa (reset, debug USB, installazione, Home) e' in
[docs/procedura.md](docs/procedura.md). In breve, a tablet gia' preparato:

    cd tablet
    .\build.ps1 -Install -SetHome              # compila e installa (APK ~29 KB)

    cd ..
    dotnet run --project pc\TabDeck.App        # apre la finestra

In cima alla cartella ci sono tre `.bat`, per non doversi ricordare i comandi:

    reinstalla-tablet.bat      compila l'APK e lo rimette sul tablet
    alleggerisci-tablet.bat    mette a riposo le app Android che non servono
    ripristina-tablet.bat      le rimette in servizio

Non fanno lavoro proprio: chiamano `tablet\build.ps1 -Install` e i due script di
`tablet\sistema`. `reinstalla-tablet.bat` vuole l'SDK di Android, come la
compilazione a mano; gli altri due no, gli basta `adb`.

Su un **PC nuovo** non si ripete niente di tutto questo: si fa un pacchetto e lo
si porta di la'.

    .\pacchetto.ps1 -Exe                       # app con .NET dentro, adb,
                                               # driver dello schermo, APK,
                                               # e TabDeck-Setup.exe
    # sull'altro PC: doppio clic su TabDeck-Setup.exe, e basta.
    # Oppure, dalla cartella, due doppi clic in fila:
    Installa.bat                               # programma, driver, firewall, icone
    Tablet.bat                                 # l'app sul tablet, senza SDK
    Alleggerisci.bat                           # e, una volta, il tablet sgombro
    Ripristina.bat                             # (indietro si torna)
    Disinstalla.bat                            # e per togliere tutto dal PC
    # I .bat chiamano gli stessi Installa.ps1 / Tablet.ps1 / Disinstalla.ps1,
    # che restano li' per le opzioni. Esistono perche' di la' un doppio clic su
    # un .ps1 lo apre nel Blocco note, e l'installazione vuole l'amministratore.

Dentro il pacchetto c'e' tutto quello che serve e niente da scaricare: il
programma con .NET compreso, `adb`, il Virtual Display Driver, l'APK gia'
compilato, e `tablet\sistema` con i suoi due elenchi. `-ConConfig` ci mette
anche il deck e le icone — le chiavi delle lampade restano fuori, a meno di
`-ConChiavi`. Quel che `Installa.ps1` fa si disfa con `Disinstalla.ps1`.
L'installazione lascia in `C:\TabDeck` anche l'APK, `Tablet.bat` e i due `.bat`
della pulizia, cosi' rimettere l'app sul tablet o rifare l'alleggerimento —
versione nuova, reset, altro tablet — non vuole piu' ritrovare il pacchetto. I
dettagli sono in
[pc/pacchetto/LEGGIMI.txt](pc/pacchetto/LEGGIMI.txt), la guida distesa in
[pc/pacchetto/GUIDA.md](pc/pacchetto/GUIDA.md).

`-Exe` aggiunge **TabDeck-Setup.exe**, 67 MB, uno solo da dare a chi non vuole
sapere niente di cartelle e di PowerShell: doppio clic, il consenso di Windows,
fatto. A impacchettarlo e' IExpress, che sta dentro Windows: per fare
l'installatore non c'e' nessun installatore da installare. Dentro l'exe c'e' il
`.zip` del pacchetto e poco altro — la cartella e lo zip restano li' comunque,
per chi preferisce vedere cosa sta installando.

## Come parlano fra loro

    USB    tablet (server) <-- adb forward tcp:18765 --> PC (client)
             LocalServerSocket "tabdeck"

    WiFi   tablet (server) <-------- tcp:8765 --------> PC (client)
             ServerSocket, piu' un risponditore UDP sulla 8766 con cui
             la finestra lo trova senza che nessuno scriva l'indirizzo,
             e un annuncio sulla 8767 nell'altro verso, per quando e' il
             tablet a doversi far trovare

Frame binario: `[u8 tipo][u32 lunghezza][payload]`.

| Direzione | Frame | Contenuto |
|---|---|---|
| PC → tablet | HELLO | geometria dello schermo trasmesso e modo dei gesti |
| PC → tablet | TILE | `[x][y][w][h][JPEG]` di una porzione cambiata |
| PC → tablet | PRESENT | fine del batch, il tablet disegna |
| PC → tablet | DECK | nome del profilo e griglia dei pulsanti: etichetta, icona a tratto o glifo, colore, nome dell'immagine, e per le cartelle l'elenco che contengono |
| PC → tablet | ICON | nome e PNG di un'icona, mandata prima della griglia che la nomina |
| PC → tablet | LUCI | le lampade di casa: nome, indirizzo, chiave, protocollo |
| tablet → PC | READY | risoluzione utile del pannello, e se lo schermo e' in vista |
| tablet → PC | TOUCH | `[azione][x][y]` nello spazio del frame |
| tablet → PC | WHEEL | tacche di rotella |
| tablet → PC | PRESS | identificativo del pulsante premuto |
| tablet → PC | ACK | frame disegnato, se ne puo' mandare un altro |

Nel frame DECK viaggiano etichetta, glifo, colore, il nome di un'icona e -
per le cartelle - l'elenco annidato di quel che contengono: **l'azione resta
sul PC**. Cambiare cosa fa un pulsante non richiede di toccare il tablet, e il
tablet non e' in grado di eseguire niente per conto suo. Delle cartelle sa solo
aprirle, che e' esattamente quanto gli serve per sfogliare il deck da solo.

Le icone sono immagini vere, scelte dal PC — un file dal disco, o l'icona che
sta dentro il programma che il pulsante avvia. Il PC le riduce a 128 pixel una
volta sola e le manda prima della griglia; **il tablet se le salva**, come le
luci, cosi' all'accensione i pulsanti hanno gia' la loro faccia e continuano ad
averla a computer spento. I ventinove glifi del font di KitKat restano per quel
che sono buoni, ma non bastano a dire « Photoshop ».

Proprio perche' il tablet se li tiene, **DECK e LUCI non partono da soli**.
Collegarsi non e' chiedere di riscrivere il tablet: quello che ha in mano
funziona anche a PC spento, e quello che sta qui puo' essere un montaggio a
meta' lasciato ieri sera. Vanno quando lo si chiede — « Manda al tablet », nella
pagina Deck e nella pagina Luci — e se qui c'e' qualcosa di piu' nuovo la
finestra lo dice appena ci si collega. L'eccezione e' il pulsante « Cambia
profilo »: li' la griglia parte subito, perche' la richiesta e' la pressione
stessa.

Il frame LUCI e' l'altra cosa che il tablet si tiene, ed e' voluta: li' viaggia
tutto il necessario per agire, chiave compresa. E' l'unica cosa che il tablet sa
*fare* senza il PC, perche' una luce si accende quando si entra in una stanza,
non quando si e' seduti al computer.

## Farsi trovare

La ricerca in rete ha un buco che nessuna insistenza dal lato PC puo' chiudere:
**funziona solo se il tablet risponde, e un tablet fermo non risponde**. Non
risponde nemmeno all'ARP, che e' broadcast, e senza ARP Windows non arriva
nemmeno a bussare alla porta 8765 — il ping torna « Host di destinazione non
raggiungibile » e la connessione scade senza essere mai partita. Appena il
tablet trasmette qualcosa di suo la voce ARP si popola e da quel momento va
tutto; le voci ARP di Windows pero' scadono in un paio di minuti, ed e' li' che
nasce il « a volte si a volte no ».

Da qui il verso in piu'. Nella barra del tablet c'e' **una freccia che entra in
uno schermo**: premendola il tablet manda un datagramma « sono qui » in
broadcast, il PC lo riceve sulla 8767 e apre lui il collegamento. Chi ha appena
premuto quel pulsante ha in mano un tablet sveglio, e un tablet sveglio parla.

Non cambia nessun ruolo: il tablet resta il server sulla 8765, il protocollo e'
lo stesso, la ricerca dritta resta dov'era. Cambia solo chi parla per primo, e
le due direzioni si coprono a vicenda.

L'annuncio e' l'unico traffico non richiesto che il PC riceve — le risposte
alla ricerca dritta rientrano da sole perche' Windows le riconosce come ritorno
di un pacchetto uscito — quindi vuole una regola del firewall, una volta sola.
E' in [docs/procedura.md](docs/procedura.md#se-il-pc-non-trova-il-tablet).

## Le luci di casa

Nella barra c'e' una terza voce, l'icona della casa: le lampade Tuya — quelle
dell'app Smart Life — accese e spente dal tablet, **a PC spento e a cavo
staccato**, purche' il tablet sia sulla rete di casa.

Alexa non c'entra e non poteva entrarci: Amazon non espone nessun modo per
comandare da fuori i dispositivi collegati a un account. Le lampade Tuya invece
accettano comandi in locale, ed e' quello che fa l'app quando il telefono e' in
casa: `tablet/src/dev/tabdeck/Tuya.java` parla lo stesso protocollo, in 3.3 e in
3.4, con socket, AES e HMAC presi dal framework di Android. Nessuna dipendenza,
4 KB di APK.

I controlli non sono decisi a priori: ogni lampada, quando le si chiede come
sta, dice anche quali numeri regola. Chi non manda la luminosita' non si vede
comparire il cursore.

**Le lampade si ritrovano da sole.** Spente al muro per qualche giorno,
tornano spesso con un altro indirizzo. Il tablet prima le cercava a quello
vecchio per mezzo minuto, l'indirizzo nuovo arrivava a lettura gia' partita e
andava perso, e ogni tocco nel frattempo cadeva: servivano tre « Aggiorna » di
fila. Adesso, mentre il pannello e' acceso, il tablet ascolta sempre gli
annunci che le lampade mandano ogni cinque secondi — un thread fermo in
attesa, nessun risveglio se nessuno parla — e una lampada che torna, che si e'
spostata o che era muta si rilegge appena si fa sentire. L'indirizzo nuovo
arriva anche alla lettura gia' in corso, i tocchi fatti mentre una lampada e'
occupata si eseguono appena ha finito, e quando il Wi-Fi si riaggancia si rifa'
il giro. A sezione Casa aperta lo stato si rilegge ogni cinque secondi, e solo
allora.

Una **routine** puo' anche aspettare: il passo « Aspetta » tiene fermi da uno a
seicento secondi prima del passo dopo — « spegni la plafoniera, aspetta un
minuto, spegni il comodino ». Sul PC le routine si duplicano, si riordinano e si
provano dalla stessa pagina dove si scrivono.

Serve una **chiave locale** per lampada. Si prendono tutte in una volta dalla
finestra, con « Rileva chiavi »: compare un codice QR, lo si inquadra con l'app
Smart Life e Tuya le consegna. Nessun account da sviluppatore - e' la stessa
strada di Home Assistant. I dettagli in [docs/luci.md](docs/luci.md).

## Timer e sveglia

Le luci sono state la prima cosa che il tablet sa fare da solo; queste sono la
seconda e la terza, e nascono dalla stessa osservazione. Staccato dal PC, il
deck e' una griglia che non comanda niente e lo schermo remoto e' un rettangolo
nero: restava un pannello da sette pollici acceso sul comodino a non fare
niente. Adesso nella barra c'e' un **cronometro**.

Schermate e comportamento sono presi dal tablet di casa, il DUODUOGO, e portati
a KitKat: `OrologioView` con la ghiera (`Quadrante`), `Ora`, `Suoneria`,
`SuoneriaView`. Timer e Sveglia sono una voce sola nella barra: la schermata ha
le sue due schede in cima, e la voce riapre quella lasciata aperta. Erano due
voci, e la seconda faceva la stessa cosa di una scheda.

**Timer.** Il tempo si gira sulla ghiera, oppure si preme una delle durate
pronte: 1, 3, 5, 10, 15, 30 minuti. Ne possono correre piu' d'uno insieme, ognuno
con la sua croce per fermarlo.

**Sveglie.** Quante se ne vogliono, in elenco: ora, giorni, interruttore. I
giorni si segnano su sette riquadri che partono dal lunedi'; nessun giorno vuol
dire « una volta sola », e dopo aver suonato la sveglia si spegne da se' ma resta
in elenco. L'ora si gira sulla ghiera, a passi di cinque minuti. Una sveglia puo'
anche **accendere una routine delle luci** quando suona (« Luci quando suona »):
parte al primo squillo, non a ogni rinvio. Se il volume della sveglia e' a zero,
il titolo dell'elenco lo dice in rosso.

**A tenere il tempo e' Android, non l'app.** Il conto alla rovescia che si vede
e' una sottrazione fra l'ora di adesso e una scadenza scritta in `ora.json`, e si
aggiorna solo mentre qualcuno guarda la pagina; quello che fa suonare e'
`AlarmManager`, con una sveglia registrata per ogni timer e ogni sveglia, e
rimessa a posto dopo un riavvio, una reinstallazione o un cambio d'ora o di
fuso. Le impostazioni della sveglia unica di prima diventano la prima sveglia
dell'elenco; i tagli di durata salvati a mano non ci sono piu'. Se il processo muore,
se il pannello si spegne, se il tablet passa la notte fermo, l'ora arriva lo
stesso - ed e' l'unico modo di rispettare fino in fondo la regola del resto
dell'app, che niente giri per conto suo: per non tenere acceso nulla bisogna
farsi svegliare da chi e' gia' sveglio.

Quando suona, una schermata prende il posto di tutto - sopra le sezioni, non
dentro una - con due soli bersagli: **BASTA**, largo sei decimi, e **ANCORA 5
MINUTI**, che per un timer non c'e'. Li si preme al buio e mezzi addormentati, e
i tocchi fuori dai tasti non fanno niente. La sveglia parte piano e sale in
trenta secondi, senza toccare il volume di sistema; il timer suona a volume fisso.
Dopo tre minuti senza risposta la suoneria si spegne da sola: una sveglia che
suona all'infinito in una casa vuota e' rumore, e a batteria costa piu' di
quanto sia servita.

## Misure sul posto

Autotest su area 960x600, sorgente 3440x1440:

| | |
|---|---|
| Frame completo, 40 tessere | 60 KB, 33 ms → tetto di 30 fps |
| Scrivania ferma | 0 tessere, 16 ms (solo confronto) |
| Banda al caso peggiore, 20 fps | ~1,2 MB/s, dentro il budget di USB 2.0 |

Il costo dominante e' la compressione, non la cattura: per questo si comprimono
solo le tessere cambiate. Sul WiFi b/g/n del tablet la banda utile sta sotto il
megabyte al secondo, quindi il cavo resta la strada per lavorarci davvero.

Quando sul tablet e' aperto il deck, il PC **smette del tutto di catturare**:
niente cattura, niente compressione, niente traffico per uno schermo che nessuno
sta guardando.

## La corrente

Il consumo non e' nell'app — nessun wakelock, i thread di rete fermi in
`accept()` bloccante, che non impedisce la sospensione — ma in quattro voci di
Android regolate per un tablet che si tiene in mano dieci minuti. Di fabbrica:
pannello acceso **mezz'ora** dopo ogni tocco, luminosita' al **74 per cento**,
radio WiFi che **non dorme mai**, e soprattutto *Rimani attivo* che tiene lo
schermo acceso **finche' il cavo e' attaccato**. Misurato: 87 per cento,
dichiarato in carica su una porta USB, **-145 mA**. Si scaricava attaccato alla
corrente.

Le due che contano di piu' si cambiano dal tablet, nella pagina delle
impostazioni: **quanto resta acceso il pannello** e la luminosita'. Le scrive
TabDeck, perche' su Android 4.4 `WRITE_SETTINGS` si concede all'installazione.
Le altre due stanno fra le impostazioni protette e l'app puo' solo mostrarle in
ambra quando remano contro; si danno da `adb`, una volta sola. Tutto in
[docs/procedura.md](docs/procedura.md#9-alimentazione-e-consumi), insieme a
`tablet/sistema/batteria.ps1`, che fa la fotografia e misura il consumo vero in
percento all'ora.

## Comandare il deck dove il deck si vede

L'anteprima non e' solo un disegno: passando sopra una cella compare il suo
« togli », il tasto destro apre i comandi di quel pulsante — gli stessi
dell'elenco a sinistra — e sopra la griglia c'e' la barra di quello che si puo'
fargli: rinominare, duplicare, provare, togliere. Vale anche per il deck grande
della pagina Uso mentre si sta disponendo, dove la cartella aperta puo' essere
un'altra.

Le scorciatoie valgono su tutta la pagina e non solo dentro l'elenco: Canc
toglie, F2 rinomina, Ctrl+D duplica, Ctrl+C, Ctrl+X e Ctrl+V spostano, Ctrl+Z e
Ctrl+Y disfanno e rifanno.

## La finestra su uno schermo qualunque

La finestra non ha una misura sola. Sopra i 1280 pixel di larghezza sta come e'
disegnata: la barra con i nomi, tre colonne nella pagina dei pulsanti, due nella
dashboard. Sotto i 1280 si stringe di margini; sotto i 1080 la barra di sinistra
si riduce ai suoi glifi, la dashboard mette i riquadri in colonna e l'anteprima
del deck lascia la sua colonna per andare sopra i campi, che e' il posto dove
resta grande. Il minimo e' 880 x 560, e a quel punto tutto ci sta ancora dentro.

Non e' un vezzo: le pagine sono fatte di colonne con una larghezza minima, e su
uno schermo piccolo la somma di quei minimi non ci starebbe. Chi decide e'
`MainWindow.Misure.cs`, che riscrive quattro misure dentro le risorse
dell'applicazione; le pagine le leggono e nessuna deve saperne niente.

## Stato

Funzionano: schermo virtuale (accensione, spegnimento, risoluzione), invio
dell'immagine, tocco come mouse, deck nativo con cartelle ed editor sul PC,
collegamento su cavo e su rete con ricerca automatica del tablet, luci di casa,
timer e sveglia sul tablet.

Il deck si monta tutto dalla finestra: icone, glifi e colori si scelgono
premendoli, le combinazioni si catturano premendo i tasti, e ogni azione si
prova sul PC prima di mandarla al tablet. Accanto all'editor c'e' l'anteprima
della griglia con le stesse proporzioni del pannello, icone, cartelle e pagine
comprese, e i pulsanti si riordinano trascinandoli dentro l'anteprima —
fermandosi sul bordo si cambia pagina, e il pulsante segue.

Sui pulsanti si lavora come su dei file: si cercano per nome o per quel che
fanno, dentro le cartelle comprese, e il risultato porta dov'e'; si copiano, si
tagliano e si incollano in un'altra cartella o in un altro profilo; si mettono
in una cartella nuova in un colpo solo; e ogni cosa fatta si disfa con Annulla,
che tiene gli ultimi cinquanta passi.

I profili sono griglie intere che si scambiano: se ne fanno, si duplicano, si
rinominano, e a passare dall'una all'altra puo' essere un pulsante del deck
stesso — cioe' il tablet, senza toccare il PC.

Le macro si registrano invece di scriverle: acceso « Registra i tasti », ogni
combinazione premuta diventa un passo e le pause vere fra un tasto e l'altro
diventano attese. Dentro una sequenza ci sono anche il clic e la rotella del
mouse, i tasti da tenere premuti per i passi che seguono — quel che resta giu'
alla fine viene rilasciato da solo — l'avvio di un programma, l'apertura di un
indirizzo e il cambio di profilo. Ogni sequenza si puo' ripetere fino a
cinquanta volte, e la finestra dice quanto durera'.

I passi si lavorano a gruppi, come i pulsanti: se ne sceglie piu' d'uno tenendo
Ctrl o Maiusc e poi si spostano, si duplicano, si copiano e si tolgono insieme.
Copiati, si incollano anche dentro un altro pulsante: e' cosi' che una macro
provata su un pulsante finisce dentro gli altri.

Un passo puo' essere a sua volta una sequenza — un **gruppo** — e un gruppo ha
ripetizioni sue: « questi tre, cinque volte » senza scriverli quindici volte.
« Raggruppa » prende i passi scelti e li mette dentro un gruppo nuovo, al loro
posto; dentro un gruppo ci si entra col doppio clic e si torna su dalle briciole
sopra l'elenco, esattamente come nelle cartelle del deck. Piu' di quattro
sequenze una dentro l'altra non si fanno, per la stessa ragione per cui non si
fanno piu' di quattro piani di cartelle.

## OBS e Twitch

Sono due azioni dei pulsanti come le altre, « OBS » e « Twitch », e si
mettono anche dentro una sequenza. Il comando si sceglie da un elenco; il
secondo campo compare solo quando il comando lo chiede (la scena, la fonte, il
messaggio, il canale). Il collegamento si imposta nello stesso riquadro, sotto
l'azione: non c'e' una pagina a parte.

- **OBS** passa dal server WebSocket che OBS ha di suo dalla 28 in poi
  (Strumenti › Impostazioni del server WebSocket), porta 4455. Scene, diretta,
  registrazione, pausa, muto di una fonte audio (senza nome, il microfono
  impostato in OBS), mostra/nascondi una fonte,
  replay, camera virtuale, transizione in modalita' studio. Il collegamento si
  apre alla prima pressione e resta aperto; scene e fonti si chiedono a OBS
  solo aprendo il loro elenco.
- **Twitch** passa dall'API Helix con l'account del canale: clip, segnalibro,
  messaggio in chat, pubblicita', titolo, categoria, solo emote, solo
  follower, modalita' lenta, solo abbonati, shoutout, raid. Serve un'app di
  tipo « Public » su dev.twitch.tv (indirizzo `http://localhost`) di cui si
  incolla il Client ID; « Collega » apre twitch.tv/activate con un codice, e
  il PC non vede mai la password. Il gettone si rinnova da solo.

Le azioni le esegue il PC, come tutte: il tablet non cambia, salvo le icone
nuove (radio, cast, tv, film, megafono, bandiera, twitch...). « Prova » le
esegue subito, senza i tre secondi, perche' non dipendono dalla finestra col
fuoco. Password e gettoni stanno in `config/streaming.json`, che
`pacchetto.ps1` lascia fuori a meno di `-ConChiavi`.

## Le estensioni

TabDeck fa tutto quello che c'e' qui sopra da solo. Quello che non fa di serie
arriva come **estensione**: un file `.tabdeck` che si installa da **Sistema ›
Estensioni › Installa da file**, si accende con una spunta e si toglie con
« Rimuovi ».

Un'estensione ha due parti, e il file le porta insieme:

- **quella del PC**, una DLL che TabDeck carica da `config\estensioni\<id>`. La
  legge in memoria, non dal disco, cosi' « Rimuovi » e un aggiornamento la
  cancellano anche mentre TabDeck gira;
- **quella del tablet**, un pacchetto Android a parte. Non si installa in
  Android e non passa da adb: il PC lo annuncia con la sua impronta, il tablet
  lo chiede se non ce l'ha, e arriva sul collegamento stesso, cavo o rete. Il
  tablet lo accetta solo se si chiama `dev.tabdeck.<id>` ed e' firmato con la
  chiave di TabDeck — la rete la apre chiunque stia sul Wi-Fi — e lo carica
  accanto al suo codice, con la sua voce nella barra dopo le sezioni.

L'APK di TabDeck non contiene nessuna estensione. Rimossa sul PC, sparisce anche
dal tablet, subito se e' collegato o al collegamento dopo se non lo e'. Se ne
accende una alla volta: i frame da 0x51 a 0x5D non dicono di chi sono.

Un'estensione **non sta in questa cartella**: e' un progetto a se', accanto, che
compila contro TabDeck ma che TabDeck non conosce.

### Come se ne crea una

Un file `.tabdeck` e' uno zip con tre file, tutti in cima:

| File | Cos'e' |
|---|---|
| `estensione.json` | il manifesto, qui sotto |
| la DLL del PC | una classe che implementa `TabDeck.Estensioni.IEstensione` |
| l'APK del tablet | un pacchetto `dev.tabdeck.<id>` con una classe che implementa `dev.tabdeck.Plugin` |

    {
      "id": "esempio",
      "nome": "Esempio",
      "versione": "1.0",
      "descrizione": "Una riga per la pagina Estensioni",
      "assembly": "Esempio.dll",
      "tipo": "Esempio.Estensione",
      "apk": "esempio.apk",
      "classe": "dev.tabdeck.esempio.Pannello",
      "opzioni": [ { "chiave": "suoni", "etichetta": "Suoni", "predefinita": true } ]
    }

**Lato PC** — un progetto .NET (`net10.0-windows`) che referenzia `TabDeck.dll`.
La classe di `tipo` ha un costruttore senza argomenti e implementa
`IEstensione` (`pc/TabDeck.App/Estensioni/Contratto.cs`): `Accendi` riceve un
`IOspite`, che e' tutto quello che TabDeck presta — le spunte di `opzioni`, il
registro, l'evento `TabletPronto`, i frame in arrivo dal tablet (`Frame`) e
`Manda` per quelli verso il tablet, JSON con tipo da 0x51 a 0x5D. Spegnerla e'
`Dispose`.

**Lato tablet** — un APK compilato contro `android.jar` (API 19) e le classi di
TabDeck, senza includerle. La classe di `classe` implementa `Plugin`
(`tablet/src/dev/tabdeck/Plugin.java`): `vista` disegna la sezione, `icona` la
voce nella barra, `suFrame` riceve i frame del PC, `mostrato` e `indietro`
dicono quando la sezione e' davanti e quando si preme indietro. Va firmato con
la stessa chiave di TabDeck (`tablet/keystore`, fuori dal repository): un
pacchetto firmato con un'altra il tablet lo rifiuta.

Zippati i tre file, l'estensione si installa da **Sistema › Estensioni ›
Installa da file**.

Un pacchetto per un altro PC le porta accanto solo se gli si dice quali:

    .\pacchetto.ps1 -Estensioni "..\percorso\Estensione.tabdeck"

## La Dashboard del tablet

E' la prima voce della barra e la schermata di casa: il tasto Home del tablet ci
riporta, e il tasto indietro, finiti i passi indietro dentro una sezione, pure. A
sinistra il tempo — l'ora grande, la data, la prossima sveglia e i timer che
corrono. A destra la casa, con le prime quattro scene e quante luci sono accese,
e il PC: collegato o no, con lo schermo a un tocco o « Connetti ». In fondo le
scorciatoie verso le sezioni accese e le estensioni. Niente si configura qui:
ogni riquadro porta dove le cose si fanno. Batte al secondo solo mentre un timer
corre, e solo mentre si vede; le luci si rileggono una volta entrando.

## Casa: scene e stanze

La sezione Casa del tablet ha in cima una fascia sola: quante luci ci sono e
quante sono accese, « Tutte spente » e la freccia che rilegge. Sotto, due
pannelli. **A sinistra le scene**, pastiglie con l'icona di quel che fanno —
indovinata dal nome come nella Home dell'altro tablet: notte, cinema, tutte — e
poi le routine; la colonna c'e' solo se ci sono scene. **A destra le luci**,
raggruppate per stanza quando il PC le ha date, ognuna una tessera: un tocco
accende o spegne, la barra in fondo si trascina per la luminosita' (arriva alla
lampada a dito alzato: una lampada Tuya accetta un comando alla volta), e
l'angolo in alto a destra apre il dettaglio a pagina intera, con colori, bianchi
e luminosita'. I nomi dentro i pulsanti si rimpiccioliscono prima di tagliarsi, e
i pulsanti con una parola sono larghi quanto la parola.

Le scene di partenza sono quelle della Home del DUODUOGO, sulle stesse due
lampade: **Buonanotte** (spegne tutto), **Cinema** (spegne la Camera, Comodino
arancione al 20%), **Tutte accese**.

Sul PC, in **Gestione › Luci**:

- ogni lampada ha una **stanza**, scritta a mano o scelta fra quelle gia' usate;
  quelle senza stanza stanno insieme in fondo;
- **« + Scena »** crea una scena e la riempie con com'e' la casa adesso;
  **« Cattura com'e' adesso »** la riscrive. Le lampade dicono se sono accese e
  con che luminosita', ma non di che colore: la cattura prende quelle due cose, e
  un colore si aggiunge come passo;
- una **routine** resta una sequenza con le sue attese (« spegni la plafoniera,
  aspetta un minuto, spegni il comodino »), e sul tablet sta dopo le scene.

## La barra del tablet

Cosa compare — dashboard, deck, schermo remoto, casa, timer e sveglie — si
sceglie in **Gestione › Tablet › Sezioni** sul PC, oppure nelle **Impostazioni
del tablet**. Le scelte sono **due**: una riga per quando il PC e' collegato e
una per quando non lo e' (senza Schermo, che senza PC non esiste). Il tablet
passa dall'una all'altra da solo quando il collegamento va e viene, e tutte e due
si cambiano da entrambi i lati, collegato o no. Dal PC la scelta si salva e arriva
subito, o al collegamento dopo; dal tablet vale subito. Ogni lato si segna
quando le ha scelte, e al collegamento **vince la scelta piu' recente**: quella
fatta sul PC a tablet spento non si perde piu' sotto una scelta vecchia del
tablet. Una sezione tolta smette
di comparire e basta: le sveglie suonano lo stesso, e lo schermo acceso dal PC si
vede lo stesso, perche' e' un gesto esplicito. **Impostazioni c'e' sempre**, per
ultima: e' la strada per tornare indietro anche con tutto spento. L'ordine e'
fisso: sezioni, estensioni, poi il gruppo di servizio (connetti, Wi-Fi, riavvia,
impostazioni). La voce **Schermo** compare solo mentre il PC sta mandando lo
schermo: senza, portava a un rettangolo nero. Collegarsi non riporta piu' il
tablet al deck — resta nella sezione in cui era; il deck arriva quando lo si
chiede dal PC.

La barra si chiude con l'icona del pannello in fondo. Chiusa, resta sul bordo
sinistro una linguetta scura con una freccia, alta quanto un pollice: si tocca
per riaprirla, e il bersaglio e' piu' largo di quello che si vede.

« Riavvia » chiude TabDeck sul tablet e Android lo riapre da capo. « Rifa'
l'interfaccia » non c'e' piu': quello che si impunta sono socket e bitmap, e
restano vivi finche' vive il processo.

## Mettere TabDeck su un tablet, o toglierlo

In **Gestione › Tablet** c'e' **Prepara un tablet**: un tablet Android qualunque,
dalla 4.4 in su, diventa TabDeck col cavo, in quattro passi che si aprono uno
dopo l'altro.

1. **Collegalo.** La finestra cerca il tablet ogni due secondi, finche' e'
   aperta, e dice cosa manca: il debug USB da accendere (Numero build sette
   volte, poi Opzioni sviluppatore), il permesso da dare sul tablet, un cavo di
   sola ricarica. Con piu' tablet attaccati si sceglie quale; uno troppo vecchio
   si vede ma non si puo' scegliere.
2. **Installa**, o aggiorna tenendo deck, luci, sveglie ed estensioni.
3. **Preparalo**: TabDeck come schermata Home; il pannello che si spegne anche
   col cavo e il Wi-Fi che resta acceso per farsi trovare; e, se si vuole, le
   app di sistema di `tablet\sistema\bloccati.txt` messe a riposo (`pm block`
   su Android 4.4, `pm disable-user` dal 5 in poi — niente si disinstalla).
4. **Collegalo a TabDeck**: il tablet riceve deck, luci, impostazioni ed
   estensioni.

**Disinstalla** usa la stessa finestra: sceglie il tablet, toglie TabDeck con
tutto quello che si era salvato, rimette in servizio le app messe a riposo e
toglie l'inoltro di adb. Sul PC non si tocca niente. Gli script
`pc\pacchetto\Tablet.ps1`, `alleggerisci.ps1` e `ripristina.ps1` restano per chi
lavora da riga di comando.

### Tenere l'app del tablet al passo

Nella stessa pagina **App sul tablet** dice se l'app installata e' la stessa di
`tablet\build\TabDeck.apk`: col cavo legge l'APK installato e ne confronta il
contenuto. **Aggiorna** la reinstalla tenendo i dati, la riapre e, se si era
collegati, si ricollega. Il controllo parte da solo quando si apre la pagina e
quando ci si collega col cavo, e legge soltanto; se l'app e' da aggiornare lo
dice anche la dashboard, col suo pulsante. In rete si puo' solo dire: per
installare serve il cavo.

## Internet col cavo

Senza Wi-Fi il tablet puo' usare la rete del PC attraverso il cavo. Si sceglie in
**Gestione › Tablet › Internet col cavo**, e la postazione se lo ricorda:

- **Niente**, com'era.
- **Solo TabDeck**: quel che TabDeck chiede a internet (oggi il meteo) lo scarica
  il PC e lo manda al tablet sul collegamento di sempre. Funziona anche in rete.
- **Tutto il tablet**: sul tablet parte una VPN locale che porta al PC il
  traffico di tutte le app, sul secondo canale del cavo
  (`adb forward tcp:18766 localabstract:tabdeck-rete`); il PC apre le
  connessioni vere e rimanda le risposte. Anche le lampade di casa si
  raggiungono cosi', dal PC. La prima volta il tablet chiede il consenso alla
  VPN. Vale solo col cavo: in rete non parte.

E' lo schema di gnirehtet rovesciato: gnirehtet vuole `adb reverse`, che su
Android 4.4 non c'e'. Passano IPv4, TCP e UDP (il DNS si gira a quello del PC);
alcune app di KitKat dicono « nessuna connessione » se non vedono il Wi-Fi,
anche quando la rete c'e'.

## Il salvaschermo

Spento di partenza, si accende in **Gestione › Salvaschermo**. Parte dopo i
minuti scelti senza tocchi, mai sopra lo schermo remoto e mai mentre suona una
sveglia; un tocco o il tasto indietro lo chiudono, e quel tocco non arriva a
niente di quello che c'e' sotto. Tre stili:

- **foto in movimento** — a tutto schermo, si avvicinano e scorrono piano e
  sfumano l'una nell'altra;
- **aurora** — chiazze di luce lente su un fondo scuro;
- **solo l'ora** — su nero, e si sposta di qualche pixel ogni minuto, perche'
  un pannello acceso per ore non ci resti segnato.

Ora e data stanno in basso a sinistra, e si tolgono. Le foto vengono da **Bing**
(l'immagine del giorno delle ultime due settimane, chiesta al piu' ogni sei ore)
o da una **galleria** di immagini scelte sul PC. Le prepara il PC a 1024x600 e
il tablet se le tiene: il salvaschermo gira anche a PC spento. Il tablet non
scarica niente da solo — su Android 4.4 i certificati di oggi sono una
lotteria. « Tieni acceso il pannello mentre scorre » e' una scelta a parte: col
cavo del PC il pannello acceso consuma piu' di quanto la porta dia.

## Cosa resterebbe da fare

- tastiera software sul tablet che mandi i tasti al PC.
