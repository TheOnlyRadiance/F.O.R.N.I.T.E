using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Cinemática de victoria al llegar a la zona de escape (la inicia GestorPartida):
///  1. La cámara se coloca frente al mapache (franjas de cine arriba y abajo) y él da un salto de festejo.
///  2. Todo el botín recogido (monedas por cada basurero + los objetos de la casa) aparece girando
///     sobre su cabeza y va cayendo, uno por uno, en un costal que carga en la espalda.
///  3. Confeti, otro festejo, se da la vuelta y sale corriendo hacia afuera del vecindario,
///     saltando por encima de la cerca.
///  4. Avisa (ListoParaTitulo) para que GestorPartida muestre "¡ESCAPASTE!".
/// Espacio / Enter / A adelanta directo al título.
/// </summary>
public class CinematicaEscape : MonoBehaviour
{
    /// <summary>True cuando ya se puede mostrar la pantalla de "¡ESCAPASTE!".</summary>
    public bool ListoParaTitulo { get; private set; }

    private static readonly Color Oro = new Color(1f, 0.78f, 0.25f);
    private static readonly int ParamVelocidad = Animator.StringToHash("Velocidad");
    private static readonly int ParamEnSuelo = Animator.StringToHash("EnSuelo");

    private class Item
    {
        public Transform t;
        public Vector3 escala;
        public float angulo, tAparece, tVuela;
        public bool guardado;
    }

    private Transform jugador;
    private Animator animator;
    private Camera cam;
    private Color colorZona;
    private float H;                     // altura del mapache (todo escala con ella)
    private Vector3 P0;                  // posición inicial del mapache
    private Vector3 salida;              // dirección horizontal hacia afuera del mapa
    private Vector3 haciaCamara;

    private Vector3 camInicioPos, camPlanoPos, camMirar;
    private Quaternion camInicioRot;

    private Transform costal;
    private float escalaCostal, pulsoCostal, escalaInv = 1f;
    private readonly List<Item> items = new();
    private readonly List<Material> materiales = new();

    private float t, tUltimoGuardado, tGiro, tCorrer, tSalto;
    private float distanciaCorrida, velCarrera;
    private bool festejoFinal, sonoSalto;
    private RectTransform barraArriba, barraAbajo;

    // ------------------------------------------------------------------ inicio

