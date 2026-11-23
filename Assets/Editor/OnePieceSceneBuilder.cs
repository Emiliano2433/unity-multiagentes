using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Construye la escena "Going Merry + Arlong en el mar".
/// Se ejecuta sola la primera vez que los modelos .glb están importados (requiere glTFast),
/// y se puede repetir desde el menú  One Piece ▸ Reconstruir escena Going Merry.
[InitializeOnLoad]
public static class OnePieceSceneBuilder
{
    const string ScenePath   = "Assets/Scenes/GoingMerry_Arlong.unity";
    const string ShipPath    = "Assets/Models/GoingMerry.glb";
    const string CrewFolder  = "Assets/Models/Sentados";
    static readonly string[] CrewOrder = { "Zoro", "Luffy", "Arlong", "Shanks", "Barbablanca" };
    const string GenFolder   = "Assets/Generated";

    const float ShipScale    = 7.5f;    // ~40 m de eslora (agrandado para que quepan las 5 sillas)
    const float RowAlongBow  = 0.2f;    // (respaldo) fila de sillas: 0 = mástil, 1 = punta de la proa
    const float RowAlongForeDeck = 0.25f; // fila de sillas en la cubierta de proa: 0 = escalón, 1 = cabra
    const float ChairBackGap = 0.04f;   // espacio entre respaldo y espalda
    const float LengthK      = 1.2f;    // barco más largo…
    const float BeamK        = 1.45f;   // …y más ancho, para que quepan los Sharks y el emprendedor
    const float SpotAlongForeDeck    = 0.74f; // dónde se para el emprendedor (0 = escalón, 1 = cabra)
    const float LandingAlongForeDeck = 0.58f; // dónde termina la escalera exterior

    static OnePieceSceneBuilder()
    {
        EditorApplication.delayCall += TryAutoBuild;
    }

