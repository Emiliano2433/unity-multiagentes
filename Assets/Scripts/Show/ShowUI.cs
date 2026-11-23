using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Interfaz del show construida por código (uGUI + fuente integrada, sin assets extra):
/// subtítulos con nombre de quien habla, etapa, solicitud, marcador de Sharks, sello de
/// "dentro / fuera", tarjetas de transición, resumen final y placas sobre cada Shark.
public class ShowUI : MonoBehaviour
{
    Font _font;
    RectTransform _root;

    // Subtítulos
    GameObject _sub;
    Image _subNombreFondo;
    Text _subNombre, _subEtiqueta, _subTexto, _subPagina;

    // Arriba
    Text _etapa, _solicitud, _estado, _ayuda;
    GameObject _etapaCaja, _solicitudCaja;

    // Marcador
    RectTransform _marcador;
    readonly List<(Text nombre, Text estado, Image franja)> _filas = new();

    // Centro
    CanvasGroup _tarjeta; Text _tarjetaGrande, _tarjetaSub; float _tarjetaHasta;
    CanvasGroup _sello; Text _selloTitulo, _selloDetalle; Image _selloFondo; float _selloT0 = -10f, _selloHasta;
    GameObject _resumen; Text _resumenTitulo, _resumenCuerpo;

    // Placas en el mundo
    readonly List<Placa> _placas = new();

    public static readonly Color Verde = new Color(0.22f, 0.78f, 0.35f);
    public static readonly Color Rojo = new Color(0.9f, 0.22f, 0.2f);
    public static readonly Color Gris = new Color(0.7f, 0.72f, 0.76f);
    public static readonly Color Dorado = new Color(1f, 0.78f, 0.25f);

    public void Construir()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("UI Show", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _root = (RectTransform)canvasGo.transform;

        // ---- Subtítulos (abajo)
        var sub = Panel(_root, "Subtitulos", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(1500, 230), new Color(0.04f, 0.06f, 0.11f, 0.86f));
        _sub = sub.gameObject;
        Borde(sub, new Color(1, 1, 1, 0.12f));
        var nombreCaja = Panel(sub, "Nombre", new Vector2(0, 1), new Vector2(0, 0), new Vector2(28, -22), new Vector2(330, 54), Color.white);
        _subNombreFondo = nombreCaja.GetComponent<Image>();
        _subNombre = Texto(nombreCaja, "Texto", 32, TextAnchor.MiddleCenter, Color.black, FontStyle.Bold);
        _subEtiqueta = Texto(sub, "Etiqueta", 24, TextAnchor.UpperLeft, new Color(1, 1, 1, 0.65f), FontStyle.Italic);
        Ubicar(_subEtiqueta.rectTransform, new Vector2(0, 1), new Vector2(380, -12), new Vector2(1000, 34));
        _subTexto = Texto(sub, "Texto", 31, TextAnchor.UpperLeft, Color.white);
        _subTexto.lineSpacing = 1.05f;
        _subTexto.supportRichText = true;
        var rt = _subTexto.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(34, 18); rt.offsetMax = new Vector2(-34, -48);
        _subPagina = Texto(sub, "Pagina", 20, TextAnchor.LowerRight, new Color(1, 1, 1, 0.5f));
        Ubicar(_subPagina.rectTransform, new Vector2(1, 0), new Vector2(-24, 8), new Vector2(200, 28), new Vector2(1, 0));
        _sub.SetActive(false);

        // ---- Etapa (arriba al centro)
        var etapa = Panel(_root, "Etapa", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(760, 64), new Color(0.55f, 0.08f, 0.08f, 0.9f));
        Borde(etapa, Dorado);
        _etapaCaja = etapa.gameObject;
        _etapa = Texto(etapa, "Texto", 32, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        _etapaCaja.SetActive(false);

        // ---- Solicitud (arriba izquierda)
        var sol = Panel(_root, "Solicitud", new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(520, 92), new Color(0.04f, 0.06f, 0.11f, 0.8f));
        _solicitudCaja = sol.gameObject;
        _solicitud = Texto(sol, "Texto", 24, TextAnchor.MiddleLeft, Color.white);
        _solicitud.rectTransform.offsetMin = new Vector2(18, 6); _solicitud.rectTransform.offsetMax = new Vector2(-12, -6);
        _solicitudCaja.SetActive(false);

        // ---- Estado de conexión (arriba derecha)
        _estado = Texto(_root, "Estado", 20, TextAnchor.UpperRight, Color.white);
        Ubicar(_estado.rectTransform, new Vector2(1, 1), new Vector2(-24, -18), new Vector2(620, 30), new Vector2(1, 1));
        Sombra(_estado);

        _ayuda = Texto(_root, "Ayuda", 17, TextAnchor.LowerRight, new Color(1, 1, 1, 0.55f));
        Ubicar(_ayuda.rectTransform, new Vector2(1, 0), new Vector2(-14, 8), new Vector2(900, 24), new Vector2(1, 0));
        _ayuda.text = "Espacio: siguiente   ·   F: rápido   ·   V: cámara libre";
        Sombra(_ayuda);

        // ---- Marcador (derecha)
        _marcador = Panel(_root, "Marcador", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -60), new Vector2(340, 60), new Color(0.04f, 0.06f, 0.11f, 0.78f));
        _marcador.gameObject.SetActive(false);

        // ---- Tarjeta de transición (centro)
        var tarjeta = Panel(_root, "Tarjeta", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 300), new Vector2(1000, 170), new Color(0.03f, 0.04f, 0.08f, 0.85f));
        Borde(tarjeta, Dorado);
        _tarjeta = tarjeta.gameObject.AddComponent<CanvasGroup>();
        _tarjetaGrande = Texto(tarjeta, "Grande", 58, TextAnchor.MiddleCenter, Dorado, FontStyle.Bold);
        _tarjetaGrande.rectTransform.offsetMin = new Vector2(10, 54);
        Sombra(_tarjetaGrande);
        _tarjetaSub = Texto(tarjeta, "Sub", 30, TextAnchor.MiddleCenter, Color.white);
        _tarjetaSub.rectTransform.offsetMax = new Vector2(-10, -108);
        _tarjeta.alpha = 0;

