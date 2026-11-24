using System.Collections;
using UnityEngine;

/// Emprendedor hecho con primitivas (lo arma OnePieceSceneBuilder) con animación procedural:
/// caminar, subir escaleras, respirar y gesticular mientras habla.
/// Se mueve en el espacio local de su padre (el barco), así se mece junto con él.
public class Emprendedor : MonoBehaviour
{
    [Header("Huesos (los asigna el constructor de la escena)")]
    public Transform cuerpo;        // pivote en la cadera: sube/baja al caminar
    public Transform piernaIzq, piernaDer;
    public Transform brazoIzq, brazoDer;
    public Transform cabeza;

    [Header("Esqueleto (modelo con huesos, p. ej. Light Yagami)")]
    public bool rig;
    public Transform pelvis, columna, huesoCabeza;
    public Transform musloIzq, musloDer, rodillaIzq, rodillaDer;
    public Transform hombroIzq, hombroDer, codoIzq, codoDer;
    [Tooltip("Altura extra del centro de la cabeza sobre el hueso de la cabeza (m)")]
    public float altoCabeza = 0.11f;

    [Header("Movimiento")]
    public float velocidadPiso = 1.35f;      // m/s caminando
    public float velocidadEscalera = 0.85f;  // m/s sobre la diagonal de la escalera
    public float giro = 540f;                // grados/s

    [HideInInspector] public bool hablando;
    public bool Caminando { get; private set; }
    public float multiplicadorVelocidad = 1f;

    float _fase;          // fase del ciclo de pasos
    float _intensidad;    // 0 = quieto, 1 = caminando
    bool _enEscalera;
    float _gesto;
    Vector3 _cuerpoBase;
    Quaternion _cabezaBase;

    // ---- modo esqueleto: pose de reposo y "bajar brazos" (en espacio del personaje)
    Transform[] _huesos;
    Quaternion[] _reposo;
    Vector3 _pelvisBase;
    Quaternion _bajarIzq = Quaternion.identity, _bajarDer = Quaternion.identity;
    bool _rigListo;

    void Awake()
    {
        if (cuerpo != null) _cuerpoBase = cuerpo.localPosition;
        if (cabeza != null) _cabezaBase = cabeza.localRotation;
        PrepararRig();
    }

    /// Centro de la cabeza en el mundo (para cámaras, foco y retrato).
    public Vector3 PuntoCabeza
    {
        get
        {
            if (rig && huesoCabeza != null) return huesoCabeza.position + transform.up * altoCabeza * transform.lossyScale.y;
            if (cabeza != null) return cabeza.position;
            return transform.position + Vector3.up * 1.6f;
        }
    }

    void PrepararRig()
    {
        if (!rig || _rigListo) return;
        _huesos = new[] { pelvis, columna, huesoCabeza, musloIzq, musloDer, rodillaIzq, rodillaDer, hombroIzq, hombroDer, codoIzq, codoDer };
        _reposo = new Quaternion[_huesos.Length];
        for (int i = 0; i < _huesos.Length; i++) if (_huesos[i] != null) _reposo[i] = _huesos[i].localRotation;
        if (pelvis != null) _pelvisBase = pelvis.localPosition;
        _bajarIzq = BajarBrazo(hombroIzq, codoIzq);
        _bajarDer = BajarBrazo(hombroDer, codoDer);
        _rigListo = true;
    }

    /// Rotación (en espacio del personaje) que lleva el brazo de la pose en A a colgar junto al cuerpo.
    Quaternion BajarBrazo(Transform hombro, Transform codo)
    {
        if (hombro == null || codo == null) return Quaternion.identity;
        Vector3 d = (codo.position - hombro.position).normalized;
        Vector3 lado = Vector3.Dot(d, transform.right) >= 0 ? transform.right : -transform.right;
        Vector3 obj = (-transform.up * 0.95f + lado * 0.2f + transform.forward * 0.07f).normalized;
        Quaternion w = Quaternion.FromToRotation(d, obj);
        return Quaternion.Inverse(transform.rotation) * w * transform.rotation;
    }

    /// Coloca al personaje en un punto (local al padre) mirando hacia 'mirar' (local).
    public void Colocar(Vector3 punto, Vector3 mirar)
    {
        transform.localPosition = punto;
        Vector3 d = mirar - punto; d.y = 0;
        if (d.sqrMagnitude > 1e-4f) transform.localRotation = Quaternion.LookRotation(d);
    }

