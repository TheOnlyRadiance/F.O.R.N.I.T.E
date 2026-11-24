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
    [SerializeField] private int cantidadBasureros = 5;
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
    /// <summary>Modelos del botín (los usa la cinemática de escape para mostrar lo que te llevas).</summary>
    public GameObject ModeloBasurero => modeloBasurero;
    public IReadOnlyList<GameObject> ModelosObjetosCasa => modelosObjetosCasa;
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
    /// <summary>Capas para colocar el botín: todo menos "Ignore Raycast" (ahí están los muros invisibles del borde).</summary>
    private const int Capas = Physics.DefaultRaycastLayers;

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

            // se crea primero (escondido) para medir su tamaño real y apoyarlo por la base
            var c = CrearApoyado(modelosObjetosCasa[idx], nombresObjetosCasa[idx], out float radio, out float alto);
            c.gameObject.SetActive(false);

            bool ok = false;
            for (int intento = 0; intento < 2500 && !ok; intento++)
            {
                var p = new Vector3(Random.Range(casa.min.x + margenMuro, casa.max.x - margenMuro), pisoY + 2.0f,
                                    Random.Range(casa.min.z + margenMuro, casa.max.z - margenMuro));
                if (!Physics.Raycast(p, Vector3.down, out var hit, 3f, Capas, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.point.y - pisoY) > 0.12f || hit.normal.y < 0.9f) continue;          // sobre el piso, no sobre muebles ni escalones
                Vector3 suelo = hit.point;
                if (!Physics.Raycast(suelo + Vector3.up * 0.2f, Vector3.up, 3.2f, Capas, QueryTriggerInteraction.Ignore)) continue; // bajo techo = dentro de la casa
                if (!RodeadoDeParedes(suelo + Vector3.up * 0.5f, 25f)) continue;                         // encerrado por muros (no afuera)
                if (!HuellaLibre(suelo, radio, alto)) continue;                                           // nada encima, debajo ni encimado
                if (!VisibleYAlcanzable(suelo, radio, alto)) continue;                                    // el mapache puede pararse junto y verlo
                if (MuyCerca(suelo, usados, distanciaMinEntreObjetosCasa)) continue;
                if (jugador != null && Vector3.Distance(suelo, jugador.position) < distanciaMinAlJugador) continue;

                usados.Add(suelo);
                c.transform.SetPositionAndRotation(suelo + Vector3.up * 0.01f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                c.gameObject.SetActive(true);
                c.girar = true;
                c.CrearBrillo(Oro, 1.8f, 0.9f, alto + 0.25f);
                objetosCasa.Add(c);
                nombresColocados.Add(c.nombre);
                ok = true;
            }
            if (!ok)
            {
                Debug.LogWarning($"[Botín] No encontré lugar libre para '{nombresObjetosCasa[idx]}'.");
                Destroy(c.gameObject);
            }
        }
    }

    /// <summary>
    /// Crea el objeto dentro de una raíz cuyo origen es el CENTRO DE LA BASE del modelo:
    /// así queda apoyado en el piso, y al girar o crecer (resaltado) no se hunde ni se mete en los muebles.
    /// Devuelve el radio horizontal que barre al girar y su altura.
    /// </summary>
    private Coleccionable CrearApoyado(GameObject prefab, string nombre, out float radio, out float alto)
    {
        var raiz = new GameObject(nombre);
        raiz.transform.SetParent(transform, false);
        raiz.transform.position = new Vector3(0f, -500f, 0f);

        var modelo = Instantiate(prefab, raiz.transform);
        modelo.transform.localPosition = Vector3.zero;
        modelo.transform.localRotation = Quaternion.identity;
        foreach (var col in modelo.GetComponentsInChildren<Collider>()) Destroy(col);   // que no estorbe al mapache

        var rends = modelo.GetComponentsInChildren<Renderer>();
        Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(raiz.transform.position, Vector3.one * 0.4f);
        foreach (var r in rends) b.Encapsulate(r.bounds);
        // centrar en X/Z y apoyar la base en y = 0 de la raíz
        Vector3 desfase = raiz.transform.position - new Vector3(b.center.x, b.min.y, b.center.z);
        modelo.transform.position += desfase;

        const float escalaResaltado = 1.2f;   // crece al resaltarlo
        radio = Mathf.Sqrt(b.extents.x * b.extents.x + b.extents.z * b.extents.z) * escalaResaltado + 0.05f;
        alto = b.size.y * escalaResaltado;

        var c = raiz.AddComponent<Coleccionable>();
        c.tipo = Coleccionable.Tipo.ObjetoCasa; c.nombre = nombre; c.accion = "Tomar";
        return c;
    }

    /// <summary>El círculo que ocupa el objeto (girando) está libre: piso parejo debajo, nada encima y sin tocar muros ni muebles.</summary>
    private static bool HuellaLibre(Vector3 suelo, float radio, float alto)
    {
        // piso al mismo nivel en el centro y alrededor (nada encima: mesas, camas, repisas)
        for (int i = 0; i <= 8; i++)
        {
            Vector3 o = i == 0 ? suelo : suelo + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * radio;
            if (!Physics.Raycast(o + Vector3.up * 1.8f, Vector3.down, out var h, 2.2f, Capas, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(h.point.y - suelo.y) > 0.05f || h.normal.y < 0.9f) return false;
        }
        // el volumen del objeto no toca nada (un poco despegado del piso para no contar el suelo)
        float r = radio + 0.1f;
        Vector3 abajo = suelo + Vector3.up * (r + 0.04f);
        Vector3 arriba = suelo + Vector3.up * Mathf.Max(r + 0.04f, alto + 0.1f - r);
        if (Physics.CheckCapsule(abajo, arriba, r, Capas, QueryTriggerInteraction.Ignore)) return false;
        // espacio libre por encima (que no quede debajo de algo)
        if (Physics.CheckBox(suelo + Vector3.up * (alto + 0.6f), new Vector3(radio, 0.45f, radio), Quaternion.identity, Capas, QueryTriggerInteraction.Ignore)) return false;
        return true;
    }

    /// <summary>Hay un lugar junto al objeto donde cabe el mapache y desde ahí se ve sin obstáculos.</summary>
    private static bool VisibleYAlcanzable(Vector3 suelo, float radio, float alto)
    {
        Vector3 centro = suelo + Vector3.up * Mathf.Max(0.15f, alto * 0.5f);
        float dist = radio + 0.55f;
        int libres = 0;
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            Vector3 pie = suelo + dir * dist;
            if (!Physics.Raycast(pie + Vector3.up * 0.6f, Vector3.down, out var h, 1f, Capas, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(h.point.y - suelo.y) > 0.1f) continue;                                                       // mismo piso
            if (Physics.CheckCapsule(h.point + Vector3.up * 0.32f, h.point + Vector3.up * 0.9f, 0.27f, Capas, QueryTriggerInteraction.Ignore)) continue; // cabe el mapache
            if (Physics.Linecast(h.point + Vector3.up * 0.7f, centro, Capas, QueryTriggerInteraction.Ignore)) continue;  // se ve el objeto
            libres++;
        }
        return libres >= 2;
    }

    private void ColocarBasureros(Bounds mapa, Bounds casa)
    {
        if (modeloBasurero == null) return;
        float sueloY = casa.min.y;
        Bounds casaAmpliada = casa; casaAmpliada.Expand(new Vector3(8f, 100f, 8f));   // +4 m por lado
        var usados = new List<Vector3>();
        float alto = mapa.max.y + 50f;
        // bien adentro del vecindario: lejos de la cerca perimetral y del muro invisible
        float margen = limitePorMarcadores ? 3f : margenBordeMapa + 3f;
        bool hayNavMesh = UnityEngine.AI.NavMesh.SamplePosition(jugador != null ? jugador.position : casa.center, out _, 5f, UnityEngine.AI.NavMesh.AllAreas);

        for (int intento = 0; intento < 8000 && basureros.Count < cantidadBasureros; intento++)
        {
            var p = new Vector3(Random.Range(mapa.min.x + margen, mapa.max.x - margen), alto,
                                Random.Range(mapa.min.z + margen, mapa.max.z - margen));
            if (casaAmpliada.Contains(new Vector3(p.x, casa.center.y, p.z))) continue;                  // fuera de la casa
            if (!Physics.Raycast(p, Vector3.down, out var hit, alto - mapa.min.y + 60f, Capas, QueryTriggerInteraction.Ignore)) continue;
            if (hit.point.y > sueloY + 0.4f || hit.normal.y < 0.9f) continue;                             // suelo, no techos ni árboles
            float altura = hit.point.y - sueloY;
            if (altura > alturaCalleMin && altura < alturaCalleMax) continue;                              // no en medio de la calle
            if (Physics.CheckSphere(hit.point + Vector3.up * 0.85f, 0.6f, Capas, QueryTriggerInteraction.Ignore)) continue; // espacio libre
            if (MuyCerca(hit.point, usados, distanciaMinEntreBasureros)) continue;
            if (jugador != null && Vector3.Distance(hit.point, jugador.position) < distanciaMinAlJugador) continue;
            if (!BuscarApoyo(hit.point, out Vector3 haciaPared, out float distPared)) continue;           // junto a un muro, cerca o árbol

            // acercarlo hasta quedar a ~60 cm del apoyo, comprobando que siga habiendo suelo
            Vector3 junto = hit.point + haciaPared * Mathf.Max(0f, distPared - 0.6f);
            if (!Physics.Raycast(junto + Vector3.up * 1.5f, Vector3.down, out var suelo2, 2.5f, Capas, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(suelo2.point.y - hit.point.y) > 0.15f || suelo2.normal.y < 0.9f) continue;
            if (Physics.CheckSphere(suelo2.point + Vector3.up * 0.7f, 0.3f, Capas, QueryTriggerInteraction.Ignore)) continue;
            if (!AlcanzableCaminando(suelo2.point, hayNavMesh)) continue;                                  // dentro del vecindario, se llega a pie

            bool tirado = Random.value < probabilidadTirado;
            if (tirado && !EspacioParaTirado(suelo2.point, -haciaPared)) tirado = false;

            // se crea y se revisa con su tamaño real: que no se encime con nada y que se vea
            var c = CrearBasurero(suelo2.point, haciaPared, tirado);
            if (!BasureroBienColocado(c, suelo2.point.y) && !(tirado && ReintentarParado(ref c, suelo2.point, haciaPared)))
            {
                c.gameObject.SetActive(false);
                Destroy(c.gameObject);
                continue;
            }
            c.GetComponentInChildren<CapsuleCollider>().enabled = true;
            c.GetComponentInChildren<UnityEngine.AI.NavMeshObstacle>().enabled = true;
            usados.Add(suelo2.point);
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
            if (Physics.Raycast(o, d, out var h, 2.4f, Capas, QueryTriggerInteraction.Ignore) && Mathf.Abs(h.normal.y) < 0.5f && h.distance < mejor)
            { mejor = h.distance; haciaPared = d; hay = true; }
        }
        distancia = mejor;
        return hay;
    }

    /// <summary>Con su tamaño real: no se encima con muros/cercas/árboles, está apoyado en el suelo y el mapache lo puede ver.</summary>
    private static bool BasureroBienColocado(Coleccionable c, float sueloY)
    {
        var rends = c.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return false;
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);

        float baseY = sueloY + 0.06f;                  // despegado del piso para no contar el suelo
        float altoLibre = Mathf.Max(0.1f, b.max.y - baseY);
        var centro = new Vector3(b.center.x, baseY + altoLibre * 0.5f + 0.05f, b.center.z);
        var mitad = new Vector3(b.extents.x + 0.05f, altoLibre * 0.5f + 0.05f, b.extents.z + 0.05f);
        if (Physics.CheckBox(centro, mitad, Quaternion.identity, Capas, QueryTriggerInteraction.Ignore)) return false;

        // suelo parejo debajo de toda la huella (no colgando de un escalón o una banqueta)
        for (int i = 0; i < 4; i++)
        {
            var esquina = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, b.max.y + 0.3f, i < 2 ? b.min.z : b.max.z);
            if (!Physics.Raycast(esquina, Vector3.down, out var h, b.size.y + 1f, Capas, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(h.point.y - sueloY) > 0.15f) return false;
        }

        return VisibleYAlcanzable(new Vector3(b.center.x, sueloY, b.center.z), Mathf.Max(b.extents.x, b.extents.z), b.size.y);
    }

    /// <summary>Si el bote tirado no cabe, se prueba parado en el mismo lugar.</summary>
    private bool ReintentarParado(ref Coleccionable c, Vector3 suelo, Vector3 haciaPared)
    {
        c.gameObject.SetActive(false);
        Destroy(c.gameObject);
        c = CrearBasurero(suelo, haciaPared, false);
        return BasureroBienColocado(c, suelo.y);
    }

    /// <summary>Se puede llegar caminando desde donde empieza el mapache (descarta patios cerrados y zonas fuera del vecindario).</summary>
    private bool AlcanzableCaminando(Vector3 p, bool hayNavMesh)
    {
        if (!hayNavMesh || jugador == null) return true;
        if (!UnityEngine.AI.NavMesh.SamplePosition(p, out var destino, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) return false;
        if (!UnityEngine.AI.NavMesh.SamplePosition(jugador.position, out var origen, 3f, UnityEngine.AI.NavMesh.AllAreas)) return true;
        var camino = new UnityEngine.AI.NavMeshPath();
        return UnityEngine.AI.NavMesh.CalculatePath(origen.position, destino.position, UnityEngine.AI.NavMesh.AllAreas, camino)
               && camino.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
    }

    /// <summary>¿Hay lugar en el suelo para que el bote quede acostado hacia 'dir'?</summary>
    private static bool EspacioParaTirado(Vector3 suelo, Vector3 dir)
    {
        return !Physics.Raycast(suelo + Vector3.up * 0.35f, dir, 1.3f, Capas, QueryTriggerInteraction.Ignore);
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

        foreach (var otro in modelo.GetComponentsInChildren<Collider>()) Destroy(otro);
        // colisión propia (para no atravesarlo); se activa solo cuando el lugar ya se validó
        var col = modelo.AddComponent<CapsuleCollider>();
        col.radius = 0.33f; col.height = 1.0f; col.center = new Vector3(0f, 0.5f, 0f); col.direction = 1;
        col.enabled = false;
        var obstaculo = modelo.AddComponent<UnityEngine.AI.NavMeshObstacle>();
        obstaculo.shape = UnityEngine.AI.NavMeshObstacleShape.Capsule;
        obstaculo.radius = 0.35f; obstaculo.height = 1f; obstaculo.center = new Vector3(0f, 0.5f, 0f); obstaculo.carving = true;
        obstaculo.enabled = false;

        var c = raiz.AddComponent<Coleccionable>();
        c.tipo = Coleccionable.Tipo.BasureroDorado; c.nombre = "Basurero dorado"; c.accion = tirado ? "Esculcar" : "Revisar";
        c.CrearBrillo(Oro, 3.5f, 1.4f, tirado ? 0.6f : 1.1f);
        return c;
    }

    /// <summary>True si en las 4 direcciones horizontales hay un muro a menos de 'dist' (el punto está dentro de un edificio).</summary>
    private static bool RodeadoDeParedes(Vector3 p, float dist)
    {
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        foreach (var d in dirs)
            if (!Physics.Raycast(p, d, dist, Capas, QueryTriggerInteraction.Ignore)) return false;
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
        if (MenuPausa.Pausado) return;
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
