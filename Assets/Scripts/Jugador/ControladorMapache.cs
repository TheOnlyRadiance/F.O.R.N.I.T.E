using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Movimiento y habilidades del mapache (Sly, el líder de la banda).
///
/// Controles (teclado / mando):
///   Mover ................ WASD o flechas      / stick izquierdo
///   Sigilo ............... Shift izquierdo     / clic del stick izquierdo   (más lento y casi sin ruido)
///   Saltar ............... Espacio             / A
///   Dash ................. C                   / X
///   Gancho (escalar) ..... Q o clic derecho    / RB   (apunta con el centro de la pantalla)
///   Sobrecarga ........... R                   / LB   (buff de velocidad temporal)
///
/// También calcula cuánto RUIDO hace el mapache (los guardias y el perro lo escuchan).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ControladorMapache : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private float velocidadCaminar = 3.0f;
    [Tooltip("Velocidad en modo sigilo (Shift).")]
    [SerializeField] private float velocidadSigilo = 1.6f;
    [SerializeField] private float aceleracion = 30f;
    [Range(0f, 1f)] [SerializeField] private float controlEnAire = 0.6f;
    [SerializeField] private float velocidadGiro = 720f;

    [Header("Salto")]
    [SerializeField] private float alturaSalto = 1.1f;
    [SerializeField] private float gravedad = -25f;
    [SerializeField] private float multiplicadorCaida = 1.6f;
    [SerializeField] private float multiplicadorSaltoCorto = 2f;
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float bufferSalto = 0.15f;

    [Header("Dash (C)")]
    [SerializeField] private float distanciaDash = 4f;
    [SerializeField] private float duracionDash = 0.18f;
    [SerializeField] private float enfriamientoDash = 10f;
    [SerializeField] private bool dashEnAire = true;

    [Header("Gancho (Q / clic derecho)")]
    [SerializeField] private float alcanceGancho = 24f;
    [SerializeField] private float velocidadGancho = 17f;
    [SerializeField] private float enfriamientoGancho = 4f;
    [Tooltip("Al llegar al borde de un techo, pequeño salto para subirse.")]
    [SerializeField] private float impulsoAlLlegar = 6.5f;

    [Header("Sobrecarga (R)")]
    [SerializeField] private float duracionSobrecarga = 6f;
    [SerializeField] private float enfriamientoSobrecarga = 20f;
    [SerializeField] private float multiplicadorVelocidadSobrecarga = 1.75f;
    [SerializeField] private float multiplicadorSaltoSobrecarga = 1.15f;
    [SerializeField] private Color colorSobrecarga = new Color(0.35f, 0.85f, 1f);

    [Header("Referencias (opcionales)")]
    [SerializeField] private Transform camara;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform modelo;
    [SerializeField] private TrailRenderer estelaDash;

    // ------------------------------------------------------------------ estado público (HUD, enemigos)

    public bool EnSuelo { get; private set; }
    public bool EnSigilo { get; private set; }
    public bool HaciendoDash => tiempoDashRestante > 0f;
    public bool UsandoGancho => estadoGancho != EstadoGancho.Inactivo;
    public bool SobrecargaActiva => tiempoSobrecargaRestante > 0f;
    /// <summary>Si es true, el jugador no puede moverse (p. ej. lo atraparon).</summary>
    public bool ControlesBloqueados { get; set; }

    /// <summary>Ruido actual de 0 (silencio) a 1 (muy ruidoso).</summary>
    public float Ruido { get; private set; }
    /// <summary>Distancia en metros a la que se escucha al mapache ahora mismo.</summary>
    public float RadioRuido => Ruido * 14f;

    // dash
    public float EnfriamientoDashNormalizado => enfriamientoDash > 0f ? Mathf.Clamp01(enfDash / enfriamientoDash) : 0f;
    public float EnfriamientoDashRestante => Mathf.Max(0f, enfDash);
    public bool DashDisponible => !ControlesBloqueados && !HaciendoDash && !UsandoGancho && enfDash <= 0f && (EnSuelo || (dashEnAire && dashAereoDisponible));
    // gancho
    public float EnfriamientoGanchoNormalizado => enfriamientoGancho > 0f ? Mathf.Clamp01(enfGancho / enfriamientoGancho) : 0f;
    public float EnfriamientoGanchoRestante => Mathf.Max(0f, enfGancho);
    public bool GanchoDisponible => !ControlesBloqueados && !UsandoGancho && enfGancho <= 0f;
    public bool ObjetivoGanchoValido { get; private set; }
    public Vector3 PuntoGancho => puntoGancho;
    // sobrecarga
    public float EnfriamientoSobrecargaNormalizado => enfriamientoSobrecarga > 0f ? Mathf.Clamp01(enfSobrecarga / enfriamientoSobrecarga) : 0f;
    public float EnfriamientoSobrecargaRestante => Mathf.Max(0f, enfSobrecarga);
    public float SobrecargaRestanteNormalizada => duracionSobrecarga > 0f ? Mathf.Clamp01(tiempoSobrecargaRestante / duracionSobrecarga) : 0f;
    public float SobrecargaRestante => Mathf.Max(0f, tiempoSobrecargaRestante);
    public bool SobrecargaDisponible => !ControlesBloqueados && !SobrecargaActiva && enfSobrecarga <= 0f;

    public event System.Action AlSaltar, AlHacerDash, AlAterrizar, AlLanzarGancho, AlEngancharse, AlActivarSobrecarga, AlTerminarSobrecarga;

    // ------------------------------------------------------------------ privados

    private enum EstadoGancho { Inactivo, Lanzando, Jalando }

    private CharacterController cc;
    private InputAction accMover, accSaltar, accDash, accSigilo, accGancho, accSobrecarga;

    private Vector3 velocidadHorizontal;
    private float velocidadVertical;
    private float ultimoEnSuelo = -10f, ultimoSaltoPulsado = -10f;
    private float tiempoDashRestante, enfDash;
    private bool dashAereoDisponible = true;
    private Vector3 direccionDash;
    private bool estabaEnSuelo;

    private EstadoGancho estadoGancho;
    private Vector3 puntoGancho, objetivoGancho;
    private float tGancho, enfGancho, tiempoAtascado;
    private LineRenderer cuerda;

    private float tiempoSobrecargaRestante, enfSobrecarga;
    private Light auraSobrecarga;

    private float ruidoPico;   // picos breves de ruido (aterrizajes, dash)

    private Vector3 escalaModeloBase = Vector3.one;
    private Quaternion rotacionModeloBase = Quaternion.identity;
    private float estiramiento, inclinacion, agachado;

    private static readonly int ParamVelocidad = Animator.StringToHash("Velocidad");
    private static readonly int ParamEnSuelo = Animator.StringToHash("EnSuelo");

    // ------------------------------------------------------------------ inicio

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (modelo == null && animator != null) modelo = animator.transform;
        if (modelo != null) { escalaModeloBase = modelo.localScale; rotacionModeloBase = modelo.localRotation; }
        if (estelaDash != null) estelaDash.emitting = false;

        CrearAcciones();
        CrearCuerda();
        CrearAura();
    }

    private void Start()
    {
        if (camara == null && Camera.main != null) camara = Camera.main.transform;
    }

    private void CrearAcciones()
    {
        accMover = new InputAction("Mover", InputActionType.Value, expectedControlType: "Vector2");
        accMover.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        accMover.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        accMover.AddBinding("<Gamepad>/leftStick");

        accSaltar = new InputAction("Saltar", InputActionType.Button, "<Keyboard>/space"); accSaltar.AddBinding("<Gamepad>/buttonSouth");
        accDash = new InputAction("Dash", InputActionType.Button, "<Keyboard>/c"); accDash.AddBinding("<Gamepad>/buttonWest");
        accSigilo = new InputAction("Sigilo", InputActionType.Button, "<Keyboard>/leftShift"); accSigilo.AddBinding("<Gamepad>/leftStickPress");
        accGancho = new InputAction("Gancho", InputActionType.Button, "<Keyboard>/q"); accGancho.AddBinding("<Mouse>/rightButton"); accGancho.AddBinding("<Gamepad>/rightShoulder");
        accSobrecarga = new InputAction("Sobrecarga", InputActionType.Button, "<Keyboard>/r"); accSobrecarga.AddBinding("<Gamepad>/leftShoulder");
    }

    private InputAction[] Acciones => new[] { accMover, accSaltar, accDash, accSigilo, accGancho, accSobrecarga };
    private void OnEnable() { foreach (var a in Acciones) a.Enable(); }
    private void OnDisable() { foreach (var a in Acciones) a.Disable(); }
    private void OnDestroy() { foreach (var a in Acciones) a.Dispose(); }

    private void CrearCuerda()
    {
        var go = new GameObject("CuerdaGancho");
        go.transform.SetParent(transform, false);
        cuerda = go.AddComponent<LineRenderer>();
        cuerda.positionCount = 2;
        cuerda.widthMultiplier = 0.035f;
        cuerda.numCapVertices = 2;
        cuerda.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        if (sh != null)
        {
            var m = new Material(sh);
            var c = new Color(0.55f, 0.4f, 0.22f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            cuerda.material = m;
        }
        cuerda.enabled = false;
    }

    private void CrearAura()
    {
        var go = new GameObject("AuraSobrecarga");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        auraSobrecarga = go.AddComponent<Light>();
        auraSobrecarga.type = LightType.Point;
        auraSobrecarga.color = colorSobrecarga;
        auraSobrecarga.range = 3.5f;
        auraSobrecarga.intensity = 0f;
        auraSobrecarga.shadows = LightShadows.None;
        auraSobrecarga.enabled = false;
    }

    // ------------------------------------------------------------------ bucle

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        EnSuelo = cc.isGrounded;
        if (EnSuelo)
        {
            ultimoEnSuelo = Time.time;
            dashAereoDisponible = true;
            if (!estabaEnSuelo) Aterrizar();
        }
        estabaEnSuelo = EnSuelo;

        if (enfDash > 0f) enfDash -= dt;
        if (enfGancho > 0f) enfGancho -= dt;
        if (enfSobrecarga > 0f) enfSobrecarga -= dt;
        if (tiempoSobrecargaRestante > 0f) { tiempoSobrecargaRestante -= dt; if (tiempoSobrecargaRestante <= 0f) TerminarSobrecarga(); }

        bool bloqueado = ControlesBloqueados;
        Vector2 entrada = bloqueado ? Vector2.zero : accMover.ReadValue<Vector2>();
        Vector3 dirDeseada = DireccionRelativaACamara(entrada);
        EnSigilo = !bloqueado && accSigilo.IsPressed() && !SobrecargaActiva;

        if (!bloqueado && accSaltar.WasPressedThisFrame()) ultimoSaltoPulsado = Time.time;

        ActualizarObjetivoGancho();
        if (!bloqueado)
        {
            if (accSobrecarga.WasPressedThisFrame()) IntentarSobrecarga();
            if (accGancho.WasPressedThisFrame()) IntentarGancho();
            if (accDash.WasPressedThisFrame()) IntentarDash(dirDeseada);
        }

        if (UsandoGancho) ActualizarGancho(dt);
        else if (HaciendoDash)
        {
            tiempoDashRestante -= dt;
            velocidadHorizontal = direccionDash * (distanciaDash / duracionDash);
            velocidadVertical = 0f;
            if (!HaciendoDash) TerminarDash();
            cc.Move(velocidadHorizontal * dt);
        }
        else MovimientoNormal(dirDeseada, dt);

        // girar hacia donde se mueve
        Vector3 plano = new Vector3(velocidadHorizontal.x, 0f, velocidadHorizontal.z);
        if (plano.sqrMagnitude > 0.01f)
        {
            float giro = HaciendoDash || UsandoGancho ? velocidadGiro * 3f : velocidadGiro;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(plano, Vector3.up), giro * dt);
        }

        CalcularRuido(dt);
        ActualizarAnimator(dt);
        ActualizarEfectosModelo(dt);
        ActualizarCuerda();
    }

    private void MovimientoNormal(Vector3 dirDeseada, float dt)
    {
        float vel = EnSigilo ? velocidadSigilo : velocidadCaminar;
        if (SobrecargaActiva) vel *= multiplicadorVelocidadSobrecarga;
        Vector3 objetivo = dirDeseada * vel;
        float acc = aceleracion * (EnSuelo ? 1f : controlEnAire);
        velocidadHorizontal = Vector3.MoveTowards(velocidadHorizontal, objetivo, acc * dt);

        bool saltoEnBuffer = Time.time - ultimoSaltoPulsado <= bufferSalto;
        bool puedeSaltar = Time.time - ultimoEnSuelo <= coyoteTime;
        if (saltoEnBuffer && puedeSaltar)
        {
            float h = alturaSalto * (SobrecargaActiva ? multiplicadorSaltoSobrecarga : 1f);
            velocidadVertical = Mathf.Sqrt(2f * h * -gravedad);
            ultimoSaltoPulsado = -10f; ultimoEnSuelo = -10f;
            estiramiento = 0.18f;
            AlSaltar?.Invoke();
        }

        if (EnSuelo && velocidadVertical < 0f) velocidadVertical = -2f;
        else
        {
            float g = gravedad;
            if (velocidadVertical < 0f) g *= multiplicadorCaida;
            else if (!accSaltar.IsPressed()) g *= multiplicadorSaltoCorto;
            velocidadVertical += g * dt;
        }

        var flags = cc.Move((velocidadHorizontal + Vector3.up * velocidadVertical) * dt);
        if ((flags & CollisionFlags.Above) != 0 && velocidadVertical > 0f) velocidadVertical = 0f;
    }

    // ------------------------------------------------------------------ dash

    private void IntentarDash(Vector3 dirDeseada)
    {
        if (!DashDisponible) return;
        direccionDash = dirDeseada.sqrMagnitude > 0.01f ? dirDeseada.normalized : transform.forward;
        direccionDash.y = 0f; direccionDash.Normalize();
        if (!EnSuelo) dashAereoDisponible = false;
        tiempoDashRestante = duracionDash;
        enfDash = enfriamientoDash;
        transform.rotation = Quaternion.LookRotation(direccionDash, Vector3.up);
        if (estelaDash != null) { estelaDash.Clear(); estelaDash.emitting = true; }
        ruidoPico = 1f;
        AlHacerDash?.Invoke();
    }

    private void TerminarDash()
    {
        velocidadHorizontal = direccionDash * velocidadCaminar * (SobrecargaActiva ? multiplicadorVelocidadSobrecarga : 1f);
        if (estelaDash != null && !SobrecargaActiva) estelaDash.emitting = false;
    }

    private void Aterrizar()
    {
        if (velocidadVertical < -6f) { estiramiento = -0.15f; ruidoPico = Mathf.Max(ruidoPico, 0.8f); }
        AlAterrizar?.Invoke();
    }

    // ------------------------------------------------------------------ gancho

    /// <summary>Busca adónde apunta el centro de la pantalla y, si es un muro, el borde superior para subirse.</summary>
    private void ActualizarObjetivoGancho()
    {
        ObjetivoGanchoValido = false;
        if (camara == null || UsandoGancho) return;

        var hits = Physics.RaycastAll(camara.position, camara.forward, alcanceGancho + 8f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.transform == transform || h.transform.IsChildOf(transform)) continue;
            Vector3 origen = transform.position + Vector3.up * 0.5f;
            if (Vector3.Distance(origen, h.point) > alcanceGancho) return;

            Vector3 destino = h.point + h.normal * 0.3f;
            if (h.normal.y < 0.5f)
            {
                // muro: buscar el borde de arriba (techo o cornisa) para trepar
                Vector3 dentro = h.point - h.normal * 0.45f;
                if (Physics.Raycast(dentro + Vector3.up * 4f, Vector3.down, out var arriba, 4f, ~0, QueryTriggerInteraction.Ignore) && arriba.normal.y > 0.5f)
                    destino = arriba.point + Vector3.up * 0.35f;
            }
            objetivoGancho = destino;
            puntoGancho = h.point;
            ObjetivoGanchoValido = true;
            return;
        }
    }

    private void IntentarGancho()
    {
        if (!GanchoDisponible || !ObjetivoGanchoValido) return;
        estadoGancho = EstadoGancho.Lanzando;
        tGancho = 0f;
        tiempoAtascado = 0f;
        enfGancho = enfriamientoGancho;
        tiempoDashRestante = 0f;
        cuerda.enabled = true;
        ruidoPico = Mathf.Max(ruidoPico, 0.5f);
        AlLanzarGancho?.Invoke();
    }

    private void ActualizarGancho(float dt)
    {
        tGancho += dt;
        if (estadoGancho == EstadoGancho.Lanzando)
        {
            velocidadHorizontal *= 0.9f;
            if (tGancho >= 0.15f) { estadoGancho = EstadoGancho.Jalando; tGancho = 0f; AlEngancharse?.Invoke(); }
            cc.Move(velocidadHorizontal * dt);
            return;
        }

        // saltar suelta el gancho
        if (Time.time - ultimoSaltoPulsado < 0.05f) { SoltarGancho(Mathf.Sqrt(2f * alturaSalto * -gravedad), 0.5f); return; }

        Vector3 desde = transform.position + Vector3.up * 0.5f;
        Vector3 hacia = objetivoGancho - desde;
        float dist = hacia.magnitude;
        if (dist < 0.9f || tGancho > 3f) { SoltarGancho(impulsoAlLlegar, 1f); return; }

        Vector3 vel = hacia / dist * velocidadGancho;
        Vector3 antes = transform.position;
        cc.Move(vel * dt);
        float avance = (transform.position - antes).magnitude;
        tiempoAtascado = avance < velocidadGancho * dt * 0.3f ? tiempoAtascado + dt : 0f;
        if (tiempoAtascado > 0.2f) { SoltarGancho(impulsoAlLlegar, 1f); return; }   // chocó con algo: intentar subirse

        velocidadHorizontal = new Vector3(vel.x, 0f, vel.z);
        velocidadVertical = vel.y;
    }

    private void SoltarGancho(float impulsoArriba, float factorHorizontal)
    {
        estadoGancho = EstadoGancho.Inactivo;
        cuerda.enabled = false;
        Vector3 plano = objetivoGancho - transform.position; plano.y = 0f;
        velocidadHorizontal = plano.sqrMagnitude > 0.01f ? plano.normalized * 3.5f * factorHorizontal : Vector3.zero;
        velocidadVertical = impulsoArriba;
        estiramiento = 0.15f;
    }

    private void ActualizarCuerda()
    {
        if (cuerda == null || !cuerda.enabled) return;
        Vector3 mano = transform.position + Vector3.up * 0.6f + transform.forward * 0.15f;
        Vector3 punta = estadoGancho == EstadoGancho.Lanzando ? Vector3.Lerp(mano, puntoGancho, Mathf.Clamp01(tGancho / 0.15f)) : puntoGancho;
        cuerda.SetPosition(0, mano);
        cuerda.SetPosition(1, punta);
    }

    // ------------------------------------------------------------------ sobrecarga

    private void IntentarSobrecarga()
    {
        if (!SobrecargaDisponible) return;
        tiempoSobrecargaRestante = duracionSobrecarga;
        enfSobrecarga = enfriamientoSobrecarga + duracionSobrecarga;   // el enfriamiento empieza a contar al terminar
        auraSobrecarga.enabled = true;
        if (estelaDash != null) { estelaDash.Clear(); estelaDash.emitting = true; }
        estiramiento = 0.2f;
        AlActivarSobrecarga?.Invoke();
    }

    private void TerminarSobrecarga()
    {
        tiempoSobrecargaRestante = 0f;
        auraSobrecarga.enabled = false;
        AlTerminarSobrecarga?.Invoke();
        if (estelaDash != null && !HaciendoDash) estelaDash.emitting = false;
    }

    // ------------------------------------------------------------------ ruido

    private void CalcularRuido(float dt)
    {
        float vel = new Vector3(velocidadHorizontal.x, 0f, velocidadHorizontal.z).magnitude;
        float base_ = 0f;
        if (EnSuelo && vel > 0.3f)
            base_ = EnSigilo ? 0.06f : (SobrecargaActiva ? 0.7f : 0.35f);
        if (UsandoGancho) base_ = Mathf.Max(base_, 0.45f);
        if (HaciendoDash) base_ = 1f;
        ruidoPico = Mathf.MoveTowards(ruidoPico, 0f, dt * 2.5f);
        Ruido = Mathf.Max(base_, ruidoPico);
    }

    // ------------------------------------------------------------------ utilidades

    private Vector3 DireccionRelativaACamara(Vector2 entrada)
    {
        if (entrada.sqrMagnitude > 1f) entrada.Normalize();
        Vector3 adelante = Vector3.forward;
        if (camara != null)
        {
            adelante = camara.forward; adelante.y = 0f;
            if (adelante.sqrMagnitude < 0.001f) adelante = camara.up;
            adelante.y = 0f; adelante.Normalize();
        }
        Vector3 derecha = Vector3.Cross(Vector3.up, adelante);
        return adelante * entrada.y + derecha * entrada.x;
    }

    private void ActualizarAnimator(float dt)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        float vel = new Vector3(velocidadHorizontal.x, 0f, velocidadHorizontal.z).magnitude;
        if (HaciendoDash) vel = 5.5f;
        if (UsandoGancho) vel = 0f;
        animator.SetFloat(ParamVelocidad, vel, 0.08f, dt);
        animator.SetBool(ParamEnSuelo, EnSuelo);
        animator.speed = SobrecargaActiva ? 1.35f : 1f;
    }

    private void ActualizarEfectosModelo(float dt)
    {
        if (modelo == null) return;

        estiramiento = Mathf.MoveTowards(estiramiento, 0f, dt * 1.2f);
        agachado = Mathf.Lerp(agachado, EnSigilo ? 1f : 0f, 1f - Mathf.Exp(-12f * dt));
        float s = (1f + estiramiento) * (1f - 0.14f * agachado);
        modelo.localScale = new Vector3(escalaModeloBase.x / Mathf.Sqrt(s), escalaModeloBase.y * s, escalaModeloBase.z / Mathf.Sqrt(s));

        float objetivo = HaciendoDash ? 25f : UsandoGancho ? -15f : (!EnSuelo ? Mathf.Clamp(-velocidadVertical * 1.5f, -10f, 10f) : 8f * agachado);
        inclinacion = Mathf.Lerp(inclinacion, objetivo, 1f - Mathf.Exp(-15f * dt));
        modelo.localRotation = Quaternion.AngleAxis(inclinacion, Vector3.right) * rotacionModeloBase;

        if (auraSobrecarga != null && auraSobrecarga.enabled)
            auraSobrecarga.intensity = 2.5f + Mathf.Sin(Time.time * 12f) * 0.8f;
    }
}
