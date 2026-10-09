# Procedura operativa

L'ordine conta: il reset di fabbrica cancella anche TabDeck, quindi si formatta
prima e si installa dopo.

## 1. Prima di formattare

Sul tablet, controlla se c'e' qualcosa da salvare (foto, file in `Download`).
Con il debug USB gia' attivo puoi tirare giu' tutto con:

    tools\platform-tools\adb.exe pull /sdcard/ backup-tablet

## 2. Reset di fabbrica

Sul tablet: **Impostazioni › Backup e ripristino › Ripristino dati di fabbrica ›
Ripristina dispositivo › Elimina tutto**.

Al riavvio, nella procedura guidata:

- **salta l'accesso all'account Google** (niente Play Services in avvio
  automatico: su 1GB di RAM sono ~120MB e un risveglio ogni pochi minuti);
- **salta l'account Samsung**;
- il Wi-Fi serve solo se vuoi il collegamento in rete; per il cavo puoi anche
  lasciarlo spento;
- disattiva ogni voce di backup e localizzazione proposta.

## 3. Attivare il debug USB

1. **Impostazioni › Info sul dispositivo**
2. Tocca **Numero build** sette volte, finche' non compare
   «Ora sei uno sviluppatore».
3. Torna indietro: **Impostazioni › Opzioni sviluppatore**
4. Attiva **Debug USB**.
5. **Non** attivare *Rimani attivo*. Terrebbe il pannello sempre acceso sotto
   carica, e su una porta USB del PC — che da' 500 mA, meno di quanto consuma
   un 7 pollici acceso — il tablet non si ricarica: resta fermo a percentuali
   basse anche col cavo attaccato. TabDeck tiene lo schermo acceso da solo, ma
   soltanto mentre stai guardando il monitor remoto.
6. Collega il cavo e sul tablet accetta **Consenti debug USB** spuntando
   *Consenti sempre da questo computer*.

Verifica dal PC:

    tools\platform-tools\adb.exe devices

Deve comparire una riga che finisce con `device`. Se dice `unauthorized`, la
richiesta sul tablet non e' stata accettata; se non compare niente, prova
un'altra porta USB o un altro cavo — molti cavi da 7 pollici sono solo di
ricarica e non portano i dati.

## 4. Installare TabDeck sul tablet

    cd tablet
    .\build.ps1 -Install -SetHome

L'ultimo comando apre la scelta della Home sul tablet: seleziona **TabDeck** e
conferma **Sempre**. Da questo momento il tasto Home riporta sempre qui e
all'accensione il tablet entra direttamente nell'app, sul deck.

Per tornare temporaneamente a TouchWiz: tieni premuto **l'indicatore in fondo
alla barra laterale** per aprire le impostazioni di Android, poi
*Applicazioni predefinite › Schermata Home*.

## 5. Alleggerire quello che resta

Il grosso lo fa uno script:

    alleggerisci-tablet.bat        # in cima alla cartella; doppio clic, chiede conferma
    ripristina-tablet.bat          # e per tornare indietro

Chiamano `tablet\sistema\alleggerisci.ps1`, che mette a riposo con `pm block`
i pacchetti elencati in `tablet\sistema\bloccati.txt` e poi ferma con
`am force-stop` quelli gia' avviati. `pm block` **nasconde** il pacchetto e gli
impedisce di ripartire all'avvio: non disinstalla niente, non tocca la
partizione di sistema, non serve il root. Chi resta acceso, e perche', e' in
`tablet\sistema\tenere.txt` — framework, `systemui`, le Impostazioni che sono
l'unica via d'uscita da TabDeck, i loro provider, `adb`, la tastiera, TabDeck.

Su Android 4.4 qualche pacchetto privilegiato rifiuta il blocco — Google Play
Services su tutti — e va spento a mano da **Impostazioni › Gestione
applicazioni › Tutte**, toccando l'app e premendo **Disattiva**. Lo script li
elenca a fine corsa e dice dove.

Riavvia il tablet per la misura vera: e' all'avvio che questi si ripresentavano.

Con TabDeck come Home, TouchWiz non viene comunque piu' caricato: e' il
risparmio piu' grosso e non costa nulla.

## 6. Il driver dello schermo virtuale

Questo passaggio e' gia' fatto su questa macchina, ma serve saperlo se si
riparte da zero: senza, il tablet resta un visore, perche' Windows non lascia
trascinare una finestra dove non c'e' un monitor.

Installato: **Virtual Display Driver** (progetto `VirtualDrivers/Virtual-Display-Driver`).
Il catalogo del driver e' firmato da SignPath Foundation con CA GlobalSign,
quindi Windows lo accetta senza toccare nessun archivio di certificati.

    C:\TabDeck\driver\schermo\          MttVDD.inf, MttVDD.dll, mttvdd.cat
    C:\TabDeck\driver\schermo\vdd_settings.xml   risoluzioni, 1024x600 per prima

La cartella la dice al driver la chiave `HKLM\SOFTWARE\MikeTheTech\VirtualDisplayDriver`,
valore `VDDPATH`; senza, il driver cerca in `C:\VirtualDisplayDriver`, dove
stava sulle installazioni di prima. Lo mette `Installa.ps1`, scaricandolo se
serve (`pc\pacchetto\SchermoVirtuale.ps1`).

