using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Todo el audio del nivel en un solo lugar. Se crea solo cuando hay un mapache en la escena
/// y carga los sonidos desde Assets/Resources/Audio (no hay que asignar nada a mano).
///
///  - Pisadas del mapache según la superficie: pasto, concreto (banqueta/calle), madera y alfombra (dentro de la casa).
///  - Habilidades: salto, aterrizaje, dash, gancho (disparo / enganche / jalón), sobrecarga (inicio / fin)
///    y un "ding" cuando una habilidad vuelve a estar lista.
///  - Ambiente nocturno: grillos en bucle + perros ladrando a lo lejos de vez en cuando.
///  - Botín: golpe metálico + monedas al revisar un basurero, fanfarria al encontrar un objeto.
///  - Enemigos (lo llaman ellos): voces del guardia, ladridos y gruñidos del perro, pisadas del guardia.
/// </summary>
public class SonidosJuego : MonoBehaviour
{
    public static SonidosJuego Instancia { get; private set; }

    [Range(0f, 1f)] public float volumenGeneral = 1f;
    [Range(0f, 1f)] public float volumenAmbiente = 0.45f;

    private readonly Dictionary<string, AudioClip[]> grupos = new();
    private ControladorMapache jugador;
    private AudioSource fuente2D, ambiente;
    private float distanciaAcumulada, tLadridoLejano;
    private Vector3 ultimaPos;
    private float sueloY = float.NaN;
    private bool dashListo = true, ganchoListo = true, sobrecargaLista = true;

