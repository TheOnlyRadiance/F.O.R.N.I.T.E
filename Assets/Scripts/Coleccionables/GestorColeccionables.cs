using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Búsqueda del botín:
///  - Coloca BASUREROS DORADOS en puntos aleatorios del vecindario (siempre en el suelo: nunca sobre techos,
///    árboles, cercas ni dentro de la casa).
///  - Coloca 4 OBJETOS dentro de la casa grande (2 por planta), aleatorios y sobre el piso
///    (nunca encima de muebles, debajo de mesas ni en la escalera).
///  - Cuando estás cerca de uno aparece "[E] ...": pulsa E (o Y/triángulo en mando) para recogerlo.
///  - Muestra en pantalla el contador y la lista de objetos de la casa.
/// Las posiciones cambian en cada partida.
/// </summary>
public class GestorColeccionables : MonoBehaviour
{
    [Header("Modelos")]
    [SerializeField] private GameObject modeloBasurero;
    [SerializeField] private GameObject[] modelosObjetosCasa = new GameObject[4];
    [SerializeField] private string[] nombresObjetosCasa = { "Cofre de joyas", "Alcancía de cerdito", "Reloj de oro", "Diamante gigante" };

    [Header("Cantidades y separación")]
    [SerializeField] private int cantidadBasureros = 8;
    [SerializeField] private float distanciaMinEntreBasureros = 12f;
    [SerializeField] private float distanciaMinEntreObjetosCasa = 4f;
    [SerializeField] private float distanciaMinAlJugador = 5f;
    [SerializeField] private float margenBordeMapa = 4f;
    [Tooltip("Porcentaje de basureros que aparecen tirados de lado (0-1).")]
    [Range(0f, 1f)] [SerializeField] private float probabilidadTirado = 0.35f;
    [Tooltip("Alturas sobre el pasto que corresponden al asfalto de la calle (ahí no se ponen basureros).")]
    [SerializeField] private float alturaCalleMin = 0.15f, alturaCalleMax = 0.3f;

    [Header("Referencias del mundo (por nombre)")]
    [SerializeField] private string nombreEstructuraCasa = "Casa_Estructura";
    [SerializeField] private string prefijoBordes = "ColBorde";
    [Tooltip("Altura de cada planta sobre la base de la casa (planta baja, planta alta), en metros.")]
    [SerializeField] private float[] alturaPlantas = { 0.11f, 2.95f };

    [Header("Interacción")]
    [SerializeField] private float radioInteraccion = 1.7f;

    public int BasurerosEncontrados { get; private set; }
    public int ObjetosCasaEncontrados { get; private set; }
    public int TotalBasureros => basureros.Count;
    public int TotalObjetosCasa => objetosCasa.Count;
    public event System.Action<Coleccionable> AlRecoger;
    public event System.Action AlCompletarTodo;

    private readonly List<Coleccionable> basureros = new();
    private readonly List<Coleccionable> objetosCasa = new();
    private readonly List<string> nombresColocados = new();     // se guardan aparte: el objeto se destruye al recogerlo
    private readonly HashSet<string> encontrados = new();
    private Transform jugador;
    private Coleccionable cercano;
    private InputAction accionInteractuar;

    // UI
    private Text txtBasureros, txtObjetos, txtLista, txtPrompt, txtAviso, txtFinal;
    private GameObject panelPrompt;
    private float tAviso, tFinal;

    private static readonly Color Oro = new Color(1f, 0.78f, 0.25f);

    // ------------------------------------------------------------------ inicio

    private void Awake()
    {
        accionInteractuar = new InputAction("Interactuar", InputActionType.Button, "<Keyboard>/e");
        accionInteractuar.AddBinding("<Gamepad>/buttonNorth");
    }

    private void OnEnable() => accionInteractuar.Enable();
    private void OnDisable() => accionInteractuar.Disable();
    private void OnDestroy() => accionInteractuar.Dispose();

