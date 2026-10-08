namespace TabDeck.Estensioni;

/// <summary>
/// Un'estensione dal lato del PC: una DLL installata in config\estensioni, che
/// TabDeck non conosce per nome.
///
/// Si costruisce con il costruttore senza argomenti e si accende una volta sola:
/// spegnerla vuol dire <see cref="IDisposable.Dispose"/>, e riaccenderla vuol dire
/// un'istanza nuova. Tutto quello che le serve di TabDeck passa da
/// <see cref="IOspite"/>, cosi' il nucleo resta libero di cambiare dentro.
/// </summary>
public interface IEstensione : IDisposable
{
    /// <summary>Una riga per la pagina Estensioni: con chi e' collegata, cosa le manca.</summary>
    string Stato { get; }

    /// <summary>Da qualunque thread: la pagina si ridisegna da se'.</summary>
    event Action? StatoCambiato;

    /// <summary>Thread della finestra.</summary>
    void Accendi(IOspite ospite);
}

/// <summary>Quello che TabDeck presta a un'estensione accesa.</summary>
public interface IOspite
{
    /// <summary>Una spunta dichiarata in estensione.json, come l'ha lasciata l'utente.</summary>
    bool Opzione(string chiave);

    /// <summary>Una riga nel registro della finestra, col nome dell'estensione davanti.</summary>
    void Registra(string messaggio);

    /// <summary>
    /// Il tablet ha caricato la sua parte dell'estensione: da qui i frame arrivano
    /// davvero, e va rimandato quello che il tablet deve sapere. Scatta a ogni
    /// collegamento e a ogni activity rifatta sul tablet.
    /// </summary>
    event Action? TabletPronto;

    /// <summary>Un frame da 0x51 a 0x5D arrivato dal tablet. Thread di rete, buffer riusato: si copia prima di tornare.</summary>
    event Action<byte, byte[], int>? Frame;

    /// <summary>Un frame JSON da 0x51 a 0x5D verso il tablet. Prima che sia pronto non parte niente.</summary>
    void Manda(byte tipo, string json);

    /// <summary>Porta il tablet sulla voce dell'estensione. Mai mentre mostra lo schermo remoto.</summary>
    void MostraSulTablet();
}
