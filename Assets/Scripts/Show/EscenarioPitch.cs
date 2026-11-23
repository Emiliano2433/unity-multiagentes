using System;
using UnityEngine;

/// Referencias de la escena del pitch (las llena OnePieceSceneBuilder) y cálculo de planos de cámara.
/// Todas las posiciones guardadas son locales al barco, porque el barco se mece con las olas.
public class EscenarioPitch : MonoBehaviour
{
    [Serializable]
    public class SharkEnEscena
    {
        public string nombre;          // nombre del modelo (Luffy, Shanks, Zoro, Barbablanca, Arlong)
        public Transform personaje;
        public Transform silla;
        public Color color = Color.white;
        [Tooltip("Centro de la cabeza, local a la silla (lo mide el constructor con los vértices del cuerpo)")]
        public Vector3 cabezaEnSilla;
        public float altura = 1.5f;
        public bool medido;
        [NonSerialized] public Vector3 cabezaLocal;
    }

    /// Mide la cabeza con los vértices del modelo: columna central del cuerpo y percentil alto,
    /// para que armas largas (bisento, espada de Arlong) no cuenten como "cabeza".
    public static bool MedirCabeza(SharkEnEscena s)
    {
        var pts = new System.Collections.Generic.List<Vector3>();
        try
        {
            foreach (var mf in s.personaje.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) Agregar(pts, mf.sharedMesh.vertices, mf.transform);
            foreach (var smr in s.personaje.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;
                var m = new Mesh();
                smr.BakeMesh(m, true);
                Agregar(pts, m.vertices, smr.transform);
                UnityEngine.Object.DestroyImmediate(m);
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[OnePiece] No se pudieron leer los vértices de {s.nombre}: {ex.Message}"); return false; }
        if (pts.Count < 50) return false;

        float Pct(System.Collections.Generic.List<float> v, float p) { v.Sort(); return v[Mathf.Clamp((int)(p * (v.Count - 1)), 0, v.Count - 1)]; }
        var xs = new System.Collections.Generic.List<float>(); var zs = new System.Collections.Generic.List<float>(); var ys = new System.Collections.Generic.List<float>();
        foreach (var p in pts) { xs.Add(p.x); zs.Add(p.z); ys.Add(p.y); }
        float cx = Pct(xs, 0.5f), cz = Pct(zs, 0.5f);
        float bajo = Pct(ys, 0.03f), alto = Pct(ys, 0.97f);
        float r = 0.17f * (alto - bajo) + 0.08f;
        var col = new System.Collections.Generic.List<float>();
        foreach (var p in pts) if ((p.x - cx) * (p.x - cx) + (p.z - cz) * (p.z - cz) < r * r) col.Add(p.y);
        if (col.Count < 20) return false;
        float tope = Pct(col, 0.985f);
        float h = tope - bajo;
        // centro de la cabeza: un poco debajo del tope, centrado en la columna de la cabeza
        float yCab = tope - h * 0.09f;
        float sx = 0, sz = 0; int n = 0;
        foreach (var p in pts)
            if (p.y > tope - h * 0.16f && p.y <= tope && (p.x - cx) * (p.x - cx) + (p.z - cz) * (p.z - cz) < r * r) { sx += p.x; sz += p.z; n++; }
        var cabeza = n > 0 ? new Vector3(sx / n, yCab, sz / n) : new Vector3(cx, yCab, cz);
        s.cabezaEnSilla = s.silla.InverseTransformPoint(cabeza);
        s.altura = Mathf.Max(h, 0.5f);
        s.medido = true;
        return true;
    }

    static void Agregar(System.Collections.Generic.List<Vector3> pts, Vector3[] v, Transform t)
    {
        int paso = Mathf.Max(1, v.Length / 6000);
        for (int i = 0; i < v.Length; i += paso) pts.Add(t.TransformPoint(v[i]));
    }

    public struct Plano
    {
        public Vector3 pos, mira;
        public float fov;
        public Plano(Vector3 pos, Vector3 mira, float fov = 40f) { this.pos = pos; this.mira = mira; this.fov = fov; }
    }

    public Transform barco;
    public Emprendedor emprendedor;
    public SharkEnEscena[] sharks;
    public Color colorEmprendedor = new Color(0.15f, 0.78f, 0.85f);

    [Header("Recorrido (local al barco)")]
    public Vector3 puntoBote;
    public Vector3[] rutaSubida;       // desde el bote hasta el punto del pitch (el último)
    public Vector3 ladoEscalera = Vector3.back;   // dirección hacia afuera del casco, local
    public Vector3 direccionProa = Vector3.right; // local

    bool _listo;

    public Vector3 PuntoPitch => rutaSubida != null && rutaSubida.Length > 0 ? rutaSubida[^1] : puntoBote;

    public void Init()
    {
        if (_listo || barco == null) return;
        foreach (var s in sharks)
        {
            if (s.personaje == null) continue;
            if (s.medido && s.silla != null)
            {
                s.cabezaLocal = barco.InverseTransformPoint(s.silla.TransformPoint(s.cabezaEnSilla));
                continue;
            }
            var rs = s.personaje.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            s.altura = b.size.y;
            s.cabezaLocal = barco.InverseTransformPoint(new Vector3(b.center.x, b.max.y - b.size.y * 0.13f, b.center.z));
        }
        _listo = true;
    }

    void Awake() => Init();

    public int IndiceShark(string nombre)
    {
        string k = FormatoShow.Clave(nombre);
        for (int i = 0; i < sharks.Length; i++)
            if (FormatoShow.Clave(sharks[i].nombre) == k) return i;
        return -1;
    }

    // ---------------------------------------------------------------- puntos en el mundo

    public Vector3 CabezaShark(int i) => barco.TransformPoint(sharks[i].cabezaLocal);

    public Vector3 CabezaEmprendedor()
    {
        if (emprendedor.cabeza != null) return emprendedor.cabeza.position;
        return emprendedor.transform.position + Vector3.up * 1.6f;
    }

    public Vector3 CentroGrupo()
    {
        Vector3 c = Vector3.zero; int n = 0;
        for (int i = 0; i < sharks.Length; i++) if (sharks[i].personaje != null) { c += CabezaShark(i); n++; }
        return n > 0 ? c / n : barco.position;
    }

    static Vector3 Plana(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

    // ---------------------------------------------------------------- planos

    /// Primer plano del Shark, ligeramente de lado, sin que el emprendedor se meta en medio.
    public Plano PlanoShark(int i)
    {
        var s = sharks[i];
        Vector3 cabeza = CabezaShark(i);
        Vector3 haciaE = CabezaEmprendedor() - cabeza;
        Vector3 fwd = Plana(s.silla != null ? s.silla.forward : haciaE);
        // abrir el ángulo hacia afuera del grupo, para que el vecino no se meta entre la cámara y el Shark
        Vector3 haciaFuera = cabeza - CentroGrupo(); haciaFuera.y = 0;
        Vector3 a = Quaternion.AngleAxis(28f, Vector3.up) * fwd, b = Quaternion.AngleAxis(-28f, Vector3.up) * fwd;
        float distE = new Vector2(haciaE.x, haciaE.z).magnitude;
        float d = Mathf.Max(Mathf.Min(1.5f + s.altura * 0.85f, distE * 0.72f), 1.4f);
        Vector3 up = Vector3.up * (0.1f + s.altura * 0.05f);
        // preferir el lado hacia afuera del grupo, salvo que ahí estorbe otro Shark
        Vector3 pa = cabeza + a * d + up, pb = cabeza + b * d + up;
        float ha = Holgura(pa, i) + (Vector3.Dot(a, haciaFuera) > Vector3.Dot(b, haciaFuera) ? 0.4f : 0f);
        float hb = Holgura(pb, i) + (Vector3.Dot(b, haciaFuera) > Vector3.Dot(a, haciaFuera) ? 0.4f : 0f);
        Vector3 pos = ha >= hb ? pa : pb;
        return new Plano(pos, cabeza - Vector3.up * s.altura * 0.04f, 38f);
    }

    /// Primer plano del emprendedor visto desde el lado de los Sharks.
    public Plano PlanoEmprendedor()
    {
        Vector3 e = CabezaEmprendedor();
        Vector3 haciaG = Plana(CentroGrupo() - e);
        Vector3 der = Vector3.Cross(Vector3.up, haciaG);
        return new Plano(e + haciaG * 2.3f + der * 0.75f + Vector3.up * 0.15f, e - Vector3.up * 0.2f, 38f);
    }

    /// Contraplano: por encima del hombro del Shark i, mirando al emprendedor.
    public Plano PlanoContraplano(int i)
    {
        var s = sharks[i];
        Vector3 c = CabezaShark(i), e = CabezaEmprendedor();
        Vector3 dir = Plana(c - e);
        Vector3 der = Vector3.Cross(Vector3.up, dir);
        Vector3 Pos(float lado) => c + dir * (0.7f + s.altura * 0.12f) + der * lado * (0.55f + s.altura * 0.2f) + Vector3.up * (0.25f + s.altura * 0.08f);
        // del lado donde ningún otro Shark se mete entre la cámara y el emprendedor
        Vector3 pos = Holgura(Pos(1f), i) >= Holgura(Pos(-1f), i) ? Pos(1f) : Pos(-1f);
        return new Plano(pos, Vector3.Lerp(e, c, 0.1f) - Vector3.up * 0.25f, 42f);
    }

    /// Qué tan lejos queda un punto (en planta) del cuerpo de los demás Sharks; negativo = dentro de alguno.
    float Holgura(Vector3 p, int excepto)
    {
        float min = float.MaxValue;
        for (int k = 0; k < sharks.Length; k++)
        {
            if (k == excepto || sharks[k].personaje == null) continue;
            Vector3 d = CabezaShark(k) - p; d.y = 0;
            min = Mathf.Min(min, d.magnitude - (0.35f + sharks[k].altura * 0.22f));
        }
        return min;
    }

    /// Plano de conjunto: detrás del emprendedor, con todos los Sharks enfrente.
    public Plano PlanoGrupo()
    {
        Vector3 g = CentroGrupo();
        Vector3 p = emprendedor.transform.position;
        Vector3 dir = Plana(p - g);
        Vector3 der = Vector3.Cross(Vector3.up, dir);
        return new Plano(p + dir * 3.2f - der * 0.9f + Vector3.up * 2.9f, g - Vector3.up * 0.3f, 50f);   // del lado de babor, lejos de la escalera
    }

    /// Plano desde atrás de los Sharks hacia el emprendedor (con la cabra de fondo).
    public Plano PlanoMesa()
    {
        Vector3 g = CentroGrupo(), e = CabezaEmprendedor();
        Vector3 dir = Plana(g - e);
        Vector3 babor = barco.TransformDirection(-ladoEscalera).normalized;   // lado contrario a la escalera
        return new Plano(g + dir * 2.2f + babor * 1.9f + Vector3.up * 2.1f, Vector3.Lerp(g, e, 0.75f), 48f);
    }

    /// Plano final: arco lento por babor alrededor de los Sharks y el emprendedor.
    public Plano PlanoFinal(float t)
    {
        Vector3 c = Vector3.Lerp(CentroGrupo(), CabezaEmprendedor(), 0.5f) - Vector3.up * 0.4f;
        Vector3 babor = barco.TransformDirection(-ladoEscalera).normalized;
        Vector3 dir = Quaternion.AngleAxis(Mathf.Sin(t * 0.13f) * 55f, Vector3.up) * babor;
        return new Plano(c + dir * 8.5f + Vector3.up * 3.6f, c, 50f);
    }

    /// Cámara que acompaña la subida por la escalera exterior.
    public Plano PlanoSubida()
    {
        Vector3 e = emprendedor.transform.position + Vector3.up * 1f;
        Vector3 fuera = barco.TransformDirection(ladoEscalera).normalized;
        Vector3 proa = barco.TransformDirection(direccionProa).normalized;
        return new Plano(e + fuera * 8.5f - proa * 4f + Vector3.up * 2.2f, e, 45f);
    }
}
