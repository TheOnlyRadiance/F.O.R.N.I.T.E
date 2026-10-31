using UnityEngine;

/// <summary>
/// Movimiento lento tipo "Ken Burns" para la imagen de fondo del menú:
/// un zoom y desplazamiento muy sutil que hace que la portada se sienta viva.
/// </summary>
public class FondoAnimado : MonoBehaviour
{
    [SerializeField] private float margenBase = 0.03f;
    [SerializeField] private float zoomExtra = 0.04f;
    [SerializeField] private float desplazamiento = 12f;
    [SerializeField] private float periodo = 24f;

    private RectTransform rt;
    private Vector2 posicionBase;
    private Vector3 escalaBase;

    private void Awake()
    {
        rt = (RectTransform)transform;
        posicionBase = rt.anchoredPosition;
        escalaBase = rt.localScale;
    }

    private void Update()
    {
        float t = Time.unscaledTime * Mathf.PI * 2f / periodo;
        // Siempre un poco ampliada (margen base) para que el desplazamiento nunca deje ver los bordes.
        float zoom = 1f + margenBase + zoomExtra * (0.5f + 0.5f * Mathf.Sin(t));
        rt.localScale = escalaBase * zoom;
        rt.anchoredPosition = posicionBase + new Vector2(Mathf.Sin(t * 0.7f), Mathf.Cos(t * 0.5f)) * desplazamiento;
    }
}
