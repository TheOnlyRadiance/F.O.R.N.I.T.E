using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Cámara en tercera persona que orbita alrededor del jugador.
///   Mouse / stick derecho ... girar la cámara
///   Rueda del mouse ......... acercar / alejar
///   Esc ..................... liberar el cursor (clic para volver a capturarlo)
/// Evita atravesar paredes acercándose cuando hay algo entre la cámara y el jugador.
/// </summary>
public class CamaraSeguimiento : MonoBehaviour
{
    [SerializeField] private Transform objetivo;
    [Tooltip("Punto que mira la cámara, relativo a los pies del objetivo.")]
    [SerializeField] private Vector3 desplazamientoObjetivo = new Vector3(0f, 0.85f, 0f);

    [Header("Distancia")]
    [SerializeField] private float distancia = 3.5f;
    [SerializeField] private float distanciaMin = 1.2f;
    [SerializeField] private float distanciaMax = 6f;
    [SerializeField] private float velocidadZoom = 0.004f;

    [Header("Rotación")]
    [SerializeField] private float sensibilidadMouse = 0.12f;
    [SerializeField] private float sensibilidadStick = 160f;
    [SerializeField] private bool invertirY = false;
    [SerializeField] private float anguloMin = -25f;
    [SerializeField] private float anguloMax = 70f;
    [SerializeField] private float suavizado = 12f;

    [Header("Colisión")]
    [SerializeField] private float radioColision = 0.25f;
    [SerializeField] private LayerMask capasColision = ~0;

    private float yaw;
    private float pitch = 18f;
    private float distanciaActual;
    private Vector3 puntoSuave;

    public void AsignarObjetivo(Transform t) { objetivo = t; Recolocar(); }

    private void Start()
    {
        BloquearCursor(true);
        Recolocar();
    }

    private void Recolocar()
    {
        if (objetivo == null) return;
        yaw = objetivo.eulerAngles.y;
        distanciaActual = distancia;
        puntoSuave = objetivo.position + desplazamientoObjetivo;
    }

    private void LateUpdate()
    {
        if (objetivo == null) return;
        float dt = Time.unscaledDeltaTime;

        ManejarCursor();

        // --- entrada de rotación
        Vector2 giro = Vector2.zero;
        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
            giro += Mouse.current.delta.ReadValue() * sensibilidadMouse;
        if (Gamepad.current != null)
            giro += Gamepad.current.rightStick.ReadValue() * sensibilidadStick * dt;

        yaw += giro.x;
        pitch += (invertirY ? giro.y : -giro.y);
        pitch = Mathf.Clamp(pitch, anguloMin, anguloMax);

        // --- zoom
        if (Mouse.current != null)
        {
            float rueda = Mouse.current.scroll.ReadValue().y;
            distancia = Mathf.Clamp(distancia - rueda * velocidadZoom * distancia, distanciaMin, distanciaMax);
        }

        // --- posición deseada
        Vector3 punto = objetivo.position + desplazamientoObjetivo;
        puntoSuave = Vector3.Lerp(puntoSuave, punto, 1f - Mathf.Exp(-suavizado * dt));
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 dir = rot * Vector3.back;

        // --- evitar paredes
        float distLibre = distancia;
        var hits = Physics.SphereCastAll(puntoSuave, radioColision, dir, distancia, capasColision, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h.transform == objetivo || h.transform.IsChildOf(objetivo)) continue;
            if (h.distance > 0f) distLibre = Mathf.Min(distLibre, h.distance);
        }
        distLibre = Mathf.Max(distLibre, 0.3f);

        // acercarse rápido, alejarse suave
        float k = distLibre < distanciaActual ? 1f : 1f - Mathf.Exp(-4f * dt);
        distanciaActual = Mathf.Lerp(distanciaActual, distLibre, k);

        transform.position = puntoSuave + dir * distanciaActual;
        transform.rotation = rot;
    }

    private void ManejarCursor()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) BloquearCursor(false);
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) BloquearCursor(true);
    }

    private static void BloquearCursor(bool bloquear)
    {
        Cursor.lockState = bloquear ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !bloquear;
    }

    private void OnDisable() => BloquearCursor(false);
}