        // ---- Sello dentro/fuera (centro)
        var sello = Panel(_root, "Sello", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 290), new Vector2(760, 180), Verde);
        _selloFondo = sello.GetComponent<Image>();
        Borde(sello, Color.white);
        _sello = sello.gameObject.AddComponent<CanvasGroup>();
        _selloTitulo = Texto(sello, "Titulo", 66, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        _selloTitulo.rectTransform.offsetMin = new Vector2(10, 58);
        Sombra(_selloTitulo);
        _selloDetalle = Texto(sello, "Detalle", 30, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        _selloDetalle.rectTransform.offsetMax = new Vector2(-10, -118);
        _sello.alpha = 0;

        // ---- Resumen final
        var res = Panel(_root, "Resumen", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, 30), new Vector2(820, 400), new Color(0.03f, 0.04f, 0.08f, 0.88f));
        Borde(res, Dorado);
        _resumen = res.gameObject;
        _resumenTitulo = Texto(res, "Titulo", 44, TextAnchor.UpperCenter, Dorado, FontStyle.Bold);
        _resumenTitulo.rectTransform.offsetMax = new Vector2(-20, -24);
        _resumenCuerpo = Texto(res, "Cuerpo", 30, TextAnchor.UpperLeft, Color.white);
        _resumenCuerpo.supportRichText = true;
        _resumenCuerpo.rectTransform.offsetMin = new Vector2(50, 24); _resumenCuerpo.rectTransform.offsetMax = new Vector2(-40, -110);
        _resumen.SetActive(false);
    }

    void Update()
    {
        float t = Time.unscaledTime;
        if (_tarjeta != null)
            _tarjeta.alpha = Mathf.MoveTowards(_tarjeta.alpha, t < _tarjetaHasta ? 1f : 0f, Time.unscaledDeltaTime * 3f);
        if (_sello != null)
        {
            float k = t - _selloT0;
            _sello.alpha = Mathf.MoveTowards(_sello.alpha, t < _selloHasta ? 1f : 0f, Time.unscaledDeltaTime * (t < _selloHasta ? 8f : 2.5f));
            float pop = k < 0.35f ? Mathf.Lerp(1.6f, 1f, EaseOut(k / 0.35f)) : 1f;
            _sello.transform.localScale = Vector3.one * pop;
            _sello.transform.localRotation = Quaternion.Euler(0, 0, k < 0.35f ? Mathf.Lerp(-9f, -3f, k / 0.35f) : -3f);
        }
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        foreach (var p in _placas) p.Actualizar(cam);
    }

    static float EaseOut(float x) => 1 - (1 - x) * (1 - x);

    // ================================================================ API

    public void Estado(string texto, Color color) { _estado.text = texto; _estado.color = color; }

    public void Etapa(string texto)
    {
        _etapaCaja.SetActive(!string.IsNullOrEmpty(texto));
        _etapa.text = texto;
    }

    public void Solicitud(string texto)
    {
        _solicitudCaja.SetActive(!string.IsNullOrEmpty(texto));
        _solicitud.text = texto;
    }

    public void Tarjeta(string grande, string sub, float segundos)
    {
        _tarjetaGrande.text = grande;
        _tarjetaSub.text = sub;
        _tarjetaHasta = Time.unscaledTime + segundos;
    }

    public void Hablante(string nombre, Color color, string etiqueta)
    {
        _sub.SetActive(true);
        _subNombre.text = nombre;
        _subNombreFondo.color = color;
        _subNombre.color = color.grayscale > 0.55f ? new Color(0.08f, 0.08f, 0.1f) : Color.white;
        float ancho = Mathf.Clamp(_font != null ? nombre.Length * 19f + 60f : 330f, 180f, 600f);
        ((RectTransform)_subNombreFondo.transform).sizeDelta = new Vector2(ancho, 54);
        _subEtiqueta.rectTransform.anchoredPosition = new Vector2(ancho + 50, -12);
        _subEtiqueta.text = etiqueta ?? "";
        _subTexto.text = "";
        _subPagina.text = "";
    }

    /// Muestra 'visibles' caracteres; el resto se escribe transparente para que el texto no salte.
    public void TextoSubtitulo(string completo, int visibles, string pagina = "", bool cursiva = false)
    {
        visibles = Mathf.Clamp(visibles, 0, completo.Length);
        string a = Escapar(completo.Substring(0, visibles)), b = Escapar(completo.Substring(visibles));
        string s = b.Length > 0 ? a + "<color=#00000000>" + b + "</color>" : a;
        _subTexto.text = cursiva ? "<i>" + s + "</i>" : s;
        _subPagina.text = pagina;
    }

    public void OcultarSubtitulo() => _sub.SetActive(false);

    static string Escapar(string s) => s.Replace("<", "‹").Replace(">", "›");

    public void ConfigurarMarcador(IList<string> nombres, IList<Color> colores)
    {
        foreach (Transform c in _marcador) Destroy(c.gameObject);
        _filas.Clear();
        _marcador.gameObject.SetActive(nombres.Count > 0);
        float alto = 62f;
        _marcador.sizeDelta = new Vector2(340, 16 + alto * nombres.Count);
        for (int i = 0; i < nombres.Count; i++)
        {
            var fila = new GameObject("Fila " + nombres[i], typeof(RectTransform)).GetComponent<RectTransform>();
            fila.SetParent(_marcador, false);
            Ubicar(fila, new Vector2(0, 1), new Vector2(8, -8 - alto * i), new Vector2(324, alto - 6), new Vector2(0, 1));
            var franja = Panel(fila, "Franja", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(8, alto - 10), colores[i]);
            franja.pivot = new Vector2(0, 0.5f); franja.anchoredPosition = Vector2.zero;
            var n = Texto(fila, "Nombre", 25, TextAnchor.UpperLeft, Color.white, FontStyle.Bold);
            n.rectTransform.offsetMin = new Vector2(20, 0);
            n.text = nombres[i];
            var e = Texto(fila, "Estado", 19, TextAnchor.LowerLeft, Gris);
            e.rectTransform.offsetMin = new Vector2(20, 2);
            e.text = "en la mesa";
            _filas.Add((n, e, franja.GetComponent<Image>()));
        }
    }

    public void Marcador(int i, string estado, Color color)
    {
        if (i < 0 || i >= _filas.Count) return;
        _filas[i].estado.text = estado;
        _filas[i].estado.color = color;
    }

    public void MarcadorResaltar(int activo)
    {
        for (int i = 0; i < _filas.Count; i++)
            _filas[i].nombre.color = i == activo ? Dorado : Color.white;
    }

    public void Sello(string titulo, string detalle, Color color, float segundos)
    {
        _selloTitulo.text = titulo;
        _selloDetalle.text = detalle;
        _selloFondo.color = color;
        _selloT0 = Time.unscaledTime;
        _selloHasta = Time.unscaledTime + segundos;
    }

    public void Resumen(string titulo, string cuerpo)
    {
        _resumen.SetActive(titulo != null);
        _resumenTitulo.text = titulo ?? "";
        _resumenCuerpo.text = cuerpo ?? "";
    }

    public void LimpiarTodo()
    {
        OcultarSubtitulo();
        Etapa(null);
        Solicitud(null);
        Resumen(null, null);
        _tarjetaHasta = 0; _selloHasta = 0;
        foreach (var p in _placas) p.Mostrar(null, Color.clear);
        for (int i = 0; i < _filas.Count; i++) Marcador(i, "en la mesa", Gris);
        MarcadorResaltar(-1);
    }

    // ---------------------------------------------------------------- placas sobre los Sharks

    public Placa CrearPlaca(Func<Vector3> posicion)
    {
        var p = new Placa(posicion, _font, transform);
        _placas.Add(p);
        return p;
    }

    public class Placa
    {
        readonly Func<Vector3> _pos;
        readonly GameObject _go;
        readonly Image _fondo;
        readonly Text _texto;
        readonly CanvasGroup _grupo;

        public Placa(Func<Vector3> pos, Font font, Transform padre)
        {
            _pos = pos;
            _go = new GameObject("Placa", typeof(Canvas));
            _go.transform.SetParent(padre, false);
            _grupo = _go.AddComponent<CanvasGroup>();
            var c = _go.GetComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)_go.transform;
            rt.sizeDelta = new Vector2(360, 90);
            rt.localScale = Vector3.one * 0.0055f;
            var f = new GameObject("Fondo", typeof(RectTransform), typeof(Image));
            f.transform.SetParent(_go.transform, false);
            var frt = (RectTransform)f.transform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            _fondo = f.GetComponent<Image>();
            var o = f.AddComponent<Outline>(); o.effectColor = Color.white; o.effectDistance = new Vector2(4, -4);
            var tgo = new GameObject("Texto", typeof(RectTransform), typeof(Text));
            tgo.transform.SetParent(_go.transform, false);
            var trt = (RectTransform)tgo.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            _texto = tgo.GetComponent<Text>();
            _texto.font = font; _texto.fontSize = 50; _texto.fontStyle = FontStyle.Bold;
            _texto.alignment = TextAnchor.MiddleCenter; _texto.color = Color.white;
            _go.SetActive(false);
        }

        public void Mostrar(string texto, Color color)
        {
            _go.SetActive(!string.IsNullOrEmpty(texto));
            _texto.text = texto;
            _fondo.color = color;
        }

        public void Actualizar(Camera cam)
        {
            if (!_go.activeSelf || cam == null) return;
            Vector3 p = _pos();
            _go.transform.position = p;
            _go.transform.rotation = Quaternion.LookRotation(p - cam.transform.position, Vector3.up);
            float d = Vector3.Distance(cam.transform.position, p);
            _go.transform.localScale = Vector3.one * Mathf.Clamp(d * 0.0006f, 0.002f, 0.012f);
            _grupo.alpha = Mathf.InverseLerp(5.5f, 7.5f, d);   // en primeros planos estorban: se ocultan
        }
    }

    // ---------------------------------------------------------------- helpers de construcción

    RectTransform Panel(RectTransform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = color;
        return rt;
    }

    Text Texto(RectTransform parent, string name, int size, TextAnchor align, Color color, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = go.GetComponent<Text>();
        t.font = _font; t.fontSize = size; t.alignment = align; t.color = color; t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void Ubicar(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Borde(RectTransform rt, Color c)
    {
        var o = rt.gameObject.AddComponent<Outline>();
        o.effectColor = c; o.effectDistance = new Vector2(3, -3);
    }

    static void Sombra(Text t)
    {
        var s = t.gameObject.AddComponent<Shadow>();
        s.effectColor = new Color(0, 0, 0, 0.8f); s.effectDistance = new Vector2(2, -2);
    }
}
