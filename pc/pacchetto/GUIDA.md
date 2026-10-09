# TabDeck — guida

Questa cartella e' TabDeck da installare: il programma per il PC, il driver
dello schermo virtuale, `adb`, e l'app gia' compilata per il tablet. Dentro c'e'
tutto: non si scarica niente e non si compila niente.

TabDeck trasforma un **Samsung Galaxy Tab 3 7.0** (SM-T210, Android 4.4) in due
cose collegate al PC via cavo o via rete:

- **un secondo monitor vero** — non la fotografia di una finestra: uno schermo
  che Windows vede per conto suo, dove le finestre si trascinano e si toccano
  col dito;
- **uno stream deck** — una griglia di pulsanti sul tablet; premendone uno, il
  PC esegue una scorciatoia, un comando multimediale, un programma.

Sul PC c'e' una finestra, e ogni cosa si comanda a mano da li'. Non c'e' nessun
servizio, nessun avvio automatico, niente che parta o decida da solo.

## La strada corta

Tre doppi clic, in quest'ordine:

1. **`Installa.bat`** — mette il programma sul PC. Windows chiede il consenso
   una volta: serve per il driver dello schermo e per il firewall.
2. **`Tablet.bat`** — con il tablet attaccato col cavo, ci mette l'app.
3. **TabDeck** dal menu Start, o dal collegamento sul desktop.

Se qualcosa si ferma, la finestra nera resta aperta e dice cosa manca: non si
chiude da sola, apposta.

Poi, una volta e quando vuoi, **`Alleggerisci.bat`**: mette a riposo le
applicazioni Android che al tablet non servono piu'. Non fa parte
dell'installazione ed e' reversibile — il capitolo 4 spiega cosa tocca.

