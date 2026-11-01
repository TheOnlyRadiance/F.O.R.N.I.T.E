using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Arma la escena Nivel1: vecindario (Kenney City Kit Suburban + Roads, CC0) con una casa grande
/// de dos plantas amueblada (Kenney Furniture Kit, CC0), mapache jugable, cámara, luces y HUD del dash.
/// Se ofrece automáticamente una vez; después se puede repetir desde
/// el menú  F.O.R.N.I.T.E > Construir Nivel 1.
/// </summary>
[InitializeOnLoad]
public static class ConstruirNivel1
{
    private const string RutaEscena = "Assets/Scenes/Nivel1.unity";
    private const string RutaCasa = "Assets/Modelos/Vecindario/Nivel1Mundo.fbx";
    private const string RutaTexturasCasa = "Assets/Modelos/Vecindario/Texturas";
    private const string RutaMapache = "Assets/Modelos/Mapache/Mapache.fbx";
    private const string RutaAnimator = "Assets/Animaciones/Mapache.controller";
    private const string RutaMatEstela = "Assets/Materiales/EstelaDash.mat";
    private const string ClavePref = "FORNITE_Nivel1_Ofrecido_v3";

    /// <summary>Tamaño del mapache respecto al modelo original (1.45 m → ~1 m).</summary>
    private const float EscalaMapache = 0.7f;
    private const float AnchoCasaEsperado = 160f;   // ancho total del vecindario
    private const float EnfriamientoDash = 10f;

    private const string RutaColeccionables = "Assets/Modelos/Coleccionables/";
    private const string ClavePrefBotin = "FORNITE_Botin_Ofrecido_v1";

    private const string ClavePrefNoche = "FORNITE_Noche_Ofrecido_v1";
    private const string RutaCieloTex = "Assets/Materiales/CieloNocturno.png";
    private const string RutaCieloMat = "Assets/Materiales/CieloNocturno.mat";

    static ConstruirNivel1()
    {
        EditorApplication.delayCall += OfrecerUnaVez;
        EditorApplication.delayCall += OfrecerBotinUnaVez;
        EditorApplication.delayCall += OfrecerNocheUnaVez;
        EditorApplication.delayCall += OfrecerEnemigosUnaVez;
    }

    // ===================================================================== ENEMIGOS

    private const string ClavePrefEnemigos = "FORNITE_Enemigos_Ofrecido_v2";
    private const string RutaGuardia = "Assets/Modelos/Enemigos/Guardia.fbx";
    private const string RutaPerro = "Assets/Modelos/Enemigos/Perro.fbx";
    private const string RutaNavMesh = "Assets/Scenes/Nivel1_NavMesh.asset";

