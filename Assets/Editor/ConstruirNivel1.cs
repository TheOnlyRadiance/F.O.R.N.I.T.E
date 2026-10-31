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

    static ConstruirNivel1()
    {
        EditorApplication.delayCall += OfrecerUnaVez;
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

        // sol: ahora hay exterior, así que luz de día con sombras (las sombras oscurecen el interior bajo techo)
        foreach (var l in Object.FindObjectsByType<Light>())
            if (l.type == LightType.Directional)
            {
                l.intensity = 1.2f;
                l.shadows = LightShadows.Soft;
                l.color = new Color(1f, 0.96f, 0.88f);
                l.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            }

        // ---------- 5) HUD del dash
        var hud = new GameObject("HUD");
        var hudDash = hud.AddComponent<HUDDash>();
        var soHud = new SerializedObject(hudDash);
        soHud.FindProperty("jugador").objectReferenceValue = control;
        soHud.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        Selection.activeGameObject = jugador;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("[Nivel1] Listo: WASD mover, Shift correr, Espacio saltar, C dash, mouse cámara, Esc liberar cursor.");
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
