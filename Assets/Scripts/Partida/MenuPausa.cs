using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menú de pausa del nivel:
///  - Esc (o Start en mando) pausa / reanuda el juego.
///  - "Continuar"  → vuelve al juego.
///  - "Salir al menú" → carga la escena del menú principal.
/// Mientras está en pausa se congela el tiempo, el audio y la cámara, y el cursor queda libre.
/// Lo crea GestorPartida automáticamente; no hace falta ponerlo en la escena.
/// </summary>
public class MenuPausa : MonoBehaviour
{
    public static bool Pausado { get; private set; }
    /// <summary>True cuando la partida terminó (escapaste); ya no se puede pausar.</summary>
    public static bool PartidaTerminada { get; set; }

    [Tooltip("Nombre de la escena del menú principal (debe estar en la lista de escenas del build).")]
    [SerializeField] private string escenaMenu = "SampleScene";

    private GameObject capa;
    private Button btnContinuar, btnMenu;
    private Text titulo;
    private float tAbierto;

    private static readonly Color Oro = new Color(1f, 0.78f, 0.25f);
    private static readonly Color FondoBoton = new Color(0.10f, 0.13f, 0.26f, 0.95f);

    private void Awake()
    {
        Pausado = false;
        PartidaTerminada = false;
        AsegurarEventSystem();
        ConstruirUI();
        capa.SetActive(false);
    }

    private void OnDestroy()
    {
        // por si se sale de la escena estando en pausa
        if (Pausado) { Time.timeScale = 1f; AudioListener.pause = false; }
        Pausado = false;
    }

