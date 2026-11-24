using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Estilo visual compartido por ShowUI y ShowInput: fuente del sistema (más legible que la
/// integrada), paneles con esquinas redondeadas, sombras suaves y paleta de colores.
/// Todo se genera por código: no hace falta importar sprites ni fuentes.
public static class EstiloUI
{
    // ---- paleta
    public static readonly Color Fondo       = new Color(0.055f, 0.065f, 0.11f, 0.94f);
    public static readonly Color FondoSuave  = new Color(0.09f, 0.105f, 0.17f, 0.92f);
    public static readonly Color Linea       = new Color(1f, 1f, 1f, 0.10f);
    public static readonly Color TextoClaro  = new Color(0.96f, 0.95f, 0.92f);
    public static readonly Color TextoSuave  = new Color(0.75f, 0.77f, 0.84f);
    public static readonly Color Dorado      = new Color(1f, 0.79f, 0.27f);
    public static readonly Color DoradoOscuro = new Color(0.78f, 0.55f, 0.1f);
    public static readonly Color Verde       = new Color(0.24f, 0.82f, 0.45f);
    public static readonly Color Rojo        = new Color(0.94f, 0.27f, 0.25f);
    public static readonly Color Sombra      = new Color(0f, 0f, 0f, 0.45f);

    static Font _fuente;
    static readonly Dictionary<int, Sprite> _redondos = new();
    static Sprite _sombra, _circulo, _degradado;

    /// Fuente del sistema con buena lectura; si no existe ninguna, la integrada de Unity.
    public static Font Fuente
    {
        get
        {
            if (_fuente != null) return _fuente;
            try
            {
                _fuente = Font.CreateDynamicFontFromOSFont(
                    new[] { "Avenir Next", "Avenir", "Helvetica Neue", "Segoe UI", "Roboto", "Arial" }, 32);
            }
            catch { _fuente = null; }
            if (_fuente == null) _fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _fuente;
        }
    }

    /// Rectángulo redondeado blanco en 9-slice (se tiñe con Image.color).
    public static Sprite Redondo(int radio = 16)
    {
        if (_redondos.TryGetValue(radio, out var s) && s != null) return s;
        int tam = radio * 2 + 4;
        var tex = new Texture2D(tam, tam, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[tam * tam];
        float c = radio + 2;
        for (int y = 0; y < tam; y++)
            for (int x = 0; x < tam; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x + 0.5f - tam * 0.5f) - (tam * 0.5f - c));
                float dy = Mathf.Max(0, Mathf.Abs(y + 0.5f - tam * 0.5f) - (tam * 0.5f - c));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radio + 0.5f - d);
                px[y * tam + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px); tex.Apply();
        float b = radio + 1;
        s = Sprite.Create(tex, new Rect(0, 0, tam, tam), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        s.name = "Redondo" + radio;
        _redondos[radio] = s;
        return s;
    }

    /// Sombra difusa en 9-slice (se pone detrás de un panel, un poco más grande).
    public static Sprite SombraSprite
    {
        get
        {
            if (_sombra != null) return _sombra;
            const int tam = 96, borde = 40;
            var tex = new Texture2D(tam, tam, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[tam * tam];
            for (int y = 0; y < tam; y++)
                for (int x = 0; x < tam; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(x + 0.5f - tam * 0.5f) - (tam * 0.5f - borde));
                    float dy = Mathf.Max(0, Mathf.Abs(y + 0.5f - tam * 0.5f) - (tam * 0.5f - borde));
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / borde;
                    float a = Mathf.Clamp01(1 - d);
                    a = a * a * (3 - 2 * a);
                    px[y * tam + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            _sombra = Sprite.Create(tex, new Rect(0, 0, tam, tam), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(borde, borde, borde, borde));
            return _sombra;
        }
    }

    public static Sprite Circulo
    {
        get
        {
            if (_circulo != null) return _circulo;
            const int tam = 128;
            var tex = new Texture2D(tam, tam, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[tam * tam];
            for (int y = 0; y < tam; y++)
                for (int x = 0; x < tam; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(tam / 2f, tam / 2f));
                    px[y * tam + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(tam / 2f - d) * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            _circulo = Sprite.Create(tex, new Rect(0, 0, tam, tam), new Vector2(0.5f, 0.5f), 100);
            return _circulo;
        }
    }

    /// Degradado vertical (transparente abajo → opaco arriba) para barras de cine y fondos.
    public static Sprite Degradado
    {
        get
        {
            if (_degradado != null) return _degradado;
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) tex.SetPixel(0, y, new Color(1, 1, 1, Mathf.SmoothStep(0, 1, y / 63f)));
            tex.Apply();
            _degradado = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f), 100);
            return _degradado;
        }
    }

    // ---------------------------------------------------------------- construcción

    /// Panel = contenedor sin imagen con hijos "Sombra" (opcional), "Contorno" (opcional) y "Fondo".
    /// El contenido que se agregue después queda encima del fondo.
    public static RectTransform Panel(Transform parent, string name, Color color, int radio, bool sombra, out Image fondo)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        if (sombra) Capa(rt, "Sombra", SombraSprite, new Color(0, 0, 0, 0.55f), 26f, -10f);
        fondo = Capa(rt, "Fondo", radio > 0 ? Redondo(radio) : null, color, 0f, 0f);
        return rt;
    }

    public static RectTransform Panel(Transform parent, string name, Color color, int radio = 18, bool sombra = false)
        => Panel(parent, name, color, radio, sombra, out _);

    public static Image FondoDe(RectTransform panel) => panel.Find("Fondo")?.GetComponent<Image>();

    /// Borde: una capa redondeada un poco mayor, justo debajo del fondo.
    public static Image Contorno(RectTransform panel, Color color, int radio, float grosor = 3f)
    {
        var img = Capa(panel, "Contorno", Redondo(radio + Mathf.RoundToInt(grosor)), color, grosor, 0f);
        var fondo = panel.Find("Fondo");
        // con espacio de color lineal, un fondo semitransparente deja "teñirse" el borde: se hace opaco
        if (fondo != null) { var fi = fondo.GetComponent<Image>(); var c = fi.color; c.a = 1f; fi.color = c; }
        img.transform.SetSiblingIndex(fondo != null ? fondo.GetSiblingIndex() : 0);
        return img;
    }

    static Image Capa(RectTransform padre, string name, Sprite sprite, Color color, float extra, float dy)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var r = (RectTransform)go.transform;
        r.SetParent(padre, false);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(-extra, -extra + dy); r.offsetMax = new Vector2(extra, extra + dy);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    public static Text Texto(Transform parent, string name, int size, TextAnchor align, Color color, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = go.GetComponent<Text>();
        t.font = Fuente; t.fontSize = size; t.alignment = align; t.color = color; t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.supportRichText = true;
        return t;
    }

    public static void Ubicar(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    public static void Llenar(RectTransform rt, float izq = 0, float abajo = 0, float der = 0, float arriba = 0)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(izq, abajo); rt.offsetMax = new Vector2(-der, -arriba);
    }

    public static void SombraTexto(Text t, float d = 2f, float a = 0.7f)
    {
        var s = t.gameObject.AddComponent<Shadow>();
        s.effectColor = new Color(0, 0, 0, a); s.effectDistance = new Vector2(d, -d);
    }

    /// Color legible (negro o blanco) sobre un fondo dado.
    public static Color TextoSobre(Color fondo) => fondo.grayscale > 0.55f ? new Color(0.07f, 0.07f, 0.1f) : Color.white;

    public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    public static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1;
        x = Mathf.Clamp01(x);
        return 1 + c3 * Mathf.Pow(x - 1, 3) + c1 * Mathf.Pow(x - 1, 2);
    }

    public static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1 - (1 - x) * (1 - x) * (1 - x); }

    /// Foto con marco de color: anillo (contorno) + imagen recortada redondeada + inicial de respaldo.
    public class Foto
    {
        public RectTransform rt;
        public Image anillo;
        public RawImage imagen;
        public Text inicial;

        public void Poner(Texture tex, string nombre, Color color)
        {
            anillo.color = color;
            imagen.texture = tex;
            imagen.enabled = tex != null;
            inicial.enabled = tex == null;
            inicial.text = string.IsNullOrEmpty(nombre) ? "?" : nombre.Substring(0, 1).ToUpperInvariant();
            inicial.color = color;
        }
    }

    public static Foto CrearFoto(Transform parent, string name, float tam, int radio, float grosor = 4f)
    {
        var f = new Foto();
        f.rt = Panel(parent, name, Color.white, radio, false, out f.anillo);
        f.rt.sizeDelta = new Vector2(tam, tam);
        var mascara = Panel(f.rt, "Mascara", new Color(0.12f, 0.14f, 0.22f), Mathf.Max(2, radio - Mathf.RoundToInt(grosor)), false, out var fondo);
        Llenar(mascara, grosor, grosor, grosor, grosor);
        fondo.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var raw = new GameObject("Imagen", typeof(RectTransform), typeof(RawImage));
        var rr = (RectTransform)raw.transform; rr.SetParent(fondo.transform, false);
        Llenar(rr);
        f.imagen = raw.GetComponent<RawImage>(); f.imagen.raycastTarget = false; f.imagen.enabled = false;
        f.inicial = Texto(fondo.transform, "Inicial", Mathf.RoundToInt(tam * 0.45f), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        return f;
    }
}
