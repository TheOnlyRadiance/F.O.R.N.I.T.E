using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Estado de sigilo del nivel:
///  - Barra de DETECCIÓN arriba al centro (el enemigo que más te tiene visto).
///  - Avisos ("¡Te escucharon!", ladridos...).
///  - Si te atrapan: bloquea al mapache, muestra "¡ATRAPADO!" y reinicia el nivel.
/// Lo usan los EnemigoVigilante; debe haber uno en la escena.
/// </summary>
public class GestorAlerta : MonoBehaviour
{
    public static GestorAlerta Instancia { get; private set; }

    [SerializeField] private float segundosAntesDeReiniciar = 3.5f;

    public bool JugadorAtrapado { get; private set; }

    private readonly Dictionary<EnemigoVigilante, float> niveles = new();
    private bool algunoPersiguiendo;
    private Image barra, fondoBarra;
    private Text txtEstado, txtMensaje, txtAtrapado, txtMotivo;
    private Image pantallaRoja;
    private float tMensaje, tAtrapado;

    private void Awake()
    {
        Instancia = this;
        ConstruirUI();
    }

    private void OnDestroy() { if (Instancia == this) Instancia = null; }

    public void Reportar(EnemigoVigilante e, float deteccion, bool persiguiendo)
    {
        niveles[e] = deteccion;
        if (persiguiendo) algunoPersiguiendo = true;
    }

    public void Mensaje(string texto, float segundos = 2.5f)
    {
        if (txtMensaje == null || JugadorAtrapado) return;
        txtMensaje.text = texto; tMensaje = segundos;
    }

    public void Atrapar(string motivo)
    {
        if (JugadorAtrapado) return;
        JugadorAtrapado = true;
        tAtrapado = 0f;
        var j = FindAnyObjectByType<ControladorMapache>();
        if (j != null) j.ControlesBloqueados = true;
        SonidosJuego.Instancia?.Atrapado();
        txtMotivo.text = motivo + "\nLa policía viene en camino...";
    }

    private void LateUpdate()
    {
        float max = 0f;
        foreach (var kv in niveles) if (kv.Key != null) max = Mathf.Max(max, kv.Value);

        // barra de detección
        barra.rectTransform.anchorMax = new Vector2(max, 1f);
        barra.color = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.15f, 0.1f), max);
        bool visible = max > 0.01f || algunoPersiguiendo;
        fondoBarra.color = new Color(0f, 0f, 0f, visible ? 0.55f : 0f);
        barra.enabled = visible;
        txtEstado.text = algunoPersiguiendo ? "¡ALERTA! ¡ESCAPA!" : max > 0.6f ? "¡TE ESTÁN VIENDO!" : max > 0.01f ? "SOSPECHA..." : "";
        txtEstado.color = algunoPersiguiendo || max > 0.6f ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.85f, 0.3f);
        algunoPersiguiendo = false;

        // avisos
        if (tMensaje > 0f) tMensaje -= Time.deltaTime;
        var c = txtMensaje.color; c.a = Mathf.Clamp01(tMensaje / 0.5f); txtMensaje.color = c;

        // atrapado
        if (JugadorAtrapado)
        {
            tAtrapado += Time.deltaTime;
            float a = Mathf.Clamp01(tAtrapado / 0.6f);
            pantallaRoja.color = new Color(0.35f, 0f, 0f, 0.55f * a);
            txtAtrapado.color = new Color(1f, 0.25f, 0.2f, a);
            txtMotivo.color = new Color(1f, 1f, 1f, a);
            txtAtrapado.rectTransform.localScale = Vector3.one * (1f + 0.05f * Mathf.Sin(Time.time * 6f));
            if (tAtrapado >= segundosAntesDeReiniciar) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    // ------------------------------------------------------------------ UI

    private void ConstruirUI()
    {
        var fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGO = new GameObject("HUD Alerta", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        var raiz = canvasGO.transform;

        var f = RT("FondoBarra", raiz, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(520f, 18f));
        fondoBarra = f.gameObject.AddComponent<Image>(); fondoBarra.raycastTarget = false;
        var b = RT("Barra", f, Vector2.zero, Vector2.zero, Vector2.zero);
        b.anchorMin = Vector2.zero; b.anchorMax = new Vector2(0f, 1f); b.pivot = new Vector2(0f, 0.5f); b.offsetMin = b.offsetMax = Vector2.zero;
        barra = b.gameObject.AddComponent<Image>(); barra.raycastTarget = false;
        txtEstado = Texto("Estado", raiz, fuente, 28, new Vector2(0.5f, 1f), new Vector2(0f, -75f));
        txtMensaje = Texto("Mensaje", raiz, fuente, 32, new Vector2(0.5f, 1f), new Vector2(0f, -210f));
        txtMensaje.color = new Color(1f, 0.6f, 0.3f, 0f);

        var p = RT("Pantalla", raiz, Vector2.zero, Vector2.zero, Vector2.zero);
        p.anchorMin = Vector2.zero; p.anchorMax = Vector2.one; p.offsetMin = p.offsetMax = Vector2.zero;
        pantallaRoja = p.gameObject.AddComponent<Image>(); pantallaRoja.color = new Color(0, 0, 0, 0); pantallaRoja.raycastTarget = false;
        txtAtrapado = Texto("Atrapado", raiz, fuente, 110, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f));
        txtAtrapado.text = "¡ATRAPADO!"; txtAtrapado.color = new Color(1, 0, 0, 0);
        txtMotivo = Texto("Motivo", raiz, fuente, 36, new Vector2(0.5f, 0.5f), new Vector2(0f, -60f));
        txtMotivo.color = new Color(1, 1, 1, 0);

        foreach (var t in canvasGO.GetComponentsInChildren<Text>())
        {
            t.fontStyle = FontStyle.Bold;
            var s = t.gameObject.AddComponent<Outline>(); s.effectColor = new Color(0, 0, 0, 0.8f); s.effectDistance = new Vector2(2, -2);
        }
    }

    private static RectTransform RT(string n, Transform padre, Vector2 ancla, Vector2 pos, Vector2 tam)
    {
        var go = new GameObject(n, typeof(RectTransform));
        var rt = (RectTransform)go.transform; rt.SetParent(padre, false);
        rt.anchorMin = rt.anchorMax = ancla; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = tam;
        return rt;
    }

    private static Text Texto(string n, Transform padre, Font fuente, int tam, Vector2 ancla, Vector2 pos)
    {
        var rt = RT(n, padre, ancla, pos, new Vector2(1600f, tam * 1.5f));
        var t = rt.gameObject.AddComponent<Text>();
        t.font = fuente; t.fontSize = tam; t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
