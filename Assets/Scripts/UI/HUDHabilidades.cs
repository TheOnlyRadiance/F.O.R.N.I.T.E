using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// HUD de las habilidades del mapache (esquina inferior izquierda):
///   Dash [C] · Gancho [Q] · Sobrecarga [R]
/// Cada ícono muestra un anillo de recarga, los segundos que faltan y "late" cuando está listo.
/// La Sobrecarga activa muestra un anillo azul que se vacía mientras dura el buff.
/// Además: mira al centro de la pantalla (verde = el gancho alcanza) e indicador de RUIDO / SIGILO.
/// Se construye solo al iniciar.
/// </summary>
public class HUDHabilidades : MonoBehaviour
{
    [SerializeField] private ControladorMapache jugador;
    [SerializeField] private Vector2 posicion = new Vector2(120f, 130f);
    [SerializeField] private float tamano = 120f;
    [SerializeField] private float separacion = 150f;

    private static readonly Color Oro = new Color(1f, 0.78f, 0.25f);
    private static readonly Color Naranja = new Color(0.95f, 0.55f, 0.15f);
    private static readonly Color Azul = new Color(0.35f, 0.85f, 1f);
    private static readonly Color Fondo = new Color(0.04f, 0.06f, 0.14f, 0.75f);

    private class Slot
    {
        public RectTransform raiz;
        public Image anillo, brillo, fondo;
        public Text icono, segundos, aviso;
        public bool estabaListo = true;
        public float tDestello, tAviso, tSacudida;
        public Vector2 pos;
    }

    private Slot dash, gancho, sobrecarga;
    private Image mira, barraRuido;
    private Text txtSigilo;
    private static Sprite circulo, aro;

    private void Start()
    {
        if (jugador == null) jugador = FindAnyObjectByType<ControladorMapache>();
        Construir();
    }

    // ------------------------------------------------------------------ construcción

