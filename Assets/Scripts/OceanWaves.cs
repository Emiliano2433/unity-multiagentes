using UnityEngine;

/// Define las olas del océano. Envía los parámetros al shader "OnePiece/Ocean"
/// y expone la misma función de altura en C# para que el barco flote sincronizado.
[ExecuteAlways]
public class OceanWaves : MonoBehaviour
{
    public static OceanWaves Instance { get; private set; }

    [Tooltip("x,y = dirección | z = amplitud (m) | w = longitud de onda (m)")]
    public Vector4[] waves =
    {
        new Vector4( 1.0f,  0.35f, 0.55f, 46f),
        new Vector4( 0.55f,-0.85f, 0.32f, 27f),
        new Vector4(-0.35f, 1.0f,  0.18f, 15f),
        new Vector4( 0.9f,  0.8f,  0.09f,  8f),
    };

    [Range(0f, 3f)] public float timeScale = 1f;

    static readonly int WavesId = Shader.PropertyToID("_OP_Waves");
    static readonly int TimeId  = Shader.PropertyToID("_OP_WaveTime");
    readonly Vector4[] _buffer = new Vector4[4];

    public float WaveTime => Application.isPlaying ? Time.time * timeScale : 0f;

    void OnEnable()  { Instance = this; Push(); }
    void OnValidate() { Push(); }
    void Update()    { Instance = this; Push(); }

    void Push()
    {
        for (int i = 0; i < 4; i++) _buffer[i] = i < waves.Length ? waves[i] : Vector4.zero;
        Shader.SetGlobalVectorArray(WavesId, _buffer);
        Shader.SetGlobalFloat(TimeId, WaveTime);
    }

    /// Altura del agua en el punto (x,z) del mundo — idéntica a la del shader.
    public float HeightAt(Vector3 world)
    {
        float t = WaveTime, h = 0f;
        for (int i = 0; i < waves.Length && i < 4; i++)
        {
            Vector4 w = waves[i];
            Vector2 d = new Vector2(w.x + 1e-5f, w.y).normalized;
            float k = 2f * Mathf.PI / Mathf.Max(w.w, 0.01f);
            float c = Mathf.Sqrt(9.81f * k);
            h += w.z * Mathf.Sin(k * (d.x * world.x + d.y * world.z) - c * t);
        }
        return transform.position.y + h;
    }
}