    /// Recorre los puntos (locales al padre) en orden. Los tramos inclinados se tratan como escalera.
    public IEnumerator Recorrer(Vector3[] puntos, int desde = 0)
    {
        Caminando = true;
        for (int i = desde; i < puntos.Length; i++)
        {
            Vector3 a = transform.localPosition, b = puntos[i];
            Vector3 d = b - a;
            float horiz = new Vector2(d.x, d.z).magnitude;
            _enEscalera = Mathf.Abs(d.y) > 0.25f && Mathf.Abs(d.y) > horiz * 0.35f;
            float largo = d.magnitude;
            if (largo < 1e-3f) continue;
            float v = _enEscalera ? velocidadEscalera : velocidadPiso;
            Quaternion objetivo = horiz > 0.05f ? Quaternion.LookRotation(new Vector3(d.x, 0, d.z)) : transform.localRotation;

            float t = 0;
            while (t < 1f)
            {
                float dt = Time.deltaTime * multiplicadorVelocidad;
                t = Mathf.Min(1f, t + v * dt / largo);
                transform.localPosition = Vector3.Lerp(a, b, t);
                transform.localRotation = Quaternion.RotateTowards(transform.localRotation, objetivo, giro * dt);
                _fase += dt * v * (_enEscalera ? 5.2f : 4.4f);
                yield return null;
            }
        }
        _enEscalera = false;
        Caminando = false;
    }

    /// Gira en su lugar hasta mirar el punto (local al padre).
    public IEnumerator Voltear(Vector3 mirar)
    {
        Vector3 d = mirar - transform.localPosition; d.y = 0;
        if (d.sqrMagnitude < 1e-4f) yield break;
        Quaternion objetivo = Quaternion.LookRotation(d);
        while (Quaternion.Angle(transform.localRotation, objetivo) > 0.5f)
        {
            transform.localRotation = Quaternion.RotateTowards(transform.localRotation, objetivo, giro * 0.6f * Time.deltaTime * multiplicadorVelocidad);
            _fase += Time.deltaTime * 3f;
            yield return null;
        }
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        _intensidad = Mathf.MoveTowards(_intensidad, Caminando ? 1f : 0f, dt * 4f);
        _gesto = Mathf.MoveTowards(_gesto, hablando && !Caminando ? 1f : 0f, dt * 3f);
        if (rig) { AnimarRig(); return; }

        float s = Mathf.Sin(_fase);
        float paso = _enEscalera ? 42f : 30f;           // grados de balanceo de piernas
        float elevar = _enEscalera ? 18f : 0f;          // en la escalera las rodillas suben más (piernas hacia adelante)

        // ángulo negativo = pierna hacia adelante
        if (piernaIzq) piernaIzq.localRotation = Quaternion.Euler((s * paso - elevar * Mathf.Max(0, -s)) * _intensidad, 0, 0);
        if (piernaDer) piernaDer.localRotation = Quaternion.Euler((-s * paso - elevar * Mathf.Max(0, s)) * _intensidad, 0, 0);

        float t = Time.time;
        float respirar = Mathf.Sin(t * 1.7f) * 2f;
        // Brazos: opuestos a las piernas al caminar; al hablar el derecho gesticula y el izquierdo acompaña
        float brazoI = -s * 28f * _intensidad + (-12f - Mathf.Sin(t * 2.3f) * 10f) * _gesto;
        float brazoD = s * 28f * _intensidad + (-55f + Mathf.Sin(t * 3.1f) * 22f) * _gesto;
        if (brazoIzq) brazoIzq.localRotation = Quaternion.Euler(brazoI + respirar, 0, -6f - 8f * _gesto);
        if (brazoDer) brazoDer.localRotation = Quaternion.Euler(brazoD + respirar, 0, 6f + 14f * _gesto * (0.6f + 0.4f * Mathf.Sin(t * 2.7f)));

        if (cuerpo)
        {
            float bob = Mathf.Abs(Mathf.Cos(_fase)) * 0.045f * _intensidad + Mathf.Sin(t * 1.7f) * 0.006f;
            cuerpo.localPosition = _cuerpoBase + Vector3.up * bob;
            cuerpo.localRotation = Quaternion.Euler((_enEscalera ? 9f : 3f) * _intensidad, Mathf.Sin(_fase) * 4f * _intensidad, 0);
        }
        if (cabeza)
            cabeza.localRotation = _cabezaBase * Quaternion.Euler(Mathf.Sin(t * 4.3f) * 5f * _gesto, Mathf.Sin(t * 1.3f) * 7f * _gesto, 0);
    }

