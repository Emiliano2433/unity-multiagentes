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

    void Awake()
    {
        if (cuerpo != null) _cuerpoBase = cuerpo.localPosition;
        if (cabeza != null) _cabezaBase = cabeza.localRotation;
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
}