    static void TryAutoBuild()
    {
        const string marker = "Temp/op_rebuild";
        bool forced = File.Exists(marker);
        if (!forced && (File.Exists(ScenePath) || SessionState.GetBool("OP_SceneBuilt", false))) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAutoBuild;
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ShipPath) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(CrewFolder + "/Arlong.glb") == null)
        {
            Debug.Log("[OnePiece] Esperando a que glTFast importe los modelos .glb…");
            return; // se reintenta en la siguiente recarga de dominio
        }
        SessionState.SetBool("OP_SceneBuilt", true);
        if (forced) File.Delete(marker);
        Build();
    }

    [MenuItem("One Piece/Reconstruir escena Going Merry")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    [MenuItem("One Piece/Guardar capturas de preview")]
    public static void CapturePreviewMenu() => CapturePreviews();

    static void Build()
    {
        var shipAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPath);
        if (shipAsset == null)
        {
            Debug.LogError("[OnePiece] No se encontraron los modelos. ¿Está instalado glTFast (com.unity.cloud.gltfast)?");
            return;
        }
        if (!AssetDatabase.IsValidFolder(GenFolder)) AssetDatabase.CreateFolder("Assets", "Generated");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------- Luz y ambiente ----------
        var sunGo = new GameObject("Sol");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.94f, 0.84f);
        sun.intensity = 2.0f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sunGo.transform.rotation = Quaternion.Euler(34f, -60f, 0f);

        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetFloat("_SunDisk", 2);
        sky.SetFloat("_SunSize", 0.035f);
        sky.SetFloat("_SunSizeConvergence", 6f);
        sky.SetFloat("_AtmosphereThickness", 0.85f);
        sky.SetColor("_SkyTint", new Color(0.42f, 0.58f, 0.86f));
        sky.SetColor("_GroundColor", new Color(0.22f, 0.33f, 0.44f));
        sky.SetFloat("_Exposure", 1.25f);
        SaveAsset(sky, GenFolder + "/CieloOnePiece.mat");

        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.56f, 0.68f, 0.86f);
        RenderSettings.ambientEquatorColor = new Color(0.52f, 0.6f, 0.68f);
        RenderSettings.ambientGroundColor = new Color(0.18f, 0.25f, 0.32f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.74f, 0.83f, 0.93f);
        RenderSettings.fogStartDistance = 140f;
        RenderSettings.fogEndDistance = 1500f;

        var probeGo = new GameObject("Reflejo del cielo");
        var probe = probeGo.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
        probe.cullingMask = 0;
        probe.size = new Vector3(2000, 600, 2000);
        probe.importance = 0;

        // ---------- Océano ----------
        var oceanMat = new Material(Shader.Find("OnePiece/Ocean"));
        SaveAsset(oceanMat, GenFolder + "/Oceano.mat");
        var oceanMesh = BuildOceanMesh(255, 1800f);
        SaveAsset(oceanMesh, GenFolder + "/OceanoMalla.asset");

        var ocean = new GameObject("Oceano");
        ocean.AddComponent<MeshFilter>().sharedMesh = oceanMesh;
        var oceanRenderer = ocean.AddComponent<MeshRenderer>();
        oceanRenderer.sharedMaterial = oceanMat;
        oceanRenderer.shadowCastingMode = ShadowCastingMode.Off;
        oceanRenderer.receiveShadows = true;
        ocean.AddComponent<OceanWaves>();

        // ---------- Going Merry ----------
        var shipRoot = new GameObject("Going Merry");
        var ship = (GameObject)PrefabUtility.InstantiatePrefab(shipAsset, shipRoot.transform);
        ship.name = "Modelo Going Merry";
        ship.transform.localScale = Vector3.one * ShipScale;
        EstirarBarco(ship.transform, LengthK, BeamK);

        // Centrar en XZ
        Bounds b = WorldBounds(ship);
        ship.transform.position -= new Vector3(b.center.x, 0, b.center.z);
        b = WorldBounds(ship);

        // Medir la cubierta con raycasts sobre colliders temporales
        var tempColliders = new List<MeshCollider>();
        foreach (var mf in ship.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; tempColliders.Add(mc); }
        foreach (var smr in ship.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (smr.sharedMesh != null) { var mc = smr.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = smr.sharedMesh; tempColliders.Add(mc); }
        Physics.SyncTransforms();

        float bottom = b.min.y, height = b.size.y;
        float mastX = 0, mastTop = float.MinValue;
        var deckProfile = new List<(float x, float y)>();
        for (int i = 0; i <= 80; i++)
        {
            float x = Mathf.Lerp(b.min.x, b.max.x, i / 80f);
            var hits = CastDown(new Vector3(x, b.max.y + 1, b.center.z), height + 2);
            if (hits.Length > 0)
            {
                float top = hits.Max(h => h.point.y);
                if (top > mastTop) { mastTop = top; mastX = x; }
            }
            float? deck = DeckHeight(hits, bottom, height);
            if (deck.HasValue) deckProfile.Add((x, deck.Value));
        }

        float mainDeck = deckProfile.Count > 0
            ? deckProfile.Select(p => p.y).OrderBy(y => y).ElementAt(deckProfile.Count / 2)
            : bottom + height * 0.25f;

        // ---------- Tripulación sentada: 5 sillas en arco sobre la cubierta elevada de proa, mirando a la cabra ----------
        var crew = LoadCrew();
        float gap = 0.55f;
        var chairW = crew.Select(c => ChairWidth(c.meta)).ToArray();
        float total = chairW.Sum() + gap * (crew.Count - 1);
        float rowX = mastX + (b.max.x - mastX) * RowAlongBow;
        float rowDeck = mainDeck;
        var fore = ForeDeck(deckProfile, mastX);
        if (fore.HasValue)
        {
            rowDeck = fore.Value.y;
            rowX = Mathf.Lerp(fore.Value.x0, fore.Value.x1, RowAlongForeDeck);
        }
        else Debug.LogWarning("[OnePiece] No se detectó la cubierta de proa; se usa la cubierta principal.");

        // Punto donde el emprendedor presenta su pitch: delante de los Sharks, hacia la cabra
        float fx0 = fore.HasValue ? fore.Value.x0 : rowX - 2f;
        float fx1 = fore.HasValue ? fore.Value.x1 : rowX + 6f;
        var spot = new Vector3(Mathf.Lerp(fx0, fx1, SpotAlongForeDeck), rowDeck, b.center.z);
        {
            var d = DeckHeight(CastDown(new Vector3(spot.x, b.max.y + 1, spot.z), height + 2), bottom, height);
            if (d.HasValue && Mathf.Abs(d.Value - rowDeck) < 0.3f) spot.y = d.Value;
        }
        float waterline = bottom + (mainDeck - bottom) * 0.42f;   // ~40% entre la quilla y la cubierta principal

        var seats = new List<(Vector3 pos, float yaw)>();
        float zc = b.center.z + total * 0.5f;              // de babor (+Z) a estribor (-Z)
        for (int i = 0; i < crew.Count; i++)
        {
            float z = zc - chairW[i] * 0.5f;
            zc -= chairW[i] + gap;
            float dz = z - b.center.z;
            float x = rowX - dz * dz * 0.06f;               // arco suave
            float yaw = Mathf.Atan2(spot.x - x, spot.z - z) * Mathf.Rad2Deg; // todos miran al emprendedor
            float deck = float.MinValue;
            foreach (float ox in new[] { -0.5f, 0f, 0.5f })
                foreach (float oz in new[] { -0.4f, 0f, 0.4f })
                {
                    var d = DeckHeight(CastDown(new Vector3(x + ox, b.max.y + 1, z + oz), height + 2), bottom, height);
                    // ignorar barandales, escalones o escotillas: solo el piso de esa cubierta
                    if (d.HasValue && Mathf.Abs(d.Value - rowDeck) < 0.3f) deck = Mathf.Max(deck, d.Value);
                }
            if (deck == float.MinValue) deck = rowDeck;
            seats.Add((new Vector3(x, deck, z), yaw));
        }

        // Escalera exterior por estribor (-Z): se mide el casco mientras existen los colliders temporales
        var subida = PlanearSubida(b, rowDeck, Mathf.Lerp(fx0, fx1, LandingAlongForeDeck), spot, waterline);

        foreach (var c in tempColliders) Object.DestroyImmediate(c);

        // Línea de flotación
        ship.transform.position += Vector3.down * waterline;
        Debug.Log($"[OnePiece] Barco: {b.size.x:0.0} m de eslora · cubierta {mainDeck - waterline:0.00} m sobre el agua · fila de sillas en x={rowX:0.0}, y={rowDeck - waterline:0.00}");

        var buoy = shipRoot.AddComponent<ShipBuoyancy>();
        buoy.halfLength = b.size.x * 0.4f;
        buoy.halfBeam = Mathf.Min(b.size.z, b.size.x * 0.3f) * 0.4f;

        var mats = ChairMaterials();
        var crewRoot = new GameObject("Tripulación");
        crewRoot.transform.SetParent(shipRoot.transform, false);
        GameObject arlong = null;
        Vector3 groupCenter = Vector3.zero;
        var enEscena = new List<EscenarioPitch.SharkEnEscena>();
        for (int i = 0; i < crew.Count; i++)
        {
            var (pos, yaw) = seats[i];
            pos.y -= waterline;
            groupCenter += pos / crew.Count;
            var chair = BuildChair(crew[i].name, crew[i].meta, mats);
            chair.transform.SetParent(crewRoot.transform, false);
            chair.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));

            var ch = (GameObject)PrefabUtility.InstantiatePrefab(crew[i].prefab, chair.transform);
            ch.name = crew[i].name;
            ch.transform.localPosition = new Vector3(0, crew[i].meta.seatHeight + 0.01f, ChairBackGap);
            ch.transform.localRotation = Quaternion.identity;
            foreach (var smr in ch.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;

            var anim = ch.GetComponentInChildren<Animation>();
            if (anim != null)
            {
                AnimationClip clip = anim.clip;
                if (clip == null) foreach (AnimationState st in anim) { clip = st.clip; break; }
                if (clip != null) clip.SampleAnimation(anim.gameObject, 0.5f);
                ch.AddComponent<CharacterIdle>();
            }
            else
            {
                var br = ch.AddComponent<CharacterBreath>();
                br.phase = i * 1.7f;
            }
            if (crew[i].name == "Arlong") arlong = ch;
            enEscena.Add(new EscenarioPitch.SharkEnEscena
            {
                nombre = crew[i].name, personaje = ch.transform, silla = chair.transform, color = ColorDe(crew[i].name),
            });
        }

        // ---------- Escalera, bote y emprendedor ----------
        var escenario = ConstruirSubida(shipRoot.transform, subida, waterline, mats);
        escenario.sharks = enEscena.ToArray();
        foreach (var sh in escenario.sharks)
            if (!EscenarioPitch.MedirCabeza(sh)) Debug.LogWarning($"[OnePiece] Cabeza de {sh.nombre}: se usarán los bounds.");
            else Debug.Log($"[OnePiece] Cabeza de {sh.nombre}: {sh.cabezaEnSilla} (altura sentado {sh.altura:0.00} m)");

        // ---------- Espuma en la proa ----------
        BuildBowSpray(shipRoot.transform, new Vector3(b.max.x - b.size.x * 0.06f, 0.2f, b.center.z), b.size.z);

        // ---------- Cámara ----------
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 45f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 3000f;
        camGo.AddComponent<AudioListener>();
        var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var cine = camGo.AddComponent<CinematicCamera>();
        cine.target = shipRoot.transform;
        cine.targetOffset = groupCenter + Vector3.up * 1.2f;
        cine.distance = b.size.x * 0.75f;
        cine.yaw = -62f;
        cine.pitch = 20f;
        cine.Place();

        // ---------- Director del show (conecta con el backend) ----------
        var dirGo = new GameObject("Director Shark Tank");
        var director = dirGo.AddComponent<ShowDirector>();
        director.escenario = escenario;
        director.camara = cam;
        director.orbita = cine;
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpAsset)
            director.materialParticulas = urpAsset.defaultParticleMaterial;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed; // http://127.0.0.1 sin TLS
        PlayerSettings.runInBackground = true;

        // ---------- Post-procesado ----------
        var volGo = new GameObject("Post Procesado");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = BuildVolumeProfile();

        // Sombras con alcance suficiente para el barco
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp && urp.shadowDistance < 160f)
        {
            urp.shadowDistance = 160f;
            EditorUtility.SetDirty(urp);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();

        Selection.activeGameObject = arlong != null ? arlong : crewRoot;
        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            sv.LookAt(shipRoot.transform.position + Vector3.up * 6, Quaternion.Euler(14, -38, 0), b.size.x * 1.3f);
        }
        Debug.Log("[OnePiece] Escena creada: " + ScenePath + "  (Play para ver olas, barco meciéndose y Arlong animado)");

        EditorApplication.delayCall += CapturePreviews;
    }

    // ---------------------------------------------------------------- tripulación y sillas

    [System.Serializable]
    class CrewMeta { public float seatHeight, depth, halfWidth, aboveSeat, standing; }
    class CrewMember { public string name; public GameObject prefab; public CrewMeta meta; }

    static List<CrewMember> LoadCrew()
    {
        var list = new List<CrewMember>();
        foreach (var n in CrewOrder)
        {
            string glb = $"{CrewFolder}/{n}.glb";
            EnsureLegacyAnimation(glb);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(glb);
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{CrewFolder}/{n}.json");
            if (prefab == null || json == null) { Debug.LogWarning($"[OnePiece] Falta {glb} o su .json"); continue; }
            list.Add(new CrewMember { name = n, prefab = prefab, meta = JsonUtility.FromJson<CrewMeta>(json.text) });
        }
        return list;
    }

    /// glTFast importa animaciones como Mecanim por defecto; las queremos Legacy para reproducirlas sin Animator.
    static void EnsureLegacyAnimation(string path)
    {
        var imp = AssetImporter.GetAtPath(path);
        if (imp == null) return;
        var so = new SerializedObject(imp);
        var prop = so.FindProperty("importSettings.animationMethod");
        if (prop != null && prop.intValue != 1)
        {
            prop.intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            imp.SaveAndReimport();
        }
    }

    static float ChairWidth(CrewMeta m) => Mathf.Max(m.halfWidth * 2f * 1.45f, 0.55f);

    static (Material wood, Material dark, Material cushion) ChairMaterials()
    {
        Material Make(string file, Color c, float smooth)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            SaveAsset(m, $"{GenFolder}/{file}.mat");
            return m;
        }
        return (Make("SillaMadera", new Color(0.50f, 0.31f, 0.16f), 0.35f),
                Make("SillaMaderaOscura", new Color(0.30f, 0.17f, 0.08f), 0.3f),
                Make("SillaCojin", new Color(0.62f, 0.07f, 0.08f), 0.15f));
    }

    /// Silla de madera con cojín, a la medida del personaje. Frente = +Z local, respaldo en z≈0.
    static GameObject BuildChair(string who, CrewMeta m, (Material wood, Material dark, Material cushion) mats)
    {
        var root = new GameObject($"Silla {who}");
        float W = ChairWidth(m);
        float k = Mathf.Clamp(W / 0.6f, 1f, 2.2f);
        float top = m.seatHeight;
        float cushion = 0.05f * k, slab = 0.05f * k, p = 0.06f * k;
        float D = Mathf.Clamp(m.depth * 0.7f, 0.42f, 1.4f * k);
        float backH = Mathf.Clamp(m.aboveSeat * 0.48f, 0.45f, 1.5f);
        float legH = Mathf.Max(top - cushion - slab, 0.05f);

        Box(root, "Asiento", new Vector3(0, legH + slab / 2, D / 2), new Vector3(W, slab, D), mats.wood);
        Box(root, "Cojín", new Vector3(0, top - cushion / 2, D / 2 + 0.01f), new Vector3(W * 0.92f, cushion, D * 0.94f), mats.cushion);
        foreach (float sx in new[] { -1f, 1f })
        {
            float x = sx * (W / 2 - p / 2);
            Box(root, "Pata delantera", new Vector3(x, legH / 2, D - p / 2), new Vector3(p, legH, p), mats.dark);
            float postH = top + backH;
            Box(root, "Poste respaldo", new Vector3(x, postH / 2, -p / 2), new Vector3(p, postH, p), mats.dark);
            var fin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(fin.GetComponent<Collider>());
            fin.name = "Remate";
            fin.transform.SetParent(root.transform, false);
            fin.transform.localPosition = new Vector3(x, postH + p * 0.5f, -p / 2);
            fin.transform.localScale = Vector3.one * p * 1.6f;
            fin.GetComponent<Renderer>().sharedMaterial = mats.wood;
        }
        float railTop = top + backH - 0.06f * k;
        Box(root, "Travesaño superior", new Vector3(0, railTop, -p / 2), new Vector3(W - p, 0.12f * k, p * 0.8f), mats.wood);
        Box(root, "Travesaño inferior", new Vector3(0, top + 0.1f * k, -p / 2), new Vector3(W - p, 0.06f * k, p * 0.7f), mats.wood);
        float slatH = railTop - (top + 0.1f * k);
        foreach (float fx in new[] { -0.25f, 0f, 0.25f })
            Box(root, "Listón", new Vector3(fx * W, top + 0.1f * k + slatH / 2, -p / 2), new Vector3(0.08f * k, slatH, p * 0.5f), mats.wood);
        Box(root, "Refuerzo frontal", new Vector3(0, legH * 0.25f, D - p / 2), new Vector3(W - p, 0.04f * k, 0.04f * k), mats.dark);
        Box(root, "Refuerzo trasero", new Vector3(0, legH * 0.25f, -p / 2), new Vector3(W - p, 0.04f * k, 0.04f * k), mats.dark);
        return root;
    }

    static void Box(GameObject parent, string name, Vector3 center, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // ---------------------------------------------------------------- barco más grande

    /// Escala el modelo en X (eslora) y Z (manga) del mundo, sin importar cómo venga rotado el .glb.
    static void EstirarBarco(Transform t, float largo, float ancho)
    {
        var s = t.localScale;
        for (int i = 0; i < 3; i++)
        {
            var eje = Vector3.zero; eje[i] = 1;
            Vector3 w = t.rotation * eje;
            if (Mathf.Abs(w.x) > 0.7f) s[i] *= largo;
            else if (Mathf.Abs(w.z) > 0.7f) s[i] *= ancho;
        }
        t.localScale = s;
    }

    static Color ColorDe(string nombre) => nombre switch
    {
        "Luffy"       => new Color(0.91f, 0.24f, 0.2f),
        "Shanks"      => new Color(0.95f, 0.62f, 0.2f),
        "Zoro"        => new Color(0.27f, 0.66f, 0.3f),
        "Barbablanca" => new Color(0.93f, 0.94f, 0.96f),
        "Arlong"      => new Color(0.3f, 0.52f, 0.9f),
        _             => Color.white,
    };

    // ---------------------------------------------------------------- escalera exterior + emprendedor

    /// Medidas de la subida, en coordenadas de mundo ANTES de bajar el barco a la línea de flotación.
    class Subida
    {
        public float xLand, zc, zRail, railTop, hLand, baseY, xBase, xTop, rise, deck, zEndLand, riseIn;
        public int n, nIn;
        public Vector3 bote;
        public Vector3[] ruta;
    }

    const float StairW = 1.3f, StairRun = 0.27f, StairRise = 0.3f, LandHalf = 0.65f;

    static Subida PlanearSubida(Bounds b, float deck, float xLand, Vector3 spot, float waterline)
    {
        var s = new Subida { xLand = xLand, deck = deck };

        // Barandal de estribor a la altura de la cubierta de proa: altura del pasamanos y su cara exterior
        // (se busca de deck+1.7 hacia abajo y se toma la mediana, para ignorar adornos altos como las volutas)
        var topes = new List<float>(); var caras = new List<float>();
        foreach (float ox in new[] { -LandHalf, -LandHalf * 0.5f, 0f, LandHalf * 0.5f, LandHalf })
        {
            for (float h = deck + 1.7f; h > deck + 0.3f; h -= 0.03f)
            {
                var z = RayLateral(xLand + ox, h, b);
                if (!z.HasValue) continue;
                topes.Add(h);
                caras.Add(RayLateral(xLand + ox, h - 0.03f, b) ?? z.Value);
                break;
            }
        }
        if (topes.Count == 0) { s.railTop = deck + 1.1f; s.zRail = b.min.z + 1f; Debug.LogWarning("[OnePiece] No se detectó el barandal de estribor."); }
        else { topes.Sort(); caras.Sort(); s.railTop = topes[topes.Count / 2]; s.zRail = caras[0]; }

        s.hLand = s.railTop + 0.12f;
        s.baseY = waterline + 0.08f;                               // piso del bote
        s.n = Mathf.CeilToInt((s.hLand - s.baseY) / StairRise);
        s.rise = (s.hLand - s.baseY) / s.n;
        s.xTop = xLand - LandHalf;
        s.xBase = s.xTop - s.n * StairRun;

        // Lo más ancho del casco a lo largo de la escalera (para que no lo atraviese)
        float zHull = s.zRail;
        for (float x = s.xBase - 2f; x <= xLand + LandHalf; x += 0.4f)
            for (float h = waterline + 0.1f; h <= s.railTop; h += 0.3f)
            {
                var z = RayLateral(x, h, b);
                if (z.HasValue) zHull = Mathf.Min(zHull, z.Value);
            }
        s.zc = zHull - 0.15f - StairW * 0.5f;

        // Del descanso (encima del barandal) se baja por una escalerita interior hasta la cubierta
        s.zEndLand = s.zRail + 0.75f;
        s.nIn = Mathf.Max(1, Mathf.CeilToInt((s.hLand - deck) / 0.26f));
        s.riseIn = (s.hLand - deck) / s.nIn;
        float zPie = s.zEndLand + s.nIn * StairRun + 0.25f;

        s.bote = new Vector3(s.xBase - 1.6f, s.baseY, s.zc);         // donde espera parado (parte delantera del bote)
        s.ruta = new[]
        {
            new Vector3(s.xBase - 0.35f, s.baseY, s.zc),
            new Vector3(s.xBase + StairRun * 0.3f, s.baseY + s.rise * 0.7f, s.zc),
            new Vector3(s.xTop - StairRun * 0.4f, s.hLand, s.zc),
            new Vector3(xLand, s.hLand, s.zc),
            new Vector3(xLand, s.hLand, s.zEndLand - 0.1f),
            new Vector3(xLand, deck, zPie),
            spot,
        };
        Debug.Log($"[OnePiece] Escalera: {s.n} escalones de {s.rise:0.00} m · barandal y={s.railTop:0.00} z={s.zRail:0.00} · casco z={zHull:0.00} · punto del pitch {spot}");
        return s;
    }

    /// Primer impacto (desde estribor hacia el centro) de un rayo horizontal a la altura h.
    static float? RayLateral(float x, float h, Bounds b)
    {
        var origen = new Vector3(x, h, b.min.z - 5f);
        float best = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(origen, Vector3.forward, b.extents.z + 5f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.point.z < b.center.z - 0.3f && hit.point.z < best) best = hit.point.z;
        return best == float.MaxValue ? (float?)null : best;
    }

    static EscenarioPitch ConstruirSubida(Transform shipRoot, Subida s, float waterline, (Material wood, Material dark, Material cushion) mats)
    {
        Vector3 L(Vector3 w) => w + Vector3.down * waterline;     // mundo (antes de bajar el barco) -> local al barco
        var root = new GameObject("Subida al barco");
        root.transform.SetParent(shipRoot, false);

        // --- escalera exterior (sube hacia la proa, pegada al casco)
        var esc = new GameObject("Escalera exterior");
        esc.transform.SetParent(root.transform, false);
        for (int i = 0; i < s.n; i++)
        {
            float top = s.baseY + (i + 1) * s.rise;
            Box(esc, "Escalón", L(new Vector3(s.xBase + (i + 0.5f) * StairRun, top - 0.035f, s.zc)), new Vector3(StairRun + 0.03f, 0.07f, StairW), mats.wood);
        }
        float largo = Mathf.Sqrt(Mathf.Pow(s.n * StairRun, 2) + Mathf.Pow(s.hLand - s.baseY, 2));
        float ang = Mathf.Atan2(s.hLand - s.baseY, s.n * StairRun) * Mathf.Rad2Deg;
        var medio = new Vector3(s.xBase + s.n * StairRun * 0.5f, (s.baseY + s.hLand) * 0.5f, s.zc);
        foreach (float lado in new[] { -1f, 1f })
        {
            float z = s.zc + lado * (StairW * 0.5f + 0.05f);
            BoxRot(esc, "Larguero", L(new Vector3(medio.x, medio.y - 0.12f, z)), new Vector3(largo + 0.3f, 0.32f, 0.09f), Quaternion.Euler(0, 0, ang), mats.dark);
            BoxRot(esc, "Pasamanos", L(new Vector3(medio.x, medio.y + 0.95f, z)), new Vector3(largo, 0.07f, 0.07f), Quaternion.Euler(0, 0, ang), mats.wood);
            for (int i = 0; i <= s.n; i += 4)
            {
                float x = s.xBase + i * StairRun, y = s.baseY + i * s.rise;
                Box(esc, "Balaustre", L(new Vector3(x, y + 0.5f, z)), new Vector3(0.07f, 1f, 0.07f), mats.dark);
            }
            // pilotes hasta el agua
            for (int i = 0; i <= s.n; i += 8)
            {
                float x = s.xBase + i * StairRun, y = s.baseY + i * s.rise;
                float alto = y - (waterline - 1.5f);
                Box(esc, "Pilote", L(new Vector3(x, y - alto * 0.5f, z)), new Vector3(0.14f, alto, 0.14f), mats.dark);
            }
        }

        // --- descanso por encima del barandal
        float zFuera = s.zc - StairW * 0.5f - 0.05f;
        var desc = new GameObject("Descanso");
        desc.transform.SetParent(root.transform, false);
        Box(desc, "Piso", L(new Vector3(s.xLand, s.hLand - 0.06f, (zFuera + s.zEndLand) * 0.5f)), new Vector3(LandHalf * 2f, 0.12f, s.zEndLand - zFuera), mats.wood);
        foreach (float ox in new[] { -LandHalf, LandHalf })
        {
            float alto = s.hLand - (waterline - 1.5f);
            Box(desc, "Pilote", L(new Vector3(s.xLand + ox, s.hLand - alto * 0.5f, zFuera)), new Vector3(0.16f, alto, 0.16f), mats.dark);
            Box(desc, "Poste", L(new Vector3(s.xLand + ox, s.hLand + 0.5f, zFuera)), new Vector3(0.08f, 1f, 0.08f), mats.dark);
        }
        Box(desc, "Pasamanos", L(new Vector3(s.xLand + 0.05f, s.hLand + 1f, zFuera)), new Vector3(LandHalf * 2f + 0.1f, 0.07f, 0.07f), mats.wood);
        // escalerita interior hasta la cubierta
        for (int k = 0; k < s.nIn - 1; k++)
        {
            float top = s.hLand - (k + 1) * s.riseIn;
            Box(desc, "Escalón interior", L(new Vector3(s.xLand, top - 0.035f, s.zEndLand + (k + 0.5f) * StairRun)), new Vector3(StairW, 0.07f, StairRun + 0.03f), mats.wood);
        }
        foreach (float ox in new[] { -1f, 1f })
        {
            float largoIn = Mathf.Sqrt(Mathf.Pow(s.nIn * StairRun, 2) + Mathf.Pow(s.hLand - s.deck, 2));
            float angIn = Mathf.Atan2(s.hLand - s.deck, s.nIn * StairRun) * Mathf.Rad2Deg;
            var c = new Vector3(s.xLand + ox * (StairW * 0.5f + 0.04f), (s.hLand + s.deck) * 0.5f - 0.1f, s.zEndLand + s.nIn * StairRun * 0.5f);
            BoxRot(desc, "Larguero interior", L(c), new Vector3(0.08f, 0.26f, largoIn), Quaternion.Euler(angIn, 0, 0), mats.dark);
        }

        // --- bote de remos al pie de la escalera
        var bote = new GameObject("Bote");
        bote.transform.SetParent(root.transform, false);
        bote.transform.localPosition = L(new Vector3(s.xBase - 2.3f, waterline, s.zc));
        var blanco = MatSimple("BoteBlanco", new Color(0.92f, 0.9f, 0.84f), 0.3f);
        Box(bote, "Fondo", new Vector3(0, 0.02f, 0), new Vector3(3.0f, 0.12f, 1.3f), mats.dark);
        Box(bote, "Costado babor", new Vector3(0, 0.25f, 0.65f), new Vector3(3.0f, 0.46f, 0.08f), mats.wood);
        Box(bote, "Costado estribor", new Vector3(0, 0.25f, -0.65f), new Vector3(3.0f, 0.46f, 0.08f), mats.wood);
        Box(bote, "Franja babor", new Vector3(0, 0.44f, 0.66f), new Vector3(3.02f, 0.08f, 0.09f), blanco);
        Box(bote, "Franja estribor", new Vector3(0, 0.44f, -0.66f), new Vector3(3.02f, 0.08f, 0.09f), blanco);
        Box(bote, "Popa", new Vector3(-1.5f, 0.25f, 0), new Vector3(0.08f, 0.46f, 1.3f), mats.wood);
        BoxRot(bote, "Proa babor", new Vector3(1.78f, 0.25f, 0.33f), new Vector3(0.75f, 0.46f, 0.08f), Quaternion.Euler(0, 60f, 0), mats.wood);
        BoxRot(bote, "Proa estribor", new Vector3(1.78f, 0.25f, -0.33f), new Vector3(0.75f, 0.46f, 0.08f), Quaternion.Euler(0, -60f, 0), mats.wood);
        Box(bote, "Banca", new Vector3(-0.7f, 0.3f, 0), new Vector3(0.3f, 0.06f, 1.25f), mats.wood);
        BoxRot(bote, "Remo", new Vector3(-0.5f, 0.42f, 0.95f), new Vector3(2.2f, 0.05f, 0.05f), Quaternion.Euler(0, 20f, -8f), mats.dark);
        BoxRot(bote, "Remo", new Vector3(-0.5f, 0.42f, -0.95f), new Vector3(2.2f, 0.05f, 0.05f), Quaternion.Euler(0, -20f, -8f), mats.dark);
        Box(bote, "Cuerda", new Vector3(1.5f, 0.6f, 0.35f), new Vector3(0.6f, 0.03f, 0.03f), mats.cushion);

        // --- emprendedor
        var emp = BuildEmprendedor(shipRoot);
        emp.Colocar(L(s.bote), L(s.ruta[0]));

        // --- escenario
        var escGo = new GameObject("Escenario Pitch");
        escGo.transform.SetParent(shipRoot, false);
        var escenario = escGo.AddComponent<EscenarioPitch>();
        escenario.barco = shipRoot;
        escenario.emprendedor = emp;
        escenario.puntoBote = L(s.bote);
        escenario.rutaSubida = s.ruta.Select(L).ToArray();
        escenario.ladoEscalera = Vector3.back;
        escenario.direccionProa = Vector3.right;
        return escenario;
    }

    static Material MatSimple(string file, Color c, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        SaveAsset(m, $"{GenFolder}/{file}.mat");
        return m;
    }

    /// Personaje del emprendedor hecho con primitivas (traje, corbata y maletín). Pies en y=0, mira a +Z.
    static Emprendedor BuildEmprendedor(Transform parent)
    {
        var traje   = MatSimple("EmpTraje",   new Color(0.13f, 0.17f, 0.32f), 0.35f);
        var camisa  = MatSimple("EmpCamisa",  new Color(0.95f, 0.95f, 0.97f), 0.2f);
        var corbata = MatSimple("EmpCorbata", new Color(0.8f, 0.1f, 0.12f), 0.4f);
        var piel    = MatSimple("EmpPiel",    new Color(0.93f, 0.74f, 0.6f), 0.25f);
        var pelo    = MatSimple("EmpPelo",    new Color(0.17f, 0.11f, 0.07f), 0.3f);
        var zapato  = MatSimple("EmpZapato",  new Color(0.06f, 0.05f, 0.05f), 0.6f);
        var maletin = MatSimple("EmpMaletin", new Color(0.4f, 0.22f, 0.1f), 0.45f);
        var negro   = MatSimple("EmpOjos",    new Color(0.05f, 0.05f, 0.06f), 0.8f);

        var root = new GameObject("Emprendedor");
        root.transform.SetParent(parent, false);
        root.transform.localScale = Vector3.one * 0.92f;
        var e = root.AddComponent<Emprendedor>();

        var cuerpo = Pivote(root.transform, "Cuerpo", new Vector3(0, 0.92f, 0));
        Prim(PrimitiveType.Cube, cuerpo, "Cadera", new Vector3(0, 0, 0), new Vector3(0.36f, 0.18f, 0.22f), traje);
        Prim(PrimitiveType.Cube, cuerpo, "Torso", new Vector3(0, 0.33f, 0), new Vector3(0.42f, 0.52f, 0.24f), traje);
        Prim(PrimitiveType.Cube, cuerpo, "Camisa", new Vector3(0, 0.42f, 0.115f), new Vector3(0.14f, 0.3f, 0.02f), camisa);
        Prim(PrimitiveType.Cube, cuerpo, "Corbata", new Vector3(0, 0.38f, 0.128f), new Vector3(0.06f, 0.3f, 0.02f), corbata);
        Prim(PrimitiveType.Cylinder, cuerpo, "Cuello", new Vector3(0, 0.63f, 0), new Vector3(0.11f, 0.05f, 0.11f), piel);

        var cabeza = Pivote(cuerpo, "Cabeza", new Vector3(0, 0.66f, 0));
        Prim(PrimitiveType.Sphere, cabeza, "Cara", new Vector3(0, 0.14f, 0), new Vector3(0.22f, 0.27f, 0.23f), piel);
        Prim(PrimitiveType.Sphere, cabeza, "Pelo", new Vector3(0, 0.21f, -0.015f), new Vector3(0.235f, 0.17f, 0.235f), pelo);
        Prim(PrimitiveType.Sphere, cabeza, "Ojo izq", new Vector3(-0.048f, 0.16f, 0.1f), Vector3.one * 0.03f, negro);
        Prim(PrimitiveType.Sphere, cabeza, "Ojo der", new Vector3(0.048f, 0.16f, 0.1f), Vector3.one * 0.03f, negro);
        Prim(PrimitiveType.Sphere, cabeza, "Nariz", new Vector3(0, 0.12f, 0.115f), new Vector3(0.035f, 0.045f, 0.035f), piel);

        Transform Brazo(string n, float x)
        {
            var p = Pivote(cuerpo, n, new Vector3(x, 0.55f, 0));
            Prim(PrimitiveType.Cube, p, "Manga", new Vector3(0, -0.27f, 0), new Vector3(0.11f, 0.56f, 0.12f), traje);
            Prim(PrimitiveType.Cube, p, "Puño", new Vector3(0, -0.55f, 0), new Vector3(0.1f, 0.03f, 0.11f), camisa);
            Prim(PrimitiveType.Sphere, p, "Mano", new Vector3(0, -0.61f, 0), Vector3.one * 0.09f, piel);
            return p;
        }
        var brazoI = Brazo("Brazo izq", -0.27f);
        var brazoD = Brazo("Brazo der", 0.27f);
        Prim(PrimitiveType.Cube, brazoD, "Maletín", new Vector3(0, -0.8f, 0), new Vector3(0.09f, 0.26f, 0.36f), maletin);
        Prim(PrimitiveType.Cube, brazoD, "Asa", new Vector3(0, -0.655f, 0), new Vector3(0.03f, 0.03f, 0.1f), zapato);

        Transform Pierna(string n, float x)
        {
            var p = Pivote(cuerpo, n, new Vector3(x, -0.04f, 0));
            Prim(PrimitiveType.Cube, p, "Pantalón", new Vector3(0, -0.42f, 0), new Vector3(0.15f, 0.84f, 0.17f), traje);
            Prim(PrimitiveType.Cube, p, "Zapato", new Vector3(0, -0.86f, 0.05f), new Vector3(0.13f, 0.08f, 0.27f), zapato);
            return p;
        }
        var piernaI = Pierna("Pierna izq", -0.1f);
        var piernaD = Pierna("Pierna der", 0.1f);

        e.cuerpo = cuerpo; e.cabeza = cabeza;
        e.brazoIzq = brazoI; e.brazoDer = brazoD;
        e.piernaIzq = piernaI; e.piernaDer = piernaD;
        return e;
    }

    static Transform Pivote(Transform parent, string name, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        return t;
    }

    static void Prim(PrimitiveType tipo, Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(tipo);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static void BoxRot(GameObject parent, string name, Vector3 center, Vector3 size, Quaternion rot, Material mat)
    {
        Box(parent, name, center, size, mat);
        parent.transform.GetChild(parent.transform.childCount - 1).localRotation = rot;
    }

    // ---------------------------------------------------------------- helpers

    static RaycastHit[] CastDown(Vector3 from, float dist) =>
        Physics.RaycastAll(from, Vector3.down, dist, ~0, QueryTriggerInteraction.Ignore);

    static float? DeckHeight(RaycastHit[] hits, float bottom, float height)
    {
        float best = float.MinValue;
        foreach (var h in hits)
            if (h.normal.y > 0.65f && h.point.y < bottom + height * 0.5f && h.point.y > best) best = h.point.y;
        return best == float.MinValue ? (float?)null : best;
    }

    /// Cubierta elevada de proa (la de la cabra): el tramo plano de al menos 3 m que llega más
    /// hacia adelante, delante del mástil. Devuelve su altura y su inicio/fin en X.
    static (float y, float x0, float x1)? ForeDeck(List<(float x, float y)> profile, float mastX)
    {
        var runs = new List<(float y, float x0, float x1)>();
        foreach (var (x, y) in profile.Where(p => p.x > mastX).OrderBy(p => p.x))
        {
            if (runs.Count > 0 && Mathf.Abs(y - runs[^1].y) < 0.3f) runs[^1] = (runs[^1].y, runs[^1].x0, x);
            else runs.Add((y, x, x));
        }
        var flat = runs.Where(r => r.x1 - r.x0 >= 3f).ToList();
        foreach (var r in flat) Debug.Log($"[OnePiece] Tramo plano: y={r.y:0.00}  x={r.x0:0.0}…{r.x1:0.0}");
        return flat.Count > 0 ? flat.OrderBy(r => r.x1).Last() : null;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Mesh BuildOceanMesh(int res, float size)
    {
        // Rejilla con densidad concentrada al centro (más detalle cerca del barco)
        var verts = new Vector3[res * res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float u = x / (res - 1f) * 2f - 1f, v = z / (res - 1f) * 2f - 1f;
                verts[z * res + x] = new Vector3(Mathf.Sign(u) * u * u, 0, Mathf.Sign(v) * v * v) * (size * 0.5f);
            }
        var tris = new int[(res - 1) * (res - 1) * 6];
        int t = 0;
        for (int z = 0; z < res - 1; z++)
            for (int x = 0; x < res - 1; x++)
            {
                int i = z * res + x;
                tris[t++] = i; tris[t++] = i + res; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + res; tris[t++] = i + res + 1;
            }
        var m = new Mesh { name = "OceanoMalla", indexFormat = IndexFormat.UInt32 };
        m.vertices = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        m.bounds = new Bounds(Vector3.zero, new Vector3(size, 10f, size));
        return m;
    }

    static void BuildBowSpray(Transform parent, Vector3 pos, float beam)
    {
        var go = new GameObject("Espuma Proa");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.6f);
        main.startColor = new Color(1, 1, 1, 0.55f);
        main.gravityModifier = 0.6f;
        main.maxParticles = 400;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission;
        em.rateOverTime = 45f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(1.5f, 0.3f, beam * 0.6f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.y = new ParticleSystem.MinMaxCurve(1f, 3f);
        vel.x = new ParticleSystem.MinMaxCurve(-0.5f, 1f);
        vel.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.6f, 0.15f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
        var pr = go.GetComponent<ParticleSystemRenderer>();
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null && urp.defaultParticleMaterial != null) pr.sharedMaterial = urp.defaultParticleMaterial;
    }

    static VolumeProfile BuildVolumeProfile()
    {
        string path = GenFolder + "/PostProcesoOnePiece.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1.05f);
        bloom.intensity.Override(0.55f);
        bloom.scatter.Override(0.65f);
        var ca = profile.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.35f);
        ca.contrast.Override(10f);
        ca.saturation.Override(14f);
        var vig = profile.Add<Vignette>(true);
        vig.intensity.Override(0.2f);
        vig.smoothness.Override(0.45f);

        foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    static void SaveAsset(Object obj, string path)
    {
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(obj, path);
    }

    static void AddToBuildSettings(string path)
    {
        var list = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
        list.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    /// Renderiza la cámara principal y un primer plano de Arlong a Logs/ (útil para revisar sin abrir Play).
    static void CapturePreviews()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var cine = cam.GetComponent<CinematicCamera>();
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
        Directory.CreateDirectory(dir);

        bool asyncPrev = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;   // evitar shaders "placeholder" en la captura
        Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation;
        Render(cam, Path.Combine(dir, "preview_escena.png"));
        Render(cam, Path.Combine(dir, "preview_escena.png"));

        var crewGo = GameObject.Find("Tripulación");
        if (crewGo != null)
        {
            var rs = crewGo.GetComponentsInChildren<Renderer>();
            var cb = rs[0].bounds; foreach (var r in rs) cb.Encapsulate(r.bounds);
            var focus = cb.center + Vector3.down * cb.extents.y * 0.15f;
            cam.transform.position = focus + Quaternion.Euler(24, -100, 0) * Vector3.back * (cb.size.z * 1.0f + 4f);
            cam.transform.LookAt(focus);
            Render(cam, Path.Combine(dir, "preview_tripulacion.png"));
            cam.transform.position = focus + Quaternion.Euler(30, -55, 0) * Vector3.back * (cb.size.z * 1.0f + 4f);
            cam.transform.LookAt(focus);
            Render(cam, Path.Combine(dir, "preview_tripulacion2.png"));
        }

        // Planos del show (los mismos que usa ShowDirector en Play)
        var esc = Object.FindFirstObjectByType<EscenarioPitch>();
        if (esc != null && esc.emprendedor != null && esc.rutaSubida != null && esc.rutaSubida.Length >= 3)
        {
            esc.Init();
            var emp = esc.emprendedor;
            Vector3 ep = emp.transform.localPosition; Quaternion er = emp.transform.localRotation;
            float fov0 = cam.fieldOfView;
            void Shot(EscenarioPitch.Plano p, string f)
            {
                cam.transform.position = p.pos;
                cam.transform.LookAt(p.mira);
                cam.fieldOfView = p.fov;
                Render(cam, Path.Combine(dir, f));
            }
            Vector3 centro = esc.barco.position + Vector3.up * 6f;
            Shot(new EscenarioPitch.Plano(centro + esc.barco.TransformDirection(Vector3.back) * 62f + Vector3.up * 10f, centro, 45f), "preview_estribor.png");
            var ruta = esc.rutaSubida;
            emp.Colocar(Vector3.Lerp(ruta[1], ruta[2], 0.55f), ruta[2]);
            Shot(esc.PlanoSubida(), "preview_subida.png");
            emp.Colocar(esc.PuntoPitch, esc.barco.InverseTransformPoint(esc.CentroGrupo()));
            Shot(esc.PlanoGrupo(), "preview_pitch_grupo.png");
            Shot(esc.PlanoEmprendedor(), "preview_pitch_emprendedor.png");
            Shot(esc.PlanoMesa(), "preview_pitch_mesa.png");
            foreach (var n in new[] { "Arlong", "Luffy", "Barbablanca", "Zoro", "Shanks" })
            {
                int i = esc.IndiceShark(n);
                if (i >= 0) Shot(esc.PlanoShark(i), $"preview_shark_{n.ToLowerInvariant()}.png");
            }
            Shot(esc.PlanoFinal(0f), "preview_final.png");
            int ia = esc.IndiceShark("Shanks");
            if (ia >= 0) Shot(esc.PlanoContraplano(ia), "preview_contraplano.png");
            emp.transform.localPosition = ep; emp.transform.localRotation = er;
            cam.fieldOfView = fov0;
        }
        ShaderUtil.allowAsyncCompilation = asyncPrev;
        if (cine != null) cine.Place(); else { cam.transform.SetPositionAndRotation(p0, r0); }
        Debug.Log("[OnePiece] Capturas guardadas en " + dir);
    }

    static void Render(Camera cam, string file)
    {
        var rt = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
