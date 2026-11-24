using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Pantalla de "CÓMO JUGAR" al iniciar el nivel: explica el objetivo y los controles
/// de movimiento y acciones (teclado y mando). El juego queda congelado hasta que el
/// jugador pulsa Espacio / Enter / clic / A.
/// La crea GestorPartida automáticamente.
/// </summary>
public class PantallaControles : MonoBehaviour
{
    /// <summary>True mientras la pantalla está visible (la pausa y la cámara la respetan).</summary>
    public static bool Abierta { get; private set; }

    [SerializeField] private float segundosAntesDeAceptar = 0.6f;

    private static readonly Color Oro = new Color(1f, 0.78f, 0.25f);
    private static readonly Color Panel = new Color(0.04f, 0.06f, 0.14f, 0.96f);
    private static readonly Color Gris = new Color(0.72f, 0.76f, 0.86f);

    private CanvasGroup grupo;
    private RectTransform rtPanel;
    private Text txtEmpezar;
    private float t, tCierre = -1f;
    private Font fuente;

    private void Awake()
    {
        Abierta = true;
        Time.timeScale = 0f;
        Construir();
    }

    private void OnDestroy()
    {
        if (Abierta) { Abierta = false; if (!MenuPausa.Pausado) Time.timeScale = 1f; }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        t += dt;

        if (tCierre < 0f)
        {
            // entrada
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.35f));
            grupo.alpha = k;
            rtPanel.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, k);

            bool listo = t >= segundosAntesDeAceptar;
            float parpadeo = listo ? 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f) : 0.25f;
            txtEmpezar.color = new Color(Oro.r, Oro.g, Oro.b, parpadeo);
            if (listo && Aceptar()) tCierre = 0f;
            return;
        }

        // salida
        tCierre += dt;
        float s = Mathf.Clamp01(tCierre / 0.25f);
        grupo.alpha = 1f - s;
        rtPanel.localScale = Vector3.one * Mathf.Lerp(1f, 1.04f, s);
        if (s >= 1f)
        {
            Abierta = false;
            Time.timeScale = 1f;
            CamaraSeguimiento.BloquearCursor(true);
            Destroy(grupo.gameObject);
            Destroy(this);
        }
    }

    private static bool Aceptar()
    {
        var kb = Keyboard.current; var gp = Gamepad.current; var m = Mouse.current;
        return (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame))
            || (gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame))
            || (m != null && m.leftButton.wasPressedThisFrame);
    }

    // ------------------------------------------------------------------ UI

    private void Construir()
    {
        fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGO = new GameObject("Pantalla Controles", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;   // encima de todo, incluso del menú de pausa
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        grupo = canvasGO.GetComponent<CanvasGroup>();
        grupo.alpha = 0f;
        grupo.blocksRaycasts = false; grupo.interactable = false;

        // fondo oscuro
        var fondo = RT("Fondo", canvasGO.transform, Vector2.zero, Vector2.zero);
        fondo.anchorMin = Vector2.zero; fondo.anchorMax = Vector2.one; fondo.offsetMin = fondo.offsetMax = Vector2.zero;
        Img(fondo, new Color(0.01f, 0.02f, 0.05f, 0.82f));

        // panel
        rtPanel = RT("Panel", canvasGO.transform, Vector2.zero, new Vector2(1440, 900));
        Img(rtPanel, Panel);
        var borde = rtPanel.gameObject.AddComponent<Outline>();
        borde.effectColor = new Color(Oro.r, Oro.g, Oro.b, 0.85f); borde.effectDistance = new Vector2(3, -3);

        var titulo = Txt("Titulo", rtPanel, "CÓMO JUGAR", 76, Oro, new Vector2(0, 385), new Vector2(1300, 90), TextAnchor.MiddleCenter);
        titulo.fontStyle = FontStyle.Bold;
        Txt("Sub", rtPanel, "Eres el mapache líder de la banda. Esta noche, el botín es tuyo.", 26, Gris, new Vector2(0, 330), new Vector2(1300, 40), TextAnchor.MiddleCenter);

        // columnas
        Columna(rtPanel, "MOVIMIENTO", new Vector2(-355, 270), new[]
        {
            ("WASD / ← ↑ ↓ →", "Stick izq.", "Moverse", "Camina por el vecindario"),
            ("Ratón", "Stick der.", "Cámara", "Gira la vista · rueda: zoom"),
            ("Shift", "L3", "Sigilo", "Más lento, pero casi sin ruido"),
            ("Espacio", "A", "Saltar", "Mantén pulsado para saltar más alto"),
            ("C", "X", "Dash", "Impulso rápido (¡hace mucho ruido!)"),
        });
        Columna(rtPanel, "ACCIONES", new Vector2(355, 270), new[]
        {
            ("Q / Clic der.", "RB", "Gancho", "Apunta al centro y trepa a techos"),
            ("R", "LB", "Sobrecarga", "Más velocidad y salto por un rato"),
            ("E", "Y", "Recoger", "Toma el botín cuando estés cerca"),
            ("Esc", "Start", "Pausa", "Pausa el juego o sal al menú"),
        });

        // objetivo
        var caja = RT("Objetivo", rtPanel, new Vector2(0, -292), new Vector2(1340, 130));
        Img(caja, new Color(Oro.r, Oro.g, Oro.b, 0.08f));
        var bc = caja.gameObject.AddComponent<Outline>();
        bc.effectColor = new Color(Oro.r, Oro.g, Oro.b, 0.35f); bc.effectDistance = new Vector2(2, -2);
        var obj = Txt("Titulo", caja, "OBJETIVO", 26, Oro, new Vector2(-640, 38), new Vector2(400, 36), TextAnchor.MiddleLeft);
        obj.fontStyle = FontStyle.Bold;
        obj.rectTransform.pivot = new Vector2(0, 0.5f);
        var desc = Txt("Texto", caja,
            "Encuentra los basureros dorados y los objetos escondidos en la casa sin que los guardias ni el perro te atrapen.\n" +
            "Con todo el botín, ¡corre a la ZONA DE ESCAPE antes de que se acabe el tiempo!  Consejo: el ruido te delata.",
            24, Color.white, new Vector2(-640, -18), new Vector2(1280, 76), TextAnchor.MiddleLeft);
        desc.rectTransform.pivot = new Vector2(0, 0.5f);
        desc.horizontalOverflow = HorizontalWrapMode.Wrap;
        desc.lineSpacing = 1.15f;

        txtEmpezar = Txt("Empezar", rtPanel, "Pulsa  ESPACIO,  ENTER  o  (A)  para empezar", 32, Oro, new Vector2(0, -395), new Vector2(1300, 50), TextAnchor.MiddleCenter);
        txtEmpezar.fontStyle = FontStyle.Bold;

        foreach (var tx in canvasGO.GetComponentsInChildren<Text>(true))
        {
            var s = tx.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, 0.7f); s.effectDistance = new Vector2(2, -2);
        }
    }

    private void Columna(RectTransform padre, string titulo, Vector2 arriba, (string tecla, string mando, string accion, string desc)[] filas)
    {
        var col = RT(titulo, padre, arriba, new Vector2(680, 40));
        col.pivot = new Vector2(0.5f, 1f);
        col.anchoredPosition = arriba;

        var cab = Txt("Cabecera", col, titulo, 30, Oro, new Vector2(-340, -20), new Vector2(400, 40), TextAnchor.MiddleLeft);
        cab.fontStyle = FontStyle.Bold;
        cab.rectTransform.pivot = new Vector2(0, 0.5f);
        var linea = RT("Linea", col, new Vector2(0, -44), new Vector2(680, 2));
        Img(linea, new Color(Oro.r, Oro.g, Oro.b, 0.4f));
        Txt("Teclado", col, "teclado", 18, new Color(1f, 1f, 1f, 0.45f), new Vector2(-245, -58), new Vector2(190, 22), TextAnchor.MiddleCenter);
        Txt("Mando", col, "mando", 18, new Color(1f, 1f, 1f, 0.45f), new Vector2(-95, -58), new Vector2(100, 22), TextAnchor.MiddleCenter);

        for (int i = 0; i < filas.Length; i++)
        {
            float y = -112 - i * 78;
            var f = filas[i];
            Tecla(col, f.tecla, new Vector2(-245, y), 190, true);
            Tecla(col, f.mando, new Vector2(-95, y), 100, false);
            var acc = Txt("Accion", col, f.accion, 30, Color.white, new Vector2(-30, y + 15), new Vector2(380, 34), TextAnchor.MiddleLeft);
            acc.fontStyle = FontStyle.Bold;
            acc.rectTransform.pivot = new Vector2(0, 0.5f);
            var d = Txt("Desc", col, f.desc, 20, Gris, new Vector2(-30, y - 18), new Vector2(380, 28), TextAnchor.MiddleLeft);
            d.rectTransform.pivot = new Vector2(0, 0.5f);
        }
    }

    /// <summary>Tecla dibujada como un botoncito: dorada para teclado, azulada para mando.</summary>
    private void Tecla(RectTransform padre, string texto, Vector2 pos, float ancho, bool teclado)
    {
        var rt = RT("Tecla_" + texto, padre, pos, new Vector2(ancho, 56));
        Img(rt, teclado ? new Color(0.16f, 0.14f, 0.08f, 1f) : new Color(0.10f, 0.14f, 0.28f, 1f));
        var o = rt.gameObject.AddComponent<Outline>();
        o.effectColor = teclado ? new Color(Oro.r, Oro.g, Oro.b, 0.9f) : new Color(0.55f, 0.7f, 1f, 0.8f);
        o.effectDistance = new Vector2(2, -2);
        int tam = texto.Length > 12 ? 20 : texto.Length > 7 ? 23 : 28;
        var tx = Txt("Texto", rt, texto, tam, teclado ? Oro : new Color(0.8f, 0.88f, 1f), Vector2.zero, new Vector2(ancho, 56), TextAnchor.MiddleCenter);
        tx.fontStyle = FontStyle.Bold;
    }

    private static RectTransform RT(string n, Transform padre, Vector2 pos, Vector2 tam)
    {
        var go = new GameObject(n, typeof(RectTransform));
        var rt = (RectTransform)go.transform; rt.SetParent(padre, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = tam;
        return rt;
    }

    private static Image Img(RectTransform rt, Color c)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
        return img;
    }

    private Text Txt(string n, RectTransform padre, string texto, int tam, Color color, Vector2 pos, Vector2 caja, TextAnchor alin)
    {
        var rt = RT(n, padre, pos, caja);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = fuente; t.text = texto; t.fontSize = tam; t.color = color; t.alignment = alin;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