    // ================================================================ animación por huesos

    Quaternion EnMundo(Quaternion local) => transform.rotation * local * Quaternion.Inverse(transform.rotation);

    static void Girar(Transform b, Quaternion q) { if (b != null) b.rotation = q * b.rotation; }

    void AnimarRig()
    {
        if (!_rigListo) PrepararRig();
        for (int i = 0; i < _huesos.Length; i++) if (_huesos[i] != null) _huesos[i].localRotation = _reposo[i];

        Vector3 R = transform.right, U = transform.up;
        float t = Time.time;
        float s = Mathf.Sin(_fase), c = Mathf.Cos(_fase);
        float I = _intensidad, G = _gesto;
        float paso = _enEscalera ? 34f : 26f;
        float elevar = _enEscalera ? 30f : 0f;

        // Cadera: sube y baja con los pasos, respira y gira un poco
        if (pelvis != null)
        {
            float bob = Mathf.Abs(c) * 0.035f * I + Mathf.Sin(t * 1.7f) * 0.004f;
            Vector3 delta = pelvis.parent != null ? pelvis.parent.InverseTransformVector(U * bob) : U * bob;
            pelvis.localPosition = _pelvisBase + delta;
            Girar(pelvis, Quaternion.AngleAxis(s * 4f * I, U));
        }
        // Torso: se inclina al subir la escalera y acompaña el gesto
        Girar(columna, Quaternion.AngleAxis((_enEscalera ? 10f : 2.5f) * I + Mathf.Sin(t * 1.7f) * 0.8f + 2f * G, R)
                       * Quaternion.AngleAxis(Mathf.Sin(t * 1.9f) * 3f * G - s * 3f * I, U));

        // Piernas (ángulo negativo = hacia adelante; rodilla positiva = la espinilla va hacia atrás)
        Girar(musloIzq, Quaternion.AngleAxis((s * paso - elevar * Mathf.Max(0, -s)) * I, R));
        Girar(musloDer, Quaternion.AngleAxis((-s * paso - elevar * Mathf.Max(0, s)) * I, R));
        float flexBase = _enEscalera ? 48f : 30f;
        Girar(rodillaIzq, Quaternion.AngleAxis((6f + flexBase * Mathf.Max(0, -c)) * I + 3f, R));
        Girar(rodillaDer, Quaternion.AngleAxis((6f + flexBase * Mathf.Max(0, c)) * I + 3f, R));

        // Brazos: bajan de la pose en A, se balancean al caminar; al hablar gesticula con la mano de la pluma
        float respirar = Mathf.Sin(t * 1.7f) * 1.5f;
        float swingI = -s * 22f * I + (-10f - Mathf.Sin(t * 2.3f) * 6f) * G + respirar;
        float swingD = s * 22f * I + (-38f + Mathf.Sin(t * 3.1f) * 16f) * G + respirar;
        Girar(hombroIzq, Quaternion.AngleAxis(swingI, R) * EnMundo(_bajarIzq));
        Girar(hombroDer, Quaternion.AngleAxis(swingD, R) * Quaternion.AngleAxis(-8f * G * (0.6f + 0.4f * Mathf.Sin(t * 2.7f)), transform.forward) * EnMundo(_bajarDer));
        Girar(codoIzq, Quaternion.AngleAxis(-(14f + 22f * G), R));       // sostiene la Death Note
        Girar(codoDer, Quaternion.AngleAxis(-(10f + 48f * G + Mathf.Sin(t * 3.4f) * 12f * G), R));

        // Cabeza: asiente y mira un poco a los lados al hablar
        Girar(huesoCabeza, Quaternion.AngleAxis(Mathf.Sin(t * 4.3f) * 4f * G - 2f * (_enEscalera ? I : 0f), R)
                           * Quaternion.AngleAxis(Mathf.Sin(t * 1.3f) * 7f * G, U));
    }
}