    private void Update()
    {
        bool pulsado = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
        if (pulsado)
        {
            if (Pausado) Continuar();
            else Pausar();
        }

        if (Pausado)
        {
            // pequeña animación de entrada del panel (tiempo real: el juego está congelado)
            tAbierto += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tAbierto / 0.18f));
            capa.transform.GetChild(0).localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, k);
            titulo.rectTransform.localScale = Vector3.one * (1f + 0.02f * Mathf.Sin(Time.unscaledTime * 3f));

            // si el ratón no está sobre nada y se usa teclado/mando, que siempre haya un botón seleccionado
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null &&
                ((Keyboard.current != null && (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame ||
                                                Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame)) ||
                 (Gamepad.current != null && Gamepad.current.dpad.ReadValue() != Vector2.zero)))
                EventSystem.current.SetSelectedGameObject(btnContinuar.gameObject);
        }
    }

    // ------------------------------------------------------------------ acciones

    public void Pausar()
    {
        if (Pausado || PartidaTerminada || PantallaControles.Abierta) return;
        if (GestorAlerta.Instancia != null && GestorAlerta.Instancia.JugadorAtrapado) return;

        Pausado = true;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        CamaraSeguimiento.BloquearCursor(false);
        tAbierto = 0f;
        capa.SetActive(true);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(btnContinuar.gameObject);
    }

    public void Continuar()
    {
        if (!Pausado) return;
        Pausado = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        capa.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        CamaraSeguimiento.BloquearCursor(true);
    }

    public void SalirAlMenu() => IrAlMenu(escenaMenu);

    /// <summary>Restaura el tiempo y el audio y carga el menú principal (también lo usa la pantalla de victoria).</summary>
    public static void IrAlMenu(string escena = "SampleScene")
    {
        Pausado = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        CamaraSeguimiento.BloquearCursor(false);
        if (!string.IsNullOrEmpty(escena) && Application.CanStreamedLevelBeLoaded(escena)) SceneManager.LoadScene(escena);
        else SceneManager.LoadScene(0);   // el menú es la primera escena del build
    }

    // ------------------------------------------------------------------ UI

    private static void AsegurarEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
        var modulo = go.AddComponent<InputSystemUIInputModule>();
        modulo.AssignDefaultActions();
    }

    private void ConstruirUI()
    {
        var fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGO = new GameObject("Menu Pausa", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;   // encima de todos los HUD
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // capa oscura que tapa el juego (bloquea clics al mundo)
        capa = new GameObject("Capa", typeof(RectTransform), typeof(Image));
        var rtCapa = (RectTransform)capa.transform;
        rtCapa.SetParent(canvasGO.transform, false);
        rtCapa.anchorMin = Vector2.zero; rtCapa.anchorMax = Vector2.one; rtCapa.offsetMin = rtCapa.offsetMax = Vector2.zero;
        capa.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.06f, 0.72f);

        // panel central
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        var rtPanel = (RectTransform)panel.transform;
        rtPanel.SetParent(rtCapa, false);
        rtPanel.anchorMin = rtPanel.anchorMax = rtPanel.pivot = new Vector2(0.5f, 0.5f);
        rtPanel.sizeDelta = new Vector2(620, 520);
        panel.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.14f, 0.94f);
        var borde = panel.AddComponent<Outline>();
        borde.effectColor = new Color(Oro.r, Oro.g, Oro.b, 0.8f);
        borde.effectDistance = new Vector2(3, -3);

        titulo = Texto("Titulo", rtPanel, fuente, "PAUSA", 84, Oro, new Vector2(0, 165), new Vector2(600, 110));
        titulo.fontStyle = FontStyle.Bold;
        Texto("Sub", rtPanel, fuente, "El vecindario espera...", 28, new Color(0.8f, 0.82f, 0.9f), new Vector2(0, 95), new Vector2(600, 40));

        btnContinuar = Boton("Continuar", rtPanel, fuente, "Continuar", new Vector2(0, -5), Continuar);
        btnMenu = Boton("SalirMenu", rtPanel, fuente, "Salir al menú", new Vector2(0, -125), SalirAlMenu);

        Texto("Ayuda", rtPanel, fuente, "Esc para continuar", 22, new Color(1f, 1f, 1f, 0.55f), new Vector2(0, -215), new Vector2(600, 30));

        // navegación vertical explícita entre los dos botones
        btnContinuar.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = btnMenu, selectOnUp = btnMenu };
        btnMenu.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = btnContinuar, selectOnUp = btnContinuar };
    }

    private Button Boton(string nombre, Transform padre, Font fuente, string texto, Vector2 pos, UnityEngine.Events.UnityAction accion)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(padre, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(420, 96);
        var img = go.GetComponent<Image>();
        img.color = Color.white;

        var b = go.GetComponent<Button>();
        b.targetGraphic = img;
        var colores = b.colors;
        colores.normalColor = FondoBoton;
        colores.highlightedColor = new Color(0.85f, 0.62f, 0.15f, 1f);
        colores.selectedColor = new Color(0.85f, 0.62f, 0.15f, 1f);
        colores.pressedColor = new Color(0.6f, 0.42f, 0.1f, 1f);
        colores.colorMultiplier = 1f;
        colores.fadeDuration = 0.08f;
        b.colors = colores;
        b.onClick.AddListener(accion);

        var t = Texto("Texto", rt, fuente, texto, 40, Color.white, Vector2.zero, rt.sizeDelta);
        t.fontStyle = FontStyle.Bold;

        // al pasar el ratón el botón queda seleccionado (mouse y teclado se comportan igual)
        var trigger = go.AddComponent<EventTrigger>();
        var entrada = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entrada.callback.AddListener(_ => { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(go); });
        trigger.triggers.Add(entrada);
        return b;
    }

    private static Text Texto(string nombre, Transform padre, Font fuente, string texto, int tam, Color color, Vector2 pos, Vector2 caja)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(padre, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = caja;
        var t = go.GetComponent<Text>();
        t.font = fuente; t.text = texto; t.fontSize = tam; t.color = color; t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var s = go.AddComponent<Shadow>();
        s.effectColor = new Color(0, 0, 0, 0.7f); s.effectDistance = new Vector2(2, -2);
        return t;
    }
}