Il nodo del dispositivo si crea con `nefconw --create-device-node --class-name
Display --hardware-id Root\MttVDD`, poi `pnputil /add-driver MttVDD.inf
/install`. Il monitor compare subito, senza riavviare.

**Perche' non Parsec VDD**, provato prima: il suo driver e' inerte finche' un
programma non gli chiede uno schermo attraverso un'interfaccia privata, e la
build 0.45 quella interfaccia non la espone (il GUID documentato non compare in
nessuno dei file installati). Restava installato e senza monitor.

Per togliere il driver: `pnputil /delete-driver oem<N>.inf /uninstall` piu'
`nefconw --remove-device-node --hardware-id Root\MttVDD`.

## 7. Aprire l'applicazione sul PC

    pc\app\TabDeck.exe

(oppure `dotnet run --project pc\TabDeck.App` dal sorgente.)

Si apre una finestra con quattro schede. Non parte niente da solo: finche' non
premi **Avvia**, l'applicazione non cattura e non trasmette.

**Chiede i permessi di amministratore**, e non e' un vezzo: servono ad accendere
e spegnere il monitor virtuale (`Enable-PnpDevice` / `Disable-PnpDevice`) e a
mandare i tocchi del tablet anche alle finestre elevate, cosa che Windows vieta
a un programma normale. Senza elevazione TabDeck funziona lo stesso, ma il
consenso lo chiede a ogni « Manda lo schermo » e a ogni « Ferma »: piu' richieste,
non meno.

Per non sentirselo chiedere piu':

    .\avvio.ps1              il collegamento passa da un'attivita' pianificata
    .\avvio.ps1 -Togli       si torna alla domanda a ogni apertura

