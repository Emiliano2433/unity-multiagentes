using System.Collections.Generic;
using UnityEngine;

/// Voces de diálogo estilo Celeste: mientras el texto se escribe, cada sílaba suena como un
/// "blip" corto con la vocal de esa letra. Cada personaje tiene su propia voz (tono, timbre,
/// aspereza). Todo el audio se sintetiza al iniciar: no hay archivos de sonido.
/// También genera efectos de interfaz (abrir diálogo, tarjeta, dentro, fuera, trato).
public class VocesShow : MonoBehaviour
{
    [Range(0f, 1f)] public float volumenVoces = 0.42f;
    [Range(0f, 1f)] public float volumenEfectos = 0.5f;
    [Tooltip("Segundos mínimos entre dos blips (más bajo = más parlanchín)")]
    public float separacion = 0.058f;

    const int Fs = 44100;

    struct Perfil
    {
        public float f0;        // tono base (Hz)
        public float ruido;     // aspereza
        public float brillo;    // peso del 2º formante
        public float dur;       // duración del blip (s)
        public float caida;     // cuánto baja el tono durante el blip
        public float cuadrada;  // 0 = suave (seno), 1 = más "chiptune"
    }

    static readonly Dictionary<string, Perfil> Perfiles = new()
    {
        ["emprendedor"] = new Perfil { f0 = 168f, ruido = 0.015f, brillo = 0.55f, dur = 0.072f, caida = 0.05f, cuadrada = 0.15f }, // Light: sereno
        ["luffy"]       = new Perfil { f0 = 285f, ruido = 0.0f,   brillo = 0.95f, dur = 0.060f, caida = 0.10f, cuadrada = 0.45f }, // agudo y alegre
        ["zoro"]        = new Perfil { f0 = 116f, ruido = 0.05f,  brillo = 0.45f, dur = 0.078f, caida = 0.03f, cuadrada = 0.25f }, // grave y seco
        ["shanks"]      = new Perfil { f0 = 148f, ruido = 0.01f,  brillo = 0.65f, dur = 0.074f, caida = 0.06f, cuadrada = 0.2f  }, // cálido
        ["barbablanca"] = new Perfil { f0 = 78f,  ruido = 0.07f,  brillo = 0.35f, dur = 0.095f, caida = 0.02f, cuadrada = 0.3f  }, // muy grave
        ["arlong"]      = new Perfil { f0 = 124f, ruido = 0.16f,  brillo = 0.6f,  dur = 0.07f,  caida = 0.08f, cuadrada = 0.55f }, // áspero
        ["_"]           = new Perfil { f0 = 160f, ruido = 0.02f,  brillo = 0.6f,  dur = 0.07f,  caida = 0.05f, cuadrada = 0.25f },
    };

    // (F1, F2) aproximados de las vocales del español
    static readonly Vector2[] Vocales = { new(750, 1250), new(470, 1900), new(310, 2250), new(500, 900), new(340, 780) };

    AudioSource _voz, _sfx;
    readonly Dictionary<string, AudioClip[]> _clips = new();
    readonly Dictionary<string, AudioClip> _efectos = new();
    float _ultimo = -1f;
    int _desdeUltimo;

