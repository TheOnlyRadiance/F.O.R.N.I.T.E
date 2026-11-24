using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Flujo de la partida en el Nivel1 (se crea solo en cualquier escena que tenga un ControladorMapache):
///  - SPAWN: coloca al mapache afuera, en la calle/jardín, lejos de la casa y de las rutas de los guardias.
///  - LÍMITES: no deja salir del vecindario y, si el mapache cae a un vacío, lo regresa al último lugar seguro.
///  - ESCAPE: al recoger todo el botín aparece una ZONA DE ESCAPE en una orilla del mapa y empieza
///            una cuenta regresiva. Llega a la zona antes de que llegue a cero para ganar.
///  - Crea el menú de pausa (Esc).
/// </summary>
public class GestorPartida : MonoBehaviour
{
    [Header("Escape")]
    [Tooltip("Segundos para llegar a la zona de escape después de recoger todo el botín.")]
    [SerializeField] private float segundosParaEscapar = 120f;
    [SerializeField] private float radioZonaEscape = 2.6f;
    [Tooltip("Qué tan dentro de la orilla del área de juego se coloca la zona de escape (m).")]
    [SerializeField] private float margenOrilla = 1.5f;
    [SerializeField] private Color colorZona = new Color(0.25f, 1f, 0.45f);
    [SerializeField] private float segundosAntesDeVolverAlMenu = 5f;

    [Header("Spawn")]
    [Tooltip("Distancia ideal entre el spawn y la casa grande (m).")]
    [SerializeField] private float distanciaSpawnCasa = 22f;
    [Tooltip("Distancia mínima del spawn a guardias, sus rutas y la zona del perro (m).")]
    [SerializeField] private float distanciaSpawnEnemigos = 16f;

    [Header("Límites del mapa")]
    [Tooltip("Si el mapache cae esta cantidad de metros por debajo del suelo, se le regresa a un lugar seguro.")]
    [SerializeField] private float caidaMaxima = 6f;

    public enum Estado { Buscando, Escapando, Escapado, Perdido }
    public Estado EstadoActual { get; private set; } = Estado.Buscando;
    public float TiempoRestante { get; private set; }

    private ControladorMapache jugador;
    private Bounds area;            // donde puede estar el mapache
    private Bounds areaInterior;    // donde se buscan spawn y zona de escape
    private bool hayArea;
    private float sueloY;
    private Bounds casa;
    private bool hayCasa;
    private Vector3 frenteCasa;     // dirección (horizontal) de la puerta principal
    private Vector3 puertaCasa;
    private bool hayNavMesh;

    private Vector3 posEscape;
    private Vector3 posSpawn;
    private Vector3 ultimaPosSegura;
    private float tGuardarSegura, tAvisoLimite;

    // visual de la zona
    private GameObject zona;
    private Material matAnillo, matHaz;
    private Light luzZona;

    // UI
    private GameObject panelEscape;
    private Text txtTiempo, txtDistancia, txtFlecha, txtAnuncio, txtVictoria, txtVictoriaSub;
    private Image capaVictoria;
    private float tAnuncio, tVictoria;
    private bool sonoUltimos10;
    private CinematicaEscape cinematica;

    private static readonly Color Oro = new Color(1f, 0.78f, 0.25f);

    // ------------------------------------------------------------------ creación automática

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Registrar()
    {
        SceneManager.sceneLoaded -= AlCargarEscena;
        SceneManager.sceneLoaded += AlCargarEscena;
    }