    // ------------------------------------------------------------------ creación automática

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CrearSiHaceFalta()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => Asegurar();
        Asegurar();
    }

    private static void Asegurar()
    {
        if (Instancia != null) return;
        if (FindAnyObjectByType<ControladorMapache>() == null) return;   // solo en niveles jugables
        new GameObject("SonidosJuego").AddComponent<SonidosJuego>();
    }

    private void Awake()
    {
        if (Instancia != null && Instancia != this) { Destroy(gameObject); return; }
        Instancia = this;

        Cargar("pasto", "Audio/Pisadas", "pasto_");
        Cargar("madera", "Audio/Pisadas", "madera_");
        Cargar("concreto", "Audio/Pisadas", "concreto_");
        Cargar("alfombra", "Audio/Pisadas", "alfombra_");
        Cargar("ladrido", "Audio/Animales", "ladrido_");
        Cargar("gruñido", "Audio/Animales", "grunido");
        Cargar("grito", "Audio/Voces", "guardia_grito");
        Cargar("sospecha", "Audio/Voces", "guardia_sospecha");
        Cargar("rendirse", "Audio/Voces", "guardia_rendirse");
        foreach (var n in new[] { "dash", "gancho_disparo", "gancho_impacto", "gancho_jalar", "sobrecarga_inicio", "sobrecarga_fin",
                                  "habilidad_lista", "habilidad_no_lista", "salto", "aterrizaje" })
            Cargar(n, "Audio/Habilidades", n);
        foreach (var n in new[] { "basurero_golpe", "monedas", "objeto_encontrado", "botin_completo" })
            Cargar(n, "Audio/Objetos", n);
        Cargar("atrapado", "Audio/Alerta", "atrapado");
        Cargar("alerta_sospecha", "Audio/Alerta", "sospecha");

        fuente2D = gameObject.AddComponent<AudioSource>();
        fuente2D.playOnAwake = false; fuente2D.spatialBlend = 0f;

        ambiente = gameObject.AddComponent<AudioSource>();
        ambiente.clip = Resources.Load<AudioClip>("Audio/Ambiente/grillos_loop");
        ambiente.loop = true; ambiente.spatialBlend = 0f; ambiente.volume = volumenAmbiente * volumenGeneral;
        if (ambiente.clip != null) ambiente.Play();
        tLadridoLejano = Random.Range(12f, 25f);
    }

    private void OnDestroy() { if (Instancia == this) Instancia = null; }

    private void Cargar(string clave, string carpeta, string prefijo)
    {
        var todos = Resources.LoadAll<AudioClip>(carpeta);
        var lista = new List<AudioClip>();
        foreach (var c in todos) if (c.name.StartsWith(prefijo)) lista.Add(c);
        if (lista.Count > 0) grupos[clave] = lista.ToArray();
    }

    private void Start()
    {
        jugador = FindAnyObjectByType<ControladorMapache>();
        if (jugador != null)
        {
            ultimaPos = jugador.transform.position;
            jugador.AlSaltar += () => Jugador("salto", 0.5f);
            jugador.AlAterrizar += () => Jugador("aterrizaje", 0.6f);
            jugador.AlHacerDash += () => Jugador("dash", 0.8f);
            jugador.AlLanzarGancho += () => Jugador("gancho_disparo", 0.8f);
            jugador.AlEngancharse += () => { Reproducir3D("gancho_impacto", jugador.PuntoGancho, 0.9f, 2f, 30f); Jugador("gancho_jalar", 0.6f); };
            jugador.AlActivarSobrecarga += () => Jugador("sobrecarga_inicio", 0.8f);
            jugador.AlTerminarSobrecarga += () => Jugador("sobrecarga_fin", 0.5f);
        }

        var botin = FindAnyObjectByType<GestorColeccionables>();
        if (botin != null)
        {
            botin.AlRecoger += c =>
            {
                if (c.tipo == Coleccionable.Tipo.BasureroDorado) { Reproducir3D("basurero_golpe", c.transform.position, 0.8f, 2f, 25f); Reproducir2D("monedas", 0.8f); }
                else { Reproducir2D("monedas", 0.7f); Reproducir2D("objeto_encontrado", 0.7f, 0f); }
            };
            botin.AlCompletarTodo += () => Reproducir2D("botin_completo", 0.9f, 0f);
        }

        var casa = GameObject.Find("Casa_Estructura");
        if (casa != null && casa.TryGetComponent<Renderer>(out var r)) sueloY = r.bounds.min.y;
    }

    // ------------------------------------------------------------------ bucle

    private void Update()
    {
        if (ambiente != null) ambiente.volume = volumenAmbiente * volumenGeneral;
        if (jugador == null) return;

        PisadasJugador();
        AvisosHabilidadLista();

        // perros ladrando a lo lejos (ambiente)
        tLadridoLejano -= Time.deltaTime;
        if (tLadridoLejano <= 0f)
        {
            tLadridoLejano = Random.Range(20f, 45f);
            Vector3 lejos = jugador.transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(35f, 60f);
            var f = Reproducir3D("ladrido", lejos, 0.35f, 15f, 90f);
            if (f != null) f.pitch = Random.Range(0.8f, 1.15f);
        }
    }

    private void PisadasJugador()
    {
        Vector3 p = jugador.transform.position;
        Vector3 d = p - ultimaPos; d.y = 0f;
        ultimaPos = p;
        if (!jugador.EnSuelo || jugador.HaciendoDash || jugador.UsandoGancho || jugador.ControlesBloqueados) { distanciaAcumulada = 0f; return; }

        distanciaAcumulada += d.magnitude;
        float paso = jugador.EnSigilo ? 0.42f : (jugador.SobrecargaActiva ? 0.75f : 0.55f);
        if (distanciaAcumulada < paso) return;
        distanciaAcumulada = 0f;

        float vol = jugador.EnSigilo ? 0.12f : (jugador.SobrecargaActiva ? 0.55f : 0.35f);
        var f = Reproducir3D(Superficie(p), p, vol, 1f, 15f);
        if (f != null) f.pitch = Random.Range(0.9f, 1.1f) * (jugador.SobrecargaActiva ? 1.1f : 1f);
    }

    /// <summary>Qué hay bajo los pies: madera/alfombra en la casa, pasto en el jardín, concreto en banqueta y calle.</summary>
    public string Superficie(Vector3 pos)
    {
        if (!Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, out var h, 1.2f, ~0, QueryTriggerInteraction.Ignore)) return "pasto";
        string n = h.collider.name;
        if (n.StartsWith("Casa_Muebles")) return "alfombra";
        if (n.StartsWith("Casa_")) return "madera";
        if (!float.IsNaN(sueloY) && h.point.y - sueloY > 0.05f) return "concreto";
        return "pasto";
    }

    /// <summary>"Ding" en el momento exacto en que termina la recarga de cada habilidad.</summary>
    private void AvisosHabilidadLista()
    {
        bool d = jugador.EnfriamientoDashRestante <= 0f, g = jugador.EnfriamientoGanchoRestante <= 0f, s = jugador.EnfriamientoSobrecargaRestante <= 0f;
        if (d && !dashListo) Reproducir2D("habilidad_lista", 0.35f);
        if (g && !ganchoListo) Reproducir2D("habilidad_lista", 0.25f);
        if (s && !sobrecargaLista) Reproducir2D("habilidad_lista", 0.45f);
        dashListo = d; ganchoListo = g; sobrecargaLista = s;
    }

    // ------------------------------------------------------------------ API pública (enemigos, alertas)

    public void VozGuardia(string tipo, Vector3 pos) => Reproducir3D(tipo, pos, tipo == "grito" ? 1f : 0.8f, 4f, tipo == "grito" ? 60f : 30f);
    public void Ladrido(Vector3 pos, float volumen = 1f) { var f = Reproducir3D("ladrido", pos, volumen, 5f, 70f); if (f != null) f.pitch = Random.Range(0.95f, 1.08f); }
    public void Gruñido(Vector3 pos) => Reproducir3D("gruñido", pos, 0.9f, 2f, 20f);
    public void PisadaEnemigo(Vector3 pos, float volumen) { var f = Reproducir3D(Superficie(pos), pos, volumen, 1f, 18f); if (f != null) f.pitch = Random.Range(0.75f, 0.9f); }
    public void Atrapado() => Reproducir2D("atrapado", 0.9f, 0f);
    public void AlertaSospecha() => Reproducir2D("alerta_sospecha", 0.35f, 0f);

    public AudioSource Reproducir3D(string clave, Vector3 pos, float volumen, float distMin, float distMax)
    {
        var clip = Elegir(clave);
        if (clip == null) return null;
        var go = new GameObject("Sonido " + clave);
        go.transform.position = pos;
        var f = go.AddComponent<AudioSource>();
        f.clip = clip; f.volume = volumen * volumenGeneral;
        f.spatialBlend = 1f; f.rolloffMode = AudioRolloffMode.Linear; f.minDistance = distMin; f.maxDistance = distMax;
        f.dopplerLevel = 0f;
        f.Play();
        Destroy(go, clip.length + 0.2f);
        return f;
    }

    public void Reproducir2D(string clave, float volumen, float variacionTono = 0.05f)
    {
        var clip = Elegir(clave);
        if (clip == null) return;
        fuente2D.pitch = 1f + Random.Range(-variacionTono, variacionTono);
        fuente2D.PlayOneShot(clip, volumen * volumenGeneral);
    }

    private void Jugador(string clave, float vol) { if (jugador != null) Reproducir3D(clave, jugador.transform.position + Vector3.up * 0.5f, vol, 2f, 25f); }

    private AudioClip Elegir(string clave) =>
        grupos.TryGetValue(clave, out var lista) && lista.Length > 0 ? lista[Random.Range(0, lista.Length)] : null;
}