L'attivita' si chiama « TabDeck » e **non ha trigger**: nessun orario, nessun
avvio all'accesso. Porta con se' soltanto il livello dei permessi, e si muove
quando la chiami premendo il collegamento — che da quel momento punta a
`schtasks /run /tn TabDeck` invece che all'eseguibile, con l'icona di TabDeck e
la finestra ridotta a icona perche' il lampo nero duri il meno possibile. Tre
impostazioni non stanno ai valori di fabbrica: niente limite di tre giorni (
ammazzerebbe TabDeck mentre sta nel vassoio), istanze **in parallelo** (serve la
seconda copia, e' quella che dice alla prima di farsi vedere) e niente stop a
batteria. Il conto da pagare e' che chiunque possa premere quel collegamento fa
partire un programma elevato senza nessuna domanda.

**La croce non spegne.** Ritira la finestra nell'area di notifica, accanto
all'orologio, e il collegamento col tablet resta aperto: chiudere davvero lo
staccava, e il gesto piu' facile della finestra era quello che costava di piu'.
L'icona si preme per riaprire; col tasto destro dice come sta il collegamento e
offre **Esci**, che e' l'unico modo di spegnere davvero. Uscendo con del deck
non salvato, la domanda di sempre.

**Ne gira una sola.** Premendo il collegamento sul desktop mentre TabDeck e' gia'
nascosto non parte una seconda copia: quella nuova si accorge del presidio — un
mutex `Local\TabDeck.unaSola` — manda alla vecchia il messaggio « mostrati » e
se ne va prima di aprire qualunque cosa. Il controllo sta in `App.xaml.cs` e non
nella finestra proprio per questo: costruire la finestra vorrebbe dire aprire le
socket e leggere la configurazione, cioe' fare il danno che si sta evitando. Per
la stessa ragione in App.xaml non c'e' piu' `StartupUri`: la finestra si apre
con una riga, dopo il controllo.

**Si aggiorna e prende le estensioni da solo** (dal 15/09/2026). TabDeck gira come
amministratore, quindi uno script normale non puo' ne' chiuderlo ne' premergli un
bottone. Due porte, che non aprono permessi nuovi (chi scrive li' scriveva gia' nelle
cartelle che TabDeck carica all'avvio):

- **`pc\app-nuovo\pronto`** — la build nuova si pubblica in `pc\app-nuovo`, e
  `aggiorna-app.ps1` lascia per ultimo il file `pronto`. TabDeck se ne accorge, lancia uno
  script coi suoi permessi che aspetta la sua chiusura, copia la build con `robocopy` e lo
  riapre. Un errore di copia resta in `pc\app-nuovo\errore.txt`, e si riapre la build di
  prima. Con del deck non salvato compare la domanda di sempre, e l'aggiornamento aspetta.
- **`config\estensioni\arrivo`** — un `.tabdeck` messo li' si installa come da « Installa
  da file », anche a TabDeck aperto, e poi si cancella; se non si installa diventa
  `.scartato` e l'errore va nel registro.

Per riaprirlo elevato senza la domanda di Windows basta l'attivita' pianificata:
`schtasks /run /tn TabDeck`.

### Scheda Schermo

0. **Che cosa mandare al tablet** — in cima alla pagina. Lo *schermo virtuale*
   e' il valore normale ed e' quello di tutti i giorni; sotto ci sono i monitor
   veri attaccati adesso. Cambiando voce mentre si trasmette la trasmissione si
   ferma: continuare a mandare il monitor di prima dopo averne scelto un altro
   sarebbe peggio. A un monitor vero la risoluzione non viene toccata mai —
   l'immagine si riduce mentre viaggia, con le bande nere — e se lo si stacca
   non parte niente: **non si ripiega su un altro schermo**, si dice che quello
   scelto non c'e'.
1. **Cerca driver** — trova l'adattatore virtuale e dice se e' acceso. Con la
   spunta *Crea lo schermo virtuale quando il tablet si collega* non serve fare
   altro: premendo **Avvia**, TabDeck si collega al tablet, accende il monitor,
   lo porta alla risoluzione del pannello e comincia a mandarlo. Fermando, o
   staccando il tablet, il monitor sparisce e le finestre tornano di qua.
   **Accendi** / **Spegni** restano per tenerlo acceso a mano.
2. **Aggiorna elenco** — mostra gli schermi. Quello virtuale e' segnato come
   tale; selezionalo: e' quello che andra' sul tablet.
3. **Risoluzione › 1024x600 › Applica** — gli stessi pixel del pannello. Se
   quella risoluzione non c'e' nell'elenco, il driver che hai scelto non la
   sostiene: prendi la piu' vicina, l'immagine verra' ridotta.
4. Cursori di **frame al secondo** e **qualita'**: oltre i 20 fps il decoder
   JPEG del PXA986 non tiene il passo, e sotto qualita' 55 gli artefatti sul
   testo si vedono.

Da qui in poi quel monitor si usa come tutti gli altri: trascinaci le finestre,
Windows lo ricorda.

### Scheda Collegamento

- **Usa il cavo** — premi *Cerca tablet*: deve comparire il numero di serie.
  Regge circa 7 MB/s, quindi non e' mai lui a rallentare.
- **Usa la rete** — premi *Cerca in rete*: il tablet risponde da solo al
  richiamo e compare nell'elenco con il suo indirizzo. Non serve leggerlo dalle
  impostazioni ne' riscriverlo quando il router lo cambia.

Poi **Avvia**, in alto a destra.

### Scheda Pulsanti

La scheda e' divisa in tre: a sinistra i pulsanti nell'ordine in cui riempiono
la griglia, al centro quello che il pulsante e' e fa, a destra **come lo vedrai
sul tablet** — la stessa griglia, con gli stessi margini e le stesse proporzioni
del pannello. Sull'anteprima si clicca: una cella scelta porta al suo pulsante,
una cella vuota ne aggiunge uno.

Sopra l'elenco c'e' il **percorso** — `Deck › Musica › Volume` — e ogni pezzo
riporta li' dov'e' scritto; sotto, un campo che **cerca** fra tutti i pulsanti,
dentro le cartelle comprese, per nome o per quel che fanno: premendo un
risultato la finestra ci va.

Sull'elenco funziona il **tasto destro**, e lo stesso menu si apre col pulsante
`⋯`: apri, rinomina, prova, duplica, copia, taglia, incolla, sposta in una
cartella, metti in una cartella nuova, togli. Con la tastiera: `Invio` apre o
va all'etichetta, `F2` rinomina, `Canc` toglie, `Ctrl+D` duplica, `Ctrl+C`
`Ctrl+X` `Ctrl+V` copiano, tagliano e incollano, `Backspace` risale di un
piano.

**Annulla** (`Ctrl+Z`) e **Rifai** in fondo alla pagina tengono gli ultimi
cinquanta passi. Non e' un lusso: una cartella tolta si porta via tutto quello
che aveva dentro, e prima l'unico modo di rimediare era chiudere senza salvare —
perdendo anche tutto il resto.

| Azione | Cosa vuole |
|---|---|
| Combinazione di tasti | `ctrl+shift+m`, `alt+f4`, `win+d` — oppure **Cattura**, e la premi |
| Tasto multimediale | si sceglie dall'elenco: `playpause`, `next`, `prev`, `mute`, `volup`, `voldown` |
| Scrivi un testo | qualsiasi stringa, indipendente dalla disposizione tastiera |
| Avvia un programma | `explorer.exe`, `code`, con argomenti; **Sfoglia** apre il disco |
| Apri un indirizzo | aperto nel browser predefinito |
| Clic del mouse | sinistro, destro, centrale o doppio, dove si trova il puntatore |
| Rotella del mouse | da -20 a 20 tacche, positivo verso l'alto |
| Sequenza di passi | i passi si aggiungono, si spostano e si provano nel riquadro sotto |
| Cartella di pulsanti | non fa niente sul PC: apre sul tablet l'elenco che contiene |
| Cambia profilo del deck | il nome di un altro profilo, scelto da un elenco: al tablet va quella griglia |

Dentro una sequenza ci sono tre tipi di passo in piu':

- **Aspetta** — fra un passo e l'altro passano 30 millisecondi, e quando non
  bastano — un programma che deve finire di aprirsi — si mette un'attesa
  esplicita;
- **Tieni premuto** e **Rilascia** — un tasto che resta giu' per i passi che
  seguono: shift durante una serie di frecce, alt mentre si preme tab piu'
  volte. Quel che non viene rilasciato a mano si rilascia da solo alla fine
  della sequenza, e nel registro compare che e' successo: un tasto rimasto
  premuto renderebbe il PC inutilizzabile senza dire perche'.

**Registra i tasti** monta la sequenza facendola invece di scriverla: acceso il
pulsante, ogni combinazione premuta sulla tastiera diventa un passo, e le pause
vere fra un tasto e l'altro diventano attese. Esc chiude. Le pause sotto un
quinto di secondo non si segnano — sono quelle di chi batte veloce, non quelle
che servono.

**Ripeti** fa rifare la sequenza intera da 1 a 50 volte, e accanto compare
quanto durera': dodici passi con mezzo secondo d'attesa fanno sei secondi in cui
il PC va per conto suo, ed e' meglio saperlo mentre li si scrive.

Quello che non funzionera' si legge mentre lo si scrive, sotto i campi: un nome
di tasto che non esiste, un programma che non si trova, una sequenza vuota. Sul
tablet un'azione sbagliata non fa niente e non lo dice, ed e' il modo peggiore
di accorgersene.

**Prova il pulsante** esegue l'azione qui sul PC dopo tre secondi. I tre secondi
servono: una combinazione di tasti finisce nella finestra che ha il fuoco, e nel
momento in cui si preme sarebbe TabDeck. Sono il tempo per andare sulla finestra
a cui la combinazione e' destinata.

**L'ordine si cambia trascinando.** Un pulsante preso nell'anteprima e portato
in un'altra cella ci va, e gli altri scorrono di conseguenza: nella griglia non
esistono buchi, si riempie in ordine. Fermandosi sul bordo destro o sinistro la
pagina cambia da sola dopo mezzo secondo, e il pulsante segue: e' l'unico modo
di portarlo dalla prima pagina alla seconda, perche' mentre si trascina le
frecce sotto l'anteprima non si possono premere. Le due frecce ▲▼ accanto
all'elenco fanno la stessa cosa una cella per volta, per quando si vuole
spostare di un posto senza prendere la mira.

Per disporli con calma c'e' una griglia grande quanto la finestra: **Uso ›
Deck**, e li' il pulsante **Disponi i pulsanti**. Acceso quello, il deck non si
preme piu': si trascina, con le stesse regole dell'anteprima e con le celle
grandi come sul tablet. Accanto compare **Salva e manda al tablet**, perche'
finche' non si salva di la' resta tutto com'era.

**Colonne** e **righe** cambiano la griglia sotto l'anteprima. I pulsanti che
non ci stanno non spariscono: finiscono nelle pagine successive, che sul tablet
si raggiungono scorrendo di lato, e i pallini in fondo dicono quante sono. La
griglia e' una sola e vale anche dentro le cartelle: e' quella del pannello.

### I profili

In cima alla colonna di sinistra c'e' il **profilo**: la griglia intera, con i
suoi pulsanti, le sue cartelle e le sue righe e colonne. Ne convive quanti se ne
vogliono - « Lavoro », « Video », « Casa » - ma sul tablet ce n'e' sempre uno
solo, ed e' quello scelto qui.

I quattro pulsanti sotto l'elenco fanno le sole cose che servono: **Nuovo** (un
profilo vuoto con la griglia di questo), **Duplica** (una copia, pulsanti
compresi), **Rinomina**, e **&#x2715;** che lo butta via chiedendo conferma e
contando quanti pulsanti si porta dietro. Il nome si scrive nella riga che
compare li' sotto, non in una finestrella a parte: Invio conferma, Esc lascia
stare.

**Cambiare profilo salva.** I profili stanno tutti in `config/deck.json`, quindi
passare da uno all'altro riscrive comunque il file: tanto vale che quello che si
stava montando ci finisca dentro invece di restare per aria. Per lo stesso
motivo, cambiando profilo la pila di **Annulla** riparte da zero — i passi
indietro erano di un altro deck.

Lo stesso elenco sta anche in **Uso &#x203A; Deck**, che e' la pagina che si
guarda mentre si lavora.

E poi c'e' la strada che conta di piu': un pulsante con l'azione **Cambia
profilo del deck**. Premuto sul tablet, il PC salva e manda la griglia dell'altro
profilo. Tre profili da venti pulsanti, e in ognuno un pulsante che porta agli
altri, sono sessanta pulsanti raggiungibili senza mai alzarsi.

Un `deck.json` scritto prima che i profili esistessero si apre lo stesso:
quello che c'era diventa il profilo « Deck ».

**Esporta** e **Importa**, nella stessa barra, portano i profili fuori di qui.
« Esporta » chiede prima quanto: *solo questo*, che e' quello che si manda a
qualcuno o si mette da parte prima di rifare il deck, oppure *tutti*, che e' il
deck intero da portare su un altro PC. Ne esce un file `.tabdeck.json`, di
testo, con dentro i profili scelti **e le immagini dei loro pulsanti**: copiare
`config/deck.json` a mano non basta, perche' le icone stanno accanto in
`config/icone` e di la' non ci sarebbero.

« Importa » li aggiunge e non sovrascrive niente. Un profilo che si chiama come
uno che c'e' gia' entra col nome libero accanto — « Lavoro 2 » — e i pulsanti
« Cambia profilo » che nominavano un profilo dello stesso pacchetto vengono
corretti col nome nuovo; se non lo fossero, porterebbero al profilo di chi
importa, che e' un altro deck. Le icone gia' presenti non si riscrivono: il nome
di un'icona e' l'impronta del suo contenuto, quindi stesso nome vuol dire stessa
immagine.

### Le cartelle

Un pulsante la cui azione e' **Cartella di pulsanti** non fa niente sul PC: sul
tablet apre l'elenco che contiene, con in cima una fascia che dice dove si e' e
riporta indietro. La apre il tablet da solo, come sfoglia le pagine da solo,
quindi funziona **anche a PC spento** — che e' tutto il motivo per cui il
contenuto viaggia nel frame DECK invece di restare qui. Si annidano fino a
quattro piani.

Nella griglia una cartella si riconosce da quattro quadratini nell'angolo in
alto a destra, una griglia in miniatura; nell'elenco, dalla freccetta `›` e
dalla riga che dice quanti pulsanti ha dentro.

Ci si entra con un doppio clic — nell'elenco o sull'anteprima — e se ne esce
premendo la fascia in cima all'anteprima, o una briciola del percorso, o
`Backspace`. Sul tablet basta un tocco per entrare, la fascia per uscire, e il
tasto ▦ della barra laterale riporta alla radice da qualunque profondita'.

Per metterci dentro un pulsante ci sono tre strade:

- **trascinarcelo sopra** nell'anteprima: lasciato sul centro di una cartella,
  ci finisce dentro in fondo; lasciato sul bordo della cella, si limita a
  prendere quel posto;
- **fermarcisi sopra** mentre lo si trascina: dopo mezzo secondo la cartella si
  apre e si continua a trascinare un piano piu' giu';
- **Sposta in**, dal tasto destro, che elenca tutte le cartelle col loro
  percorso: e' la strada che funziona anche verso una cartella che sta tre
  pagine piu' in la'.

**Metti in una cartella nuova** fa il contrario: prende il pulsante scelto e
gli costruisce una cartella attorno, al suo posto. E' come nascono quasi
sempre — non si decide di fare una cartella e poi si cerca cosa metterci.

Togliendo una cartella si toglie anche quel che ha dentro, e la finestra lo
chiede contando quanti sono. Cambiando l'azione di una cartella in qualcos'altro
il contenuto non si perde: resta in memoria e torna se si rimette « Cartella di
pulsanti », ma nel file e sul tablet non ci va, e il registro lo dice.

**Salva e manda al tablet** scrive il file e aggiorna subito la griglia sul
tablet, senza reinstallare niente.

### La faccia del pulsante: icona o glifo

**Scegli un'immagine** prende un PNG, un JPG, un BMP, un GIF o un ICO dal disco;
**Dal programma** prende l'icona che sta dentro l'eseguibile dell'azione
« Avvia un programma », che per quei pulsanti e' quasi sempre quella giusta.
Dove c'e' un'icona, il glifo non viene disegnato.

L'immagine non resta dov'era: viene ridotta a 128 pixel, riscritta in PNG e
copiata in `config/icone` con un nome che e' l'impronta del suo contenuto.
Quindi spostare o cancellare l'originale non lascia il pulsante senza faccia,
due pulsanti con la stessa immagine occupano un file solo, e al salvataggio le
immagini che nessun pulsante usa piu' vengono tolte.

Al tablet le icone arrivano prima della griglia, e **lui se le salva**: come le
luci, restano al loro posto a PC spento e all'accensione i pulsanti hanno gia'
la loro faccia. Il PC le rimanda una volta per collegamento, perche' dall'altra
parte potrebbe esserci un tablet appena reinstallato.

Il glifo resta per quel che e' buono — una freccia, un quadrato — e si sceglie
dai gettoni sotto il campo, senza scriverlo: sono i simboli che il font di
KitKat sa disegnare, provati uno per uno sul pannello.

    ▲ ▼ ◀ ▶   ■ □ ▢ ▣ ▤ ▥ ▦ ▧ ▨ ▩ ▪ ▫
    ◆ ◇ ○ ● ◉ ◎ ◐   × ÷ ± – + ↻

Questi invece **no**, ed escono come celle vuote: ▬ ▭ ▮ ▯. E' anche il motivo
per cui esistono le icone: ventinove simboli non bastano a dire « Photoshop ».

## 8. Usare il tablet

**Il deck e le luci non arrivano da soli.** Il tablet se li tiene — e' cosi' che
il deck si sfoglia e le lampade si accendono a PC spento — quindi collegarsi non
li riscrive: quello che sta sul PC puo' essere un montaggio lasciato a meta'.
Vanno quando lo si chiede:

| Cosa | Da dove |
|---|---|
| il deck | **Uso › Deck**, oppure **Gestione › Pulsanti**: « Manda al tablet ». « Salva e manda » fa tutt'e due |
| le luci | **Gestione › Luci**: « Manda al tablet » |

Se qui c'e' qualcosa di piu' nuovo di quello che ha il tablet, la riga di stato
lo dice appena ci si collega, e sotto il deck compare « Salvato, non ancora sul
tablet ». L'unica cosa che parte comunque e' il cambio di profilo: li' la
richiesta e' la pressione stessa, sul tablet o nell'elenco.

La barra laterale, dall'alto: **deck** (griglia), **schermo** (monitor),
**chiudi barra**, poi in fondo **Wi-Fi**, **impostazioni di Android**,
**riavvia** (tenuto premuto chiude il processo), e gli indicatori di CPU, RAM e
batteria.

Se qualcosa si blocca ci sono tre vie d'uscita, in ordine di comodita':

1. i due tasti **Wi-Fi** e **impostazioni** nella barra;
2. **tieni premuto il tasto Indietro**: apre le impostazioni di Android. Vale
   anche a barra nascosta, perche' il tasto e' fisico;
3. **tieni premuta la maniglia** sul bordo sinistro.

TouchWiz e' rimasto installato apposta: se un giorno TabDeck non partisse, il
tablet avrebbe comunque una Home a cui tornare da *Impostazioni › Applicazioni
predefinite › Schermata Home*.

I due modi:

- **▦ deck** — la griglia dei pulsanti. E' il modo predefinito, e funziona anche
  a PC spento (i pulsanti restano visibili ma spenti; le cartelle no, quelle si
  aprono lo stesso). Premendo di nuovo l'icona, da dentro una cartella si torna
  alla radice. Mentre e' aperto, il PC
  smette del tutto di catturare.
- **■ schermo** — il monitor. Il **‹** nasconde la barra e porta lo schermo a
  1024x600 pieni; la maniglia **›** al centro del bordo sinistro la fa tornare.

Sullo schermo, i tocchi diventano mouse: tocco = clic, trascinamento = drag,
pressione lunga = tasto destro, due dita = rotella.

## 9. Alimentazione e consumi

Il tablet vuole un alimentatore da 2 A. Una porta USB del PC ne da' 500 mA:
basta a tenerlo vivo con lo schermo spento, non a ricaricarlo mentre lo si usa.
Se ti serve che si ricarichi davvero, dagli il suo alimentatore e collegati in
rete invece che col cavo.

Segnale che non sta ricevendo abbastanza: l'indicatore **BAT** nella barra
laterale mostra il **+** della carica ma la percentuale non sale, o scende.

### Le quattro impostazioni che decidono il consumo

Il consumo non dipende quasi per niente da TabDeck — l'app non tiene nessun
wakelock, i suoi thread di rete stanno in `accept()` bloccante e non impediscono
la sospensione — ma da quattro voci di Android. Il tablet esce di fabbrica con
i valori pensati per stare in mano dieci minuti, non appoggiato alla scrivania
tutto il giorno. Misurate su questo SM-T210 prima di toccare niente:

| Voce | Di fabbrica | Cosa vuol dire |
|---|---|---|
| `screen_off_timeout` | 1 800 000 ms | mezz'ora di pannello acceso dopo ogni tocco |
| `stay_on_while_plugged_in` | 3 | col cavo attaccato lo schermo **non si spegne mai** |
| `screen_brightness` | 188/255 | il 74 per cento |
| `wifi_sleep_policy` | 2 | la radio non dorme mai |

Il colpevole principale e' il secondo, e non si vede: `stay_on_while_plugged_in`
vince sulla scelta « tieni acceso lo schermo » dell'app, perche' quando il cavo
c'e' il timeout di Android non scade e il ramo « lascia fare ad Android » non fa
niente. Con quei valori il tablet all'87 per cento, dichiarato *in carica* su
una porta USB, segnava **-145 mA**: si stava scaricando attaccato alla corrente.

**Le prime due righe si cambiano dal tablet**, nella pagina delle impostazioni
dell'app: durata del pannello acceso e luminosita'. Le scrive TabDeck stesso —
su Android 4.4 `WRITE_SETTINGS` e' un permesso normale, concesso
all'installazione — e quindi restano a portata anche a PC spento.

**Le altre due no**: stanno in `Settings.Global` e vogliono
`WRITE_SECURE_SETTINGS`, che si da' solo alle app di sistema. La pagina si
limita a mostrarle in ambra quando sono contro il risparmio. Si cambiano da qui,
una volta sola:

    tools\platform-tools\adb.exe shell settings put global stay_on_while_plugged_in 0
    tools\platform-tools\adb.exe shell settings put global wifi_sleep_policy 0

La seconda ha un prezzo: con la radio che dorme insieme al pannello, al risveglio
servono due o tre secondi di riaggancio prima che una lampada risponda, e finche'
dorme il PC non trova il tablet in broadcast. Col cavo USB non cambia niente.

### Misurare

    cd tablet\sistema
    .\batteria.ps1              stato, impostazioni, chi tiene sveglio il tablet
    .\batteria.ps1 -Misura 30   consumo vero in %/ora, a cavo staccato

La fotografia legge anche `/sys/kernel/debug/wakeup_sources`, cioe' il conto di
quanto ogni pezzo di hardware ha tenuto il SoC fuori dalla sospensione. Due nomi
tornano sempre: **mv-udc** e' la porta USB — finche' il cavo e' attaccato il
tablet non si sospende mai, ed e' cosi' per forza — e **mmc2** e' la radio WiFi.

Il contatore di corrente `current_now` c'e' ed e' in mA con segno, ma viene da un
fuel gauge lento: non serve a confrontare due stati a un minuto di distanza,
provato, i numeri si sovrappongono. L'unica misura che tiene e' la percentuale
che scende in mezz'ora.

## 10. Se il tablet non sta dietro

Il PC non manda mai un frame nuovo prima che il tablet abbia confermato di aver
disegnato il precedente. Se il PXA986 rallenta, il PC salta dei frame invece di
accodarli, e in alto compare il conteggio accanto alla banda.

E' il comportamento giusto: chi guarda vuole l'ultima immagine, non tutte.
Diventa un problema solo se salta quasi tutto; in quel caso abbassa i frame al
secondo o la qualita' nella scheda Schermo. Il tablet dice la sua ogni cinque
secondi:

    tools\platform-tools\adb.exe logcat -s TabDeck.Screen

## 11. Su un altro PC

Non si ripete la procedura: si fa un pacchetto e lo si porta di la'.

    .\pacchetto.ps1 -Exe

Dentro finiscono il programma **con .NET compreso** (~150 MB: sul PC nuovo non
c'e' niente da scaricare), `adb`, il Virtual Display Driver (preso da
`C:\VirtualDisplayDriver` se c'e', altrimenti scaricato), l'APK del tablet gia' compilato e `tablet\sistema`
con i suoi due elenchi, cosi' di la' si puo' anche alleggerire. Con `-Leggero` il
.NET resta fuori e il pacchetto scende a pochi megabyte, ma di la' serve il
*.NET Desktop Runtime 10*. Con `-ConConfig` ci vanno anche deck e icone; le
chiavi delle lampade **restano fuori** a meno di `-ConChiavi`, perche' con
quelle chiunque sia sulla rete di casa accende le luci.

Con `-Exe` ne esce anche **`TabDeck-Setup.exe`**, 67 MB: uno solo da portare, e
di la' basta il doppio clic. Lo impacchetta `iexpress`, che sta dentro Windows
da sempre — per fare l'installatore non c'e' nessun installatore da installare.
Dentro ci sono il `.zip` del pacchetto e `Avvia.cmd`, che lo apre in una
cartella temporanea e chiama `Installa.ps1` chiedendo il consenso. Due cose da
sapere se lo si tocca: IExpress e' un programma **con finestra**, quindi
lanciato con `&` PowerShell non lo aspetta e l'exe sembra non farsi; e vuole il
percorso della ricetta `.sed` **senza virgolette**, il che vuol dire che la
cartella di lavoro non puo' avere spazi nel nome — per questo il banco sta in
`%TEMP%` e non accanto al pacchetto.

Sul PC nuovo si va di doppio clic, dentro la cartella:

    Installa.bat          # copia in C:\TabDeck, driver, firewall, icone
    Tablet.bat            # installa l'app sul tablet, senza SDK di Android
    Alleggerisci.bat      # e, una volta, il tablet sgombro (Ripristina.bat indietro)
    Disinstalla.bat       # per togliere tutto dal PC

I `.bat` chiamano gli stessi `.ps1`, che restano li' per le opzioni; chiedono
loro il consenso dove serve. A mano, in un PowerShell **come amministratore**:

    .\Installa.ps1        # copia in C:\TabDeck, driver, firewall, icone
    .\Tablet.ps1          # installa l'app sul tablet, senza SDK di Android

`Installa.ps1` fa quattro cose e le dice tutte mentre le fa: copia la cartella
(la `config` che c'e' gia' non la tocca mai), se lo schermo virtuale manca mette il driver in
`C:\TabDeck\driver\schermo`, scaricandolo se il pacchetto non ce l'ha, e **crea il nodo del dispositivo** — lo schermo
virtuale non e' una scheda che si attacca, nessun bus lo annuncia, quindi il
dispositivo va creato a mano sotto la radice; apre udp 8766 e 8767 nel firewall
piu' il programma; fa le icone nel menu Start e sul desktop. Le tre chiamate a
`setupapi` che creano il nodo sono le stesse di `devcon` e di `nefcon`, prese
direttamente: cosi' nel pacchetto non serve portarsi dietro un eseguibile di
terze parti per venti righe di lavoro.

Se il nodo non si crea, lo dice e va avanti: senza schermo virtuale restano il
deck, le luci, il timer e la sveglia — manca solo il secondo monitor.

`.\Disinstalla.ps1` disfa tutto: regole, icone, dispositivo, driver dal
magazzino, chiave `VDDPATH`, cartella. Con `-TieniConfig` lascia il deck dov'e'.

`Tablet.ps1` guarda il modello prima di installare: con due tablet attaccati,
« il primo che adb elenca » installerebbe sul tablet sbagliato senza dire
niente. Se trova un E960 al posto di un SM-T210, si ferma e lo scrive. Lo stesso
vale per `alleggerisci.ps1`, `ripristina.ps1` e `batteria.ps1`, che passano da
`tools\dispositivo.ps1` — nel pacchetto ci va anche quello, un piano sopra
`tablet\sistema`, cioe' dove sta qui.

## Se il PC non trova il tablet

Il sintomo e' capriccioso: a volte « Cerca in rete » lo trova subito, a volte
non trova niente pur essendo tutto acceso e sulla stessa rete. Misurato sul
posto, la causa non e' capricciosa per niente.

Un tablet fermo **non risponde all'ARP**, che e' broadcast. Senza ARP Windows
non sa il MAC del tablet e non riesce a mandargli nemmeno il primo pacchetto:

    ping 192.168.1.7
    Risposta da 192.168.1.9: Host di destinazione non raggiungibile.

Quella risposta arriva dal PC stesso, non dal tablet. Appena il tablet
trasmette qualcosa di suo — basta un ping in uscita da lui — la voce ARP si
popola sul PC e da quel momento funziona tutto: ping, porta 8765, ricerca in
rete sei volte su sei. Le voci ARP di Windows scadono pero' in un paio di
minuti, e si torna al punto di prima.

**La soluzione e' il pulsante sul tablet**, la freccia che entra nello schermo,
nel gruppo in fondo alla barra. Il tablet manda in broadcast « sono qui » sulla
udp:8767 e il PC apre il collegamento. Chi preme quel pulsante ha in mano un
tablet sveglio, e un tablet sveglio parla.

### La regola del firewall

L'annuncio e' l'unico pacchetto **non richiesto** che TabDeck riceve: le
risposte alla ricerca dritta rientrano da sole, perche' Windows le riconosce
come ritorno di un pacchetto uscito. Un annuncio no, e il firewall lo scarta in
silenzio — il tablet direbbe « chiamata mandata » a un PC che non sente niente.

Su questa macchina le regole ci sono gia', per l'eseguibile in `pc/app/` sul
profilo **Public** (la rete di casa e' classificata cosi'), quindi funziona
lanciando TabDeck da li'. Avviandolo da un'altra cartella — `dotnet run` dai
sorgenti compila sotto `bin/`, che e' un altro percorso — Windows chiede il
permesso al primo avvio, e va concesso.

Per metterla a mano, da un PowerShell **come amministratore**:

    New-NetFirewallRule -DisplayName "TabDeck - richiamo del tablet" `
      -Direction Inbound -Protocol UDP -LocalPort 8767 -Action Allow `
      -Profile Any

Per verificare che l'annuncio esca davvero dal tablet, senza tirare in ballo il
firewall: si apre la socket **e si manda prima qualcosa in uscita** dalla stessa
porta, cosi' Windows apre lo stato e lascia rientrare la risposta. E' il modo in
cui e' stato accertato che il problema fosse il firewall e non il tablet.

### Perche' il 255.255.255.255 da solo non basta

Le socket dell'app sul tablet nascono **IPv6 dual-stack** legate a `::`, e non
c'e' modo di farle nascere IPv4: libcore di Android apre l'fd con `AF_INET6`
dentro `IoBridge.socket()` qualunque cosa si chieda, e
`java.net.preferIPv4Stack` e' una proprieta' di OpenJDK che qui non guarda
nessuno. Verificato in `/proc/net/tcp6` e `/proc/net/udp6`.

Misurato con tre sonde dal PC:

| Sonda | Esito |
|---|---|
| unicast a `192.168.1.7:8766` | risponde |
| broadcast di sottorete `192.168.1.255` | risponde |
| broadcast limitato `255.255.255.255` | **nessuna risposta** |

Il doppio stack consegna l'unicast e il broadcast di sottorete, il
255.255.255.255 no. E' per questo che `ProbeTargets()` sul PC manda il richiamo
anche ai 254 indirizzi della sottorete: non per le schede di rete multiple, ma
perche' il broadcast limitato non arriva.


## Se qualcosa non va

| Sintomo | Causa quasi certa |
|---|---|
| `adb devices` vuoto | Debug USB spento, richiesta non accettata, o cavo di sola ricarica |
| «il tablet e' collegato ma TabDeck non risponde» | L'app sul tablet non e' in primo piano: aprila |
| Nessuna risposta a *Cerca in rete* | Tablet su un'altra rete, oppure rete impostata come pubblica e il broadcast e' filtrato |
| «Nessun driver di schermo virtuale installato» | Manca il punto 6, o su un PC nuovo `Installa.ps1` non ha creato il nodo |
| Il tablet mostra un deck vecchio | Non gli e' stato mandato: « Manda al tablet ». Il collegamento da solo non lo manda |
| Chiudendo la finestra il tablet non si scollega | E' voluto: la croce ritira nell'area di notifica. Per spegnere, tasto destro sull'icona › Esci |
| 1024x600 non compare fra le risoluzioni | Il driver scelto ha solo modalita' standard: usbmmidd fa cosi' |
| Schermo nero sul tablet, barra laterale viva | Lo schermo scelto non esiste piu': *Aggiorna elenco* e riseleziona |
| I clic finiscono nel posto sbagliato | Gli schermi si sono spostati; ferma e riavvia |
| I clic non arrivano a una finestra | Quella finestra gira come amministratore: serve avviare come amministratore anche TabDeck |
| L'immagine resta indietro di secondi | Cavo USB che perde pacchetti, rete satura, o fps troppo alti per il tablet |
