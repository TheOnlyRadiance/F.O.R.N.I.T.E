using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Indicador del dash en pantalla (esquina inferior izquierda).
///  - Recargando: anillo que se llena, ícono apagado y segundos restantes.
///  - Listo: anillo completo y brillante, el ícono "late" y aparece "¡LISTO!".
///  - Si pulsas C mientras recarga, el ícono se sacude en rojo.
/// Se construye solo al iniciar: basta con poner este componente en cualquier objeto de la escena.
/// </summary>
public class HUDDash : MonoBehaviour
{
    [SerializeField] private ControladorMapache jugador;
    [SerializeField] private Vector2 posicion = new Vector2(150f, 150f);   // desde la esquina inferior izquierda (en px de 1920x1080)
    [SerializeField] private float tamano = 150f;
    [SerializeField] private Color colorListo = new Color(1f, 0.78f, 0.25f);
    [SerializeField] private Color colorCargando = new Color(0.95f, 0.55f, 0.15f);
    [SerializeField] private Color colorFondo = new Color(0.04f, 0.06f, 0.14f, 0.72f);

    private RectTransform raiz;
    private Image anillo, anilloFondo, brillo, fondo;
    private Text icono, segundos, etiqueta, tecla, aviso;
    private bool estabaListo = true;
    private float tiempoAviso, tiempoSacudida, tiempoDestello;

    private static Sprite circulo, aro;

    private void Start()
    {
        if (jugador == null) jugador = FindAnyObjectByType<ControladorMapache>();
        Construir();
    }

    // ------------------------------------------------------------------ construcción de la UI

