package dev.tabdeck;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RadialGradient;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.drawable.GradientDrawable;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/**
 * La pagina delle impostazioni, dentro l'app.
 *
 * Serve perche' il tablet non ha piu' nessun altro posto dove guardare: TabDeck
 * e' la Home, non c'e' un cassetto delle applicazioni, e le impostazioni di
 * Android sono raggiungibili solo di qui. Le stesse voci ci sono anche nella
 * finestra sul PC — chi le cambia per ultimo vince — ma con il PC spento, o su
 * un'altra rete, questa e' l'unica strada.
 *
 * Com'e' fatta: la fascia in cima come le altre sezioni, e sotto due colonne di
 * riquadri. Ogni riga ha la sua icona e a destra quel che si fa - un
 * interruttore, un segno di spunta, un valore, o la freccia di quel che apre.
 * Era un elenco di scritte verdi e pallini su un fondo nero, con un paragrafo
 * di spiegazione dopo ogni gruppo.
 *
 * Costruita con viste normali e non disegnata a mano come il deck: si apre di
 * rado e si scorre, quindi qui conta piu' che sia semplice da leggere che
 * veloce da disegnare.
 */
public final class SettingsView extends FrameLayout {

    public interface Listener {
        /** "never", "screen" o "always". */
        void onKeepAwakeChosen(String policy);
        /**
         * 0-100, oppure -1 per la luminosita' di sistema.
         *
         * {@code finale} distingue il trascinamento dal dito alzato: durante il
         * trascinamento si cambia solo la finestra, che costa niente; alzato il
         * dito si scrive anche la luminosita' di sistema, che passa da un
         * content provider e non va fatta a ogni pixel.
         */
        void onBrightnessChosen(int percent, boolean finale);
        /** Millisecondi di pannello acceso dopo l'ultimo tocco. */
        void onScreenTimeoutChosen(int ms);
        /** Manda al PC l'annuncio « sono qui ». */
        void onConnectToPc();
        void onOpenAndroidSettings();
        void onOpenWifiSettings();
        /** Opzioni sviluppatore: e' li' che vive "Rimani attivo". */
        void onOpenDeveloperSettings();
        void onRestart();
        /** « collegato.deck », « scollegato.casa »: la scelta, e la sezione dentro. */
        void onSezioneScelta(String chiave, boolean visibile);
    }

    private static final int COLOR_TESTO = 0xFFE6E7EA;
    private static final int COLOR_TENUE = 0xFF9A9DA5;
    private static final int COLOR_ICONA = 0xFF9A9DA5;
    private static final int COLOR_LINEA = 0xFF26282D;
    private static final int COLOR_ACCESO = 0xFF3DDC84;
    /** Per i valori che costano corrente e che l'app non puo' cambiare da se'. */
    private static final int COLOR_AVVISO = 0xFFD9A441;

    private final float density;
    private final Listener listener;
    private final LinearLayout sinistra, destra;

    private final Spunta[] awakeSegni = new Spunta[3];
    private static final String[] AWAKE_KEYS = { "never", "screen", "always" };

    private final TextView[] durataChips = new TextView[Risparmio.DURATE.length];

    private TextView brightnessValue;
    private Cursore brightnessBar;
    private TextView statusValue;
    private TextView addressValue;
    private TextView cavoValue;
    private TextView radioValue;
    private TextView durataHeader;

    private String awake = "screen";

    private static final String[] SEZIONI_CHIAVI = { "dashboard", "deck", "schermo", "casa", "orologio" };
    private static final String[] SEZIONI_NOMI = { "Home", "Deck", "Schermo remoto", "Casa", "Orologio" };
    private static final String[] SEZIONI_ICONE = { "layout-dashboard", "layout-grid", "monitor", "house", "timer" };
    private static final String[] GRUPPI = { "collegato", "scollegato" };
    /** Gli interruttori delle due scelte: [0] col PC collegato, [1] senza. */
    private final Interruttore[][] sezioniInt = new Interruttore[2][SEZIONI_CHIAVI.length];
    private final boolean[][] sezioniAccese = { { true, true, true, true, true }, { true, true, true, true, true } };