    void Awake()
    {
        _voz = gameObject.AddComponent<AudioSource>();
        _voz.playOnAwake = false; _voz.spatialBlend = 0f;
        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false; _sfx.spatialBlend = 0f;

        foreach (var kv in Perfiles) _clips[kv.Key] = GenerarVoz(kv.Key, kv.Value);
        _efectos["abrir"]   = Efecto("abrir", 0.11f, (t, d) => Seno(Mathf.Lerp(520, 980, t / d), t) * Env(t, d, 0.004f) * 0.5f);
        _efectos["pagina"]  = Efecto("pagina", 0.05f, (t, d) => Seno(1500, t) * Env(t, d, 0.002f) * 0.25f);
        _efectos["tarjeta"] = Efecto("tarjeta", 0.55f, Whoosh);
        _efectos["dentro"]  = Efecto("dentro", 0.75f, (t, d) => Arpegio(t, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.075f, 0.45f));
        _efectos["fuera"]   = Efecto("fuera", 0.6f, (t, d) => Arpegio(t, new[] { 311.13f, 233.08f }, 0.16f, 0.4f, 0.6f));
        _efectos["trato"]   = Efecto("trato", 1.3f, (t, d) => Arpegio(t, new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f, 1567.98f }, 0.07f, 0.8f) + Brillo(t) * 0.12f);
        _efectos["turno"]   = Efecto("turno", 0.16f, (t, d) => (Seno(880, t) + 0.5f * Seno(1320, t)) * Env(t, d, 0.003f) * 0.3f);
    }

    // ================================================================ API

    /// Llamar por cada letra nueva que aparece en el diálogo.
    public void Letra(string voz, char c)
    {
        _desdeUltimo++;
        if (!char.IsLetterOrDigit(c)) return;
        float ahora = Time.unscaledTime;
        if (ahora - _ultimo < separacion || _desdeUltimo < 2) return;
        _ultimo = ahora; _desdeUltimo = 0;
        if (!_clips.TryGetValue(voz ?? "_", out var clips)) clips = _clips["_"];
        int vocal = Vocal(c);
        int variante = Random.Range(0, 3);
        var clip = clips[vocal * 3 + variante];
        _voz.pitch = Random.Range(0.95f, 1.06f);
        _voz.PlayOneShot(clip, volumenVoces);
    }

    public void Efecto(string nombre, float volumen = 1f)
    {
        if (_efectos.TryGetValue(nombre, out var c)) _sfx.PlayOneShot(c, volumenEfectos * volumen);
    }

    /// Clave de voz para un nombre ("Whitebeard", "Luffy", …).
    public static string Clave(string nombre)
    {
        string k = FormatoShow.Clave(nombre);
        return Perfiles.ContainsKey(k) ? k : "_";
    }

    static int Vocal(char c)
    {
        switch (char.ToLowerInvariant(c))
        {
            case 'a': case 'á': return 0;
            case 'e': case 'é': return 1;
            case 'i': case 'í': case 'y': return 2;
            case 'o': case 'ó': return 3;
            case 'u': case 'ú': case 'ü': return 4;
            default: return (char.ToLowerInvariant(c) * 7) % 5;   // consonantes: vocal "pegada"
        }
    }

    // ================================================================ síntesis

    AudioClip[] GenerarVoz(string nombre, Perfil p)
    {
        var res = new AudioClip[Vocales.Length * 3];
        var rnd = new System.Random(nombre.GetHashCode());
        for (int v = 0; v < Vocales.Length; v++)
            for (int k = 0; k < 3; k++)
            {
                float f0 = p.f0 * (k == 0 ? 1f : k == 1 ? 1.06f : 0.95f);
                int n = Mathf.CeilToInt(p.dur * Fs);
                var data = new float[n];
                float F1 = Vocales[v].x, F2 = Vocales[v].y, pico = 0;
                int armonicos = Mathf.Min(40, Mathf.FloorToInt(4200f / f0));
                var fase = new float[armonicos + 1];
                for (int h = 1; h <= armonicos; h++) fase[h] = (float)rnd.NextDouble() * Mathf.PI * 2;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Fs;
                    float f = f0 * (1f + p.caida * Mathf.Exp(-t / 0.02f)) * (1f + 0.01f * Mathf.Sin(t * 38f));
                    float s = 0;
                    for (int h = 1; h <= armonicos; h++)
                    {
                        float fh = f * h;
                        float a = Mathf.Exp(-Sq((fh - F1) / 140f)) + p.brillo * 0.75f * Mathf.Exp(-Sq((fh - F2) / 230f)) + 0.06f;
                        a *= 1f / Mathf.Pow(h, 0.35f - 0.25f * p.cuadrada);
                        s += a * Mathf.Sin(2 * Mathf.PI * fh * t + fase[h]);
                    }
                    s += p.ruido * ((float)rnd.NextDouble() * 2 - 1) * 2f;
                    s *= Env(t, p.dur, 0.005f);
                    data[i] = s;
                    pico = Mathf.Max(pico, Mathf.Abs(s));
                }
                if (pico > 0) for (int i = 0; i < n; i++) data[i] = data[i] / pico * 0.8f;
                var clip = AudioClip.Create($"voz_{nombre}_{v}_{k}", n, 1, Fs, false);
                clip.SetData(data, 0);
                res[v * 3 + k] = clip;
            }
        return res;
    }

    static AudioClip Efecto(string nombre, float dur, System.Func<float, float, float> f)
    {
        int n = Mathf.CeilToInt(dur * Fs);
        var data = new float[n];
        float pico = 0;
        for (int i = 0; i < n; i++) { data[i] = f(i / (float)Fs, dur); pico = Mathf.Max(pico, Mathf.Abs(data[i])); }
        if (pico > 0.95f) for (int i = 0; i < n; i++) data[i] *= 0.95f / pico;
        var clip = AudioClip.Create("sfx_" + nombre, n, 1, Fs, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Sq(float x) => x * x;
    static float Seno(float f, float t) => Mathf.Sin(2 * Mathf.PI * f * t);

    /// Envolvente: ataque corto y caída exponencial hasta 0 al final.
    static float Env(float t, float dur, float ataque)
    {
        float a = Mathf.Clamp01(t / ataque);
        float r = Mathf.Exp(-t / (dur * 0.38f));
        float fin = Mathf.Clamp01((dur - t) / (dur * 0.15f));
        return a * r * fin;
    }

    static float Arpegio(float t, float[] notas, float paso, float vol, float sostener = 0.35f)
    {
        float s = 0;
        for (int i = 0; i < notas.Length; i++)
        {
            float t0 = i * paso;
            if (t < t0) break;
            float tt = t - t0;
            float env = Mathf.Clamp01(tt / 0.006f) * Mathf.Exp(-tt / sostener);
            float onda = Seno(notas[i], tt) * 0.7f + Seno(notas[i] * 2, tt) * 0.2f + Mathf.Sign(Seno(notas[i], tt)) * 0.08f;
            s += onda * env;
        }
        return s * vol;
    }

    static float Whoosh(float t, float d)
    {
        float x = t / d;
        float env = Mathf.Sin(Mathf.PI * Mathf.Pow(x, 0.7f));
        float f = Mathf.Lerp(220f, 900f, x);
        float ruido = Mathf.PerlinNoise(t * f * 0.12f, 0.3f) * 2 - 1;
        return (ruido * 0.6f + Seno(f * 0.5f, t) * 0.1f) * env * 0.35f;
    }

    static float Brillo(float t)
    {
        float s = 0;
        for (int i = 0; i < 6; i++)
        {
            float t0 = 0.25f + i * 0.09f;
            if (t < t0) break;
            float tt = t - t0;
            s += Seno(2093f + i * 260f, tt) * Mathf.Exp(-tt / 0.08f);
        }
        return s;
    }
}
