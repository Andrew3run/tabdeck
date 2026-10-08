# Le luci di casa dal deck

Le luci sono lampade Tuya, quelle che l'app si chiama **Smart Life** (e che poi
si collegano ad Alexa). Alexa qui non c'entra: **non esiste un modo per
comandare da fuori i dispositivi collegati a un account Alexa**. L'API Smart
Home di Amazon va nella direzione opposta — serve a un produttore per far
vedere le sue lampadine *ad* Alexa — e l'unica strada che gira su internet
(`alexa-remote`) e' un programma che si finge il browser sul sito Amazon
riusando i cookie di sessione: va tenuto acceso, va riautenticato ogni tanto, e
si rompe quando Amazon cambia il login.

Le lampadine Tuya, pero', **sanno parlare in locale**. E' quello che fa l'app
quando il telefono e' in casa. Quindi non si passa da nessun cloud: si parla
direttamente alla lampadina, sulla rete di casa, e funziona anche con internet
staccato.

## Che cosa c'e' in casa

Ascoltando i loro annunci in broadcast (le lampade Tuya si presentano da sole,
come fa il tablet sulla 8766) sono uscite **tre** lampade, non due:

| Indirizzo | Identificativo | Protocollo |
|---|---|---|
| 192.168.1.4 | `bf…aed` | 3.3 |
| 192.168.1.5 | `bf…in6` | 3.3 |
| 192.168.1.2 | `bf…vng` | 3.4 |

Rispondono tutte e tre, quindi il controllo locale e' aperto. Il numero del
protocollo conta: la 3.3 cifra e manda, la 3.4 negozia prima una chiave di
sessione. Per ora se ne occupa la libreria; conta quando il codice passera' in
C# dentro TabDeck.

Sono in `config/luci.json`. Manca solo la **chiave locale** di ognuna.

## Le chiavi, una volta sola

Ogni lampada ha una chiave di sedici caratteri, generata da Tuya quando l'hai
accoppiata con l'app. Senza quella non risponde a nessuno, e l'app non la
mostra.

Nella finestra, scheda **Casa**:

1. Nell'app Smart Life: **Io → Impostazioni → Account e sicurezza → Codice
   utente**. Copialo nel campo *Codice utente*.
2. Premi **« Rileva chiavi »**: compare un codice QR.
3. Nell'app, icona della scansione in alto a destra, inquadra, conferma.
4. Le chiavi entrano nelle righe, insieme ai nomi che hai dato nell'app. Nel
   Registro finisce anche la **categoria** di ogni dispositivo: e' li' che si
   scopre che il terzo apparecchio e' una presa e non una lampada.

Non serve nessun account da sviluppatore, nessun progetto cloud, nessun data
center da indovinare e nessuna prova che scade dopo un mese. E' la stessa strada
che usa Home Assistant dal 2024: ci si presenta col suo identificativo pubblico,
e Tuya consegna l'elenco a chi conferma dal telefono.

Serve internet solo durante questi quattro passi. Dopo, le chiavi restano in
`config/luci.json` e le lampade si comandano in casa: cambiano solo se
ri-accoppi una lampada da capo.

## La scheda Casa sul PC

Ha la stessa forma della scheda Deck, e divide due mestieri:

- **a sinistra e al centro si configura**: quali luci ci sono, come si chiamano,
  che glifo e che colore hanno sul tablet, che routine esistono;
- **a destra c'e' il pannello**, e li' si accende davvero. Il PC parla alle
  lampade sulla rete con lo stesso protocollo del tablet.

Il pannello serve perche' una routine si scrive alla cieca: l'unico modo di
sapere se « Film » fa la luce giusta e' vederla accendersi mentre la si prepara,
senza alzarsi e senza passare dal tablet.

### Routine