    // sceneLoaded llega después de Awake y antes de Start: el spawn se mueve antes de que se coloque el botín.
    private static void AlCargarEscena(Scene escena, LoadSceneMode modo)
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (FindAnyObjectByType<GestorPartida>() != null) return;
        if (FindAnyObjectByType<ControladorMapache>() == null) return;   // solo en niveles jugables
        new GameObject("Partida").AddComponent<GestorPartida>();
    }

    // ------------------------------------------------------------------ inicio

    private void Awake()
    {
        jugador = FindAnyObjectByType<ControladorMapache>();
        if (jugador == null) { enabled = false; return; }

        Physics.SyncTransforms();
        CalcularMundo();

        var cc = jugador.GetComponent<CharacterController>();
        bool ccActivo = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;     // que el propio mapache no estorbe en las pruebas de espacio libre
        try
        {
            ColocarSpawn();
            ElegirZonaEscape();
        }
        finally { if (cc != null) cc.enabled = ccActivo; }

        CrearMurosInvisibles();
        ultimaPosSegura = jugador.transform.position;
        gameObject.AddComponent<MenuPausa>();
        ConstruirUI();
        gameObject.AddComponent<PantallaControles>();   // "cómo jugar": congela el juego hasta que el jugador empieza
    }

    private GestorColeccionables botin;

    private void Start()
    {
        botin = FindAnyObjectByType<GestorColeccionables>();
        if (botin != null) botin.AlCompletarTodo += IniciarEscape;
        else Debug.LogWarning("[Partida] No hay GestorColeccionables en la escena: la zona de escape no se activará.");
    }

    /// <summary>
    /// Respaldo del evento AlCompletarTodo: si por cualquier motivo no llegó (otro suscriptor lanzó una
    /// excepción, el botín se creó después, etc.), aquí se revisa el contador directamente.
    /// </summary>
    private void RevisarBotinCompleto()
    {
        if (EstadoActual != Estado.Buscando) return;
        if (botin == null) { botin = FindAnyObjectByType<GestorColeccionables>(); if (botin == null) return; }
        int total = botin.TotalBasureros + botin.TotalObjetosCasa;
        if (total > 0 && botin.BasurerosEncontrados >= botin.TotalBasureros && botin.ObjetosCasaEncontrados >= botin.TotalObjetosCasa)
            IniciarEscape();
    }

    private void OnDestroy()
    {
        if (matAnillo != null) Destroy(matAnillo);
        if (matHaz != null) Destroy(matHaz);
    }

    // ------------------------------------------------------------------ mundo

    private void CalcularMundo()
    {
        var casaGO = GameObject.Find("Casa_Estructura");
        var casaR = casaGO != null ? casaGO.GetComponent<Renderer>() : null;
        if (casaR != null) { casa = casaR.bounds; hayCasa = true; sueloY = casa.min.y; }
        else sueloY = jugador.transform.position.y - 0.2f;

        Vector3 centro = hayCasa ? new Vector3(casa.center.x, sueloY, casa.center.z) : jugador.transform.position;
        var frente = GameObject.Find("Porche_09");
        var atras = GameObject.Find("Porche_10");
        if (frente != null) { frenteCasa = frente.transform.position - centro; puertaCasa = frente.transform.position; }
        else if (atras != null) { frenteCasa = centro - atras.transform.position; puertaCasa = centro; }
        else { frenteCasa = Vector3.zero; puertaCasa = centro; }
        frenteCasa.y = 0f;
        if (frenteCasa.sqrMagnitude > 0.001f) frenteCasa.Normalize();

        // 1) marcadores del área de juego (2.5 m dentro de la cerca perimetral)
        var mMin = GameObject.Find("LimiteJuego_Min");
        var mMax = GameObject.Find("LimiteJuego_Max");
        if (mMin != null && mMax != null)
        {
            areaInterior = new Bounds(mMin.transform.position, Vector3.zero);
            areaInterior.Encapsulate(mMax.transform.position);
            area = areaInterior;
            area.Expand(new Vector3(3f, 0f, 3f));                 // se puede llegar hasta la cerca
            hayArea = true;
        }
        else
        {
            // 2) paredes invisibles del borde del mapa
            bool hay = false; Bounds b = new Bounds();
            foreach (var c in FindObjectsByType<Collider>())
            {
                if (!c.name.StartsWith("ColBorde")) continue;
                if (!hay) { b = c.bounds; hay = true; } else b.Encapsulate(c.bounds);
            }
            // 3) respaldo: todo lo que tiene collider en el mundo
            if (!hay)
            {
                var mundo = GameObject.Find("Nivel1Mundo");
                if (mundo != null)
                    foreach (var c in mundo.GetComponentsInChildren<Collider>())
                    { if (!hay) { b = c.bounds; hay = true; } else b.Encapsulate(c.bounds); }
            }
            if (hay)
            {
                area = b; area.Expand(new Vector3(-1.6f, 0f, -1.6f));
                areaInterior = b; areaInterior.Expand(new Vector3(-6f, 0f, -6f));
                hayArea = area.size.x > 4f && area.size.z > 4f;
            }
        }

        if (!hayArea)
        {
            Debug.LogWarning("[Partida] No encontré los límites del mapa (LimiteJuego_Min/Max o ColBorde*). Solo se usará el rescate por caída.");
            areaInterior = hayCasa ? new Bounds(casa.center, new Vector3(casa.size.x * 5f, 10f, casa.size.z * 5f)) : new Bounds(jugador.transform.position, new Vector3(80f, 10f, 80f));
        }

        var tri = NavMesh.CalculateTriangulation();
        hayNavMesh = tri.indices != null && tri.indices.Length > 0;
    }

    /// <summary>
    /// Cuatro muros invisibles altísimos alrededor del área de juego (de 30 m bajo el suelo a 300 m de alto),
    /// así no se puede salir ni con el gancho ni saltando desde un techo. Van en la capa "Ignore Raycast":
    /// el gancho no se engancha de ellos, pero el mapache sí choca.
    /// </summary>
    private void CrearMurosInvisibles()
    {
        if (!hayArea) return;
        const float grosor = 4f, abajo = 30f, arriba = 300f;
        int capa = LayerMask.NameToLayer("Ignore Raycast");
        var raiz = new GameObject("Muros invisibles");
        raiz.transform.SetParent(transform, false);
        float alto = abajo + arriba, cy = sueloY - abajo + alto * 0.5f;
        float largoX = area.size.x + grosor * 2f, largoZ = area.size.z + grosor * 2f;

        void Muro(string n, Vector3 centro, Vector3 tam)
        {
            var go = new GameObject(n);
            go.layer = capa < 0 ? 2 : capa;
            go.transform.SetParent(raiz.transform, false);
            go.transform.position = centro;
            go.AddComponent<BoxCollider>().size = tam;
        }
        Muro("Oeste", new Vector3(area.min.x - grosor * 0.5f, cy, area.center.z), new Vector3(grosor, alto, largoZ));
        Muro("Este", new Vector3(area.max.x + grosor * 0.5f, cy, area.center.z), new Vector3(grosor, alto, largoZ));
        Muro("Sur", new Vector3(area.center.x, cy, area.min.z - grosor * 0.5f), new Vector3(largoX, alto, grosor));
        Muro("Norte", new Vector3(area.center.x, cy, area.max.z + grosor * 0.5f), new Vector3(largoX, alto, grosor));

        // techo invisible muy alto para que el gancho tampoco lance al mapache por encima
        Muro("Techo", new Vector3(area.center.x, sueloY + arriba, area.center.z), new Vector3(largoX, grosor, largoZ));
    }

    /// <summary>Busca suelo plano y despejado (no techos, árboles, cercas ni interiores) en (x, z).</summary>
    private bool SueloLibre(float x, float z, float radio, out Vector3 punto)
    {
        punto = default;
        var origen = new Vector3(x, sueloY + 80f, z);
        if (!Physics.Raycast(origen, Vector3.down, out var hit, 140f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (hit.normal.y < 0.9f) return false;
        if (hit.point.y > sueloY + 0.45f || hit.point.y < sueloY - 0.6f) return false;   // al nivel de la calle / jardín
        Vector3 p = hit.point;
        if (Physics.CheckCapsule(p + Vector3.up * (radio + 0.12f), p + Vector3.up * 1.7f, radio, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (hayNavMesh)
        {
            if (!NavMesh.SamplePosition(p, out var nh, 0.8f, NavMesh.AllAreas)) return false;
            if (Mathf.Abs(nh.position.y - p.y) > 0.5f) return false;
        }
        punto = p;
        return true;
    }

    private float DistanciaACasa(Vector3 p)
    {
        if (!hayCasa) return 100f;
        Vector3 q = casa.ClosestPoint(new Vector3(p.x, casa.center.y, p.z));
        q.y = p.y = 0f;
        return Vector3.Distance(p, q);
    }

    /// <summary>Distancia horizontal al enemigo, su ruta o su zona más cercana.</summary>
    private float DistanciaAEnemigos(Vector3 p)
    {
        float min = 999f;
        Vector2 P = new Vector2(p.x, p.z);
        foreach (var e in FindObjectsByType<EnemigoVigilante>())
        {
            Vector3 ep = e.transform.position;
            min = Mathf.Min(min, Vector2.Distance(P, new Vector2(ep.x, ep.z)));
            if (e.EsPerro && e.ZonaCentro != Vector3.zero)
                min = Mathf.Min(min, Vector2.Distance(P, new Vector2(e.ZonaCentro.x, e.ZonaCentro.z)) - 5f);
            var r = e.Ruta;
            if (r == null) continue;
            for (int i = 0; i < r.Count; i++)
            {
                Vector3 a = r[i], b = r[(i + 1) % r.Count];
                min = Mathf.Min(min, DistanciaSegmento(P, new Vector2(a.x, a.z), new Vector2(b.x, b.z)));
            }
        }
        return min;
    }

    private static float DistanciaSegmento(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    private bool HayCamino(Vector3 desde, Vector3 hasta)
    {
        if (!hayNavMesh) return true;
        if (!NavMesh.SamplePosition(desde, out var a, 2f, NavMesh.AllAreas)) return false;
        if (!NavMesh.SamplePosition(hasta, out var b, 3f, NavMesh.AllAreas)) return false;
        var camino = new NavMeshPath();
        return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, camino) && camino.status == NavMeshPathStatus.PathComplete;
    }

    // ------------------------------------------------------------------ spawn

    /// <summary>True si el punto ya está afuera, lejos de la casa (así se respeta la posición puesta en el editor).</summary>
    private bool FueraDeLaCasa(Vector3 p) => !hayCasa || DistanciaACasa(p) >= 8f;

    private void ColocarSpawn()
    {
        // si en la escena el mapache ya está afuera (colocado desde el editor), se respeta esa posición
        if (FueraDeLaCasa(jugador.transform.position))
        {
            posSpawn = jugador.transform.position;
            return;
        }

        if (!BuscarSpawn(out posSpawn, out var rot))
        {
            Debug.LogWarning("[Partida] No encontré un spawn libre fuera de la casa; se usa el de la escena.");
            posSpawn = jugador.transform.position;
            return;
        }
        jugador.Teletransportar(posSpawn + Vector3.up * 0.05f, rot);
        Debug.Log($"[Partida] Spawn del mapache en {posSpawn} ({DistanciaACasa(posSpawn):0} m de la casa).");
    }

#if UNITY_EDITOR
    /// <summary>Lo usa el editor (F.O.R.N.I.T.E > Colocar mapache fuera de la casa) para guardar el spawn en la escena.</summary>
    public bool CalcularSpawnParaEditor(ControladorMapache j, out Vector3 pos, out Quaternion rot)
    {
        jugador = j;
        Physics.SyncTransforms();
        CalcularMundo();
        var cc = j.GetComponent<CharacterController>();
        bool ccActivo = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;
        try { return BuscarSpawn(out pos, out rot); }
        finally { if (cc != null) cc.enabled = ccActivo; }
    }
#endif

    private bool BuscarSpawn(out Vector3 pos, out Quaternion rot)
    {
        pos = default; rot = Quaternion.identity;
        var candidatos = new List<(Vector3 p, float puntos)>();
        const float paso = 2.5f;
        for (float x = areaInterior.min.x + 1f; x <= areaInterior.max.x - 1f; x += paso)
            for (float z = areaInterior.min.z + 1f; z <= areaInterior.max.z - 1f; z += paso)
            {
                if (!SueloLibre(x, z, 0.7f, out var p)) continue;
                float dCasa = DistanciaACasa(p);
                if (dCasa < 12f) continue;                                        // nunca dentro ni pegado a la casa
                float dEnem = DistanciaAEnemigos(p);
                if (dEnem < distanciaSpawnEnemigos) continue;
                float puntos = Mathf.Min(dEnem, 30f) * 0.5f - Mathf.Abs(dCasa - distanciaSpawnCasa);
                if (frenteCasa != Vector3.zero && Vector3.Dot((p - puertaCasa).normalized, frenteCasa) > 0.3f) puntos += 6f;   // del lado de la puerta principal
                candidatos.Add((p, puntos));
            }

        candidatos.Sort((a, b) => b.puntos.CompareTo(a.puntos));
        Vector3? elegido = null;
        for (int i = 0; i < candidatos.Count && i < 40; i++)
            if (HayCamino(candidatos[i].p, puertaCasa)) { elegido = candidatos[i].p; break; }
        if (elegido == null && candidatos.Count > 0) elegido = candidatos[0].p;

        if (elegido == null) return false;

        pos = elegido.Value;
        Vector3 mirar = (hayCasa ? casa.center : areaInterior.center) - pos; mirar.y = 0f;
        rot = mirar.sqrMagnitude > 0.01f ? Quaternion.LookRotation(mirar.normalized, Vector3.up) : jugador.transform.rotation;
        return true;
    }

    // ------------------------------------------------------------------ zona de escape

    private void ElegirZonaEscape()
    {
        var candidatos = new List<(Vector3 p, float puntos)>();
        Bounds b = areaInterior;
        float m = margenOrilla + radioZonaEscape * 0.5f;
        float x0 = b.min.x + m, x1 = b.max.x - m, z0 = b.min.z + m, z1 = b.max.z - m;
        const float paso = 2f;

        void Probar(float x, float z)
        {
            if (!SueloLibre(x, z, Mathf.Min(radioZonaEscape * 0.6f, 1.4f), out var p)) return;
            float dCasa = DistanciaACasa(p);
            if (dCasa < 15f) return;
            float dSpawn = Vector3.Distance(p, posSpawn);
            if (dSpawn < 25f) return;
            candidatos.Add((p, dSpawn + dCasa * 0.5f));
        }
        for (float x = x0; x <= x1; x += paso) { Probar(x, z0); Probar(x, z1); }
        for (float z = z0; z <= z1; z += paso) { Probar(x0, z); Probar(x1, z); }

        candidatos.Sort((a, c) => c.puntos.CompareTo(a.puntos));
        for (int i = 0; i < candidatos.Count && i < 40; i++)
            if (HayCamino(posSpawn, candidatos[i].p)) { posEscape = candidatos[i].p; Debug.Log($"[Partida] Zona de escape en {posEscape}."); return; }
        if (candidatos.Count > 0) { posEscape = candidatos[0].p; return; }

        // respaldo: escapar por donde llegaste
        Debug.LogWarning("[Partida] No encontré un lugar en la orilla para la zona de escape; se usa el punto de inicio.");
        posEscape = posSpawn;
    }

    private void IniciarEscape()
    {
        if (EstadoActual != Estado.Buscando) return;
        EstadoActual = Estado.Escapando;
        TiempoRestante = segundosParaEscapar;
        Debug.Log($"[Partida] Botín completo: zona de escape activada en {posEscape} (a {Vector3.Distance(posEscape, jugador.transform.position):0} m).");
        panelEscape.SetActive(true);
        try { CrearZonaVisual(); }
        catch (System.Exception e) { Debug.LogException(e); }
        Anunciar($"¡Corre a la ZONA DE ESCAPE! Tienes {FormatoTiempo(segundosParaEscapar)}", 5f);
        SonidosJuego.Instancia?.AlertaSospecha();
    }

    private void CrearZonaVisual()
    {
        zona = new GameObject("Zona de Escape");
        zona.transform.position = posEscape;
        // anillo en el suelo
        var anillo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        anillo.name = "Anillo";
        Destroy(anillo.GetComponent<Collider>());
        anillo.transform.SetParent(zona.transform, false);
        anillo.transform.localPosition = Vector3.up * 0.04f;
        anillo.transform.localScale = new Vector3(radioZonaEscape * 2f, 0.02f, radioZonaEscape * 2f);
        matAnillo = MaterialTransparente(new Color(colorZona.r, colorZona.g, colorZona.b, 0.45f));
        var rA = anillo.GetComponent<Renderer>();
        rA.sharedMaterial = matAnillo; rA.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rA.receiveShadows = false;

        // haz de luz vertical, visible desde lejos
        var haz = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        haz.name = "Haz";
        Destroy(haz.GetComponent<Collider>());
        haz.transform.SetParent(zona.transform, false);
        haz.transform.localPosition = Vector3.up * 30f;
        haz.transform.localScale = new Vector3(radioZonaEscape * 1.6f, 30f, radioZonaEscape * 1.6f);
        matHaz = MaterialTransparente(new Color(colorZona.r, colorZona.g, colorZona.b, 0.16f));
        var rH = haz.GetComponent<Renderer>();
        rH.sharedMaterial = matHaz; rH.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rH.receiveShadows = false;

        var luzGO = new GameObject("Luz");
        luzGO.transform.SetParent(zona.transform, false);
        luzGO.transform.localPosition = Vector3.up * 1.5f;
        luzZona = luzGO.AddComponent<Light>();
        luzZona.type = LightType.Point; luzZona.color = colorZona; luzZona.range = 9f; luzZona.intensity = 4f; luzZona.shadows = LightShadows.None;
    }

    /// <summary>
    /// Material sin luz, semitransparente y que brilla. En URP se usa "Universal Render Pipeline/Unlit"
    /// configurado como transparente (Sprites/Default en un cilindro sin colores de vértice puede salir invisible).
    /// </summary>
    private static Material MaterialTransparente(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh != null)
        {
            var m = new Material(sh);
            m.SetFloat("_Surface", 1f);            // transparente
            m.SetFloat("_Blend", 0f);              // alpha
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);               // se ve también desde dentro del haz
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", c);
            return m;
        }
        var resp = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
        return new Material(resp) { color = c };
    }

    private static void ColorMaterial(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    // ------------------------------------------------------------------ bucle

    private void LateUpdate()
    {
        if (jugador == null) return;
        MantenerEnElMapa();

        RevisarBotinCompleto();
        if (EstadoActual == Estado.Escapando) ActualizarEscape();
        if (EstadoActual == Estado.Escapado) ActualizarVictoria();
        ActualizarZonaVisual();
        ActualizarAnuncio();
    }

    private void MantenerEnElMapa()
    {
        if (MenuPausa.Pausado || EstadoActual == Estado.Escapado) return;   // en la cinemática el mapache sale del mapa a propósito
        Vector3 p = jugador.transform.position;

        // cayó a un vacío → regresar al último lugar seguro
        if (p.y < sueloY - caidaMaxima)
        {
            jugador.Teletransportar(ultimaPosSegura + Vector3.up * 0.15f);
            GestorAlerta.Instancia?.Mensaje("¡Uy! Regresaste a tierra firme", 2f);
            return;
        }

        // no salir del área de juego
        if (hayArea)
        {
            float x = Mathf.Clamp(p.x, area.min.x, area.max.x);
            float z = Mathf.Clamp(p.z, area.min.z, area.max.z);
            if (!Mathf.Approximately(x, p.x) || !Mathf.Approximately(z, p.z))
            {
                jugador.EmpujarHaciaDentro(new Vector3(x - p.x, 0f, z - p.z));
                if (Time.time > tAvisoLimite)
                {
                    GestorAlerta.Instancia?.Mensaje("No puedes salir del vecindario", 1.8f);
                    tAvisoLimite = Time.time + 2.5f;
                }
                p = jugador.transform.position;

                // si algo lo atoró afuera (una cerca, un techo), regresarlo directo al último lugar seguro
                Vector3 dentro = new Vector3(Mathf.Clamp(p.x, area.min.x, area.max.x), p.y, Mathf.Clamp(p.z, area.min.z, area.max.z));
                if ((dentro - p).sqrMagnitude > 0.25f * 0.25f)
                {
                    jugador.Teletransportar(ultimaPosSegura + Vector3.up * 0.15f);
                    return;
                }
            }
        }

        // recordar posiciones seguras (en el suelo, sin vacío debajo y no pegado a la orilla)
        tGuardarSegura -= Time.deltaTime;
        bool lejosDeLaOrilla = !hayArea || (p.x > area.min.x + 2f && p.x < area.max.x - 2f && p.z > area.min.z + 2f && p.z < area.max.z - 2f);
        if (tGuardarSegura <= 0f && lejosDeLaOrilla && jugador.EnSuelo && !jugador.UsandoGancho && p.y > sueloY - 1f && p.y < sueloY + 0.6f)
        {
            tGuardarSegura = 0.4f;
            if (Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore)) ultimaPosSegura = p;
        }
    }

    private void ActualizarEscape()
    {
        bool atrapado = GestorAlerta.Instancia != null && GestorAlerta.Instancia.JugadorAtrapado;
        if (atrapado) { EstadoActual = Estado.Perdido; panelEscape.SetActive(false); return; }

        TiempoRestante -= Time.deltaTime;
        Vector3 pj = jugador.transform.position;
        Vector3 d = posEscape - pj;
        float dy = d.y; d.y = 0f;
        float dist = d.magnitude;

        // ¿llegó?
        if (dist <= radioZonaEscape && Mathf.Abs(dy) < 3f)
        {
            Ganar();
            return;
        }

        // ¿se acabó el tiempo?
        if (TiempoRestante <= 0f)
        {
            TiempoRestante = 0f;
            EstadoActual = Estado.Perdido;
            panelEscape.SetActive(false);
            if (GestorAlerta.Instancia != null) GestorAlerta.Instancia.Atrapar("¡Se acabó el tiempo!");
            else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        // HUD
        bool urgente = TiempoRestante <= 15f;
        txtTiempo.text = FormatoTiempo(TiempoRestante);
        txtTiempo.color = urgente ? Color.Lerp(new Color(1f, 0.25f, 0.2f), Color.white, Mathf.PingPong(Time.time * 3f, 1f) * 0.4f) : Color.white;
        txtTiempo.rectTransform.localScale = Vector3.one * (urgente ? 1f + 0.06f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 1f);
        txtDistancia.text = $"Zona de escape: {dist:0} m";
        if (TiempoRestante <= 10f && !sonoUltimos10) { sonoUltimos10 = true; SonidosJuego.Instancia?.AlertaSospecha(); }

        // flecha que apunta hacia la zona según hacia dónde mira la cámara
        var cam = Camera.main;
        if (cam != null && dist > 0.1f)
        {
            Vector3 f = cam.transform.forward; f.y = 0f;
            float ang = Vector3.SignedAngle(f.sqrMagnitude > 0.001f ? f : Vector3.forward, d, Vector3.up);
            txtFlecha.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -ang);
        }
    }

    private void Ganar()
    {
        EstadoActual = Estado.Escapado;
        MenuPausa.PartidaTerminada = true;
        jugador.ControlesBloqueados = true;
        foreach (var e in FindObjectsByType<EnemigoVigilante>())
        {
            e.enabled = false;
            var ag = e.GetComponent<NavMeshAgent>();
            if (ag != null && ag.isOnNavMesh) ag.isStopped = true;
        }
        panelEscape.SetActive(false);
        tAnuncio = 0f;
        tVictoria = 0f;

        var botin = FindAnyObjectByType<GestorColeccionables>();
        string resumen = botin != null
            ? $"Te llevaste {botin.BasurerosEncontrados} basureros dorados y {botin.ObjetosCasaEncontrados} objetos de la casa"
            : "Botín asegurado";
        txtVictoriaSub.text = $"{resumen}\nTe sobraron {FormatoTiempo(TiempoRestante)} · Volviendo al menú...";
        SonidosJuego.Instancia?.Reproducir2D("sobrecarga_inicio", 0.8f, 0f);

        // cinemática: el mapache guarda el botín en su costal y huye saltando la cerca
        Vector3 salida = posEscape - areaInterior.center; salida.y = 0f;
        if (salida.sqrMagnitude < 1f) salida = jugador.transform.forward;
        cinematica = gameObject.AddComponent<CinematicaEscape>();
        cinematica.Iniciar(jugador, salida, botin, colorZona, transform);
    }

    private void ActualizarVictoria()
    {
        if (cinematica != null && !cinematica.ListoParaTitulo) return;   // el título sale cuando el mapache ya va huyendo
        tVictoria += Time.unscaledDeltaTime;
        float a = Mathf.Clamp01(tVictoria / 0.8f);
        capaVictoria.color = new Color(0.02f, 0.08f, 0.04f, 0.65f * a);
        txtVictoria.color = new Color(colorZona.r, colorZona.g, colorZona.b, a);
        txtVictoriaSub.color = new Color(1f, 1f, 1f, a);
        txtVictoria.rectTransform.localScale = Vector3.one * (1f + 0.05f * Mathf.Sin(Time.unscaledTime * 5f));
        if (tVictoria >= segundosAntesDeVolverAlMenu) { enabled = false; MenuPausa.IrAlMenu(); }
    }

    private void ActualizarZonaVisual()
    {
        if (zona == null) return;
        float pulso = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
        ColorMaterial(matAnillo, new Color(colorZona.r, colorZona.g, colorZona.b, 0.35f + 0.3f * pulso));
        ColorMaterial(matHaz, new Color(colorZona.r, colorZona.g, colorZona.b, 0.14f + 0.12f * pulso));
        luzZona.intensity = 3f + 2.5f * pulso;
        zona.transform.GetChild(0).localScale = new Vector3(radioZonaEscape * 2f, 0.02f, radioZonaEscape * 2f) * (1f + 0.04f * pulso);
    }

    // ------------------------------------------------------------------ UI

    private void Anunciar(string texto, float segundos) { txtAnuncio.text = texto; tAnuncio = segundos; }

    private void ActualizarAnuncio()
    {
        if (tAnuncio > 0f) tAnuncio -= Time.deltaTime;
        var c = txtAnuncio.color; c.a = Mathf.Clamp01(tAnuncio / 0.5f); txtAnuncio.color = c;
    }

    private static string FormatoTiempo(float s)
    {
        int t = Mathf.CeilToInt(Mathf.Max(0f, s));
        return $"{t / 60:00}:{t % 60:00}";
    }

    private void ConstruirUI()
    {
        var fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGO = new GameObject("HUD Partida", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var raiz = canvasGO.transform;

        // panel de escape (arriba a la izquierda)
        var panel = RT("Escape", raiz, new Vector2(0, 1), new Vector2(30, -30), new Vector2(400, 190), new Vector2(0, 1));
        var img = panel.gameObject.AddComponent<Image>(); img.color = new Color(0.04f, 0.06f, 0.14f, 0.75f); img.raycastTarget = false;
        var borde = panel.gameObject.AddComponent<Outline>(); borde.effectColor = new Color(colorZona.r, colorZona.g, colorZona.b, 0.8f); borde.effectDistance = new Vector2(2, -2);
        var titulo = Texto("Titulo", panel, fuente, "¡ESCAPA!", 30, colorZona, new Vector2(20, -12), new Vector2(360, 40), TextAnchor.UpperLeft, new Vector2(0, 1));
        titulo.fontStyle = FontStyle.Bold;
        txtTiempo = Texto("Tiempo", panel, fuente, "00:00", 64, Color.white, new Vector2(20, -52), new Vector2(240, 76), TextAnchor.MiddleLeft, new Vector2(0, 1));
        txtTiempo.fontStyle = FontStyle.Bold;
        txtTiempo.rectTransform.pivot = new Vector2(0, 1);
        txtDistancia = Texto("Distancia", panel, fuente, "", 24, new Color(0.85f, 0.9f, 1f), new Vector2(20, -140), new Vector2(360, 34), TextAnchor.UpperLeft, new Vector2(0, 1));
        // flecha
        var cajaFlecha = RT("Brujula", panel, new Vector2(1, 1), new Vector2(-70, -90), new Vector2(80, 80), new Vector2(0.5f, 0.5f));
        var circ = cajaFlecha.gameObject.AddComponent<Image>(); circ.color = new Color(1f, 1f, 1f, 0.08f); circ.raycastTarget = false;
        txtFlecha = Texto("Flecha", cajaFlecha, fuente, "▲", 52, colorZona, Vector2.zero, new Vector2(80, 80), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
        txtFlecha.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        panelEscape = panel.gameObject;
        panelEscape.SetActive(false);

        // anuncio
        txtAnuncio = Texto("Anuncio", raiz, fuente, "", 40, colorZona, new Vector2(0, -280), new Vector2(1600, 60), TextAnchor.MiddleCenter, new Vector2(0.5f, 1));
        txtAnuncio.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        txtAnuncio.fontStyle = FontStyle.Bold;

        // victoria
        var capa = RT("Victoria", raiz, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        capa.anchorMin = Vector2.zero; capa.anchorMax = Vector2.one; capa.offsetMin = capa.offsetMax = Vector2.zero;
        capaVictoria = capa.gameObject.AddComponent<Image>(); capaVictoria.color = Color.clear; capaVictoria.raycastTarget = false;
        txtVictoria = Texto("Titulo", capa, fuente, "¡ESCAPASTE!", 120, Color.clear, new Vector2(0, 60), new Vector2(1600, 160), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
        txtVictoria.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        txtVictoria.fontStyle = FontStyle.Bold;
        txtVictoriaSub = Texto("Sub", capa, fuente, "", 38, Color.clear, new Vector2(0, -70), new Vector2(1600, 120), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
        txtVictoriaSub.rectTransform.pivot = new Vector2(0.5f, 0.5f);

        foreach (var t in canvasGO.GetComponentsInChildren<Text>(true))
        {
            var s = t.gameObject.AddComponent<Outline>();
            s.effectColor = new Color(0, 0, 0, 0.8f); s.effectDistance = new Vector2(2, -2);
        }
    }

    private static RectTransform RT(string n, Transform padre, Vector2 ancla, Vector2 pos, Vector2 tam, Vector2 pivote)
    {
        var go = new GameObject(n, typeof(RectTransform));
        var rt = (RectTransform)go.transform; rt.SetParent(padre, false);
        rt.anchorMin = rt.anchorMax = ancla; rt.pivot = pivote;
        rt.anchoredPosition = pos; rt.sizeDelta = tam;
        return rt;
    }

    private static Text Texto(string n, Transform padre, Font fuente, string texto, int tam, Color color, Vector2 pos, Vector2 caja, TextAnchor alin, Vector2 ancla)
    {
        var rt = RT(n, padre, ancla, pos, caja, ancla);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = fuente; t.text = texto; t.fontSize = tam; t.color = color; t.alignment = alin;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
