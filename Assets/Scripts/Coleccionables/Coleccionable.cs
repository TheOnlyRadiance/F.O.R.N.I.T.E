using UnityEngine;

/// <summary>
/// Objeto que el jugador puede recoger con la tecla E.
/// Lo crea y lo coloca GestorColeccionables; este componente solo se encarga
/// de verse bien (brillo, giro suave, resaltado cuando estás cerca) y de la animación al recogerlo.
/// </summary>
public class Coleccionable : MonoBehaviour
{
    public enum Tipo { BasureroDorado, ObjetoCasa }

    public Tipo tipo;
    public string nombre = "Objeto";
    [Tooltip("Texto de la acción que aparece en pantalla, p. ej. \"Revisar\" o \"Tomar\".")]
    public string accion = "Tomar";
    [Tooltip("Los objetos de la casa giran despacio sobre el suelo para llamar la atención.")]
    public bool girar;

    public bool Recogido { get; private set; }

    private Light brillo;
    private Vector3 escalaBase;
    private float intensidadBase;
    private bool resaltado;
    private float tRecoger = -1f;

    private void Awake()
    {
        escalaBase = transform.localScale;
    }

    /// <summary>Agrega una luz dorada suave para que el objeto se pueda descubrir.</summary>
    public void CrearBrillo(Color color, float alcance, float intensidad, float altura)
    {
        var go = new GameObject("Brillo");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * altura;
        brillo = go.AddComponent<Light>();
        brillo.type = LightType.Point;
        brillo.color = color;
        brillo.range = alcance;
        brillo.intensity = intensidad;
        brillo.shadows = LightShadows.None;
        intensidadBase = intensidad;
    }

    public void Resaltar(bool activo) => resaltado = activo;

    private void Update()
    {
        float t = Time.time;

        if (tRecoger >= 0f)
        {
            // animación de recoger: salta, gira y se encoge
            tRecoger += Time.deltaTime;
            float k = Mathf.Clamp01(tRecoger / 0.55f);
            float salto = Mathf.Sin(k * Mathf.PI) * 0.6f;
            transform.position += Vector3.up * (salto - ultimoSalto);
            ultimoSalto = salto;
            transform.Rotate(0f, 720f * Time.deltaTime, 0f, Space.World);
            transform.localScale = escalaBase * (k < 0.3f ? 1f + k : Mathf.Lerp(1.3f, 0f, (k - 0.3f) / 0.7f));
            if (brillo != null) brillo.intensity = intensidadBase * 4f * (1f - k);
            if (k >= 1f) Destroy(gameObject);
            return;
        }

        if (girar) transform.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);

        float pulso = resaltado ? 1f + 0.06f * Mathf.Sin(t * 8f) : 1f;
        transform.localScale = Vector3.Lerp(transform.localScale, escalaBase * (resaltado ? 1.12f * pulso : 1f), 1f - Mathf.Exp(-12f * Time.deltaTime));

        if (brillo != null)
            brillo.intensity = intensidadBase * (resaltado ? 2f : 1f) * (0.8f + 0.2f * Mathf.Sin(t * 3f + transform.position.x));
    }

    private float ultimoSalto;

    /// <summary>Lo marca como recogido y reproduce la animación (después se destruye solo).</summary>
    public void Recoger()
    {
        if (Recogido) return;
        Recogido = true;
        resaltado = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        tRecoger = 0f;
        ultimoSalto = 0f;
    }
}