    public void Iniciar(ControladorMapache j, Vector3 direccionSalida, GestorColeccionables botin, Color zona, Transform mantenerCanvasDe)
    {
        jugador = j.transform;
        colorZona = zona;
        animator = j.GetComponentInChildren<Animator>();
        P0 = jugador.position;

        salida = direccionSalida; salida.y = 0f;
        salida = salida.sqrMagnitude > 0.01f ? salida.normalized : jugador.forward;

        // el mapache deja de responder a los controles: ahora lo mueve la cinemática
        j.ControlesBloqueados = true;
        j.enabled = false;
        var cc = j.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        H = MedirAltura(j, cc);

        // cámara
        cam = Camera.main;
        if (cam != null)
        {
            var seg = cam.GetComponent<CamaraSeguimiento>();
            if (seg != null) seg.enabled = false;
            camInicioPos = cam.transform.position;
            camInicioRot = cam.transform.rotation;
        }
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;

        // plano: un poco hacia dentro del mapa y de lado (3/4), así el mapache luego corre alejándose de la cámara
        Vector3 lado = Quaternion.Euler(0f, 28f, 0f) * -salida;
        camMirar = P0 + Vector3.up * H * 1.25f;
        float dist = Mathf.Max(2.6f, H * 3.8f);
        Vector3 dirCam = (lado * dist + Vector3.up * H * 1.1f).normalized;
        float libre = dist;
        if (Physics.SphereCast(camMirar, 0.25f, dirCam, out var hit, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            libre = Mathf.Max(1.2f, hit.distance - 0.2f);
        camPlanoPos = camMirar + dirCam * libre;
        haciaCamara = camPlanoPos - P0; haciaCamara.y = 0f; haciaCamara.Normalize();

        OcultarOtrosHUD(mantenerCanvasDe);
        CrearFranjas();
        CrearCostal();
        CrearBotin(botin);
    }

    private float MedirAltura(ControladorMapache j, CharacterController cc)
    {
        bool hay = false; Bounds b = default;
        foreach (var r in j.GetComponentsInChildren<Renderer>())
        {
            if (r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer || !r.enabled) continue;
            if (!hay) { b = r.bounds; hay = true; } else b.Encapsulate(r.bounds);
        }
        float h = hay ? b.max.y - jugador.position.y : (cc != null ? cc.height : 1f);
        return Mathf.Clamp(h, 0.4f, 2.5f);
    }

    private void OnDestroy()
    {
        foreach (var m in materiales) if (m != null) Destroy(m);
    }

    // ------------------------------------------------------------------ bucle

    private void Update()
    {
        if (jugador == null) return;
        float dt = Time.deltaTime;
        t += dt;

        if (!ListoParaTitulo && t > 0.6f && SaltarPulsado()) ListoParaTitulo = true;

        ActualizarFranjas();
        ActualizarMapache(dt);
        ActualizarBotin(dt);
        ActualizarCamara(dt);
    }

    private static bool SaltarPulsado()
    {
        var kb = Keyboard.current; var gp = Gamepad.current;
        return (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            || (gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame));
    }

    private void ActualizarMapache(float dt)
    {
        float vel = 0f;
        float altura = 0f;
        Quaternion mirarCamara = Quaternion.LookRotation(haciaCamara, Vector3.up);

        if (tCorrer <= 0f || t < tCorrer)
        {
            // voltear hacia la cámara y festejar
            Quaternion objetivo = mirarCamara;
            if (tGiro > 0f && t >= tGiro) objetivo = Quaternion.LookRotation(salida, Vector3.up);
            float spin = 0f;
            altura += Salto(0.35f, 0.55f, H * 0.55f, ref spin);
            if (festejoFinal) altura += Salto(tUltimoGuardado + 0.25f, 0.6f, H * 0.7f, ref spin);
            jugador.rotation = Quaternion.Slerp(jugador.rotation, objetivo, 1f - Mathf.Exp(-10f * dt)) ;
            if (spin != 0f) jugador.rotation = objetivo * Quaternion.Euler(0f, spin, 0f);
            jugador.position = P0 + Vector3.up * altura;
        }
        else
        {
            // ¡a correr! acelera, y salta por encima de la cerca
            velCarrera = Mathf.MoveTowards(velCarrera, Mathf.Max(6.5f, H * 8f), 18f * dt);
            distanciaCorrida += velCarrera * dt;
            vel = 5.5f;
            jugador.rotation = Quaternion.LookRotation(salida, Vector3.up);

            float k = (t - tSalto) / 0.95f;
            if (k > 0f && k < 1f)
            {
                altura = Mathf.Max(2.3f, H * 2.6f) * 4f * k * (1f - k);
                if (!sonoSalto)
                {
                    sonoSalto = true;
                    SonidosJuego.Instancia?.Reproducir2D("salto", 0.8f);
                    SonidosJuego.Instancia?.Reproducir2D("dash", 0.6f);
                }
            }
            jugador.position = P0 + salida * distanciaCorrida + Vector3.up * altura;

            if (!ListoParaTitulo && t >= tCorrer + 1.7f) ListoParaTitulo = true;
        }

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetFloat(ParamVelocidad, vel, 0.08f, dt);
            animator.SetBool(ParamEnSuelo, altura < 0.05f);
        }
    }

    /// <summary>Salto con vuelta completa entre t0 y t0+dur; devuelve la altura y escribe el giro en grados.</summary>
    private float Salto(float t0, float dur, float alto, ref float giro)
    {
        float k = (t - t0) / dur;
        if (k <= 0f || k >= 1f) return 0f;
        giro = Mathf.SmoothStep(0f, 360f, k);
        return alto * 4f * k * (1f - k);
    }

    private void ActualizarCamara(float dt)
    {
        if (cam == null) return;
        Vector3 pos; Vector3 mirar;
        if (tCorrer <= 0f || t < tCorrer)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.1f));
            pos = Vector3.Lerp(camInicioPos, camPlanoPos, k);
            // un leve vaivén para que el plano no se sienta congelado
            pos += Vector3.Cross(Vector3.up, haciaCamara) * Mathf.Sin(t * 0.6f) * 0.15f * k;
            mirar = camMirar;
            Quaternion rot = Quaternion.LookRotation(mirar - pos, Vector3.up);
            cam.transform.SetPositionAndRotation(pos, Quaternion.Slerp(camInicioRot, rot, k));
            return;
        }

        // la cámara se queda en su sitio, sube un poco y sigue con la mirada al mapache que se aleja
        float s = t - tCorrer;
        pos = camPlanoPos + Vector3.up * Mathf.Min(s * 0.5f, 1.2f);
        mirar = jugador.position + Vector3.up * H * 0.8f;
        Quaternion objetivo = Quaternion.LookRotation(mirar - pos, Vector3.up);
        cam.transform.position = Vector3.Lerp(cam.transform.position, pos, 1f - Mathf.Exp(-3f * dt));
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, objetivo, 1f - Mathf.Exp(-6f * dt));
    }

    // ------------------------------------------------------------------ botín y costal

    private void CrearCostal()
    {
        costal = new GameObject("Costal del botín").transform;
        costal.SetParent(jugador, false);
        escalaInv = 1f / Mathf.Max(0.01f, jugador.lossyScale.y);   // por si el mapache tiene escala distinta de 1
        costal.localPosition = new Vector3(0f, H * 0.62f, -H * 0.32f) * escalaInv;

        var bolsa = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(bolsa.GetComponent<Collider>());
        bolsa.transform.SetParent(costal, false);
        bolsa.transform.localScale = new Vector3(0.95f, 1f, 0.85f);
        bolsa.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.55f, 0.38f, 0.2f), 0f);

        var nudo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(nudo.GetComponent<Collider>());
        nudo.transform.SetParent(costal, false);
        nudo.transform.localPosition = new Vector3(0f, 0.52f, 0f);
        nudo.transform.localScale = new Vector3(0.4f, 0.3f, 0.4f);
        nudo.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.42f, 0.28f, 0.14f), 0f);

        // signo de dinero dorado pintado en el costal
        var signo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(signo.GetComponent<Collider>());
        signo.transform.SetParent(costal, false);
        signo.transform.localPosition = new Vector3(0f, 0f, -0.42f);
        signo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        signo.transform.localScale = new Vector3(0.38f, 0.02f, 0.38f);
        signo.GetComponent<Renderer>().sharedMaterial = Mat(Oro, 0.8f);

        costal.localScale = Vector3.zero;
    }

    private void CrearBotin(GestorColeccionables botin)
    {
        int basureros = botin != null ? botin.BasurerosEncontrados : 3;
        var modelos = new List<GameObject>();
        if (botin != null && botin.ModelosObjetosCasa != null)
            foreach (var m in botin.ModelosObjetosCasa) if (m != null) modelos.Add(m);

        float tam = Mathf.Max(0.28f, H * 0.42f);
        var nuevos = new List<Transform>();
        foreach (var m in modelos) { var c = Copiar(m, tam); if (c != null) nuevos.Add(c); }
        int monedas = Mathf.Clamp(basureros * 2, 3, 12);
        for (int i = 0; i < monedas; i++) nuevos.Add(Moneda(tam * 0.55f));

        // mezclar objetos y monedas para que caigan alternados
        for (int i = nuevos.Count - 1; i > 0; i--) { int r = Random.Range(0, i + 1); (nuevos[i], nuevos[r]) = (nuevos[r], nuevos[i]); }

        int n = nuevos.Count;
        float intervalo = Mathf.Min(0.16f, 1.6f / Mathf.Max(1, n));
        for (int i = 0; i < n; i++)
        {
            var it = new Item
            {
                t = nuevos[i],
                escala = nuevos[i].localScale,
                angulo = 360f * i / n,
                tAparece = 1.0f + i * 0.06f,
                tVuela = 2.1f + i * intervalo,
            };
            it.t.localScale = Vector3.zero;
            items.Add(it);
        }
        tUltimoGuardado = n > 0 ? items[n - 1].tVuela + 0.4f : 1.5f;
        tGiro = tUltimoGuardado + 1.1f;
        tCorrer = tGiro + 0.45f;
        tSalto = tCorrer + 0.5f;
    }

    private void ActualizarBotin(float dt)
    {
        // el costal aparece y se infla con cada objeto
        float objetivo = t < 0.9f ? 0f : Mathf.Max(0.32f, H * 0.42f) * (0.65f + 0.35f * Guardados() / Mathf.Max(1, items.Count));
        escalaCostal = Mathf.Lerp(escalaCostal, objetivo, 1f - Mathf.Exp(-10f * dt));
        pulsoCostal = Mathf.MoveTowards(pulsoCostal, 0f, dt * 2.5f);
        float p = 1f + 0.25f * Mathf.Sin(pulsoCostal * Mathf.PI);
        costal.localScale = new Vector3(escalaCostal * p, escalaCostal / Mathf.Sqrt(p), escalaCostal * p) * escalaInv;
        if (tCorrer > 0f && t > tCorrer) costal.localRotation = Quaternion.Euler(Mathf.Sin(t * 18f) * 10f, 0f, 0f);   // rebota al correr

        Vector3 centro = P0 + Vector3.up * H * 2.0f;
        float radio = Mathf.Max(0.8f, H * 1.15f);
        foreach (var it in items)
        {
            if (it.guardado) continue;
            if (t < it.tAparece) continue;

            float a = (it.angulo + t * 80f) * Mathf.Deg2Rad;
            Vector3 orbita = centro + new Vector3(Mathf.Cos(a), 0.15f * Mathf.Sin(t * 3f + it.angulo), Mathf.Sin(a)) * radio;

            if (t < it.tVuela)
            {
                float k = Mathf.Clamp01((t - it.tAparece) / 0.3f);
                it.t.position = orbita;
                it.t.localScale = it.escala * Elastico(k);
                it.t.Rotate(0f, 160f * dt, 0f, Space.World);
                continue;
            }

            // vuela en arco hasta el costal y se hace chiquito
            float v = Mathf.Clamp01((t - it.tVuela) / 0.38f);
            Vector3 destino = costal.position + Vector3.up * escalaCostal * 0.3f;
            Vector3 medio = (orbita + destino) * 0.5f + Vector3.up * H * 0.8f;
            float e = v * v;
            it.t.position = Vector3.Lerp(Vector3.Lerp(orbita, medio, e), Vector3.Lerp(medio, destino, e), e);
            it.t.localScale = it.escala * Mathf.Lerp(1f, 0.15f, e);
            it.t.Rotate(0f, 600f * dt, 0f, Space.World);
            if (v >= 1f)
            {
                it.guardado = true;
                Destroy(it.t.gameObject);
                pulsoCostal = 1f;
                SonidosJuego.Instancia?.Reproducir2D("monedas", 0.45f, 0.15f);
            }
        }

        if (!festejoFinal && items.Count > 0 && Guardados() == items.Count)
        {
            festejoFinal = true;
            tUltimoGuardado = t;
            Confeti(costal.position + Vector3.up * H * 0.3f);
            SonidosJuego.Instancia?.Reproducir2D("objeto_encontrado", 0.8f, 0f);
        }
        else if (!festejoFinal && items.Count == 0 && t > 1.5f)
        {
            festejoFinal = true;
            Confeti(P0 + Vector3.up * H);
        }
    }

    private int Guardados() { int c = 0; foreach (var it in items) if (it.guardado) c++; return c; }

    private static float Elastico(float k)
    {
        if (k <= 0f) return 0f;
        if (k >= 1f) return 1f;
        return 1f + Mathf.Pow(2f, -10f * k) * Mathf.Sin((k * 10f - 0.75f) * (2f * Mathf.PI / 3f));
    }

    /// <summary>Copia del modelo del botín, solo visual (sin scripts ni colisiones), centrada y del tamaño pedido.</summary>
    private Transform Copiar(GameObject prefab, float tam)
    {
        var raiz = new GameObject("Botin_" + prefab.name).transform;
        var apagado = new GameObject("tmp"); apagado.SetActive(false);   // instanciar apagado: que no corra Awake de nada
        var copia = Instantiate(prefab, apagado.transform);
        foreach (var c in copia.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var r in copia.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(r);
        foreach (var b in copia.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(b);
        copia.transform.SetParent(raiz, false);
        copia.transform.localPosition = Vector3.zero;
        copia.SetActive(true);
        Destroy(apagado);

        bool hay = false; Bounds bb = default;
        foreach (var r in copia.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!hay) { bb = r.bounds; hay = true; } else bb.Encapsulate(r.bounds);
        }
        if (!hay) { Destroy(raiz.gameObject); return null; }
        copia.transform.localPosition = -(bb.center - raiz.position);
        float mayor = Mathf.Max(bb.size.x, bb.size.y, bb.size.z);
        raiz.localScale = Vector3.one * (mayor > 0.001f ? tam / mayor : 1f);
        return raiz;
    }

    private Transform Moneda(float tam)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Moneda";
        Destroy(go.GetComponent<Collider>());
        go.transform.rotation = Quaternion.Euler(90f, 0f, Random.Range(0f, 360f));
        go.transform.localScale = new Vector3(tam, tam * 0.08f, tam);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Mat(Oro, 0.9f);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    private Material Mat(Color c, float brillo)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", brillo > 0f ? 0.75f : 0.15f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", brillo > 0f ? 0.8f : 0f);
        if (brillo > 0f && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * brillo);
        }
        materiales.Add(m);
        return m;
    }

    private void Confeti(Vector3 pos)
    {
        var go = new GameObject("Confeti");
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(-90f, 0f, 0f));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 1f; main.loop = false; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0.7f;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var g = new Gradient { mode = GradientMode.Fixed };
        g.SetKeys(new[]
        {
            new GradientColorKey(Oro, 0.25f), new GradientColorKey(colorZona, 0.5f),
            new GradientColorKey(new Color(1f, 0.35f, 0.45f), 0.75f), new GradientColorKey(new Color(0.4f, 0.7f, 1f), 1f),
        }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 40f; sh.radius = 0.15f;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = fade;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        var shP = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shP == null) shP = Shader.Find("Sprites/Default");
        var mp = new Material(shP);
        materiales.Add(mp);
        rend.sharedMaterial = mp;
        ps.Play();
    }

    // ------------------------------------------------------------------ HUD

    /// <summary>Esconde los HUD del juego (habilidades, botín, alerta) para que se vea como cinemática.</summary>
    private static void OcultarOtrosHUD(Transform mantener)
    {
        foreach (var c in FindObjectsByType<Canvas>())
        {
            if (!c.isRootCanvas) continue;
            if (mantener != null && c.transform.IsChildOf(mantener)) continue;
            if (c.renderMode == RenderMode.WorldSpace) continue;
            c.enabled = false;
        }
    }

    private void CrearFranjas()
    {
        var go = new GameObject("Franjas de cine", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 29;   // debajo del HUD de la partida (ahí va el título de victoria)
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        RectTransform Barra(string n, float y)
        {
            var b = new GameObject(n, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)b.transform;
            rt.SetParent(go.transform, false);
            rt.anchorMin = new Vector2(0f, y); rt.anchorMax = new Vector2(1f, y); rt.pivot = new Vector2(0.5f, y);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(0f, 0f);
            var img = b.GetComponent<Image>(); img.color = Color.black; img.raycastTarget = false;
            return rt;
        }
        barraArriba = Barra("Arriba", 1f);
        barraAbajo = Barra("Abajo", 0f);
    }

    private void ActualizarFranjas()
    {
        float alto = 120f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.7f));
        barraArriba.sizeDelta = barraAbajo.sizeDelta = new Vector2(0f, alto);
    }
}