## Cosa c'e' in questa cartella

    Installa.bat        installa TabDeck su questo PC (chiede il consenso)
    Tablet.bat          mette l'app sul tablet attaccato col cavo
    Alleggerisci.bat    mette a riposo le app Android che non servono
    Ripristina.bat      le rimette in servizio
    Disinstalla.bat     toglie tutto quello che Installa.bat aveva messo
    LEGGIMI.txt         le stesse cose in breve, senza formattazione
    GUIDA.md            questo file

    app\                il programma. Se dentro c'e' System.Private.CoreLib.dll,
                        .NET e' compreso e non serve installare altro
    tools\              adb: il collegamento col cavo e l'app del tablet
    driver\schermo\     Virtual Display Driver: il monitor virtuale
    driver\usb\         il driver USB del tablet, se chi ha fatto il pacchetto
                        ne aveva uno da esportare
    tablet\TabDeck.apk  l'app del tablet, gia' compilata e firmata
    tablet\sistema\     la pulizia del sistema Android: alleggerisci.ps1,
                        ripristina.ps1, batteria.ps1, e i due elenchi
                        bloccati.txt (chi va a riposo) e tenere.txt (chi resta,
                        con scritto perche')
    config\             deck, icone e luci, solo se il pacchetto e' stato fatto
                        con -ConConfig
    versione.txt        quando e' stato fatto, e con cosa dentro

    Installa.ps1  Tablet.ps1  Disinstalla.ps1  Avvio.ps1
                        gli script veri: i .bat qui sopra non fanno altro che
                        chiamarli. Chi vuole le opzioni parte da qui, in un
                        PowerShell come amministratore

I `.bat` esistono per due ragioni sole: un doppio clic su un `.ps1` lo apre nel
Blocco note invece di eseguirlo, e Windows vuole il consenso prima di lasciar
toccare driver e firewall. Il `.bat` risolve tutti e due i punti e poi si toglie
di mezzo.

## 1. Installare sul PC — `Installa.bat`

Doppio clic. Windows chiede il consenso, e poi:

    cartella    copia il programma in C:\TabDeck
    schermo     se manca, mette il Virtual Display Driver in
                C:\TabDeck\driver\schermo (scaricandolo se il pacchetto non
                ce l'ha), lo registra e crea il monitor virtuale
    usb         se nel pacchetto c'e' un driver USB, lo mette nel magazzino
                di Windows
    firewall    apre udp 8766 e 8767 in entrata, piu' il programma
    avvio       crea l'attivita' pianificata "TabDeck" — nessun orario, nessun
                avvio automatico — cosi' Windows non chiede il consenso a ogni
                apertura
    tablet      lascia in C:\TabDeck l'APK e Tablet.bat, per le volte dopo
    icone       un collegamento nel menu Start e uno sul desktop

Reinstallando sopra un'installazione che c'e' gia', **la configurazione non si
tocca**: il deck e le icone restano quelli.

### Perche' TabDeck chiede l'amministratore

Per due cose sole: accendere e spegnere il monitor virtuale, e mandare i tocchi
del tablet anche alle finestre che girano elevate — cosa che Windows vieta a un
programma normale. Senza elevazione TabDeck funziona lo stesso, ma il consenso
lo chiede a ogni « Manda lo schermo » e a ogni « Ferma »: piu' domande, non meno.

`Installa.bat` lo dice a Windows una volta sola, creando un'attivita' pianificata
di nome `TabDeck`. Non ha orario e non parte all'accesso: porta con se' soltanto
il livello dei permessi, e si muove quando premi il collegamento. Da quel momento
chiunque possa premere quel collegamento fa partire un programma elevato senza
vedere nessuna domanda: su una macchina propria e' quello che si vuole, su una
condivisa e' una cosa da sapere.

Per tornare alla domanda a ogni apertura, da un PowerShell come amministratore
dentro questa cartella:

    .\Avvio.ps1 -Ese C:\TabDeck\app\TabDeck.exe -Diretto

## 2. Mettere l'app sul tablet — `Tablet.bat`

Attacca il tablet col cavo e fai doppio clic. Non serve l'amministratore e non
serve l'SDK di Android: l'APK e' gia' compilato qui dentro.

Perche' `adb` veda il tablet, sul tablet ci vuole il **debug USB**:

1. Impostazioni > Info sul dispositivo
2. tocca **Numero build** sette volte
3. Impostazioni > Opzioni sviluppatore > **Debug USB**
4. ricollega il cavo — molti cavi sono di sola ricarica

La prima volta il tablet chiede « Consenti debug USB »: sblocca lo schermo,
spunta « Consenti sempre da questo computer », tocca OK. Finche' non si risponde,
`adb` lo vede ma lo chiama *unauthorized*, e `Tablet.bat` lo dice.

Alla fine il tablet apre la scelta della schermata Home: **scegli TabDeck e
conferma « Sempre »**. Non e' un vezzo — con TabDeck come Home, TouchWiz non
viene proprio caricato, e sono decine di megabyte di RAM che tornano
all'applicazione. Per installare senza toccare la Home, da PowerShell:
`.\Tablet.ps1 -SenzaHome`.

Il tablet giusto e' un SM-T210 e non un altro: se ne sono attaccati due, il
modello viene guardato prima di installare, e su quello sbagliato non si scrive
niente.

## 3. Rimettere sul tablet una versione nuova

E' lo stesso `Tablet.bat`, e si puo' rifare quante volte si vuole. Dopo
l'installazione lo trovi in due posti, con l'APK accanto:

- in questa cartella, se te la sei tenuta;
- in **`C:\TabDeck\Tablet.bat`**, che ci resta apposta.

L'installazione tiene i dati (`adb install -r`): il deck, le luci e le icone che
il tablet si e' salvato restano dov'erano. Non c'e' bisogno di disinstallare
prima, e non c'e' bisogno di rifare la Home.

Se l'APK nuovo arriva da un pacchetto nuovo, sovrascrivi `tablet\TabDeck.apk` e
rilancia `Tablet.bat`; oppure rilancia `Installa.bat`, che riporta in
`C:\TabDeck` anche l'APK.

Sul PC dove il progetto si compila la strada e' un'altra: in cima alla cartella
del progetto c'e' `reinstalla-tablet.bat`, che ricompila l'APK e lo rimette sul
tablet in un colpo solo.

## 4. Alleggerire il sistema del tablet — `Alleggerisci.bat`

Il tablet arriva pieno di roba che a uno schermo con dei pulsanti non serve:
Google, Samsung, widget, riproduttori, cloud, stampa. Su un dispositivo con
832 MB di RAM utili non e' un dettaglio estetico — e' la differenza fra un deck
che risponde e uno che aspetta.

`Alleggerisci.bat` mette a riposo tutto quello che sta in
`tablet\sistema\bloccati.txt`. Non e' un passaggio dell'installazione, e non
parte da solo: e' un pulsante suo, si fa una volta, e chiede conferma prima di
muoversi.

**Cosa fa davvero.** `pm block` su ogni pacchetto dell'elenco, poi
`am force-stop` su quelli che erano gia' in esecuzione. `pm block` **nasconde**
il pacchetto e gli impedisce di ripartire all'avvio: non disinstalla niente, non
tocca la partizione di sistema, non serve il root. Alla fine dice quanta memoria
si e' liberata.

**Cosa resta acceso.** L'elenco sta in `tablet\sistema\tenere.txt`, con accanto
il perche' di ognuno: il framework, `systemui`, le Impostazioni — che sono
l'unica via d'uscita da TabDeck —, i provider che servono a quelle, `adb`,
l'installatore di pacchetti, la tastiera, il keyguard, la roba della GPU
Marvell, e TabDeck. Tutto il resto va a dormire.

**Cosa non riesce.** Su Android 4.4 qualche pacchetto privilegiato rifiuta il
blocco: su questo tablet capita a Google Play Services e a poco altro. Non c'e'
un giro da adb che lo aggiri — `pm disable-user` fa cadere il package manager, e
`pm block pacchetto/componente` risponde « true » solo perche' non trova il
pacchetto. Lo script li elenca a fine corsa e dice dove spegnerli a mano:
Impostazioni > Gestione applicazioni > Tutte > [app] > Disattiva.

**Dopo.** Riavvia il tablet: la misura vera e' quella all'avvio, che e' il
momento in cui questi si ripresentavano. Se qualcosa che ti serviva e' sparito,
`Ripristina.bat` rimette tutto in servizio — sblocca gli stessi pacchetti, e al
riavvio tornano dov'erano.

Il guadagno piu' grosso pero' non e' qui: e' TabDeck come schermata Home. Senza
launcher Samsung, TouchWiz non viene proprio caricato. Quello lo fa gia'
`Tablet.bat` al passo 2.

## 5. Aprire TabDeck

Dal menu Start, o dal collegamento sul desktop.

**La croce della finestra non spegne**: ritira la finestra accanto all'orologio,
e il tablet resta collegato. Chiudere davvero staccava il tablet, spegneva lo
schermo virtuale e lasciava il deck a comandare niente — il gesto piu' facile
faceva la cosa piu' cara. Per spegnere davvero: tasto destro sull'icona accanto
all'orologio, « Esci ».

Ne gira una sola: premendo il collegamento mentre TabDeck e' nascosto accanto
all'orologio, la finestra che c'e' gia' torna a farsi vedere.

## 6. Il deck e le luci non arrivano da soli

Collegarsi non riscrive il tablet. Il tablet il suo deck e le sue luci se li
tiene — e' cosi' che funziona a PC spento — e quello che sta sul PC puo' essere
un montaggio lasciato a meta'. Vanno mandati quando lo si chiede:

    il deck    Uso > Deck, oppure Gestione > Pulsanti: « Manda al tablet »
    le luci    Gestione > Luci: « Manda al tablet »

Se sul PC c'e' qualcosa di piu' nuovo di quello che ha il tablet, la finestra lo
dice appena ci si collega.

### Portare il proprio deck da un altro PC

Da Gestione > Pulsanti, sotto PROFILO, c'e' « Esporta »: scrive un file solo con
dentro i profili scelti e le immagini dei loro pulsanti. Sull'altro PC, dalla
stessa barra, « Importa » li aggiunge senza toccare quelli che ci sono gia'.

Copiare `config\deck.json` a mano non basta: le icone stanno accanto, in
`config\icone`, e senza quelle arriva un deck di quadratini vuoti.

## Se il cavo non va

Il collegamento col cavo passa da `adb`, e `adb` ha bisogno che Windows
riconosca il tablet. Su Windows 11 di solito succede da solo. Se `Tablet.bat`
dice che non vede nessun tablet:

- il **debug USB** dev'essere acceso (vedi sopra);
- **sblocca lo schermo** del tablet e rispondi alla richiesta;
- **prova un altro cavo**: molti sono di sola ricarica;
- se ancora niente, installa il driver USB Samsung da
  `developer.samsung.com/android-usb-driver` e riprova.

Il collegamento in rete non ha bisogno di niente di tutto questo: basta che
tablet e PC siano sulla stessa rete.

## Se il tablet chiama e il PC non sente

Il tablet ha in barra una freccia che entra in uno schermo: premendola manda in
broadcast « sono qui », e il PC apre lui il collegamento. Quell'annuncio e'
l'unico pacchetto non richiesto che TabDeck riceve, e senza una regola il
firewall lo butta via in silenzio — il tablet direbbe « chiamata mandata » a un
PC che non sente niente. La regola la mette `Installa.bat`. Se e' stata tolta, la
rimette una nuova installazione.

## Lo schermo virtuale

Windows non lascia trascinare una finestra dove non c'e' un monitor: perche' il
tablet sia un secondo schermo e non una fotografia del primo, un monitor in piu'
deve esistere davvero. Lo crea il Virtual Display Driver.

Il catalogo del driver e' firmato da SignPath Foundation con CA GlobalSign,
quindi Windows lo accetta senza toccare nessun archivio di certificati. Il driver
resta inerte finche' TabDeck non accende il monitor, e sparisce quando si preme
« Ferma », quando si chiude o quando si stacca il tablet.

Se nel pacchetto il driver non c'e', `Installa.bat` lo scarica da
`github.com/VirtualDrivers/Virtual-Display-Driver` (versione fissata, impronta e
firma controllate). Senza internet: rilancia `Installa.bat` quando c'e'.

Senza driver TabDeck funziona lo stesso: restano il deck, le luci, il timer e la
sveglia. Manca solo il secondo monitor.

## Togliere tutto — `Disinstalla.bat`

Doppio clic, consenso, e del passaggio di `Installa.bat` non resta niente: la
cartella, il driver, le regole del firewall, l'attivita' pianificata e i
collegamenti.

Chiudi prima TabDeck **davvero** — icona accanto all'orologio, tasto destro,
« Esci » — altrimenti lo script si ferma e lo dice.

Sul tablet l'app resta: si toglie da li', o con
`tools\platform-tools\adb.exe uninstall dev.tabdeck`.

E se il tablet era stato alleggerito, resta alleggerito: `Ripristina.bat`
**prima** di `Disinstalla.bat`, perche' la disinstallazione porta via anche
quello. Se te ne accorgi dopo, si riparte da questa cartella.

Per tenere qualcosa, da un PowerShell come amministratore:

    .\Disinstalla.ps1 -TieniConfig     lascia il deck e le icone dove sono
    .\Disinstalla.ps1 -TieniDriver     lascia lo schermo virtuale installato

## Le opzioni, per chi le vuole

I `.bat` fanno la cosa normale. Tutto il resto sta negli script, e si passa da un
PowerShell **come amministratore** aperto in questa cartella:

    Set-ExecutionPolicy -Scope Process Bypass    # solo per questa finestra

    .\Installa.ps1 -Dove D:\TabDeck              # da un'altra parte
    .\Installa.ps1 -SenzaDriver                  # niente schermo virtuale
    .\Installa.ps1 -SenzaFirewall                # niente regola per il richiamo
    .\Installa.ps1 -SenzaCollegamenti            # niente icone
    .\Installa.ps1 -SenzaAttivita                # il consenso a ogni apertura

    .\Tablet.ps1 -SenzaHome                      # installa e basta
    .\Tablet.ps1 -Info                           # dice solo cosa vede

La pulizia del tablet non vuole l'amministratore, e sta un piano piu' in basso:

    tablet\sistema\alleggerisci.ps1 -Filtro sec  # solo i pacchetti Samsung
    tablet\sistema\ripristina.ps1 -Filtro sec    # e solo quelli, indietro
    tablet\sistema\batteria.ps1                  # dove se ne va la corrente
    tablet\sistema\batteria.ps1 -Misura 30       # mezz'ora di consumo vero