    private void Start()
    {
        var ctrl = FindAnyObjectByType<ControladorMapache>();
        if (ctrl != null) jugador = ctrl.transform;

        var casaGO = GameObject.Find(nombreEstructuraCasa);
        var casaRend = casaGO != null ? casaGO.GetComponent<Renderer>() : null;
        if (casaRend == null)
        {
            Debug.LogError($"[Botín] No encontré '{nombreEstructuraCasa}' en la escena; no puedo colocar los objetos.");
            return;
        }
        Bounds casa = casaRend.bounds;
        Bounds mapa = CalcularLimitesMapa(casaGO.transform.root, casa);

        ColocarObjetosCasa(casa);
        ColocarBasureros(mapa, casa);
        ConstruirUI();
        ActualizarContadores();
        Mostrar($"Encuentra {TotalBasureros} basureros dorados y {TotalObjetosCasa} objetos en la casa", 4f);
    }

    // ------------------------------------------------------------------ colocación aleatoria

    private bool limitePorMarcadores;

    private Bounds CalcularLimitesMapa(Transform raizMundo, Bounds casa)
    {
        // 1) área de juego marcada dentro de la cerca perimetral
        var mMin = GameObject.Find("LimiteJuego_Min");
        var mMax = GameObject.Find("LimiteJuego_Max");
        if (mMin != null && mMax != null)
        {
            var area = new Bounds(mMin.transform.position, Vector3.zero);
            area.Encapsulate(mMax.transform.position);
            area.Expand(new Vector3(0f, 20f, 0f));
            limitePorMarcadores = true;
            return area;
        }

        // 2) respaldo: paredes invisibles del borde
        bool hay = false;
        Bounds b = new Bounds();
        foreach (var c in FindObjectsByType<Collider>())
        {
            if (!c.name.StartsWith(prefijoBordes)) continue;
            if (!hay) { b = c.bounds; hay = true; } else b.Encapsulate(c.bounds);
        }
        if (!hay)
        {
            foreach (var r in raizMundo.GetComponentsInChildren<Renderer>())
                if (!hay) { b = r.bounds; hay = true; } else b.Encapsulate(r.bounds);
        }
        if (!hay) b = new Bounds(casa.center, casa.size * 4f);
        return b;
    }

