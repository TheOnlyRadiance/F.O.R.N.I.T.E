using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Movimiento del mapache en tercera persona.
///
/// Controles (teclado / mando):
///   Mover ............ WASD o flechas   / stick izquierdo
///   Correr ........... Shift izquierdo  / clic del stick izquierdo
///   Saltar ........... Espacio          / botón A (sur)
///   Dash (habilidad) . C                / botón X (oeste)
///
/// El movimiento es relativo a la cámara. Incluye "coyote time" (puedes saltar
/// un instante después de salir de un borde), buffer de salto, salto variable
/// (si sueltas el botón antes, salta menos) y un dash con enfriamiento que
/// también se puede usar una vez en el aire.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ControladorMapache : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private float velocidadCaminar = 3.0f;
    [SerializeField] private float velocidadCorrer = 5.5f;
    [SerializeField] private float aceleracion = 30f;
    [Tooltip("Qué tanto control tienes en el aire (0 = nada, 1 = igual que en el suelo).")]
    [Range(0f, 1f)] [SerializeField] private float controlEnAire = 0.6f;
    [SerializeField] private float velocidadGiro = 720f;

    [Header("Salto")]
    [SerializeField] private float alturaSalto = 1.1f;
    [SerializeField] private float gravedad = -25f;
    [Tooltip("Multiplica la gravedad al caer para que el salto se sienta menos flotante.")]
    [SerializeField] private float multiplicadorCaida = 1.6f;
    [Tooltip("Gravedad extra cuando sueltas el botón de salto antes de llegar arriba (salto corto).")]
    [SerializeField] private float multiplicadorSaltoCorto = 2f;
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float bufferSalto = 0.15f;

    [Header("Dash (tecla C)")]
    [SerializeField] private float distanciaDash = 4f;
    [SerializeField] private float duracionDash = 0.18f;
    [Tooltip("Segundos que tarda en recargarse el dash.")]
    [SerializeField] private float enfriamientoDash = 10f;
    [Tooltip("Permite un dash mientras estás en el aire (se recarga al tocar el suelo).")]
    [SerializeField] private bool dashEnAire = true;

    [Header("Referencias (opcionales)")]
    [Tooltip("Cámara que define hacia dónde es 'adelante'. Si está vacío usa Camera.main.")]
    [SerializeField] private Transform camara;
    [Tooltip("Animator del modelo. Si está vacío se busca en los hijos.")]
    [SerializeField] private Animator animator;
    [Tooltip("Transform del modelo visual (para inclinarlo y estirarlo). Si está vacío se usa el del Animator.")]
    [SerializeField] private Transform modelo;
    [Tooltip("Estela que se muestra durante el dash.")]
    [SerializeField] private TrailRenderer estelaDash;

    // ---- estado público (útil para UI / sonidos)
    public bool EnSuelo { get; private set; }
    public bool HaciendoDash => tiempoDashRestante > 0f;
    /// <summary>0 = dash listo, 1 = recién usado.</summary>
    public float EnfriamientoDashNormalizado => enfriamientoDash > 0f ? Mathf.Clamp01(enfriamientoRestante / enfriamientoDash) : 0f;
    /// <summary>Segundos que faltan para poder usar el dash (0 = listo).</summary>
    public float EnfriamientoDashRestante => Mathf.Max(0f, enfriamientoRestante);
    /// <summary>True si el dash se puede usar ahora mismo (recargado y, si estás en el aire, con el dash aéreo disponible).</summary>
    public bool DashDisponible => !HaciendoDash && enfriamientoRestante <= 0f && (EnSuelo || (dashEnAire && dashAereoDisponible));
    public event System.Action AlSaltar;
    public event System.Action AlHacerDash;
    public event System.Action AlAterrizar;

    private CharacterController cc;
    private InputAction accionMover, accionSaltar, accionDash, accionCorrer;

    private Vector3 velocidadHorizontal;
    private float velocidadVertical;
    private float ultimoTiempoEnSuelo = -10f;
    private float ultimoTiempoSaltoPulsado = -10f;
    private float tiempoDashRestante;
    private float enfriamientoRestante;
    private bool dashAereoDisponible = true;
    private Vector3 direccionDash;
    private bool estabaEnSuelo;

    // efectos visuales del modelo
    private Vector3 escalaModeloBase = Vector3.one;
    private Quaternion rotacionModeloBase = Quaternion.identity;
    private float estiramiento;   // >0 estira (salto), <0 aplasta (aterrizaje)
    private float inclinacion;

    private static readonly int ParamVelocidad = Animator.StringToHash("Velocidad");
    private static readonly int ParamEnSuelo = Animator.StringToHash("EnSuelo");

    // ------------------------------------------------------------------ inicio

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (modelo == null && animator != null) modelo = animator.transform;
        if (modelo != null)
        {
            escalaModeloBase = modelo.localScale;
            rotacionModeloBase = modelo.localRotation;
        }
        if (estelaDash != null) estelaDash.emitting = false;

        CrearAcciones();
    }

    private void Start()
    {
        if (camara == null && Camera.main != null) camara = Camera.main.transform;
    }

    private void CrearAcciones()
    {
        accionMover = new InputAction("Mover", InputActionType.Value, expectedControlType: "Vector2");
        accionMover.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        accionMover.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        accionMover.AddBinding("<Gamepad>/leftStick");

        accionSaltar = new InputAction("Saltar", InputActionType.Button, "<Keyboard>/space");
        accionSaltar.AddBinding("<Gamepad>/buttonSouth");

        accionDash = new InputAction("Dash", InputActionType.Button, "<Keyboard>/c");
        accionDash.AddBinding("<Gamepad>/buttonWest");

        accionCorrer = new InputAction("Correr", InputActionType.Button, "<Keyboard>/leftShift");
        accionCorrer.AddBinding("<Gamepad>/leftStickPress");
    }

    private void OnEnable()
    {
        accionMover.Enable(); accionSaltar.Enable(); accionDash.Enable(); accionCorrer.Enable();
    }

    private void OnDisable()
    {
        accionMover.Disable(); accionSaltar.Disable(); accionDash.Disable(); accionCorrer.Disable();
    }

    private void OnDestroy()
    {
        accionMover.Dispose(); accionSaltar.Dispose(); accionDash.Dispose(); accionCorrer.Dispose();
    }

    // ------------------------------------------------------------------ bucle

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        EnSuelo = cc.isGrounded;
        if (EnSuelo)
        {
            ultimoTiempoEnSuelo = Time.time;
            dashAereoDisponible = true;
            if (!estabaEnSuelo) Aterrizar();
        }
        estabaEnSuelo = EnSuelo;

        if (enfriamientoRestante > 0f) enfriamientoRestante -= dt;
        if (accionSaltar.WasPressedThisFrame()) ultimoTiempoSaltoPulsado = Time.time;

        Vector3 direccionDeseada = DireccionRelativaACamara(accionMover.ReadValue<Vector2>());

        // --- dash
        if (accionDash.WasPressedThisFrame()) IntentarDash(direccionDeseada);

        if (HaciendoDash)
        {
            tiempoDashRestante -= dt;
            velocidadHorizontal = direccionDash * (distanciaDash / duracionDash);
            velocidadVertical = 0f;   // el dash es horizontal, ignora la gravedad mientras dura
            if (!HaciendoDash) TerminarDash();
        }
        else
        {
            // --- movimiento normal
            float velMax = accionCorrer.IsPressed() ? velocidadCorrer : velocidadCaminar;
            Vector3 objetivo = direccionDeseada * velMax;
            float acc = aceleracion * (EnSuelo ? 1f : controlEnAire);
            velocidadHorizontal = Vector3.MoveTowards(velocidadHorizontal, objetivo, acc * dt);

            // --- salto (con buffer y coyote time)
            bool saltoEnBuffer = Time.time - ultimoTiempoSaltoPulsado <= bufferSalto;
            bool puedeSaltar = Time.time - ultimoTiempoEnSuelo <= coyoteTime;
            if (saltoEnBuffer && puedeSaltar)
            {
                velocidadVertical = Mathf.Sqrt(2f * alturaSalto * -gravedad);
                ultimoTiempoSaltoPulsado = -10f;
                ultimoTiempoEnSuelo = -10f;
                estiramiento = 0.18f;
                AlSaltar?.Invoke();
            }

            // --- gravedad
            if (EnSuelo && velocidadVertical < 0f)
            {
                velocidadVertical = -2f; // mantiene pegado al suelo en rampas/escalones
            }
            else
            {
                float g = gravedad;
                if (velocidadVertical < 0f) g *= multiplicadorCaida;
                else if (!accionSaltar.IsPressed()) g *= multiplicadorSaltoCorto;
                velocidadVertical += g * dt;
            }
        }

        // --- mover
        Vector3 movimiento = (velocidadHorizontal + Vector3.up * velocidadVertical) * dt;
        CollisionFlags flags = cc.Move(movimiento);
        if ((flags & CollisionFlags.Above) != 0 && velocidadVertical > 0f) velocidadVertical = 0f;

        // --- girar hacia donde se mueve
        Vector3 planar = new Vector3(velocidadHorizontal.x, 0f, velocidadHorizontal.z);
        if (planar.sqrMagnitude > 0.01f)
        {
            Quaternion objetivoRot = Quaternion.LookRotation(planar, Vector3.up);
            float giro = HaciendoDash ? velocidadGiro * 3f : velocidadGiro;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, objetivoRot, giro * dt);
        }

        ActualizarAnimator(dt);
        ActualizarEfectosModelo(dt);
    }

    // ------------------------------------------------------------------ dash

    private void IntentarDash(Vector3 direccionDeseada)
    {
        if (HaciendoDash || enfriamientoRestante > 0f) return;
        if (!EnSuelo && !(dashEnAire && dashAereoDisponible)) return;

        direccionDash = direccionDeseada.sqrMagnitude > 0.01f ? direccionDeseada.normalized : transform.forward;
        direccionDash.y = 0f;
        direccionDash.Normalize();

        if (!EnSuelo) dashAereoDisponible = false;
        tiempoDashRestante = duracionDash;
        enfriamientoRestante = enfriamientoDash;
        transform.rotation = Quaternion.LookRotation(direccionDash, Vector3.up);
        if (estelaDash != null) { estelaDash.Clear(); estelaDash.emitting = true; }
        AlHacerDash?.Invoke();
    }

    private void TerminarDash()
    {
        // al terminar conserva algo de impulso para que no se sienta un frenazo
        velocidadHorizontal = direccionDash * velocidadCorrer;
        if (estelaDash != null) estelaDash.emitting = false;
    }

    private void Aterrizar()
    {
        if (velocidadVertical < -6f) estiramiento = -0.15f;
        AlAterrizar?.Invoke();
    }

    // ------------------------------------------------------------------ utilidades

    private Vector3 DireccionRelativaACamara(Vector2 entrada)
    {
        if (entrada.sqrMagnitude > 1f) entrada.Normalize();

        Vector3 adelante = Vector3.forward, derecha = Vector3.right;
        if (camara != null)
        {
            adelante = camara.forward; adelante.y = 0f;
            if (adelante.sqrMagnitude < 0.001f) adelante = camara.up; // cámara mirando recto hacia abajo
            adelante.y = 0f; adelante.Normalize();
            derecha = Vector3.Cross(Vector3.up, adelante);
        }
        return adelante * entrada.y + derecha * entrada.x;
    }

    private void ActualizarAnimator(float dt)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        float vel = new Vector3(velocidadHorizontal.x, 0f, velocidadHorizontal.z).magnitude;
        if (HaciendoDash) vel = velocidadCorrer;
        animator.SetFloat(ParamVelocidad, vel, 0.08f, dt);
        animator.SetBool(ParamEnSuelo, EnSuelo);
    }

    /// <summary>Pequeños detalles de animación por código: estirar al saltar, aplastar al caer, inclinarse en el dash.</summary>
    private void ActualizarEfectosModelo(float dt)
    {
        if (modelo == null) return;

        estiramiento = Mathf.MoveTowards(estiramiento, 0f, dt * 1.2f);
        float s = 1f + estiramiento;
        modelo.localScale = new Vector3(escalaModeloBase.x / Mathf.Sqrt(s), escalaModeloBase.y * s, escalaModeloBase.z / Mathf.Sqrt(s));

        float inclinacionObjetivo = HaciendoDash ? 25f : (!EnSuelo ? Mathf.Clamp(-velocidadVertical * 1.5f, -10f, 10f) : 0f);
        inclinacion = Mathf.Lerp(inclinacion, inclinacionObjetivo, 1f - Mathf.Exp(-15f * dt));
        modelo.localRotation = Quaternion.AngleAxis(inclinacion, Vector3.right) * rotacionModeloBase;
    }
}
