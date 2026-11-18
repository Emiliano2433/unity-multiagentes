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
        var seats = new List<(Vector3 pos, float yaw)>();
        float zc = b.center.z + total * 0.5f;              // de babor (+Z) a estribor (-Z)
        for (int i = 0; i < crew.Count; i++)
        {
            float z = zc - chairW[i] * 0.5f;
            zc -= chairW[i] + gap;
            float dz = z - b.center.z;
            float x = rowX - dz * dz * 0.06f;               // arco suave
            float yaw = 90f + Mathf.Atan2(dz, 9f) * Mathf.Rad2Deg; // todos miran a un punto delante del grupo
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

        foreach (var c in tempColliders) Object.DestroyImmediate(c);

        // Línea de flotación: ~40% entre la quilla y la cubierta principal
        float waterline = bottom + (mainDeck - bottom) * 0.42f;
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
        }

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
        cine.distance = b.size.x * 0.55f;
        cine.yaw = -62f;
        cine.pitch = 20f;
        cine.Place();

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
