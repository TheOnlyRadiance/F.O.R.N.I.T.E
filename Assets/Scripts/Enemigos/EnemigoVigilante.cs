using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// Guardia nocturno con linterna o perro guardián (según el GDD de F.O.R.N.I.T.E).
///
/// GUARDIA: hace rondas fijas por una ruta. Ve con un cono de visión (su linterna) y escucha el ruido.
///          Si ve al mapache 3 segundos seguidos, llama a la policía y te atrapan.
///          Si solo te escucha o te pierde de vista, va a investigar el último lugar.
/// PERRO:   deambula por su zona. Huele al mapache muy cerca (en todas direcciones) y también ve y escucha.
///          Cuando te detecta ladra (los guardias cercanos vienen a investigar) y te persigue;
///          si te alcanza, te atrapa.
/// Usa NavMeshAgent para moverse (la escena necesita un NavMesh horneado).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemigoVigilante : MonoBehaviour
{
    public enum Tipo { Guardia, Perro }
    private enum Estado { Patrulla, Investigando, Persiguiendo }

    [SerializeField] private Tipo tipo = Tipo.Guardia;

    [Header("Ruta (guardia) o zona (perro)")]
    [SerializeField] private List<Vector3> ruta = new();
    [SerializeField] private Vector3 zonaCentro;
    [SerializeField] private Vector3 zonaTamano = new Vector3(20f, 4f, 10f);
    [SerializeField] private Vector2 esperaEnPunto = new Vector2(1.5f, 3.5f);

    [Header("Velocidades")]
    [SerializeField] private float velocidadPatrulla = 1.6f;
    [SerializeField] private float velocidadInvestigar = 2.4f;
    [SerializeField] private float velocidadPersecucion = 4.4f;

    [Header("Sentidos")]
    [SerializeField] private float alcanceVision = 13f;
    [SerializeField] private float anguloVision = 60f;
    [SerializeField] private float alturaOjos = 1.55f;
    [Tooltip("Radio en el que detecta sin necesidad de verte (olfato del perro). 0 = desactivado.")]
    [SerializeField] private float radioOlfato = 0f;
    [SerializeField] private float multiplicadorOido = 1f;
    [Tooltip("Segundos viéndote para dar la alarma (GDD: 3 s).")]
    [SerializeField] private float tiempoParaDetectar = 3f;
    [SerializeField] private float distanciaAtrapar = 1.1f;

    [Header("Referencias")]
    [SerializeField] private Animator animator;
    [SerializeField] private Light linterna;

    public float Deteccion { get; private set; }   // 0..1
    public bool Alertado => estado == Estado.Persiguiendo;

    private static readonly List<EnemigoVigilante> todos = new();
    private static readonly int ParamVelocidad = Animator.StringToHash("Velocidad");

    private NavMeshAgent agente;
    private ControladorMapache jugador;
    private Estado estado = Estado.Patrulla;
    private int indiceRuta;
    private float espera, tiempoInvestigando, tiempoSinVer, tiempoLadrido;
    private Vector3 ultimaPosicionConocida;
    private Text indicador;
    private Color colorLinterna;

    // ------------------------------------------------------------------ inicio

    private void Awake()
    {
        agente = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (linterna != null) colorLinterna = linterna.color;
    }

    private void OnEnable() => todos.Add(this);
    private void OnDisable() => todos.Remove(this);

    private void Start()
    {
        jugador = FindAnyObjectByType<ControladorMapache>();
        agente.speed = velocidadPatrulla;
        if (!agente.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var h, 3f, NavMesh.AllAreas)) agente.Warp(h.position);
        CrearIndicador();
        IrAlSiguientePunto();
    }

    // ------------------------------------------------------------------ bucle

    private void Update()
    {
        if (jugador == null || !agente.isOnNavMesh) { Animar(); return; }
        float dt = Time.deltaTime;
        bool atrapado = GestorAlerta.Instancia != null && GestorAlerta.Instancia.JugadorAtrapado;

        bool loVe = PuedeVerAlJugador(out float distancia) && !atrapado;
        bool loHuele = !atrapado && radioOlfato > 0f && distancia < radioOlfato && !jugador.UsandoGancho;
        bool loOye = !atrapado && Vector3.Distance(transform.position, jugador.transform.position) < jugador.RadioRuido * multiplicadorOido;

        if (loVe || loHuele)
        {
            float cerca = Mathf.Lerp(1.8f, 0.6f, Mathf.Clamp01(distancia / alcanceVision));
            float sigilo = jugador.EnSigilo && distancia > 5f ? 0.5f : 1f;
            Deteccion = Mathf.Min(1f, Deteccion + dt / tiempoParaDetectar * cerca * sigilo * (loHuele ? 1.6f : 1f));
            ultimaPosicionConocida = jugador.transform.position;
            tiempoSinVer = 0f;
        }
        else
        {
            tiempoSinVer += dt;
            Deteccion = Mathf.Max(0f, Deteccion - dt * (estado == Estado.Persiguiendo ? 0.12f : 0.3f));
        }

        if (loOye && estado == Estado.Patrulla) Investigar(jugador.transform.position);

        switch (tipo)
        {
            case Tipo.Guardia: LogicaGuardia(loVe, dt); break;
            case Tipo.Perro: LogicaPerro(loVe || loHuele, dt); break;
        }

        GestorAlerta.Instancia?.Reportar(this, Deteccion, estado == Estado.Persiguiendo);
        SonidosDeSospecha();
        PisadasPropias();
        ActualizarIndicador();
        ActualizarLinterna();
        Animar();
    }

    private void LogicaGuardia(bool loVe, float dt)
    {
        if (loVe)
        {
            // se detiene y te ilumina con la linterna mientras se llena la detección
            agente.isStopped = true;
            Mirar(jugador.transform.position, dt);
            if (Deteccion >= 1f && !yaGrito)
            {
                yaGrito = true;
                SonidosJuego.Instancia?.VozGuardia("grito", transform.position + Vector3.up * 1.6f);   // "¡Alto ahí! ¡Te tengo, ladrón!"
                GestorAlerta.Instancia?.Atrapar("¡El guardia te descubrió y llamó a la policía!");
            }
            return;
        }
        agente.isStopped = false;
        if (Deteccion > 0.15f && estado == Estado.Patrulla) Investigar(ultimaPosicionConocida);
        ComportamientoBase(dt);
    }

    private void LogicaPerro(bool loDetecta, float dt)
    {
        // detección completa → ladra fuerte y te persigue (antes solo gruñe y se acerca a olfatear)
        if (estado != Estado.Persiguiendo && Deteccion >= 1f) EmpezarPersecucion();
        else if (estado != Estado.Persiguiendo && loDetecta)
        {
            agente.isStopped = false;
            agente.speed = velocidadInvestigar;
            agente.SetDestination(jugador.transform.position);
            return;
        }

        if (estado == Estado.Persiguiendo)
        {
            agente.isStopped = false;
            agente.speed = velocidadPersecucion;
            agente.SetDestination(loDetecta ? jugador.transform.position : ultimaPosicionConocida);
            tiempoLadrido -= dt;
            if (tiempoLadrido <= 0f) { Ladrar(); tiempoLadrido = Random.Range(1.4f, 2.4f); }

            Vector3 d = jugador.transform.position - transform.position;
            if (d.magnitude < distanciaAtrapar && Mathf.Abs(d.y) < 1f && !jugador.UsandoGancho)
                GestorAlerta.Instancia?.Atrapar("¡El perro te atrapó!");

            // se rinde si te alejas o te pierde mucho tiempo (por ejemplo, subiendo a un techo)
            if (tiempoSinVer > 4f || Vector3.Distance(transform.position, jugador.transform.position) > 22f)
            {
                estado = Estado.Investigando;
                Investigar(ultimaPosicionConocida);
            }
            return;
        }
        ComportamientoBase(dt);
    }

    private void ComportamientoBase(float dt)
    {
        if (estado == Estado.Investigando)
        {
            agente.speed = velocidadInvestigar;
            if (!agente.pathPending && agente.remainingDistance < 0.6f)
            {
                tiempoInvestigando += dt;
                transform.Rotate(0f, 90f * dt * Mathf.Sin(tiempoInvestigando * 1.5f), 0f);   // mira a los lados
                if (tiempoInvestigando > 3.5f) { estado = Estado.Patrulla; Rendirse(); IrAlSiguientePunto(); }
            }
            return;
        }

        // patrulla
        agente.speed = velocidadPatrulla;
        if (!agente.pathPending && agente.remainingDistance < 0.5f)
        {
            espera -= dt;
            if (espera <= 0f) IrAlSiguientePunto();
        }
    }

    // ------------------------------------------------------------------ acciones

    private void IrAlSiguientePunto()
    {
        espera = Random.Range(esperaEnPunto.x, esperaEnPunto.y);
        Vector3 destino;
        if (tipo == Tipo.Guardia && ruta.Count > 0)
        {
            destino = ruta[indiceRuta];
            indiceRuta = (indiceRuta + 1) % ruta.Count;
        }
        else
        {
            destino = zonaCentro + new Vector3(Random.Range(-0.5f, 0.5f) * zonaTamano.x, 0f, Random.Range(-0.5f, 0.5f) * zonaTamano.z);
        }
        if (NavMesh.SamplePosition(destino, out var h, 3f, NavMesh.AllAreas)) agente.SetDestination(h.position);
    }

    /// <summary>Va a revisar un punto (ruido, ladrido, último lugar donde te vio).</summary>
    public void Investigar(Vector3 punto)
    {
        if (estado == Estado.Persiguiendo) return;
        estado = Estado.Investigando;
        tiempoInvestigando = 0f;
        if (NavMesh.SamplePosition(punto, out var h, 4f, NavMesh.AllAreas)) agente.SetDestination(h.position);
    }

    private void EmpezarPersecucion()
    {
        estado = Estado.Persiguiendo;
        tiempoLadrido = 0f;
    }

    private void Ladrar()
    {
        SonidosJuego.Instancia?.Ladrido(transform.position + Vector3.up * 0.6f);
        GestorAlerta.Instancia?.Mensaje("¡GUAU! ¡GUAU! El perro te descubrió");
        foreach (var e in todos)
            if (e != this && e.tipo == Tipo.Guardia && Vector3.Distance(e.transform.position, transform.position) < 30f)
                e.Investigar(transform.position);
    }

    // ------------------------------------------------------------------ sonidos

    private bool yaGrito, sospechaAnunciada, huboSospecha;
    private float distPasos;
    private Vector3 posAnterior;

    /// <summary>Guardia: "¿Quién anda ahí?" al empezar a sospechar. Perro: gruñido. Y "habrá sido el viento" al rendirse.</summary>
    private void SonidosDeSospecha()
    {
        var s = SonidosJuego.Instancia;
        if (s == null) return;
        if (Deteccion >= 0.3f && !sospechaAnunciada && estado != Estado.Persiguiendo)
        {
            sospechaAnunciada = true; huboSospecha = true;
            if (tipo == Tipo.Guardia) s.VozGuardia("sospecha", transform.position + Vector3.up * 1.6f);
            else s.Gruñido(transform.position + Vector3.up * 0.5f);
            s.AlertaSospecha();
        }
        if (Deteccion <= 0.01f) sospechaAnunciada = false;
    }

    private void Rendirse()
    {
        if (tipo == Tipo.Guardia && huboSospecha) SonidosJuego.Instancia?.VozGuardia("rendirse", transform.position + Vector3.up * 1.6f);
        huboSospecha = false;
    }

    private void PisadasPropias()
    {
        if (tipo != Tipo.Guardia) { posAnterior = transform.position; return; }
        Vector3 d = transform.position - posAnterior; d.y = 0f;
        posAnterior = transform.position;
        distPasos += d.magnitude;
        if (distPasos >= 0.75f) { distPasos = 0f; SonidosJuego.Instancia?.PisadaEnemigo(transform.position, 0.45f); }
    }

    private void Mirar(Vector3 punto, float dt)
    {
        Vector3 d = punto - transform.position; d.y = 0f;
        if (d.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * dt);
    }

    // ------------------------------------------------------------------ sentidos

    private bool PuedeVerAlJugador(out float distancia)
    {
        Vector3 ojos = transform.position + Vector3.up * alturaOjos;
        Vector3 objetivo = jugador.transform.position + Vector3.up * 0.5f;
        Vector3 d = objetivo - ojos;
        distancia = d.magnitude;
        if (distancia > alcanceVision) return false;
        Vector3 plano = new Vector3(d.x, 0f, d.z);
        if (Vector3.Angle(transform.forward, plano) > anguloVision * 0.5f && distancia > 1.5f) return false;
        float elevacion = Mathf.Atan2(d.y, plano.magnitude) * Mathf.Rad2Deg;
        if (Mathf.Abs(elevacion) > 40f) return false;   // muy arriba/abajo (p. ej. en un techo)

        // línea de vista: lo primero que toque el rayo debe ser el jugador
        if (Physics.Raycast(ojos, d / distancia, out var hit, distancia + 0.2f, ~0, QueryTriggerInteraction.Ignore))
            return hit.transform == jugador.transform || hit.transform.IsChildOf(jugador.transform);
        return true;
    }

    // ------------------------------------------------------------------ visuales

    private void Animar()
    {
        if (animator != null && animator.runtimeAnimatorController != null)
            animator.SetFloat(ParamVelocidad, agente != null ? agente.velocity.magnitude : 0f, 0.1f, Time.deltaTime);
    }

    private void ActualizarLinterna()
    {
        if (linterna == null) return;
        Color alerta = Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.15f, 0.1f), Mathf.InverseLerp(0.4f, 1f, Deteccion));
        linterna.color = Deteccion > 0.05f ? Color.Lerp(colorLinterna, alerta, Mathf.Clamp01(Deteccion * 2f)) : colorLinterna;
    }

    private void CrearIndicador()
    {
        var canvasGO = new GameObject("Indicador", typeof(Canvas));
        canvasGO.transform.SetParent(transform, false);
        var rend = GetComponentInChildren<Renderer>();
        float alto = rend != null ? rend.bounds.max.y - transform.position.y : 2f;
        canvasGO.transform.localPosition = Vector3.up * (alto + 0.45f);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)canvasGO.transform;
        rt.sizeDelta = new Vector2(100f, 100f);
        rt.localScale = Vector3.one * 0.012f;
        var tGO = new GameObject("Texto", typeof(RectTransform), typeof(Text));
        tGO.transform.SetParent(canvasGO.transform, false);
        ((RectTransform)tGO.transform).sizeDelta = new Vector2(100f, 100f);
        indicador = tGO.GetComponent<Text>();
        indicador.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        indicador.fontSize = 90; indicador.fontStyle = FontStyle.Bold;
        indicador.alignment = TextAnchor.MiddleCenter;
        indicador.horizontalOverflow = HorizontalWrapMode.Overflow; indicador.verticalOverflow = VerticalWrapMode.Overflow;
        var sombra = tGO.AddComponent<Outline>(); sombra.effectColor = Color.black; sombra.effectDistance = new Vector2(3f, -3f);
    }

    private void ActualizarIndicador()
    {
        if (indicador == null) return;
        if (estado == Estado.Persiguiendo || Deteccion > 0.7f) { indicador.text = "!"; indicador.color = new Color(1f, 0.2f, 0.15f); }
        else if (Deteccion > 0.05f || estado == Estado.Investigando) { indicador.text = "?"; indicador.color = new Color(1f, 0.85f, 0.2f); }
        else indicador.text = "";
        var cam = Camera.main;
        if (cam != null) indicador.canvas.transform.rotation = Quaternion.LookRotation(indicador.canvas.transform.position - cam.transform.position);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        for (int i = 0; i < ruta.Count; i++) { Gizmos.DrawSphere(ruta[i], 0.25f); Gizmos.DrawLine(ruta[i], ruta[(i + 1) % ruta.Count]); }
        if (tipo == Tipo.Perro) { Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f); Gizmos.DrawWireCube(zonaCentro, zonaTamano); }
    }
}