    private static void OfrecerEnemigosUnaVez()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorPrefs.GetBool(ClavePrefEnemigos, false)) return;
        EditorPrefs.SetBool(ClavePrefEnemigos, true);
        if (EditorUtility.DisplayDialog("F.O.R.N.I.T.E",
                "¿Agregar GUARDIAS y PERRO al Nivel1?\n\n• Guardia humano con uniforme y linterna dentro de la casa (ambas plantas)\n• Guardia rondando el exterior de la casa\n• Perro guardián en el patio trasero\n• Se hornea el NavMesh para que caminen\n\n(No se reconstruye el resto del nivel.)\nTambién está en F.O.R.N.I.T.E > Agregar guardias y perro al Nivel 1",
                "Agregar", "Más tarde"))
            AgregarEnemigosMenu();
    }

    [MenuItem("F.O.R.N.I.T.E/Agregar guardias y perro al Nivel 1")]
    public static void AgregarEnemigosMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var escena = EditorSceneManager.OpenScene(RutaEscena, OpenSceneMode.Single);
        if (AgregarEnemigos(escena))
        {
            EditorSceneManager.MarkSceneDirty(escena);
            EditorSceneManager.SaveScene(escena);
        }
    }

    private static bool AgregarEnemigos(UnityEngine.SceneManagement.Scene escena)
    {
        var guardiaFBX = AssetDatabase.LoadAssetAtPath<GameObject>(RutaGuardia);
        var perroFBX = AssetDatabase.LoadAssetAtPath<GameObject>(RutaPerro);
        var raices = escena.GetRootGameObjects();
        var mundo = raices.FirstOrDefault(r => r.name == "Nivel1Mundo");
        var jugadorGO = raices.FirstOrDefault(r => r.name == "Mapache");
        if (guardiaFBX == null || perroFBX == null || mundo == null)
        {
            EditorUtility.DisplayDialog("Enemigos", "Faltan los modelos de Assets/Modelos/Enemigos o el objeto Nivel1Mundo en la escena.", "OK");
            return false;
        }
        foreach (var r in raices)
            if (r.name is "Enemigos" or "NavMesh" or "Alerta") Object.DestroyImmediate(r);

        // --- 1) agente más delgado para que quepa por las puertas (1.1 m)
        ConfigurarAgenteNavMesh(0.28f, 1.8f, 0.4f, 45f);

        // --- 2) hornear NavMesh con los colliders del mundo (sin el jugador)
        bool jugadorActivo = jugadorGO != null && jugadorGO.activeSelf;
        if (jugadorGO != null) jugadorGO.SetActive(false);
        var navGO = new GameObject("NavMesh");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(navGO, escena);
        var superficie = navGO.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
        superficie.collectObjects = Unity.AI.Navigation.CollectObjects.All;
        superficie.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        Physics.SyncTransforms();
        EditorUtility.DisplayProgressBar("F.O.R.N.I.T.E", "Horneando NavMesh del vecindario...", 0.5f);
        try { superficie.BuildNavMesh(); }
        finally { EditorUtility.ClearProgressBar(); if (jugadorGO != null) jugadorGO.SetActive(jugadorActivo); }
        AssetDatabase.DeleteAsset(RutaNavMesh);
        if (superficie.navMeshData != null) AssetDatabase.CreateAsset(superficie.navMeshData, RutaNavMesh);

        // --- 3) animaciones
        var ctrlGuardia = CrearAnimatorEnemigo(RutaGuardia, "Assets/Animaciones/Guardia.controller", ("Idle", 0f, 1f), ("Walk", 1.6f, 1f), ("Run", 4.4f, 1f));
        var ctrlPerro = CrearAnimatorEnemigo(RutaPerro, "Assets/Animaciones/Perro.controller", ("Idle", 0f, 1f), ("Walk", 1.5f, 1f), ("Gallop", 5f, 1f));
        guardiaFBX = AssetDatabase.LoadAssetAtPath<GameObject>(RutaGuardia);
        perroFBX = AssetDatabase.LoadAssetAtPath<GameObject>(RutaPerro);
        Quaternion correccion = CorreccionOrientacionModelos();

        // --- 4) puntos de referencia del mundo
        var casaR = mundo.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "Casa_Estructura");
        Bounds casa = casaR != null ? casaR.bounds : new Bounds(mundo.transform.position, Vector3.one * 20f);
        Transform Marca(string n) => BuscarHijo(mundo.transform, n);
        Vector3 Piso(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 0.2f, Vector3.down, out var h, 6f, ~0, QueryTriggerInteraction.Ignore)) p = h.point;
            return UnityEngine.AI.NavMesh.SamplePosition(p, out var nh, 1.5f, UnityEngine.AI.NavMesh.AllAreas) ? nh.position : p;
        }
        var puertaTrasera = Marca("Porche_10"); var puertaFrente = Marca("Porche_09");
        Vector3 centro = new Vector3(casa.center.x, casa.min.y, casa.center.z);
        Vector3 atras = puertaTrasera != null ? puertaTrasera.position - centro : Vector3.forward; atras.y = 0f; atras.Normalize();
        Vector3 lado = Vector3.Cross(Vector3.up, atras);
        float medioAncho = Mathf.Abs(Vector3.Dot(casa.extents, new Vector3(Mathf.Abs(lado.x), 0, Mathf.Abs(lado.z))));
        float medioFondo = Mathf.Abs(Vector3.Dot(casa.extents, new Vector3(Mathf.Abs(atras.x), 0, Mathf.Abs(atras.z))));

        var grupo = new GameObject("Enemigos");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(grupo, escena);

        // guardia dentro de la casa: recorre los cuartos de ambas plantas (marcadores de luz de cada cuarto)
        int[] orden = { 1, 0, 3, 0, 1, 2, 6, 2, 1, 4, 11, 8, 7, 8, 9, 13, 9, 8, 11, 10, 11, 4 };
        var rutaCasa = orden.Select(i => Marca($"Luz_{i:00}")).Where(t => t != null).Select(t => Piso(t.position)).ToList();
        CrearEnemigo(grupo.transform, "Guardia Casa", EnemigoVigilante.Tipo.Guardia, guardiaFBX, ctrlGuardia, correccion, 1.85f,
                     rutaCasa.Count > 0 ? rutaCasa[0] : Piso(centro), rutaCasa, Vector3.zero, Vector3.zero);

        // guardia exterior: rodea la casa y pasa por la banqueta del frente
        float m = 3.5f;
        Vector3 E(float a, float b) => Piso(centro + lado * a + atras * b);
        var rutaFuera = new List<Vector3>
        {
            E(-(medioAncho + m), -(medioFondo + m)), E(-(medioAncho + 6f), -(medioFondo + 9f)), E(medioAncho + 6f, -(medioFondo + 9f)),
            E(medioAncho + m, -(medioFondo + m)), E(medioAncho + m, medioFondo + m), E(-(medioAncho + m), medioFondo + m)
        };
        CrearEnemigo(grupo.transform, "Guardia Patio", EnemigoVigilante.Tipo.Guardia, guardiaFBX, ctrlGuardia, correccion, 1.85f,
                     rutaFuera[1], rutaFuera, Vector3.zero, Vector3.zero);

        // perro: patio trasero (GDD: "un perro guardián en el patio")
        Vector3 zona = centro + atras * (medioFondo + 6.5f);
        var tamZona = new Vector3(Mathf.Abs(lado.x) * (medioAncho * 2f + 4f) + Mathf.Abs(atras.x) * 8f, 4f,
                                  Mathf.Abs(lado.z) * (medioAncho * 2f + 4f) + Mathf.Abs(atras.z) * 8f);
        CrearEnemigo(grupo.transform, "Perro", EnemigoVigilante.Tipo.Perro, perroFBX, ctrlPerro, correccion, 0.85f,
                     Piso(zona), null, zona, tamZona);

        // --- 5) gestor de alerta (detección, atrapado, reinicio)
        var alerta = new GameObject("Alerta");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(alerta, escena);
        alerta.AddComponent<GestorAlerta>();
        Debug.Log($"[Enemigos] 2 guardias y 1 perro agregados. Ruta interior: {rutaCasa.Count} puntos. NavMesh guardado en {RutaNavMesh}.");
        return true;
    }

    private static void CrearEnemigo(Transform padre, string nombre, EnemigoVigilante.Tipo tipo, GameObject fbx, AnimatorController ctrl,
                                     Quaternion correccion, float altura, Vector3 pos, List<Vector3> ruta, Vector3 zonaCentro, Vector3 zonaTam)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.position = pos;

        var modelo = (GameObject)PrefabUtility.InstantiatePrefab(fbx, go.transform);
        modelo.name = "Modelo";
        modelo.transform.localPosition = Vector3.zero;
        modelo.transform.localRotation = correccion;
        EscalarAAltura(modelo.transform, altura);
        var anim = modelo.GetComponent<Animator>();
        if (anim == null) anim = modelo.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        var agente = go.AddComponent<UnityEngine.AI.NavMeshAgent>();
        bool perro = tipo == EnemigoVigilante.Tipo.Perro;
        agente.radius = perro ? 0.3f : 0.28f; agente.height = perro ? 0.9f : 1.8f;
        agente.angularSpeed = 540f; agente.acceleration = 12f; agente.stoppingDistance = 0.2f;
        agente.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.LowQualityObstacleAvoidance;

        Light linterna = null;
        if (!perro)
        {
            var l = new GameObject("Linterna");
            l.transform.SetParent(go.transform, false);
            l.transform.localPosition = new Vector3(0.25f, 1.3f, 0.35f);
            l.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            linterna = l.AddComponent<Light>();
            linterna.type = LightType.Spot; linterna.spotAngle = 60f; linterna.innerSpotAngle = 35f;
            linterna.range = 14f; linterna.intensity = 14f; linterna.color = new Color(1f, 0.95f, 0.8f);
            linterna.shadows = LightShadows.Soft;
        }

        var ia = go.AddComponent<EnemigoVigilante>();
        var so = new SerializedObject(ia);
        so.FindProperty("tipo").enumValueIndex = (int)tipo;
        so.FindProperty("animator").objectReferenceValue = anim;
        so.FindProperty("linterna").objectReferenceValue = linterna;
        var r = so.FindProperty("ruta"); r.arraySize = ruta?.Count ?? 0;
        for (int i = 0; i < r.arraySize; i++) r.GetArrayElementAtIndex(i).vector3Value = ruta[i];
        so.FindProperty("zonaCentro").vector3Value = zonaCentro;
        so.FindProperty("zonaTamano").vector3Value = zonaTam;
        if (perro)
        {
            so.FindProperty("velocidadPatrulla").floatValue = 1.5f;
            so.FindProperty("velocidadInvestigar").floatValue = 2.6f;
            so.FindProperty("velocidadPersecucion").floatValue = 5f;      // la Sobrecarga (5.25 m/s) le gana
            so.FindProperty("alcanceVision").floatValue = 9f;
            so.FindProperty("anguloVision").floatValue = 140f;
            so.FindProperty("alturaOjos").floatValue = 0.6f;
            so.FindProperty("radioOlfato").floatValue = 3.2f;
            so.FindProperty("multiplicadorOido").floatValue = 1.3f;
            so.FindProperty("distanciaAtrapar").floatValue = 0.9f;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigurarAgenteNavMesh(float radio, float alto, float escalon, float pendiente)
    {
        var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset").FirstOrDefault();
        if (asset == null) return;
        var so = new SerializedObject(asset);
        var lista = so.FindProperty("m_Settings");
        if (lista == null || lista.arraySize == 0) return;
        var s = lista.GetArrayElementAtIndex(0);
        s.FindPropertyRelative("agentRadius").floatValue = radio;
        s.FindPropertyRelative("agentHeight").floatValue = alto;
        s.FindPropertyRelative("agentClimb").floatValue = escalon;
        s.FindPropertyRelative("agentSlope").floatValue = pendiente;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static AnimatorController CrearAnimatorEnemigo(string rutaFBX, string rutaCtrl, params (string clip, float umbral, float escala)[] estados)
    {
        if (AssetImporter.GetAtPath(rutaFBX) is ModelImporter mi)
        {
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
                c.loopTime = !c.name.Contains("Jump") && !c.name.Contains("Death") && !c.name.Contains("Attack") && !c.name.Contains("Grounded");
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
        }
        var todos = AssetDatabase.LoadAllAssetsAtPath(rutaFBX).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
        System.IO.Directory.CreateDirectory("Assets/Animaciones");
        AssetDatabase.DeleteAsset(rutaCtrl);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(rutaCtrl);
        ctrl.AddParameter("Velocidad", AnimatorControllerParameterType.Float);
        ctrl.CreateBlendTreeInController("Movimiento", out BlendTree arbol, 0);
        arbol.blendType = BlendTreeType.Simple1D; arbol.blendParameter = "Velocidad"; arbol.useAutomaticThresholds = false;
        foreach (var (clip, umbral, escala) in estados)
        {
            var c = todos.FirstOrDefault(x => x.name == clip) ?? todos.FirstOrDefault(x => x.name.Contains(clip));
            if (c == null) { Debug.LogWarning($"[Enemigos] No encontré la animación '{clip}' en {rutaFBX}"); continue; }
            arbol.AddChild(c, umbral);
        }
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    /// <summary>Los modelos se exportaron igual que el mapache: usamos su corrección para que miren hacia +Z.</summary>
    private static Quaternion CorreccionOrientacionModelos()
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(RutaMapache);
        if (p == null) return Quaternion.identity;
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(p);
        tmp.transform.position = new Vector3(0f, -500f, 0f);
        tmp.transform.rotation = Quaternion.identity;
        OrientarModeloHaciaAdelante(tmp.transform);
        var q = tmp.transform.localRotation;
        Object.DestroyImmediate(tmp);
        return q;
    }

    private static void EscalarAAltura(Transform t, float altura)
    {
        var rends = t.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
        if (b.size.y > 0.01f) t.localScale *= altura / b.size.y;
        // apoyar los pies en el suelo
        rends = t.GetComponentsInChildren<Renderer>();
        b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
        t.position += Vector3.up * (t.parent.position.y - b.min.y);
    }

    private static void OfrecerNocheUnaVez()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorPrefs.GetBool(ClavePrefNoche, false)) return;
        EditorPrefs.SetBool(ClavePrefNoche, true);
        if (EditorUtility.DisplayDialog("F.O.R.N.I.T.E",
                "¿Convertir el Nivel1 en NOCHE?\n\n• Cielo estrellado con luna\n• Luz de luna azulada y niebla nocturna\n• Faroles de la calle y luces de porche encendidos\n\n(No se reconstruye el resto del nivel.)\nTambién está en F.O.R.N.I.T.E > Aplicar noche al Nivel 1",
                "Aplicar noche", "Más tarde"))
            AplicarNocheMenu();
    }

    [MenuItem("F.O.R.N.I.T.E/Aplicar noche al Nivel 1")]
    public static void AplicarNocheMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var escena = EditorSceneManager.OpenScene(RutaEscena, OpenSceneMode.Single);
        AplicarNoche(escena);
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
    }

    /// <summary>Luna, cielo nocturno, luz ambiental, niebla, faroles y porches. Se puede repetir sin duplicar luces.</summary>
    private static void AplicarNoche(UnityEngine.SceneManagement.Scene escena)
    {
        // --- luna (luz direccional)
        Light luna = null;
        foreach (var l in Object.FindObjectsByType<Light>())
            if (l.type == LightType.Directional && l.gameObject.scene == escena) { luna = l; break; }
        if (luna == null)
        {
            var go = new GameObject("Luna");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, escena);
            luna = go.AddComponent<Light>(); luna.type = LightType.Directional;
        }
        luna.name = "Luna";
        luna.color = new Color(0.62f, 0.72f, 1f);
        luna.intensity = 0.45f;
        luna.shadows = LightShadows.Soft;
        luna.shadowStrength = 0.8f;
        luna.transform.rotation = Quaternion.Euler(38f, 145f, 0f);

        // --- cielo estrellado
        var cielo = CrearCieloNocturno();
        if (cielo != null) RenderSettings.skybox = cielo;
        RenderSettings.sun = luna;

        // --- luz ambiental y niebla
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.13f, 0.16f, 0.30f);
        RenderSettings.ambientEquatorColor = new Color(0.07f, 0.08f, 0.14f);
        RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.05f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.04f, 0.06f, 0.12f);
        RenderSettings.fogDensity = 0.011f;
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;

        // --- luces de la calle y porches (desde los marcadores del FBX)
        foreach (var raiz in escena.GetRootGameObjects())
            if (raiz.name == "Luces Noche") Object.DestroyImmediate(raiz);
        var mundo = escena.GetRootGameObjects().FirstOrDefault(r => r.name == "Nivel1Mundo");
        if (mundo != null)
        {
            var grupo = new GameObject("Luces Noche");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(grupo, escena);
            int faroles = 0, porches = 0;
            foreach (var t in mundo.GetComponentsInChildren<Transform>())
            {
                if (t.name.StartsWith("Farol_"))
                {
                    var go = new GameObject("Farol " + t.name.Substring(6));
                    go.transform.SetParent(grupo.transform, false);
                    go.transform.position = t.position;
                    go.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Spot; l.spotAngle = 115f; l.innerSpotAngle = 60f;
                    l.range = 16f; l.intensity = 9f; l.color = new Color(1f, 0.8f, 0.52f);
                    l.shadows = LightShadows.None;
                    // pequeño halo en la bombilla
                    var halo = new GameObject("Bombilla");
                    halo.transform.SetParent(go.transform, false);
                    var lh = halo.AddComponent<Light>();
                    lh.type = LightType.Point; lh.range = 2.5f; lh.intensity = 3f; lh.color = l.color; lh.shadows = LightShadows.None;
                    faroles++;
                }
                else if (t.name.StartsWith("Porche_"))
                {
                    var go = new GameObject("Porche " + t.name.Substring(7));
                    go.transform.SetParent(grupo.transform, false);
                    go.transform.position = t.position;
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point; l.range = 7f; l.intensity = 3f; l.color = new Color(1f, 0.74f, 0.42f);
                    l.shadows = LightShadows.None;
                    porches++;
                }
            }
            Debug.Log($"[Noche] {faroles} faroles y {porches} luces de porche encendidos.");
        }

        // --- luces interiores un poco más cálidas e intensas de noche
        foreach (var raiz in escena.GetRootGameObjects())
            if (raiz.name == "Luces Casa")
                foreach (var l in raiz.GetComponentsInChildren<Light>())
                { l.intensity = 2.4f; l.range = 7.5f; l.color = new Color(1f, 0.82f, 0.6f); }

        // --- la cámara usa el cielo como fondo
        var cam = Camera.main;
        if (cam != null) cam.clearFlags = CameraClearFlags.Skybox;
    }

    /// <summary>Genera (una vez) una textura panorámica de cielo nocturno con estrellas y luna, y su material de skybox.</summary>
    private static Material CrearCieloNocturno()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RutaCieloMat);
        if (mat != null) return mat;

        System.IO.Directory.CreateDirectory("Assets/Materiales");
        const int W = 4096, H = 2048;
        var px = new Color32[W * H];
        var rnd = new System.Random(1234);
        Color horizonte = new Color(0.07f, 0.10f, 0.22f), cenit = new Color(0.005f, 0.008f, 0.03f), suelo = new Color(0.02f, 0.025f, 0.05f);
        for (int y = 0; y < H; y++)
        {
            float lat = (y / (float)(H - 1)) * 2f - 1f;            // -1 abajo, 1 arriba
            Color c = lat >= 0 ? Color.Lerp(horizonte, cenit, Mathf.Pow(lat, 0.6f)) : Color.Lerp(horizonte, suelo, Mathf.Pow(-lat, 0.5f));
            var c32 = (Color32)c;
            for (int x = 0; x < W; x++) px[y * W + x] = c32;
        }
        // vía láctea tenue (banda inclinada)
        for (int i = 0; i < 260000; i++)
        {
            float u = (float)rnd.NextDouble();
            float banda = Mathf.Sin(u * Mathf.PI * 2f) * 0.25f + 0.55f + (float)(rnd.NextDouble() - 0.5) * 0.16f;
            int x = (int)(u * (W - 1)), y = (int)(Mathf.Clamp01(banda) * (H - 1));
            if (y < H / 2 + 20) continue;
            Sumar(px, W, x, y, new Color(0.10f, 0.11f, 0.16f) * 0.25f);
        }
        // estrellas
        for (int i = 0; i < 9000; i++)
        {
            int x = rnd.Next(W);
            float v = Mathf.Sqrt((float)rnd.NextDouble());          // más densidad hacia arriba
            int y = H / 2 + 8 + (int)(v * (H / 2 - 10));
            float b = Mathf.Pow((float)rnd.NextDouble(), 3f);
            Color col = Color.Lerp(new Color(0.75f, 0.82f, 1f), new Color(1f, 0.92f, 0.8f), (float)rnd.NextDouble()) * (0.35f + b * 1.1f);
            Sumar(px, W, x, y, col);
            if (b > 0.7f)
            {
                Color brillo = col * 0.35f;
                Sumar(px, W, x + 1, y, brillo); Sumar(px, W, x - 1, y, brillo);
                Sumar(px, W, x, y + 1, brillo); Sumar(px, W, x, y - 1, brillo);
            }
        }
        // luna
        int mx = (int)(W * 0.15f), my = (int)(H * 0.78f); float rLuna = 46f;
        for (int y = my - 160; y <= my + 160; y++)
            for (int x = mx - 160; x <= mx + 160; x++)
            {
                if (x < 0 || x >= W || y < 0 || y >= H) continue;
                float d = Mathf.Sqrt((x - mx) * (x - mx) + (y - my) * (y - my));
                if (d < rLuna)
                {
                    float n = Mathf.PerlinNoise(x * 0.08f, y * 0.08f);
                    Color lc = Color.Lerp(new Color(0.78f, 0.80f, 0.86f), new Color(0.98f, 0.97f, 0.92f), 0.4f + 0.6f * n);
                    float borde = Mathf.Clamp01(rLuna - d);
                    px[y * W + x] = Color.Lerp(px[y * W + x], lc, borde);
                }
                else
                {
                    float g = Mathf.Exp(-(d - rLuna) / 38f) * 0.35f;
                    Sumar(px, W, x, y, new Color(0.55f, 0.62f, 0.85f) * g);
                }
            }

        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.SetPixels32(px); tex.Apply();
        System.IO.File.WriteAllBytes(RutaCieloTex, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(RutaCieloTex);
        if (AssetImporter.GetAtPath(RutaCieloTex) is TextureImporter ti)
        {
            ti.mipmapEnabled = false; ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;
            ti.maxTextureSize = 4096; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        var shader = Shader.Find("Skybox/Panoramic");
        if (shader == null) return null;
        mat = new Material(shader);
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(RutaCieloTex));
        mat.SetFloat("_Mapping", 1f);      // latitud-longitud
        mat.SetFloat("_ImageType", 0f);    // 360°
        mat.SetFloat("_Exposure", 1.0f);
        AssetDatabase.CreateAsset(mat, RutaCieloMat);
        return mat;
    }

    private static void Sumar(Color32[] px, int w, int x, int y, Color c)
    {
        int h = px.Length / w;
        if (x < 0) x += w; if (x >= w) x -= w;
        if (y < 0 || y >= h) return;
        Color o = px[y * w + x];
        px[y * w + x] = new Color(Mathf.Min(1, o.r + c.r), Mathf.Min(1, o.g + c.g), Mathf.Min(1, o.b + c.b));
    }

    private static void OfrecerBotinUnaVez()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorPrefs.GetBool(ClavePrefBotin, false)) return;
        EditorPrefs.SetBool(ClavePrefBotin, true);
        if (EditorUtility.DisplayDialog("F.O.R.N.I.T.E",
                "¿Agregar ahora el BOTÍN al Nivel1?\n\n• 8 basureros dorados en el vecindario\n• 4 objetos escondidos en la casa\n\nSe colocan al azar en cada partida y se recogen con la tecla E.\n(No se reconstruye el resto del nivel.)\n\nTambién está en F.O.R.N.I.T.E > Agregar botín al Nivel 1",
                "Agregar", "Más tarde"))
            AgregarBotinMenu();
    }

    [MenuItem("F.O.R.N.I.T.E/Agregar botín al Nivel 1")]
    public static void AgregarBotinMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var escena = EditorSceneManager.OpenScene(RutaEscena, OpenSceneMode.Single);
        if (AgregarBotin(escena))
        {
            EditorSceneManager.MarkSceneDirty(escena);
            EditorSceneManager.SaveScene(escena);
        }
    }

    /// <summary>Crea (o reemplaza) el objeto "Botin" con el GestorColeccionables y los modelos asignados.</summary>
    private static bool AgregarBotin(UnityEngine.SceneManagement.Scene escena)
    {
        var basurero = AssetDatabase.LoadAssetAtPath<GameObject>(RutaColeccionables + "BasureroDorado.fbx");
        string[] archivos = { "CofreJoyas", "Alcancia", "RelojOro", "Diamante" };
        var objetos = archivos.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(RutaColeccionables + n + ".fbx")).ToArray();
        if (basurero == null || objetos.Any(o => o == null))
        {
            EditorUtility.DisplayDialog("Botín", "Faltan modelos en " + RutaColeccionables, "OK");
            return false;
        }
        foreach (var raiz in escena.GetRootGameObjects())
            if (raiz.name == "Botin") Object.DestroyImmediate(raiz);

        var go = new GameObject("Botin");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, escena);
        var gestor = go.AddComponent<GestorColeccionables>();
        var so = new SerializedObject(gestor);
        so.FindProperty("modeloBasurero").objectReferenceValue = basurero;
        var arr = so.FindProperty("modelosObjetosCasa");
        arr.arraySize = objetos.Length;
        for (int i = 0; i < objetos.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = objetos[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log("[Botín] Agregado al Nivel1: 8 basureros dorados + 4 objetos en la casa (tecla E).");
        return true;
    }

    private static void OfrecerUnaVez()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorPrefs.GetBool(ClavePref, false)) return;
        EditorPrefs.SetBool(ClavePref, true);
        if (EditorUtility.DisplayDialog("F.O.R.N.I.T.E",
                "¿Construir ahora el Nivel1?\n\nSe abrirá la escena Nivel1 y se agregarán la casa, el mapache jugable, la cámara y las luces.\n\n(También puedes hacerlo luego desde el menú F.O.R.N.I.T.E > Construir Nivel 1)",
                "Construir", "Más tarde"))
            Construir();
    }

    [MenuItem("F.O.R.N.I.T.E/Construir Nivel 1")]
    public static void Construir()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // ---------- 1) importación de modelos
        ConfigurarCasa();
        ConfigurarMapache();
        var controlador = CrearAnimator();
        var matEstela = CrearMaterialEstela();

        var prefabCasa = AssetDatabase.LoadAssetAtPath<GameObject>(RutaCasa);
        var prefabMapache = AssetDatabase.LoadAssetAtPath<GameObject>(RutaMapache);
        if (prefabCasa == null || prefabMapache == null)
        {
            EditorUtility.DisplayDialog("Nivel 1", "No se encontraron los modelos:\n" + RutaCasa + "\n" + RutaMapache, "OK");
            return;
        }

        // ---------- 2) escena
        var escena = EditorSceneManager.OpenScene(RutaEscena, OpenSceneMode.Single);
        foreach (var raiz in escena.GetRootGameObjects())
            if (raiz.name is "CasaNivel1" or "CasaGrande" or "Nivel1Mundo" or "Mapache" or "Luces Casa" or "HUD")
                Object.DestroyImmediate(raiz);

        // vecindario + casa
        var casa = (GameObject)PrefabUtility.InstantiatePrefab(prefabCasa, escena);
        casa.name = "Nivel1Mundo";
        casa.isStatic = true;
        foreach (var t in casa.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        CorregirEscala(casa.transform, AnchoCasaEsperado, "Mundo", usarAncho: true);
        Transform spawn = BuscarHijo(casa.transform, "SpawnMapache");

        // límites invisibles del mapa: conservan el collider pero no se dibujan
        foreach (var r in casa.GetComponentsInChildren<MeshRenderer>())
            if (r.name.StartsWith("ColBorde")) r.enabled = false;

        // luces en cada cuarto
        var luces = new GameObject("Luces Casa");
        foreach (var marca in casa.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Luz_")))
        {
            var go = new GameObject("Luz " + marca.name.Substring(4));
            go.transform.SetParent(luces.transform, false);
            go.transform.position = marca.position;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 7f;
            l.intensity = 1.8f;
            l.color = new Color(1f, 0.9f, 0.75f);
            l.shadows = LightShadows.None;
        }

        // ---------- 3) jugador
        var jugador = new GameObject("Mapache");
        jugador.transform.position = (spawn != null ? spawn.position : Vector3.zero) + Vector3.up * 0.05f;
        jugador.transform.rotation = Quaternion.identity;

        var cc = jugador.AddComponent<CharacterController>();
        // tamaño de la cápsula acorde al mapache escalado (~1 m); stepOffset 0.3 sube escalones de 0.24-0.27 m
        cc.height = 1.0f;
        cc.radius = 0.24f;
        cc.center = new Vector3(0f, 0.52f, 0f);
        cc.stepOffset = 0.3f;
        cc.slopeLimit = 50f;
        cc.skinWidth = 0.03f;

        var modelo = (GameObject)PrefabUtility.InstantiatePrefab(prefabMapache, jugador.transform);
        modelo.name = "Modelo";
        modelo.transform.localPosition = Vector3.zero;
        modelo.transform.localRotation = Quaternion.identity;
        OrientarModeloHaciaAdelante(modelo.transform);
        CorregirEscala(modelo.transform, 1.45f, "Mapache");
        modelo.transform.localScale *= EscalaMapache;

        var animator = modelo.GetComponent<Animator>();
        if (animator == null) animator = modelo.AddComponent<Animator>();
        animator.runtimeAnimatorController = controlador;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // luz de la linterna (sigue al hueso de la cabeza)
        var cabeza = BuscarHijo(modelo.transform, "Head");
        if (cabeza != null)
        {
            var linterna = new GameObject("LuzLinterna");
            linterna.transform.SetParent(cabeza, false);
            Vector3 adelante = jugador.transform.forward;
            linterna.transform.position = cabeza.position + (Vector3.up * 0.36f + adelante * 0.33f) * EscalaMapache;
            linterna.transform.rotation = Quaternion.LookRotation(adelante + Vector3.down * 0.25f, Vector3.up);
            var l = linterna.AddComponent<Light>();
            l.type = LightType.Spot;
            l.range = 10f;
            l.spotAngle = 55f;
            l.innerSpotAngle = 30f;
            l.intensity = 6f;
            l.color = new Color(1f, 0.88f, 0.6f);
            l.shadows = LightShadows.Soft;
        }

        // estela del dash
        var estelaGO = new GameObject("EstelaDash");
        estelaGO.transform.SetParent(jugador.transform, false);
        estelaGO.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        var estela = estelaGO.AddComponent<TrailRenderer>();
        estela.time = 0.25f;
        estela.minVertexDistance = 0.05f;
        estela.widthCurve = new AnimationCurve(new Keyframe(0f, 0.9f), new Keyframe(1f, 0f));
        estela.colorGradient = new Gradient
        {
            colorKeys = new[] { new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.6f, 0.2f), 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) }
        };
        estela.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        estela.sharedMaterial = matEstela;
        estela.emitting = false;

        var control = jugador.AddComponent<ControladorMapache>();
        var so = new SerializedObject(control);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("modelo").objectReferenceValue = modelo.transform;
        so.FindProperty("estelaDash").objectReferenceValue = estela;
        so.FindProperty("enfriamientoDash").floatValue = EnfriamientoDash;

        // ---------- 4) cámara
        var cam = Camera.main;
        if (cam == null)
        {
            var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
        }
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 400f;
        cam.fieldOfView = 60f;
        cam.transform.position = jugador.transform.position + new Vector3(0f, 1.9f, -3.4f);
        cam.transform.LookAt(jugador.transform.position + Vector3.up * 0.85f);
        var seguir = cam.GetComponent<CamaraSeguimiento>();
        if (seguir == null) seguir = cam.gameObject.AddComponent<CamaraSeguimiento>();
        var soCam = new SerializedObject(seguir);
        soCam.FindProperty("objetivo").objectReferenceValue = jugador.transform;
        soCam.ApplyModifiedPropertiesWithoutUndo();
        so.FindProperty("camara").objectReferenceValue = cam.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ambientación nocturna (luna, cielo estrellado, faroles y porches)
        AplicarNoche(escena);

        // ---------- 5) HUD de habilidades
        var hud = new GameObject("HUD");
        var hudHab = hud.AddComponent<HUDHabilidades>();
        var soHud = new SerializedObject(hudHab);
        soHud.FindProperty("jugador").objectReferenceValue = control;
        soHud.ApplyModifiedPropertiesWithoutUndo();

        // ---------- 6) botín (basureros dorados + objetos de la casa)
        AgregarBotin(escena);

        // ---------- 7) guardias, perro, NavMesh y alerta
        AgregarEnemigos(escena);

        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        Selection.activeGameObject = jugador;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("[Nivel1] Listo: WASD mover, Shift sigilo, Espacio saltar, C dash, Q gancho, R sobrecarga, E interactuar.");
    }

    // ------------------------------------------------------------------ importación

    private static void ConfigurarCasa()
    {
        var mi = AssetImporter.GetAtPath(RutaCasa) as ModelImporter;
        if (mi == null) return;
        bool cambio = false;
        if (!mi.addCollider) { mi.addCollider = true; cambio = true; }
        if (mi.importAnimation) { mi.importAnimation = false; cambio = true; }
        if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; cambio = true; }
        if (cambio) mi.SaveAndReimport();

        // los mapas de normales de la casa deben importarse como "Normal map"
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { RutaTexturasCasa }))
        {
            var ruta = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(ruta) is not TextureImporter ti) continue;
            bool esNormal = System.IO.Path.GetFileName(ruta).ToLower().Contains("normal");
            if (esNormal && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
        }
    }

    private static void ConfigurarMapache()
    {
        var mi = AssetImporter.GetAtPath(RutaMapache) as ModelImporter;
        if (mi == null) return;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true;
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            if (c.name.Contains("Caminar")) c.name = "Caminar";
            else if (c.name.Contains("Idle")) c.name = "Idle";
            c.loopTime = true;
            c.lockRootHeightY = c.lockRootPositionXZ = c.lockRootRotation = true;
            c.keepOriginalPositionY = c.keepOriginalPositionXZ = c.keepOriginalOrientation = true;
        }
        mi.clipAnimations = clips;
        mi.SaveAndReimport();
    }

    private static AnimatorController CrearAnimator()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(RutaMapache).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
        var idle = clips.FirstOrDefault(c => c.name.Contains("Idle"));
        var caminar = clips.FirstOrDefault(c => c.name.Contains("Caminar"));

        System.IO.Directory.CreateDirectory("Assets/Animaciones");
        AssetDatabase.DeleteAsset(RutaAnimator);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(RutaAnimator);
        ctrl.AddParameter("Velocidad", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("EnSuelo", AnimatorControllerParameterType.Bool);

        ctrl.CreateBlendTreeInController("Locomocion", out BlendTree arbol, 0);
        arbol.blendType = BlendTreeType.Simple1D;
        arbol.blendParameter = "Velocidad";
        arbol.useAutomaticThresholds = false;
        if (idle != null) arbol.AddChild(idle, 0f);
        if (caminar != null)
        {
            // umbrales = velocidades de ControladorMapache (caminar 3, correr 5.5)
            arbol.AddChild(caminar, 3.0f);
            arbol.AddChild(caminar, 5.5f);
            var hijos = arbol.children;
            hijos[hijos.Length - 2].timeScale = 1.3f;   // el mapache es más pequeño: pasos más rápidos
            hijos[hijos.Length - 1].timeScale = 2.2f;   // "correr" = caminar acelerado
            arbol.children = hijos;
        }
        if (idle == null || caminar == null)
            Debug.LogWarning("[Nivel1] No se encontraron las animaciones Idle/Caminar en Mapache.fbx.");
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    private static Material CrearMaterialEstela()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RutaMatEstela);
        if (mat != null) return mat;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        mat = new Material(shader);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);      // transparente
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);          // aditivo
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        System.IO.Directory.CreateDirectory("Assets/Materiales");
        AssetDatabase.CreateAsset(mat, RutaMatEstela);
        return mat;
    }

    // ------------------------------------------------------------------ utilidades

    private static Transform BuscarHijo(Transform raiz, string nombre)
    {
        foreach (var t in raiz.GetComponentsInChildren<Transform>(true))
            if (t.name == nombre) return t;
        return null;
    }

    /// <summary>Gira el modelo para que el mapache mire hacia +Z del jugador (usa los huesos del pie).</summary>
    private static void OrientarModeloHaciaAdelante(Transform modelo)
    {
        var pie = BuscarHijo(modelo, "Foot.L");
        var dedos = BuscarHijo(modelo, "Toes.L");
        if (pie == null || dedos == null) return;
        Vector3 adelante = dedos.position - pie.position;
        adelante.y = 0f;
        if (adelante.sqrMagnitude < 1e-6f) return;
        modelo.localRotation = Quaternion.Inverse(Quaternion.LookRotation(adelante.normalized, Vector3.up)) * modelo.localRotation;
    }

    /// <summary>
    /// Si el modelo llegó con una escala muy distinta a la esperada (p. ej. 100× por unidades del FBX),
    /// lo corrige. Tamaños razonables (±40 %) se dejan tal cual.
    /// </summary>
    private static void CorregirEscala(Transform t, float tamanoEsperado, string nombre, bool usarAncho = false)
    {
        var rends = t.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        float medido = usarAncho ? Mathf.Max(b.size.x, b.size.z) : b.size.y;
        if (medido <= 0f) return;
        float ratio = tamanoEsperado / medido;
        if (ratio > 0.6f && ratio < 1.4f) return;
        t.localScale *= ratio;
        Debug.Log($"[Nivel1] {nombre}: tamaño importado {medido:0.##} m, se ajustó la escala ×{ratio:0.###}.");
    }
}