    private void ColocarObjetosCasa(Bounds casa)
    {
        int n = Mathf.Min(modelosObjetosCasa.Length, nombresObjetosCasa.Length);
        // 2 objetos por planta, en orden aleatorio
        var plantas = new List<int>();
        for (int i = 0; i < n; i++) plantas.Add(i % alturaPlantas.Length);
        Barajar(plantas);
        var orden = new List<int>(); for (int i = 0; i < n; i++) orden.Add(i);
        Barajar(orden);

        var usados = new List<Vector3>();
        float baseY = casa.min.y;
        const float margenMuro = 1.3f;   // el volado del tejado hace que los bounds sean más grandes que los muros

        for (int k = 0; k < n; k++)
        {
            int idx = orden[k];
            if (modelosObjetosCasa[idx] == null) continue;
            float pisoY = baseY + alturaPlantas[plantas[k]];

            bool ok = false;
            for (int intento = 0; intento < 1500 && !ok; intento++)
            {
                var p = new Vector3(Random.Range(casa.min.x + margenMuro, casa.max.x - margenMuro), pisoY + 2.0f,
                                    Random.Range(casa.min.z + margenMuro, casa.max.z - margenMuro));
                if (!Physics.Raycast(p, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.point.y - pisoY) > 0.12f || hit.normal.y < 0.9f) continue;          // sobre el piso, no sobre muebles ni escalones
                if (!Physics.Raycast(hit.point + Vector3.up * 0.2f, Vector3.up, 3.2f, ~0, QueryTriggerInteraction.Ignore)) continue; // bajo techo = dentro de la casa
                if (!RodeadoDeParedes(hit.point + Vector3.up * 0.5f, 25f)) continue;                     // encerrado por muros (no afuera)
                if (Physics.CheckSphere(hit.point + Vector3.up * 0.45f, 0.35f, ~0, QueryTriggerInteraction.Ignore)) continue;      // lejos de muros y muebles
                if (Physics.CheckSphere(hit.point + Vector3.up * 1.1f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;        // nada encima (no debajo de mesas)
                if (MuyCerca(hit.point, usados, distanciaMinEntreObjetosCasa)) continue;
                if (jugador != null && Vector3.Distance(hit.point, jugador.position) < distanciaMinAlJugador) continue;

                usados.Add(hit.point);
                var c = Crear(modelosObjetosCasa[idx], hit.point, Coleccionable.Tipo.ObjetoCasa, nombresObjetosCasa[idx], "Tomar");
                c.girar = true;
                c.CrearBrillo(Oro, 1.8f, 0.9f, 0.35f);
                objetosCasa.Add(c);
                nombresColocados.Add(c.nombre);
                ok = true;
            }
            if (!ok) Debug.LogWarning($"[Botín] No encontré lugar libre para '{nombresObjetosCasa[idx]}'.");
        }
    }

    private void ColocarBasureros(Bounds mapa, Bounds casa)
    {
        if (modeloBasurero == null) return;
        float sueloY = casa.min.y;
        Bounds casaAmpliada = casa; casaAmpliada.Expand(new Vector3(8f, 100f, 8f));   // +4 m por lado
        var usados = new List<Vector3>();
        float alto = mapa.max.y + 50f;
        float margen = limitePorMarcadores ? 1f : margenBordeMapa;   // los marcadores ya están 2.5 m dentro de la cerca

        for (int intento = 0; intento < 6000 && basureros.Count < cantidadBasureros; intento++)
        {
            var p = new Vector3(Random.Range(mapa.min.x + margen, mapa.max.x - margen), alto,
                                Random.Range(mapa.min.z + margen, mapa.max.z - margen));
            if (casaAmpliada.Contains(new Vector3(p.x, casa.center.y, p.z))) continue;                  // fuera de la casa
            if (!Physics.Raycast(p, Vector3.down, out var hit, alto - mapa.min.y + 60f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (hit.point.y > sueloY + 0.4f || hit.normal.y < 0.9f) continue;                             // suelo, no techos ni árboles
            float altura = hit.point.y - sueloY;
            if (altura > alturaCalleMin && altura < alturaCalleMax) continue;                              // no en medio de la calle
            if (Physics.CheckSphere(hit.point + Vector3.up * 0.85f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue; // espacio libre
            if (MuyCerca(hit.point, usados, distanciaMinEntreBasureros)) continue;
            if (jugador != null && Vector3.Distance(hit.point, jugador.position) < distanciaMinAlJugador) continue;
            if (!BuscarApoyo(hit.point, out Vector3 haciaPared, out float distPared)) continue;           // junto a un muro, cerca o árbol

            // acercarlo hasta quedar a ~45 cm del apoyo, comprobando que siga habiendo suelo
            Vector3 junto = hit.point + haciaPared * Mathf.Max(0f, distPared - 0.45f);
            if (!Physics.Raycast(junto + Vector3.up * 1.5f, Vector3.down, out var suelo2, 2.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(suelo2.point.y - hit.point.y) > 0.15f || suelo2.normal.y < 0.9f) continue;
            if (Physics.CheckSphere(suelo2.point + Vector3.up * 0.7f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;

            bool tirado = Random.value < probabilidadTirado;
            if (tirado && !EspacioParaTirado(suelo2.point, -haciaPared)) tirado = false;

            usados.Add(suelo2.point);
            var c = CrearBasurero(suelo2.point, haciaPared, tirado);
            basureros.Add(c);
        }
        if (basureros.Count < cantidadBasureros)
            Debug.LogWarning($"[Botín] Solo cupieron {basureros.Count} de {cantidadBasureros} basureros.");
    }

    /// <summary>Busca un muro/cerca/árbol cerca (como un bote de basura real) y devuelve la dirección hacia él.</summary>
    private static bool BuscarApoyo(Vector3 suelo, out Vector3 haciaPared, out float distancia)
    {
        haciaPared = Vector3.forward;
        distancia = 0f;
        float mejor = float.MaxValue; bool hay = false;
        Vector3 o = suelo + Vector3.up * 0.6f;
        for (int i = 0; i < 12; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward;
            if (Physics.Raycast(o, d, out var h, 2.4f, ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(h.normal.y) < 0.5f && h.distance < mejor)
            { mejor = h.distance; haciaPared = d; hay = true; }
        }
        distancia = mejor;
        return hay;
    }

    /// <summary>¿Hay lugar en el suelo para que el bote quede acostado hacia 'dir'?</summary>
    private static bool EspacioParaTirado(Vector3 suelo, Vector3 dir)
    {
        return !Physics.Raycast(suelo + Vector3.up * 0.35f, dir, 1.3f, ~0, QueryTriggerInteraction.Ignore);
    }

    /// <summary>Crea un basurero dorado parado (de espaldas al muro) o tirado de lado sobre el suelo.</summary>
    private Coleccionable CrearBasurero(Vector3 suelo, Vector3 haciaPared, bool tirado)
    {
        var raiz = new GameObject("Basurero dorado");
        raiz.transform.SetParent(transform, false);
        raiz.transform.position = suelo;
        Vector3 afuera = -haciaPared;
        raiz.transform.rotation = Quaternion.LookRotation(afuera, Vector3.up);   // el emblema mira hacia afuera del muro

        var modelo = Instantiate(modeloBasurero, raiz.transform);
        modelo.transform.localPosition = Vector3.zero;
        modelo.transform.localRotation = Quaternion.identity;
        if (tirado)
        {
            // acostado: la base hacia el muro y la tapa hacia afuera, con un pequeño giro aleatorio
            float giro = Random.Range(-35f, 35f);
            modelo.transform.localRotation = Quaternion.Euler(0f, giro, 0f) * Quaternion.Euler(90f, 0f, 0f);
            modelo.transform.localPosition = Vector3.up * 0.33f + Quaternion.Euler(0f, giro, 0f) * Vector3.forward * 0.05f;
        }
        else
        {
            modelo.transform.localPosition = Vector3.zero;
            modelo.transform.localRotation = Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f);
        }

        var col = modelo.AddComponent<CapsuleCollider>();
        col.radius = 0.33f; col.height = 1.0f; col.center = new Vector3(0f, 0.5f, 0f); col.direction = 1;
        var obstaculo = modelo.AddComponent<UnityEngine.AI.NavMeshObstacle>();
        obstaculo.shape = UnityEngine.AI.NavMeshObstacleShape.Capsule;
        obstaculo.radius = 0.35f; obstaculo.height = 1f; obstaculo.center = new Vector3(0f, 0.5f, 0f); obstaculo.carving = true;

        var c = raiz.AddComponent<Coleccionable>();
        c.tipo = Coleccionable.Tipo.BasureroDorado; c.nombre = "Basurero dorado"; c.accion = tirado ? "Esculcar" : "Revisar";
        c.CrearBrillo(Oro, 3.5f, 1.4f, tirado ? 0.6f : 1.1f);
        return c;
    }

    private Coleccionable Crear(GameObject modelo, Vector3 pos, Coleccionable.Tipo tipo, string nombre, string accion)
    {
        var go = Instantiate(modelo, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
        go.name = nombre;
        var c = go.AddComponent<Coleccionable>();
        c.tipo = tipo; c.nombre = nombre; c.accion = accion;
        return c;
    }

    /// <summary>True si en las 4 direcciones horizontales hay un muro a menos de 'dist' (el punto está dentro de un edificio).</summary>
    private static bool RodeadoDeParedes(Vector3 p, float dist)
    {
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        foreach (var d in dirs)
            if (!Physics.Raycast(p, d, dist, ~0, QueryTriggerInteraction.Ignore)) return false;
        return true;
    }

    private static bool MuyCerca(Vector3 p, List<Vector3> otros, float dist)
    {
        foreach (var o in otros) if ((o - p).sqrMagnitude < dist * dist) return true;
        return false;
    }

    private static void Barajar<T>(List<T> l)
    {
        for (int i = l.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (l[i], l[j]) = (l[j], l[i]); }
    }

    // ------------------------------------------------------------------ interacción con E

    private void Update()
    {
        if (jugador == null)
        {
            var ctrl = FindAnyObjectByType<ControladorMapache>();
            if (ctrl == null) return;
            jugador = ctrl.transform;
        }

        // el más cercano dentro del radio (misma planta: diferencia de altura pequeña)
        Coleccionable mejor = null; float mejorD = radioInteraccion * radioInteraccion;
        Vector3 pj = jugador.position;
        foreach (var lista in new[] { basureros, objetosCasa })
            foreach (var c in lista)
            {
                if (c == null || c.Recogido) continue;
                Vector3 d = c.transform.position - pj;
                if (Mathf.Abs(d.y) > 1.2f) continue;
                d.y = 0f;
                if (d.sqrMagnitude < mejorD) { mejorD = d.sqrMagnitude; mejor = c; }
            }

        if (mejor != cercano)
        {
            if (cercano != null) cercano.Resaltar(false);
            cercano = mejor;
            if (cercano != null) cercano.Resaltar(true);
        }

        if (panelPrompt != null)
        {
            panelPrompt.SetActive(cercano != null);
            if (cercano != null) txtPrompt.text = $"{cercano.accion} {cercano.nombre.ToLower()}";
        }

        if (cercano != null && accionInteractuar.WasPressedThisFrame()) Recoger(cercano);

        ActualizarAvisos();
    }

    private void Recoger(Coleccionable c)
    {
        c.Recoger();
        cercano = null;
        if (c.tipo == Coleccionable.Tipo.BasureroDorado)
        {
            BasurerosEncontrados++;
            Mostrar($"¡Basurero dorado!  {BasurerosEncontrados}/{TotalBasureros}", 2.2f);
        }
        else
        {
            ObjetosCasaEncontrados++;
            encontrados.Add(c.nombre);
            Mostrar($"¡Encontraste: {c.nombre}!  {ObjetosCasaEncontrados}/{TotalObjetosCasa}", 2.5f);
        }
        ActualizarContadores();
        AlRecoger?.Invoke(c);

        if (BasurerosEncontrados >= TotalBasureros && ObjetosCasaEncontrados >= TotalObjetosCasa)
        {
            tFinal = 6f;
            AlCompletarTodo?.Invoke();
        }
    }

    // ------------------------------------------------------------------ UI

    private void ConstruirUI()
    {
        var fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGO = new GameObject("HUD Botín", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 11;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var raiz = canvasGO.transform;

        // panel superior derecho
        var panel = Panel("Contador", raiz, new Vector2(1, 1), new Vector2(-30, -30), new Vector2(430, 250), new Color(0.04f, 0.06f, 0.14f, 0.7f));
        Texto("Titulo", panel, fuente, "BOTÍN", 30, Oro, new Vector2(20, -14), new Vector2(390, 40), TextAnchor.UpperLeft).fontStyle = FontStyle.Bold;
        txtBasureros = Texto("Basureros", panel, fuente, "", 26, Color.white, new Vector2(20, -60), new Vector2(390, 34), TextAnchor.UpperLeft);
        txtObjetos = Texto("Objetos", panel, fuente, "", 26, Color.white, new Vector2(20, -96), new Vector2(390, 34), TextAnchor.UpperLeft);
        txtLista = Texto("Lista", panel, fuente, "", 22, Color.white, new Vector2(34, -134), new Vector2(380, 110), TextAnchor.UpperLeft);
        txtLista.supportRichText = true;
        txtLista.lineSpacing = 1.05f;

        // aviso para interactuar (abajo al centro)
        var p = Panel("Prompt", raiz, new Vector2(0.5f, 0), new Vector2(0, 170), new Vector2(560, 70), new Color(0.04f, 0.06f, 0.14f, 0.78f), centrado: true);
        panelPrompt = p.gameObject;
        var tecla = Panel("Tecla", p, new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(50, 50), Oro, centrado: false, pivote: new Vector2(0, 0.5f));
        Texto("E", tecla, fuente, "E", 32, new Color(0.1f, 0.08f, 0.05f), Vector2.zero, new Vector2(50, 50), TextAnchor.MiddleCenter, centrado: true).fontStyle = FontStyle.Bold;
        txtPrompt = Texto("Accion", p, fuente, "", 28, Color.white, new Vector2(84, 0), new Vector2(460, 60), TextAnchor.MiddleLeft, pivote: new Vector2(0, 0.5f), ancla: new Vector2(0, 0.5f));
        panelPrompt.SetActive(false);

        // avisos temporales y mensaje final
        txtAviso = Texto("Aviso", raiz, fuente, "", 38, Oro, new Vector2(0, -150), new Vector2(1400, 60), TextAnchor.MiddleCenter, centrado: true, ancla: new Vector2(0.5f, 1));
        txtAviso.fontStyle = FontStyle.Bold;
        txtFinal = Texto("Final", raiz, fuente, "", 72, Oro, Vector2.zero, new Vector2(1600, 200), TextAnchor.MiddleCenter, centrado: true, ancla: new Vector2(0.5f, 0.5f));
        txtFinal.fontStyle = FontStyle.Bold;

        foreach (var t in canvasGO.GetComponentsInChildren<Text>())
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, 0.7f);
            s.effectDistance = new Vector2(2, -2);
        }
    }

    private void ActualizarContadores()
    {
        if (txtBasureros == null) return;
        txtBasureros.text = $"Basureros dorados   {BasurerosEncontrados} / {TotalBasureros}";
        txtObjetos.text = $"Objetos de la casa  {ObjetosCasaEncontrados} / {TotalObjetosCasa}";
        txtBasureros.color = BasurerosEncontrados >= TotalBasureros ? Oro : Color.white;
        txtObjetos.color = ObjetosCasaEncontrados >= TotalObjetosCasa ? Oro : Color.white;

        var sb = new System.Text.StringBuilder();
        foreach (var nombre in nombresColocados)
        {
            bool hecho = encontrados.Contains(nombre);
            sb.AppendLine(hecho ? $"<color=#FFC740>[X] {nombre}</color>" : $"<color=#BBBBBB>[  ] {nombre}</color>");
        }
        txtLista.text = sb.ToString();
    }

    private void Mostrar(string texto, float segundos)
    {
        if (txtAviso == null) return;
        txtAviso.text = texto;
        tAviso = segundos;
    }

    private void ActualizarAvisos()
    {
        if (txtAviso == null) return;
        if (tAviso > 0f) tAviso -= Time.deltaTime;
        var c = txtAviso.color; c.a = Mathf.Clamp01(tAviso / 0.5f); txtAviso.color = c;

        if (tFinal > 0f)
        {
            tFinal -= Time.deltaTime;
            txtFinal.text = "¡BOTÍN COMPLETO!";
            float a = Mathf.Clamp01(tFinal / 1f);
            txtFinal.color = new Color(Oro.r, Oro.g, Oro.b, a);
            txtFinal.rectTransform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(Time.time * 5f));
        }
        else if (txtFinal.text.Length > 0) txtFinal.text = "";
    }

    private static RectTransform Panel(string nombre, Transform padre, Vector2 ancla, Vector2 pos, Vector2 tam, Color color,
                                       bool centrado = false, Vector2? pivote = null)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(padre, false);
        rt.anchorMin = rt.anchorMax = ancla;
        rt.pivot = pivote ?? (centrado ? new Vector2(0.5f, 0.5f) : ancla);
        rt.anchoredPosition = pos;
        rt.sizeDelta = tam;
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    private static Text Texto(string nombre, Transform padre, Font fuente, string texto, int tam, Color color, Vector2 pos, Vector2 caja,
                              TextAnchor alineacion, bool centrado = false, Vector2? pivote = null, Vector2? ancla = null)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(padre, false);
        rt.anchorMin = rt.anchorMax = ancla ?? (centrado ? new Vector2(0.5f, 0.5f) : new Vector2(0, 1));
        rt.pivot = pivote ?? (centrado ? new Vector2(0.5f, 0.5f) : new Vector2(0, 1));
        rt.anchoredPosition = pos;
        rt.sizeDelta = caja;
        var t = go.GetComponent<Text>();
        t.font = fuente; t.text = texto; t.fontSize = tam; t.color = color; t.alignment = alineacion;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