    private void Construir()
    {
        if (circulo == null) circulo = CrearSpriteCirculo(256, 0f);
        if (aro == null) aro = CrearSpriteCirculo(256, 0.78f);
        var fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGO = new GameObject("HUD Dash", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        raiz = NuevoRT("Dash", canvasGO.transform);
        raiz.anchorMin = raiz.anchorMax = Vector2.zero;
        raiz.pivot = new Vector2(0.5f, 0.5f);
        raiz.anchoredPosition = posicion;
        raiz.sizeDelta = new Vector2(tamano, tamano);

        brillo = NuevaImagen("Brillo", raiz, circulo, new Color(colorListo.r, colorListo.g, colorListo.b, 0f), 1.35f);
        fondo = NuevaImagen("Fondo", raiz, circulo, colorFondo, 1f);
        anilloFondo = NuevaImagen("AnilloFondo", raiz, aro, new Color(1f, 1f, 1f, 0.12f), 1f);
        anillo = NuevaImagen("Anillo", raiz, aro, colorListo, 1f);
        anillo.type = Image.Type.Filled;
        anillo.fillMethod = Image.FillMethod.Radial360;
        anillo.fillOrigin = (int)Image.Origin360.Top;
        anillo.fillClockwise = true;

        icono = NuevoTexto("Icono", raiz, fuente, "»", Mathf.RoundToInt(tamano * 0.55f), Color.white, new Vector2(0f, tamano * 0.04f));
        icono.fontStyle = FontStyle.Bold;
        segundos = NuevoTexto("Segundos", raiz, fuente, "", Mathf.RoundToInt(tamano * 0.36f), Color.white, Vector2.zero);
        segundos.fontStyle = FontStyle.Bold;

        // insignia con la tecla
        var insignia = NuevaImagen("Tecla", raiz, circulo, new Color(1f, 1f, 1f, 0.95f), 0.32f);
        var irt = insignia.rectTransform;
        irt.anchoredPosition = new Vector2(tamano * 0.36f, -tamano * 0.36f);
        tecla = NuevoTexto("TeclaTexto", irt, fuente, "C", Mathf.RoundToInt(tamano * 0.2f), new Color(0.1f, 0.1f, 0.15f), Vector2.zero);
        tecla.fontStyle = FontStyle.Bold;

        etiqueta = NuevoTexto("Etiqueta", raiz, fuente, "DASH", Mathf.RoundToInt(tamano * 0.17f), Color.white, new Vector2(0f, -tamano * 0.66f));
        etiqueta.fontStyle = FontStyle.Bold;
        aviso = NuevoTexto("Aviso", raiz, fuente, "¡LISTO!", Mathf.RoundToInt(tamano * 0.2f), colorListo, new Vector2(0f, tamano * 0.68f));
        aviso.fontStyle = FontStyle.Bold;
        aviso.color = new Color(colorListo.r, colorListo.g, colorListo.b, 0f);

        foreach (var t in raiz.GetComponentsInChildren<Text>())
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.6f);
            s.effectDistance = new Vector2(2f, -2f);
        }
    }

    // ------------------------------------------------------------------ actualización

    private void Update()
    {
        if (raiz == null) return;
        if (jugador == null) { jugador = FindAnyObjectByType<ControladorMapache>(); if (jugador == null) return; }

        float dt = Time.unscaledDeltaTime;
        float restante = jugador.EnfriamientoDashRestante;
        bool recargado = restante <= 0f;
        bool listo = jugador.DashDisponible;

        // pulsar C mientras recarga → sacudida roja
        if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame && !listo && !jugador.HaciendoDash)
            tiempoSacudida = 0.35f;

        // al recargarse → destello y "¡LISTO!"
        if (recargado && !estabaListo) { tiempoDestello = 0.5f; tiempoAviso = 1.4f; }
        estabaListo = recargado;

        // anillo
        anillo.fillAmount = 1f - jugador.EnfriamientoDashNormalizado;
        Color cAnillo = recargado ? colorListo : colorCargando;
        if (recargado && !listo) cAnillo = Color.Lerp(colorListo, Color.gray, 0.5f);   // en el aire sin dash aéreo
        anillo.color = cAnillo;

        // ícono y segundos
        segundos.text = recargado ? "" : Mathf.CeilToInt(restante).ToString();
        icono.color = recargado ? (listo ? Color.white : new Color(1f, 1f, 1f, 0.45f)) : new Color(1f, 1f, 1f, 0.18f);

        // latido cuando está listo
        float escala = 1f;
        if (listo) escala += Mathf.Sin(Time.unscaledTime * 4f) * 0.035f;
        if (tiempoDestello > 0f)
        {
            tiempoDestello -= dt;
            escala += Mathf.Clamp01(tiempoDestello / 0.5f) * 0.25f;
        }
        raiz.localScale = Vector3.one * escala;

        float aBrillo = listo ? 0.18f + 0.1f * Mathf.Sin(Time.unscaledTime * 4f) : 0f;
        if (tiempoDestello > 0f) aBrillo = Mathf.Max(aBrillo, tiempoDestello / 0.5f * 0.8f);
        brillo.color = new Color(colorListo.r, colorListo.g, colorListo.b, aBrillo);

        // sacudida (intentaste usarlo antes de tiempo)
        Vector2 offset = Vector2.zero;
        Color cFondo = colorFondo;
        if (tiempoSacudida > 0f)
        {
            tiempoSacudida -= dt;
            float k = tiempoSacudida / 0.35f;
            offset.x = Mathf.Sin(Time.unscaledTime * 60f) * 10f * k;
            cFondo = Color.Lerp(colorFondo, new Color(0.8f, 0.1f, 0.1f, 0.85f), k);
        }
        raiz.anchoredPosition = posicion + offset;
        fondo.color = cFondo;

        // aviso "¡LISTO!"
        if (tiempoAviso > 0f) tiempoAviso -= dt;
        float aAviso = Mathf.Clamp01(tiempoAviso / 0.4f);
        aviso.color = new Color(colorListo.r, colorListo.g, colorListo.b, aAviso);
        aviso.rectTransform.anchoredPosition = new Vector2(0f, tamano * 0.68f + (1f - aAviso) * 12f);
    }

    // ------------------------------------------------------------------ utilidades

    private static RectTransform NuevoRT(string nombre, Transform padre)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(padre, false);
        return (RectTransform)go.transform;
    }

    private Image NuevaImagen(string nombre, Transform padre, Sprite sprite, Color color, float escalaRelativa)
    {
        var rt = NuevoRT(nombre, padre);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.one * tamano * escalaRelativa;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static Text NuevoTexto(string nombre, Transform padre, Font fuente, string texto, int tam, Color color, Vector2 pos)
    {
        var rt = NuevoRT(nombre, padre);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(300f, tam * 1.6f);
        rt.anchoredPosition = pos;
        var t = rt.gameObject.AddComponent<Text>();
        t.font = fuente;
        t.text = texto;
        t.fontSize = tam;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>Genera un círculo (o un aro si radioInterior &gt; 0) con bordes suavizados.</summary>
    private static Sprite CrearSpriteCirculo(int tam, float radioInterior)
    {
        var tex = new Texture2D(tam, tam, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[tam * tam];
        float c = (tam - 1) * 0.5f, rExt = tam * 0.5f - 1f, rInt = rExt * radioInterior;
        for (int y = 0; y < tam; y++)
            for (int x = 0; x < tam; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(rExt - d + 0.5f);
                if (radioInterior > 0f) a *= Mathf.Clamp01(d - rInt + 0.5f);
                px[y * tam + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, tam, tam), new Vector2(0.5f, 0.5f), 100f);
    }
}
