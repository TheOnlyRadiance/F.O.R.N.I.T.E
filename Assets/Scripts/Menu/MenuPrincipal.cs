using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menú principal en un solo script:
///  - Botón Jugar  → carga la escena del juego (por defecto "Nivel1").
///  - Botón Salir  → cierra el juego (en el editor detiene el Play Mode).
///  - Animación de los botones: crecen al pasar el mouse / seleccionarlos y se aplastan al pulsarlos.
///  - Fundido a negro al entrar y al salir (la capa negra se crea sola).
/// Ponlo en el Canvas y arrastra los dos botones a sus campos.
/// </summary>
public class MenuPrincipal : MonoBehaviour
{
    [Header("Escena")]
    [Tooltip("Nombre exacto de la escena que se carga al pulsar Jugar. Debe estar en la lista de escenas del build.")]
    [SerializeField] private string escenaJuego = "Nivel1";

    [Header("Botones")]
    [SerializeField] private Button botonJugar;
    [SerializeField] private Button botonSalir;

    [Header("Animación de botones")]
    [SerializeField] private float escalaResaltado = 1.08f;
    [SerializeField] private float escalaPulsado = 0.95f;
    [SerializeField] private float velocidadEscala = 14f;

    [Header("Fundido")]
    [SerializeField] private float duracionFundido = 0.6f;
    [SerializeField] private Color colorFundido = Color.black;

    private CanvasGroup fundido;
    private Button[] botones;
    private Vector3[] escalasBase;
    private Button botonPulsado;
    private bool ocupado;

    // ------------------------------------------------------------------ ciclo de vida

    private void Awake()
    {
        botones = new[] { botonJugar, botonSalir };
        escalasBase = new Vector3[botones.Length];
        for (int i = 0; i < botones.Length; i++)
        {
            if (botones[i] == null) continue;
            escalasBase[i] = botones[i].transform.localScale;
            AgregarEventosDeAnimacion(botones[i]);
        }

        // Navegación con teclado / mando entre los dos botones
        if (botonJugar != null && botonSalir != null)
        {
            var navJ = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = botonSalir, selectOnUp = botonSalir };
            var navS = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = botonJugar, selectOnDown = botonJugar };
            botonJugar.navigation = navJ;
            botonSalir.navigation = navS;
        }

        fundido = CrearCapaFundido();
    }

    private IEnumerator Start()
    {
        Time.timeScale = 1f;

        fundido.alpha = 1f;
        fundido.blocksRaycasts = true;
        yield return Fundir(1f, 0f);
        fundido.blocksRaycasts = false;

        if (botonJugar != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(botonJugar.gameObject);
    }

    private void Update()
    {
        GameObject seleccionado = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        float k = 1f - Mathf.Exp(-velocidadEscala * Time.unscaledDeltaTime);

        for (int i = 0; i < botones.Length; i++)
        {
            Button b = botones[i];
            if (b == null) continue;

            float factor = 1f;
            if (b == botonPulsado) factor = escalaPulsado;
            else if (b.gameObject == seleccionado && b.interactable) factor = escalaResaltado;

            b.transform.localScale = Vector3.Lerp(b.transform.localScale, escalasBase[i] * factor, k);
        }
    }

    // ------------------------------------------------------------------ acciones de los botones

    /// <summary>Asignado al OnClick del botón Jugar.</summary>
    public void Jugar()
    {
        if (ocupado) return;

        if (!Application.CanStreamedLevelBeLoaded(escenaJuego))
        {
            Debug.LogError($"[MenuPrincipal] La escena \"{escenaJuego}\" no existe o no está en la lista de escenas del build (File > Build Profiles > Scene List).");
            return;
        }

        StartCoroutine(CargarEscena());
    }

    /// <summary>Asignado al OnClick del botón Salir.</summary>
    public void Salir()
    {
        if (ocupado) return;
        StartCoroutine(SalirDelJuego());
    }

    private IEnumerator CargarEscena()
    {
        ocupado = true;
        yield return FundirASalida();
        SceneManager.LoadScene(escenaJuego);
    }

    private IEnumerator SalirDelJuego()
    {
        ocupado = true;
        yield return FundirASalida();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ animación de botones

    private void AgregarEventosDeAnimacion(Button boton)
    {
        var trigger = boton.GetComponent<EventTrigger>();
        if (trigger == null) trigger = boton.gameObject.AddComponent<EventTrigger>();

        // Al pasar el mouse el botón queda seleccionado, así mouse y teclado se comportan igual.
        Agregar(trigger, EventTriggerType.PointerEnter, _ =>
        {
            if (EventSystem.current != null && boton.interactable)
                EventSystem.current.SetSelectedGameObject(boton.gameObject);
        });
        Agregar(trigger, EventTriggerType.PointerDown, _ => botonPulsado = boton);
        Agregar(trigger, EventTriggerType.PointerUp, _ => { if (botonPulsado == boton) botonPulsado = null; });
        Agregar(trigger, EventTriggerType.PointerExit, _ => { if (botonPulsado == boton) botonPulsado = null; });
    }

    private static void Agregar(EventTrigger trigger, EventTriggerType tipo, UnityEngine.Events.UnityAction<BaseEventData> accion)
    {
        var entrada = new EventTrigger.Entry { eventID = tipo };
        entrada.callback.AddListener(accion);
        trigger.triggers.Add(entrada);
    }

    // ------------------------------------------------------------------ fundido

    private CanvasGroup CrearCapaFundido()
    {
        var go = new GameObject("Fundido", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        go.layer = gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling();               // siempre encima de todo

        go.GetComponent<Image>().color = colorFundido;
        var cg = go.GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        return cg;
    }

    private IEnumerator FundirASalida()
    {
        foreach (var b in botones) if (b != null) b.interactable = false;
        fundido.blocksRaycasts = true;
        yield return Fundir(fundido.alpha, 1f);
    }

    private IEnumerator Fundir(float desde, float hasta)
    {
        float t = 0f;
        while (t < duracionFundido)
        {
            t += Time.unscaledDeltaTime;
            fundido.alpha = Mathf.Lerp(desde, hasta, Mathf.SmoothStep(0f, 1f, t / duracionFundido));
            yield return null;
        }
        fundido.alpha = hasta;
    }
}