    public SettingsView(Context context, Listener listener) {
        super(context);
        this.listener = listener;
        density = context.getResources().getDisplayMetrics().density;
        setBackground(new Fondo());

        LinearLayout pagina = new LinearLayout(context);
        pagina.setOrientation(LinearLayout.VERTICAL);
        addView(pagina, new LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT));

        // La fascia in cima, come nelle altre sezioni.
        TextView titolo = new TextView(context);
        titolo.setText("Impostazioni");
        titolo.setTextColor(COLOR_TESTO);
        titolo.setTextSize(TypedValue.COMPLEX_UNIT_PX, Math.min(dp(20), 20f * density));
        titolo.setGravity(Gravity.CENTER_VERTICAL);
        titolo.setPadding(dp(20), 0, dp(20), 0);
        pagina.addView(titolo, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(52)));

        ScrollView scorre = new ScrollView(context);
        scorre.setVerticalScrollBarEnabled(false);
        pagina.addView(scorre, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f));

        LinearLayout colonne = new LinearLayout(context);
        colonne.setOrientation(LinearLayout.HORIZONTAL);
        colonne.setPadding(dp(20), 0, dp(20), dp(20));
        scorre.addView(colonne, new LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT));

        sinistra = new LinearLayout(context);
        sinistra.setOrientation(LinearLayout.VERTICAL);
        destra = new LinearLayout(context);
        destra.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams ps = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        ps.rightMargin = dp(9);
        LinearLayout.LayoutParams pd = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        pd.leftMargin = dp(9);
        colonne.addView(sinistra, ps);
        colonne.addView(destra, pd);

        build(context);
    }

    private void build(Context context) {
        // ---- a sinistra: le sezioni ----
        String[] titoli = { "SEZIONI COL PC", "SEZIONI SENZA PC" };
        for (int g = 0; g < GRUPPI.length; g++) {
            LinearLayout riquadro = riquadro(sinistra, titoli[g]);
            for (int i = 0; i < SEZIONI_CHIAVI.length; i++) {
                // Senza PC lo schermo remoto non c'e': la riga non servirebbe a niente.
                if (g == 1 && "schermo".equals(SEZIONI_CHIAVI[i])) continue;
                final int gruppo = g, indice = i;
                Interruttore inter = new Interruttore(context);
                sezioniInt[g][i] = inter;
                riga(riquadro, SEZIONI_ICONE[i], SEZIONI_NOMI[i], inter, new OnClickListener() {
                    @Override public void onClick(View v) {
                        sezioniAccese[gruppo][indice] = !sezioniAccese[gruppo][indice];
                        dipingiSezioni();
                        listener.onSezioneScelta(GRUPPI[gruppo] + "." + SEZIONI_CHIAVI[indice],
                                sezioniAccese[gruppo][indice]);
                    }
                });
            }
        }

        // ---- sempre a sinistra: il collegamento ----
        LinearLayout collegamento = riquadro(sinistra, "COLLEGAMENTO");
        statusValue = valore(collegamento, "plug", "Stato", "scollegato");
        addressValue = valore(collegamento, "wifi", "Indirizzo del tablet", "—");
        riga(collegamento, "send", "Connetti al PC", freccia(context), new OnClickListener() {
            @Override public void onClick(View v) { listener.onConnectToPc(); }
        });
        riga(collegamento, "wifi", "Wi-Fi di Android", freccia(context), new OnClickListener() {
            @Override public void onClick(View v) { listener.onOpenWifiSettings(); }
        });

        // ---- sempre a sinistra: la Home ----
        // Il meteo chiede la posizione a ip-api.com e il tempo a Open-Meteo:
        // spento, il tablet non chiede piu' niente a nessuno dei due.
        LinearLayout home = riquadro(sinistra, "HOME");
        final Interruttore meteo = new Interruttore(context);
        meteo.metti(Meteo.acceso(context));
        riga(home, "cloud-sun", "Meteo sopra l'ora", meteo, new OnClickListener() {
            @Override public void onClick(View v) {
                boolean si = !Meteo.acceso(getContext());
                Meteo.accendi(getContext(), si);
                meteo.metti(si);
            }
        });

        final TextView localita = new TextView(context);
        localita.setTextColor(COLOR_TENUE);
        localita.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        localita.setSingleLine(true);
        scriviLocalita(localita);
        riga(home, "map-pin", "Localita'", localita, new OnClickListener() {
            @Override public void onClick(View v) { chiediLocalita(localita); }
        });

        // ---- a destra: lo schermo ----
        LinearLayout schermo = riquadro(destra, "TIENI ACCESO IL PANNELLO");
        String[] awakeLabels = { "Mai, si spegne da solo", "Mentre guardo lo schermo del PC", "Sempre" };
        String[] awakeIcone = { "moon", "monitor", "sun" };
        for (int i = 0; i < awakeLabels.length; i++) {
            final int index = i;
            awakeSegni[i] = new Spunta(context);
            riga(schermo, awakeIcone[i], awakeLabels[i], awakeSegni[i], new OnClickListener() {
                @Override public void onClick(View v) {
                    setKeepAwake(AWAKE_KEYS[index]);
                    listener.onKeepAwakeChosen(AWAKE_KEYS[index]);
                }
            });
        }

        LinearLayout durata = riquadro(destra, "SPEGNI IL PANNELLO DOPO");
        durataHeader = (TextView) ((ViewGroup) durata.getParent()).getChildAt(0);
        durata.addView(durataRow(context));

        LinearLayout luce = riquadro(destra, "LUMINOSITA'");
        luce.addView(brightnessRow(context));

        // ---- a destra: l'app e il tablet ----
        LinearLayout app = riquadro(destra, "APPLICAZIONE");
        riga(app, "settings", "Impostazioni di Android", freccia(context), new OnClickListener() {
            @Override public void onClick(View v) { listener.onOpenAndroidSettings(); }
        });
        riga(app, "code", "Opzioni sviluppatore", freccia(context), new OnClickListener() {
            @Override public void onClick(View v) { listener.onOpenDeveloperSettings(); }
        });
        riga(app, "refresh-cw", "Riavvia TabDeck", freccia(context), new OnClickListener() {
            @Override public void onClick(View v) { listener.onRestart(); }
        });

        LinearLayout tablet = riquadro(destra, "QUESTO TABLET");
        cavoValue = valore(tablet, "plug", "Col cavo attaccato", "—");
        radioValue = valore(tablet, "wifi", "Radio Wi-Fi", "—");
        valore(tablet, "tablet", "Modello", android.os.Build.MODEL);
        valore(tablet, "code", "Android", android.os.Build.VERSION.RELEASE);
        valore(tablet, "monitor", "Pannello", getResources().getDisplayMetrics().widthPixels + "x"
                + getResources().getDisplayMetrics().heightPixels);

        setKeepAwake(awake);
        dipingiSezioni();
    }

    /** La localita' del meteo come si legge nella riga: la citta', o « automatica ». */
    private void scriviLocalita(TextView v) {
        String citta = Meteo.citta(getContext());
        v.setText(Meteo.manuale(getContext()) ? citta
                : citta.length() > 0 ? "automatica · " + citta : "automatica");
    }

    /**
     * La localita' si scrive: una casella con la tastiera, e tre risposte. La
     * citta' si cerca su Open-Meteo, e se non c'e' lo si dice qui invece di
     * mostrare poi il meteo di un posto a caso.
     */
    private void chiediLocalita(final TextView riga) {
        final Context c = getContext();
        final android.widget.EditText campo = new android.widget.EditText(c);
        campo.setSingleLine(true);
        campo.setHint("Citta', per esempio Milano");
        if (Meteo.manuale(c)) campo.setText(Meteo.citta(c));
        campo.setSelection(campo.getText().length());
        FrameLayout cornice = new FrameLayout(c);
        cornice.setPadding(dp(20), dp(8), dp(20), 0);
        cornice.addView(campo);

        final android.app.AlertDialog finestra = new android.app.AlertDialog.Builder(c,
                android.app.AlertDialog.THEME_DEVICE_DEFAULT_DARK)
                .setTitle("Localita' del meteo")
                .setView(cornice)
                .setPositiveButton("Imposta", null)
                .setNeutralButton("Automatica", new android.content.DialogInterface.OnClickListener() {
                    @Override public void onClick(android.content.DialogInterface d, int w) {
                        Meteo.automatica(c);
                        scriviLocalita(riga);
                    }
                })
                .setNegativeButton("Annulla", null)
                .create();
        finestra.setOnShowListener(new android.content.DialogInterface.OnShowListener() {
            @Override public void onShow(android.content.DialogInterface d) {
                // « Imposta » non chiude da solo: la finestra resta finche' la
                // citta' non e' stata trovata, o finche' si dice che non c'e'.
                finestra.getButton(android.app.AlertDialog.BUTTON_POSITIVE).setOnClickListener(new OnClickListener() {
                    @Override public void onClick(View v) {
                        final String nome = campo.getText().toString().trim();
                        if (nome.length() == 0) return;
                        finestra.setTitle("Cerco " + nome + "…");
                        Meteo.scegliCitta(c, nome, new Meteo.Trovata() {
                            @Override public void trovata(String trovato) {
                                if (trovato == null) {
                                    finestra.setTitle("Non trovo « " + nome + " »");
                                    return;
                                }
                                scriviLocalita(riga);
                                finestra.dismiss();
                            }
                        });
                    }
                });
            }
        });
        finestra.getWindow().setSoftInputMode(android.view.WindowManager.LayoutParams.SOFT_INPUT_STATE_VISIBLE);
        finestra.show();
    }

    // ---- aggiornamenti dall'esterno ----

    /** Segna la scelta attiva; non richiama il listener. */
    public void setKeepAwake(String policy) {
        awake = policy == null ? "screen" : policy;
        for (int i = 0; i < awakeSegni.length; i++) awakeSegni[i].metti(AWAKE_KEYS[i].equals(awake));
    }

    /** Segna le sezioni come sono; non richiama il listener. */
    public void setSezioni(boolean[] conPc, boolean[] senzaPc) {
        System.arraycopy(conPc, 0, sezioniAccese[0], 0, sezioniAccese[0].length);
        System.arraycopy(senzaPc, 0, sezioniAccese[1], 0, sezioniAccese[1].length);
        dipingiSezioni();
    }

    private void dipingiSezioni() {
        for (int g = 0; g < sezioniInt.length; g++) {
            for (int i = 0; i < sezioniInt[g].length; i++) {
                if (sezioniInt[g][i] != null) sezioniInt[g][i].metti(sezioniAccese[g][i]);
            }
        }
    }

    public void setBrightness(int percent) {
        if (brightnessBar == null) return;
        boolean auto = percent < 0;
        brightnessBar.setProgress(auto ? 60 : Math.max(5, Math.min(100, percent)));
        brightnessValue.setText(auto ? "come Android" : percent + "%");
    }

    /**
     * Segna la durata attiva. Il valore vero sta sempre nel titolo, anche
     * quando non e' nessuna di quelle offerte — succede col valore di fabbrica,
     * mezz'ora: senza, la pagina mostrerebbe sei caselle tutte spente senza
     * dire cosa sta facendo il tablet.
     */
    public void setScreenTimeout(int ms) {
        if (durataHeader != null) {
            durataHeader.setText("SPEGNI IL PANNELLO DOPO — " + Risparmio.durataDetta(ms).toUpperCase());
            durataHeader.setTextColor(ms > Risparmio.DURATA_ECCESSIVA ? COLOR_AVVISO : COLOR_TENUE);
        }
        for (int i = 0; i < durataChips.length; i++) {
            if (durataChips[i] == null) continue;
            boolean on = Risparmio.DURATE[i] == ms;
            durataChips[i].setTextColor(on ? 0xFFFFFFFF : COLOR_TESTO);
            durataChips[i].setBackground(tasto(on ? 0xFF4A78E6 : 0xFF33363E, on ? 0xFF3A63C9 : 0xFF2A2C32));
        }
    }

    /** Le due impostazioni protette, lette dal sistema. */
    public void setRisparmioDiSistema(boolean cavoTieneAcceso, int politicaRadio) {
        if (cavoValue != null) {
            cavoValue.setText(cavoTieneAcceso ? "resta sempre acceso" : "si spegne da solo");
            cavoValue.setTextColor(cavoTieneAcceso ? COLOR_AVVISO : COLOR_TENUE);
        }
        if (radioValue != null) {
            boolean maiDorme = politicaRadio == android.provider.Settings.Global.WIFI_SLEEP_POLICY_NEVER;
            radioValue.setText(Risparmio.radioDetta(politicaRadio));
            radioValue.setTextColor(maiDorme ? COLOR_AVVISO : COLOR_TENUE);
        }
    }

    public void setLinkState(boolean connected, String transport) {
        if (statusValue == null) return;
        statusValue.setText(connected ? "collegato via " + transport : "scollegato");
        statusValue.setTextColor(connected ? COLOR_ACCESO : COLOR_TENUE);
    }

    public void setAddress(String address) {
        if (addressValue != null) addressValue.setText(address);
    }

    // ---- pezzi dell'interfaccia ----

    /**
     * Un riquadro con il titolino sopra: il piano grafite dei pannelli, con il
     * filo di bordo. Le righe vanno dentro, divise da una linea sottile.
     */
    private LinearLayout riquadro(LinearLayout colonna, String titolo) {
        LinearLayout gruppo = new LinearLayout(getContext());
        gruppo.setOrientation(LinearLayout.VERTICAL);

        TextView t = new TextView(getContext());
        t.setText(titolo);
        t.setTextColor(COLOR_TENUE);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        t.setPadding(dp(4), dp(6), 0, dp(8));
        gruppo.addView(t);

        LinearLayout dentro = new LinearLayout(getContext());
        dentro.setOrientation(LinearLayout.VERTICAL);
        GradientDrawable piano = new GradientDrawable();
        piano.setColor(Tinte.PIANO);
        piano.setCornerRadius(dp(16));
        piano.setStroke(Math.max(1, dp(1)), Tinte.PIANO_BORDO);
        dentro.setBackground(piano);
        gruppo.addView(dentro, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));

        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        p.bottomMargin = dp(16);
        colonna.addView(gruppo, p);
        return dentro;
    }

    /** Una riga: icona, nome, e a destra quel che si fa. Toccata tutta, non solo a destra. */
    private LinearLayout riga(LinearLayout riquadro, String icona, String nome, View aDestra, OnClickListener tocco) {
        if (riquadro.getChildCount() > 0) {
            View linea = new View(getContext());
            linea.setBackgroundColor(COLOR_LINEA);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT, Math.max(1, dp(1)));
            lp.leftMargin = dp(46);
            riquadro.addView(linea, lp);
        }
        LinearLayout r = new LinearLayout(getContext());
        r.setOrientation(LinearLayout.HORIZONTAL);
        r.setGravity(Gravity.CENTER_VERTICAL);
        r.setPadding(dp(14), 0, dp(14), 0);
        r.setMinimumHeight(dp(48));

        r.addView(new Icona(getContext(), icona), new LinearLayout.LayoutParams(dp(20), dp(20)));

        TextView t = new TextView(getContext());
        t.setText(nome);
        t.setTextColor(COLOR_TESTO);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        t.setSingleLine(true);
        LinearLayout.LayoutParams tp = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
        tp.leftMargin = dp(12);
        r.addView(t, tp);

        if (aDestra != null) r.addView(aDestra);
        if (tocco != null) {
            r.setOnClickListener(tocco);
            r.setBackground(new android.graphics.drawable.ColorDrawable(0x00000000));
        }
        riquadro.addView(r, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, dp(48)));
        return r;
    }

    /** Una riga di sola lettura: il valore a destra, grigio. */
    private TextView valore(LinearLayout riquadro, String icona, String nome, String valore) {
        TextView val = new TextView(getContext());
        val.setText(valore);
        val.setTextColor(COLOR_TENUE);
        val.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        val.setSingleLine(true);
        riga(riquadro, icona, nome, val, null);
        return val;
    }

    private View freccia(Context c) {
        Icona f = new Icona(c, "chevron-right");
        f.setLayoutParams(new LinearLayout.LayoutParams(dp(18), dp(18)));
        return f;
    }

    /** Un tasto del deck come fondo di una casella: sfumato, smussato, col filo di luce. */
    private GradientDrawable tasto(int alto, int basso) {
        GradientDrawable g = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM, new int[] { alto, basso });
        g.setCornerRadius(dp(10));
        g.setStroke(Math.max(1, dp(1)), 0x14FFFFFF);
        return g;
    }

    /**
     * Le durate una accanto all'altra, come tasti: sei righe una sotto l'altra
     * riempirebbero mezzo pannello per una scelta che si fa una volta.
     */
    private View durataRow(Context context) {
        LinearLayout line = new LinearLayout(context);
        line.setOrientation(LinearLayout.HORIZONTAL);
        line.setPadding(dp(10), dp(10), dp(10), dp(10));

        for (int i = 0; i < Risparmio.DURATE.length; i++) {
            final int ms = Risparmio.DURATE[i];
            TextView chip = new TextView(context);
            chip.setText(Risparmio.DURATE_ETICHETTE[i]);
            chip.setTextColor(COLOR_TESTO);
            chip.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
            chip.setBackground(tasto(0xFF33363E, 0xFF2A2C32));
            chip.setGravity(Gravity.CENTER);
            chip.setOnClickListener(new OnClickListener() {
                @Override public void onClick(View v) { listener.onScreenTimeoutChosen(ms); }
            });
            LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, dp(40), 1f);
            p.setMargins(i == 0 ? 0 : dp(6), 0, 0, 0);
            line.addView(chip, p);
            durataChips[i] = chip;
        }
        line.setLayoutParams(new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return line;
    }

    private View brightnessRow(Context context) {
        LinearLayout line = new LinearLayout(context);
        line.setOrientation(LinearLayout.HORIZONTAL);
        line.setPadding(dp(14), dp(8), dp(14), dp(8));
        line.setGravity(Gravity.CENTER_VERTICAL);
        line.setMinimumHeight(dp(52));

        line.addView(new Icona(context, "sun-dim"), new LinearLayout.LayoutParams(dp(20), dp(20)));

        brightnessBar = new Cursore(context);
        brightnessBar.setProgress(60);
        LinearLayout.LayoutParams cp = new LinearLayout.LayoutParams(0, dp(32), 1f);
        cp.leftMargin = dp(10);
        line.addView(brightnessBar, cp);

        brightnessValue = new TextView(context);
        brightnessValue.setText("come Android");
        brightnessValue.setTextColor(COLOR_TENUE);
        brightnessValue.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        brightnessValue.setPadding(dp(8), 0, 0, 0);
        line.addView(brightnessValue, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        line.setLayoutParams(new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return line;
    }

    private int dp(int v) {
        return Math.round(v * density);
    }

    // ---- i pezzi disegnati ----

    /** Lo stesso fondo delle altre sezioni: grafite con la luce al centro. */
    private static final class Fondo extends android.graphics.drawable.Drawable {
        private final Paint p = new Paint();
        private final Paint alone = new Paint();

        @Override
        protected void onBoundsChange(android.graphics.Rect b) {
            p.setShader(new RadialGradient(b.exactCenterX(), b.height() * 0.4f,
                    Math.max(1, Math.max(b.width(), b.height()) * 0.75f),
                    Tinte.FONDO_ALTO, Tinte.FONDO_BASSO, Shader.TileMode.CLAMP));
            // La luce della sezione, come nelle altre: qui grigio-azzurra.
            alone.setShader(Vetro.aloneSezione(b.width(), b.height(), 0xFF7D8CA8));
        }

        @Override public void draw(Canvas c) {
            c.drawRect(getBounds(), p);
            c.drawRect(getBounds(), alone);
        }
        @Override public void setAlpha(int a) {}
        @Override public void setColorFilter(android.graphics.ColorFilter f) {}
        @Override public int getOpacity() { return android.graphics.PixelFormat.OPAQUE; }
    }

    /** Un'icona Lucide a tratto, grigia. */
    private static final class Icona extends View {
        private final String nome;
        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);

        Icona(Context c, String nome) {
            super(c);
            this.nome = nome;
            p.setColor(COLOR_ICONA);
        }

        @Override
        protected void onDraw(Canvas c) {
            Pittogrammi.disegna(c, nome, getWidth() / 2f, getHeight() / 2f, Math.min(getWidth(), getHeight()), p);
        }
    }

    /** L'interruttore di una sezione: azzurro e a destra quando e' acceso. */
    private final class Interruttore extends View {
        private boolean acceso;
        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF r = new RectF();

        Interruttore(Context c) {
            super(c);
            setLayoutParams(new LinearLayout.LayoutParams(dp(40), dp(24)));
        }

        void metti(boolean si) {
            if (acceso == si) return;
            acceso = si;
            invalidate();
        }

        @Override
        protected void onDraw(Canvas c) {
            float h = getHeight();
            r.set(0, 0, getWidth(), h);
            p.setColor(acceso ? Tinte.AZIONE : 0xFF33363E);
            c.drawRoundRect(r, h / 2f, h / 2f, p);
            p.setColor(acceso ? 0xFFFFFFFF : 0xFF9A9DA5);
            float raggio = h / 2f - dp(3);
            c.drawCircle(acceso ? getWidth() - h / 2f : h / 2f, h / 2f, raggio, p);
        }
    }

    /** Il segno di spunta della scelta fra tre: c'e' solo su quella attiva. */
    private final class Spunta extends View {
        private boolean acceso;
        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);

        Spunta(Context c) {
            super(c);
            p.setColor(Tinte.AZIONE);
            setLayoutParams(new LinearLayout.LayoutParams(dp(20), dp(20)));
        }

        void metti(boolean si) {
            if (acceso == si) return;
            acceso = si;
            invalidate();
        }

        @Override
        protected void onDraw(Canvas c) {
            if (acceso) Pittogrammi.disegna(c, "check", getWidth() / 2f, getHeight() / 2f, getWidth(), p);
        }
    }

    /**
     * Il cursore della luminosita': binario grafite, parte percorsa azzurra,
     * pomello bianco. Disegnato qui perche' quello di KitKat non si lascia
     * assottigliare: o resta una striscia spessa, o sparisce.
     */
    private final class Cursore extends View {
        private int valore = 60;
        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF r = new RectF();

        Cursore(Context c) {
            super(c);
            setClickable(true);
        }

        void setProgress(int v) {
            valore = Math.max(0, Math.min(100, v));
            invalidate();
        }

        private float raggio() {
            return dp(10);
        }

        @Override
        protected void onDraw(Canvas c) {
            float rg = raggio();
            float x0 = rg, x1 = getWidth() - rg, y = getHeight() / 2f, alto = dp(5);
            float x = x0 + (x1 - x0) * valore / 100f;
            r.set(x0, y - alto / 2f, x1, y + alto / 2f);
            p.setColor(0xFF33363E);
            c.drawRoundRect(r, alto / 2f, alto / 2f, p);
            r.right = x;
            p.setColor(Tinte.AZIONE);
            c.drawRoundRect(r, alto / 2f, alto / 2f, p);
            p.setColor(0xFFFFFFFF);
            c.drawCircle(x, y, rg, p);
        }

        @Override
        public boolean onTouchEvent(android.view.MotionEvent e) {
            int a = e.getActionMasked();
            if (a == android.view.MotionEvent.ACTION_DOWN || a == android.view.MotionEvent.ACTION_MOVE
                    || a == android.view.MotionEvent.ACTION_UP) {
                if (getParent() != null) getParent().requestDisallowInterceptTouchEvent(a != android.view.MotionEvent.ACTION_UP);
                float rg = raggio();
                int v = Math.round((e.getX() - rg) / Math.max(1f, getWidth() - rg * 2f) * 100f);
                setProgress(Math.max(5, v));
                brightnessValue.setText(valore + "%");
                listener.onBrightnessChosen(valore, a == android.view.MotionEvent.ACTION_UP);
                return true;
            }
            return super.onTouchEvent(e);
        }
    }
}