    private void Construir()
    {
        if (circulo == null) circulo = CrearSpriteCirculo(256, 0f);
        if (aro == null) aro = CrearSpriteCirculo(256, 0.78f);
        var fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGO = new GameObject("HUD Habilidades", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var raiz = canvasGO.transform;

        dash = CrearSlot(raiz, fuente, "»", "C", "DASH", posicion);
        gancho = CrearSlot(raiz, fuente, "↑", "Q", "GANCHO", posicion + new Vector2(separacion, 0f));
        sobrecarga = CrearSlot(raiz, fuente, "▲", "R", "SOBRECARGA", posicion + new Vector2(separacion * 2f, 0f));

        // mira
        var m = NuevoRT("Mira", raiz); m.anchorMin = m.anchorMax = new Vector2(0.5f, 0.5f); m.sizeDelta = new Vector2(14f, 14f);
        mira = m.gameObject.AddComponent<Image>(); mira.sprite = aro; mira.raycastTarget = false;
        var punto = NuevoRT("Punto", m); punto.anchorMin = punto.anchorMax = new Vector2(0.5f, 0.5f); punto.sizeDelta = new Vector2(4f, 4f);
        var ip = punto.gameObject.AddComponent<Image>(); ip.sprite = circulo; ip.raycastTarget = false;

        // ruido / sigilo (encima de los íconos)
        var fondoRuido = NuevoRT("Ruido", raiz);
        fondoRuido.anchorMin = fondoRuido.anchorMax = Vector2.zero; fondoRuido.pivot = new Vector2(0f, 0.5f);
        fondoRuido.anchoredPosition = posicion + new Vector2(-tamano * 0.5f, tamano * 0.95f);
        fondoRuido.sizeDelta = new Vector2(separacion * 2f + tamano, 12f);
        var imgF = fondoRuido.gameObject.AddComponent<Image>(); imgF.color = new Color(0f, 0f, 0f, 0.5f); imgF.raycastTarget = false;
        var barra = NuevoRT("Barra", fondoRuido); barra.anchorMin = Vector2.zero; barra.anchorMax = new Vector2(0f, 1f); barra.pivot = new Vector2(0f, 0.5f);
        barra.offsetMin = barra.offsetMax = Vector2.zero;
        barraRuido = barra.gameObject.AddComponent<Image>(); barraRuido.raycastTarget = false;
        var lbl = Texto("Lbl", fondoRuido, fuente, "RUIDO", 18, new Color(1, 1, 1, 0.8f), new Vector2(0f, 18f), TextAnchor.MiddleLeft);
        lbl.rectTransform.anchorMin = lbl.rectTransform.anchorMax = new Vector2(0f, 0.5f); lbl.rectTransform.pivot = new Vector2(0f, 0.5f);
        txtSigilo = Texto("Sigilo", fondoRuido, fuente, "", 22, Azul, new Vector2(0f, 18f), TextAnchor.MiddleRight);
        txtSigilo.fontStyle = FontStyle.Bold;
        txtSigilo.rectTransform.anchorMin = txtSigilo.rectTransform.anchorMax = new Vector2(1f, 0.5f); txtSigilo.rectTransform.pivot = new Vector2(1f, 0.5f);

        foreach (var t in canvasGO.GetComponentsInChildren<Text>())
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.6f); s.effectDistance = new Vector2(2f, -2f);
        }
    }

    private Slot CrearSlot(Transform padre, Font fuente, string simbolo, string tecla, string nombre, Vector2 pos)
    {
        var s = new Slot { pos = pos };
        s.raiz = NuevoRT(nombre, padre);
        s.raiz.anchorMin = s.raiz.anchorMax = Vector2.zero;
        s.raiz.anchoredPosition = pos;
        s.raiz.sizeDelta = new Vector2(tamano, tamano);

        s.brillo = Img("Brillo", s.raiz, circulo, new Color(Oro.r, Oro.g, Oro.b, 0f), 1.35f);
        s.fondo = Img("Fondo", s.raiz, circulo, Fondo, 1f);
        Img("AnilloFondo", s.raiz, aro, new Color(1f, 1f, 1f, 0.12f), 1f);
        s.anillo = Img("Anillo", s.raiz, aro, Oro, 1f);
        s.anillo.type = Image.Type.Filled; s.anillo.fillMethod = Image.FillMethod.Radial360;
        s.anillo.fillOrigin = (int)Image.Origin360.Top; s.anillo.fillClockwise = true;

        s.icono = Texto("Icono", s.raiz, fuente, simbolo, Mathf.RoundToInt(tamano * 0.5f), Color.white, new Vector2(0f, tamano * 0.03f), TextAnchor.MiddleCenter);
        s.icono.fontStyle = FontStyle.Bold;
        s.segundos = Texto("Segundos", s.raiz, fuente, "", Mathf.RoundToInt(tamano * 0.36f), Color.white, Vector2.zero, TextAnchor.MiddleCenter);
        s.segundos.fontStyle = FontStyle.Bold;

        var insignia = Img("Tecla", s.raiz, circulo, new Color(1f, 1f, 1f, 0.95f), 0.34f);
        insignia.rectTransform.anchoredPosition = new Vector2(tamano * 0.36f, -tamano * 0.36f);
        var tt = Texto("TeclaTxt", insignia.rectTransform, fuente, tecla, Mathf.RoundToInt(tamano * 0.22f), new Color(0.1f, 0.1f, 0.15f), Vector2.zero, TextAnchor.MiddleCenter);
        tt.fontStyle = FontStyle.Bold;

        var et = Texto("Etiqueta", s.raiz, fuente, nombre, Mathf.RoundToInt(tamano * 0.15f), Color.white, new Vector2(0f, -tamano * 0.66f), TextAnchor.MiddleCenter);
        et.fontStyle = FontStyle.Bold;
        s.aviso = Texto("Aviso", s.raiz, fuente, "¡LISTO!", Mathf.RoundToInt(tamano * 0.18f), Oro, new Vector2(0f, tamano * 0.7f), TextAnchor.MiddleCenter);
        s.aviso.fontStyle = FontStyle.Bold;
        s.aviso.color = new Color(Oro.r, Oro.g, Oro.b, 0f);
        return s;
    }

    // ------------------------------------------------------------------ actualización

    private void Update()
    {
        if (dash == null) return;
        if (jugador == null) { jugador = FindAnyObjectByType<ControladorMapache>(); if (jugador == null) return; }
        float dt = Time.unscaledDeltaTime;
        var kb = Keyboard.current;

        ActualizarSlot(dash, jugador.EnfriamientoDashNormalizado, jugador.EnfriamientoDashRestante, jugador.DashDisponible,
                       0f, false, kb != null && kb.cKey.wasPressedThisFrame && !jugador.DashDisponible && !jugador.HaciendoDash, dt);
        ActualizarSlot(gancho, jugador.EnfriamientoGanchoNormalizado, jugador.EnfriamientoGanchoRestante, jugador.GanchoDisponible,
                       0f, false, kb != null && kb.qKey.wasPressedThisFrame && !jugador.GanchoDisponible && !jugador.UsandoGancho, dt);
        ActualizarSlot(sobrecarga, jugador.EnfriamientoSobrecargaNormalizado, jugador.EnfriamientoSobrecargaRestante, jugador.SobrecargaDisponible,
                       jugador.SobrecargaRestanteNormalizada, jugador.SobrecargaActiva, kb != null && kb.rKey.wasPressedThisFrame && !jugador.SobrecargaDisponible && !jugador.SobrecargaActiva, dt);
        if (jugador.SobrecargaActiva) sobrecarga.segundos.text = Mathf.CeilToInt(jugador.SobrecargaRestante).ToString();

        // mira del gancho
        bool valido = jugador.ObjetivoGanchoValido && jugador.GanchoDisponible;
        mira.color = valido ? new Color(0.3f, 1f, 0.45f, 0.95f) : new Color(1f, 1f, 1f, 0.45f);
        mira.rectTransform.sizeDelta = Vector2.one * (valido ? 22f : 14f);

        // ruido y sigilo
        float r = jugador.Ruido;
        barraRuido.rectTransform.anchorMax = new Vector2(Mathf.Lerp(barraRuido.rectTransform.anchorMax.x, r, 1f - Mathf.Exp(-10f * dt)), 1f);
        barraRuido.color = Color.Lerp(new Color(0.3f, 0.9f, 0.5f), new Color(1f, 0.25f, 0.2f), r);
        txtSigilo.text = jugador.EnSigilo ? "SIGILO" : (jugador.SobrecargaActiva ? "SOBRECARGA" : "");
        txtSigilo.color = jugador.EnSigilo ? Azul : Azul;
    }

    private void ActualizarSlot(Slot s, float enfNorm, float restante, bool listo, float activoNorm, bool activo, bool fallo, float dt)
    {
        bool recargado = restante <= 0f;
        if (recargado && !s.estabaListo) { s.tDestello = 0.5f; s.tAviso = 1.3f; }
        s.estabaListo = recargado;
        if (fallo) s.tSacudida = 0.35f;

        if (activo) { s.anillo.fillAmount = activoNorm; s.anillo.color = Azul; }
        else { s.anillo.fillAmount = 1f - enfNorm; s.anillo.color = recargado ? (listo ? Oro : Color.Lerp(Oro, Color.gray, 0.5f)) : Naranja; }

        s.segundos.text = activo || recargado ? "" : Mathf.CeilToInt(restante).ToString();
        s.icono.color = activo ? Azul : recargado ? (listo ? Color.white : new Color(1f, 1f, 1f, 0.45f)) : new Color(1f, 1f, 1f, 0.18f);

        float escala = 1f;
        if (listo) escala += Mathf.Sin(Time.unscaledTime * 4f) * 0.03f;
        if (activo) escala += Mathf.Sin(Time.unscaledTime * 10f) * 0.04f;
        if (s.tDestello > 0f) { s.tDestello -= dt; escala += Mathf.Clamp01(s.tDestello / 0.5f) * 0.22f; }
        s.raiz.localScale = Vector3.one * escala;

        float aBrillo = activo ? 0.35f : listo ? 0.15f + 0.08f * Mathf.Sin(Time.unscaledTime * 4f) : 0f;
        if (s.tDestello > 0f) aBrillo = Mathf.Max(aBrillo, s.tDestello / 0.5f * 0.8f);
        Color cb = activo ? Azul : Oro;
        s.brillo.color = new Color(cb.r, cb.g, cb.b, aBrillo);

        Vector2 offset = Vector2.zero; Color cf = Fondo;
        if (s.tSacudida > 0f)
        {
            s.tSacudida -= dt;
            float k = s.tSacudida / 0.35f;
            offset.x = Mathf.Sin(Time.unscaledTime * 60f) * 8f * k;
            cf = Color.Lerp(Fondo, new Color(0.8f, 0.1f, 0.1f, 0.85f), k);
        }
        s.raiz.anchoredPosition = s.pos + offset;
        s.fondo.color = cf;

        if (s.tAviso > 0f) s.tAviso -= dt;
        float a = Mathf.Clamp01(s.tAviso / 0.4f);
        s.aviso.color = new Color(Oro.r, Oro.g, Oro.b, a);
    }

    // ------------------------------------------------------------------ utilidades

    private static RectTransform NuevoRT(string nombre, Transform padre)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(padre, false);
        return (RectTransform)go.transform;
    }

    private Image Img(string nombre, Transform padre, Sprite sprite, Color color, float escalaRel)
    {
        var rt = NuevoRT(nombre, padre);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.one * tamano * escalaRel;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        return img;
    }

    private static Text Texto(string nombre, Transform padre, Font fuente, string texto, int tam, Color color, Vector2 pos, TextAnchor alin)
    {
        var rt = NuevoRT(nombre, padre);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(320f, tam * 1.6f);
        rt.anchoredPosition = pos;
        var t = rt.gameObject.AddComponent<Text>();
        t.font = fuente; t.text = texto; t.fontSize = tam; t.color = color; t.alignment = alin;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

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
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, tam, tam), new Vector2(0.5f, 0.5f), 100f);
    }
}