« Nuova routine » crea un pulsante che tocca piu' lampade in fila. Ogni passo
dice a quale luce (o « Tutte »), cosa fare (spegni, accendi, inverti,
luminosita', colore, bianco) e con che valore. I passi vanno in ordine, uno alla
volta: due comandi insieme verso la stessa lampada si pesterebbero i piedi,
perche' una lampada Tuya accetta una connessione per volta.

Nome, glifo e colore si scelgono come per i pulsanti del deck - i glifi da un
elenco, perche' il font di KitKat ne copre pochi e gli altri escono come
quadratini vuoti.

## Sul tablet: la sezione Casa

Icona della casa nella barra. In cima le routine, come pulsanti del deck: tinta
piena e glifo grande, perche' sono quello che si preme entrando in una stanza.
Sotto, una scheda per lampada: il riquadro col glifo e' l'interruttore, e si
accende della sua tinta quando la luce e' accesa.

**Funziona a PC spento e a cavo staccato.** L'elenco arriva dal PC una volta
sola e resta salvato sul tablet; da quel momento e' il tablet a parlare con le
lampade, sulla rete di casa, senza cloud e senza internet.

Niente gira per conto suo: lo stato si rilegge entrando nella sezione e
premendo « Aggiorna », piu' quello che torna gratis dopo ogni comando. Entrando
il tablet ascolta anche gli annunci delle lampade e si riallinea se il router ha
cambiato loro l'indirizzo.

### La lampada « che va in timeout »

Capitava che una lampada, sempre una sola e ogni volta un'altra, non rispondesse
piu' dal tablet: « non risponde » sulla scheda, mentre dal PC la stessa lampada
si accendeva senza fare una piega. Non era ne' la chiave ne' l'indirizzo.

Le lampade Tuya risparmiano corrente spegnendo la radio in ricezione, e mentre
dormono **non sentono il broadcast**. L'ARP — la domanda « chi ha questo
indirizzo? » con cui comincia ogni collegamento — e' broadcast. Finche' il
tablet non ha in mano l'indirizzo hardware della lampada non puo' mandarle
niente, e per averlo deve chiederlo a voce alta a una che non sta ascoltando.
E' lo stesso buco che il README racconta fra il PC e il tablet, con i ruoli
scambiati.

Il PC non lo vede mai perche' la sua tabella e' sempre calda: sta li' acceso che
parla con le lampade. Il tablet ci arriva freddo tutte le volte che la rete si
riaggancia — un riavvio del router, il WiFi che si riaggancia — e a quel punto e'
una questione di insistenza. Misurato sul posto, dal tablet verso una lampada di
cui aveva perso l'indirizzo hardware:

| Tentativi | Esito |
|---|---|
| 4 in 5 secondi | nessuna risposta, « host irraggiungibile » |
| 20 in 19 secondi | nessuna risposta |
| 60 in 60 secondi | silenzio per 21 secondi, poi risponde sempre |

La lampada prima o poi la sente: le tocca svegliarsi comunque ogni tanto, e in
una di quelle finestre l'ARP passa. Da li' in avanti la voce resta in tabella e
si rinfresca da sola con richieste **dirette**, non piu' broadcast, che la
lampada riceve senza problemi: il secondo comando e tutti quelli dopo partono in
due decimi di secondo.

Prima l'app si arrendeva dopo quattro secondi, cioe' proprio nel mezzo della
finestra sbagliata. Adesso insiste ad **aprire** per mezzo minuto. Si insiste
solo sull'apertura, mai su un comando gia' partito: un « inverti » ripetuto
riporterebbe la luce come stava.

Nel registro finisce quanto e' costato, e serve a distinguere « lampada spenta al
muro » da « lampada che dormiva »:

    I/TabDeck.Tuya: 192.168.1.4 ha risposto al tentativo 3, dopo 8019 ms

Quello che resta fuori portata dell'app: scrivere a mano la voce nella tabella
degli indirizzi hardware vorrebbe i permessi di amministratore, e svegliare la
lampada puo' farlo solo chi quell'indirizzo ce l'ha gia'. Restava bussare, e
bussare abbastanza a lungo.

I controlli di una lampada non spariscono mai, una volta comparsi. Prima lo
facevano, e sembrava un difetto di disegno: dopo un comando la lampada rimanda
**solo i valori cambiati**, e ricavare da quella risposta parziale che cosa sa
fare voleva dire concludere che appena accesa non regolava piu' niente. Adesso
lo stato si aggiorna pezzo per pezzo invece di essere riscritto da capo.

## Metterle anche nel deck

Se una luce serve a portata di pollice senza cambiare sezione, il tipo di azione
**« Avvia un programma »** la comanda dal PC:

| Campo | Valore |
|---|---|
| Programma | `pythonw.exe` |
| Argomenti | `"<percorso>\pc\luci\luce.py" comodino inverti` |

`pythonw.exe` invece di `python.exe` perche' il secondo apre una finestra nera
per mezzo secondo a ogni pressione. Questa strada pero' passa dal PC: se e'
spento, il pulsante non fa niente. La sezione Casa no.

## Come e' stato provato

Il protocollo e' stato scritto leggendo il formato dei pacchetti byte per byte,
e provato contro una **lampada finta** costruita con le funzioni di `tinytuya` -
cioe' un'implementazione fatta da altri, cosi' un errore uguale sui due lati non
puo' passare inosservato. La lampada finta risponde in 3.3 e in 3.4, stretta di
mano compresa.

Due cose che e' facile sbagliare, e che li' vengono fuori subito:

- l'intestazione di versione va **dopo** aver cifrato in 3.3 e **prima** in 3.4;
- le risposte dei dispositivi portano in testa quattro byte di esito che i
  comandi del client non hanno: chi li salta sempre, o non li salta mai, perde
  l'allineamento del cifrato e non capisce piu' niente.
