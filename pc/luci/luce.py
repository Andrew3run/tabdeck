"""
luce.py - comanda le luci Tuya/Smart Life di casa, dalla riga di comando.

Parla alle lampadine in locale, sulla rete di casa: nessun cloud, nessun
account interrogato al momento della pressione, niente che gira in background.
Un comando, una lampadina che cambia, il programma finisce.

    python luce.py                          elenco delle luci e stato di ognuna
    python luce.py <nome> on
    python luce.py <nome> off
    python luce.py <nome> inverti           accesa -> spenta, spenta -> accesa
    python luce.py <nome> luminosita 60     da 1 a 100
    python luce.py <nome> colore ff8800     RRGGBB
    python luce.py <nome> bianco
    python luce.py tutte on|off|inverti
    python luce.py prova                    lampeggia una luce per volta, per
                                            capire dal vivo quale e' quale

Le luci stanno in config/luci.json, riempito da chiavi.py.
"""

import json
import os
import sys
import time

try:
    import tinytuya
except ImportError:
    sys.exit("manca tinytuya: installalo con  pip install tinytuya")

CONFIG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "config", "luci.json")

# Oltre questo la lampadina e' spenta, o su un'altra rete: meglio dirlo subito
# che restare appesi mentre il tablet aspetta.
ATTESA = 5


def main():
    argomenti = sys.argv[1:]
    luci = carica()

    if not argomenti:
        return elenco(luci)
    if argomenti[0] == "prova":
        return prova(luci)

    nome = argomenti[0].lower()
    comando = argomenti[1].lower() if len(argomenti) > 1 else "inverti"
    valore = argomenti[2] if len(argomenti) > 2 else None

    scelte = luci if nome == "tutte" else [trova(luci, nome)]
    esito = 0
    for luce in scelte:
        if not esegui(luce, comando, valore):
            esito = 1
    return esito


# ---- comandi ----

def esegui(luce, comando, valore):
    """Un comando su una luce. Vero se e' andato a buon fine."""
    try:
        d = apri(luce)
        stato = d.status()
        if not stato or "Error" in stato:
            return fallita(luce, messaggio(stato))

        if comando in ("on", "accendi"):
            r = d.turn_on()
        elif comando in ("off", "spegni"):
            r = d.turn_off()
        elif comando in ("inverti", "toggle"):
            r = d.turn_off() if accesa(d, stato) else d.turn_on()
        elif comando in ("luminosita", "luce"):
            r = d.set_brightness_percentage(percentuale(valore))
        elif comando == "colore":
            r, g, b = rgb(valore)
            r = d.set_colour(r, g, b)
        elif comando == "bianco":
            r = d.set_mode("white")
        else:
            return fallita(luce, f"comando sconosciuto: '{comando}'")

        if isinstance(r, dict) and "Error" in r:
            return fallita(luce, messaggio(r))

        print(f"{luce['nome']}: {comando}{' ' + valore if valore else ''} - fatto")
        return True
    except Exception as e:
        return fallita(luce, str(e))


def elenco(luci):
    """Che luci ci sono e come stanno adesso."""
    for luce in luci:
        try:
            d = apri(luce)
            stato = d.status()
            if not stato or "Error" in stato:
                print(f"  {luce['nome']:<16} {luce['ip']:<15} non risponde ({messaggio(stato)})")
                continue
            acceso = "accesa" if accesa(d, stato) else "spenta"
            print(f"  {luce['nome']:<16} {luce['ip']:<15} {acceso}{luminosita(d, stato)}")
        except Exception as e:
            print(f"  {luce['nome']:<16} {luce['ip']:<15} non risponde ({e})")
    return 0


def prova(luci):
    """
    Spegne e riaccende una luce per volta. Serve a capire quale nome
    corrisponde a quale lampada, guardandole mentre succede.
    """
    for luce in luci:
        print(f"guarda: adesso lampeggia '{luce['nome']}' ({luce['ip']})")
        for _ in range(3):
            esegui(luce, "inverti", None)
            time.sleep(0.6)
        time.sleep(1.5)
    return 0


# ---- utilita' ----

def carica():
    if not os.path.exists(CONFIG):
        sys.exit(f"manca {CONFIG}")
    with open(CONFIG, encoding="utf-8") as f:
        config = json.load(f)

    luci = [l for l in config.get("luci", []) if l.get("chiave")]
    if not luci:
        sys.exit("nessuna luce ha una chiave locale in config/luci.json:\n"
                 "prendile una volta sola con  python chiavi.py --chiave ... --segreto ...\n"
                 "(la procedura e' in docs/luci.md)")
    for i, l in enumerate(luci):
        if not l.get("nome"):
            l["nome"] = f"luce{i + 1}"
    return luci


def trova(luci, nome):
    for l in luci:
        if l["nome"].lower() == nome:
            return l
    sys.exit(f"non c'e' nessuna luce che si chiama '{nome}'. Ci sono: "
             + ", ".join(l["nome"] for l in luci))


def apri(luce):
    d = tinytuya.BulbDevice(luce["id"], luce["ip"], luce["chiave"], version=float(luce["versione"]))
    d.set_socketTimeout(ATTESA)
    return d


def accesa(d, stato):
    dp = d.dpset.get("switch") or "1"
    return bool(stato.get("dps", {}).get(dp))


def luminosita(d, stato):
    dp = d.dpset.get("brightness")
    massimo = d.dpset.get("value_max", 0)
    valore = stato.get("dps", {}).get(dp) if dp else None
    if not valore or massimo <= 0:
        return ""
    return f", {round(valore * 100 / massimo)}%"


def percentuale(valore):
    if valore is None:
        sys.exit("serve la percentuale: per esempio  luce.py comodino luminosita 60")
    try:
        n = int(valore)
    except ValueError:
        sys.exit(f"'{valore}' non e' un numero da 1 a 100")
    if not 1 <= n <= 100:
        sys.exit("la luminosita' va da 1 a 100 (per spegnerla c'e' 'off')")
    return n


def rgb(valore):
    testo = (valore or "").lstrip("#")
    if len(testo) != 6:
        sys.exit("il colore si scrive come RRGGBB, per esempio ff8800")
    try:
        return int(testo[0:2], 16), int(testo[2:4], 16), int(testo[4:6], 16)
    except ValueError:
        sys.exit(f"'{valore}' non e' un colore esadecimale")


def messaggio(risposta):
    if not risposta:
        return "nessuna risposta"
    return str(risposta.get("Err", "")) + " " + str(risposta.get("Error", "")).strip()


def fallita(luce, perche):
    print(f"{luce['nome']}: non ha risposto - {perche}", file=sys.stderr)
    return False


if __name__ == "__main__":
    sys.exit(main())
