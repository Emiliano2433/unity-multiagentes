using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

/// Entrada del usuario dentro de Unity: formulario del pitch, respuesta a cada pregunta
/// y elección de la oferta. Todo se construye por código (uGUI). El director decide cuándo
/// mostrar cada panel y manda los datos al backend.
public class ShowInput : MonoBehaviour
{
    public static bool Escribiendo =>
        EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
        EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() is InputField f && f.isFocused;

    public event Action<string, string, double, double> AlEnviarPitch;   // nombre, pitch, monto, %
    public event Action AlPedirDemo;
    public event Action<string> AlResponder;
    public event Action<string> AlElegir;                                // null = ninguna
    public event Action AlNuevoPitch;

    Font _font;
    RectTransform _root;

    GameObject _form; InputField _nombre, _pitch, _monto, _porcentaje; Text _formError, _formEstado; Button _btnEnviar;
    GameObject _resp; Text _respTitulo, _respPregunta, _respError; Image _respTituloFondo; InputField _respuesta; Button _btnResponder;
    GameObject _elec; RectTransform _elecLista; Text _elecError;
    GameObject _nuevo;

    static readonly Color Panel = new Color(0.04f, 0.06f, 0.11f, 0.94f);
    static readonly Color Campo = new Color(1f, 1f, 1f, 0.95f);
    static readonly Color Dorado = new Color(1f, 0.78f, 0.25f);

    public bool FormularioVisible => _form != null && _form.activeSelf;
    public bool RespuestaVisible => _resp != null && _resp.activeSelf;
    public bool EleccionVisible => _elec != null && _elec.activeSelf;

    public void Construir()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (FindFirstObjectByType<EventSystem>() == null)
        {
#if ENABLE_INPUT_SYSTEM
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
#else
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
        }

        var canvasGo = new GameObject("UI Entrada", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;   // encima de la UI del show
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _root = (RectTransform)canvasGo.transform;

        ConstruirFormulario();
        ConstruirRespuesta();
        ConstruirEleccion();

        var nuevo = Boton(_root, "Nuevo pitch", "NUEVO PITCH  »", new Vector2(1, 0), new Vector2(-40, 70), new Vector2(330, 76), Dorado, Color.black, 30);
        nuevo.onClick.AddListener(() => AlNuevoPitch?.Invoke());
        _nuevo = nuevo.gameObject;
        _nuevo.SetActive(false);
    }

    // ================================================================ formulario del pitch

    void ConstruirFormulario()
    {
        var p = Caja(_root, "Formulario", new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1180, 880), Panel);
        Borde(p, Dorado);
        _form = p.gameObject;

        Etiqueta(p, "SHARK TANK  ·  a bordo del Going Merry", new Vector2(40, -28), new Vector2(1100, 56), 42, Dorado, FontStyle.Bold);
        Etiqueta(p, "Escribe (o pega) tu pitch. Al enviarlo, subirás al barco a presentarlo frente a los Sharks.", new Vector2(40, -88), new Vector2(1100, 34), 22, new Color(1, 1, 1, 0.7f), FontStyle.Italic);

        Etiqueta(p, "Tu nombre", new Vector2(40, -140), new Vector2(500, 32), 24, Color.white, FontStyle.Bold);
        _nombre = Entrada(p, "Nombre", new Vector2(40, -176), new Vector2(1100, 54), "Ej. Ana", false, 26);

        Etiqueta(p, "Tu pitch  (problema, solución, tracción, equipo, para qué es el dinero…)", new Vector2(40, -246), new Vector2(1100, 32), 24, Color.white, FontStyle.Bold);
        _pitch = Entrada(p, "Pitch", new Vector2(40, -282), new Vector2(1100, 330), "Hola Sharks, soy … y les presento …", true, 23);

        Etiqueta(p, "¿Cuánto dinero pides? ($)", new Vector2(40, -630), new Vector2(540, 32), 24, Color.white, FontStyle.Bold);
        _monto = Entrada(p, "Monto", new Vector2(40, -666), new Vector2(530, 54), "1500000", false, 26);
        _monto.contentType = InputField.ContentType.DecimalNumber;
        Etiqueta(p, "¿Qué % de tu empresa ofreces?", new Vector2(610, -630), new Vector2(540, 32), 24, Color.white, FontStyle.Bold);
        _porcentaje = Entrada(p, "Porcentaje", new Vector2(610, -666), new Vector2(530, 54), "10", false, 26);
        _porcentaje.contentType = InputField.ContentType.DecimalNumber;

        _formError = Etiqueta(p, "", new Vector2(40, -734), new Vector2(1100, 32), 23, new Color(1f, 0.45f, 0.4f), FontStyle.Bold);

        _btnEnviar = Boton(p, "Enviar", "SUBIR AL BARCO  »", new Vector2(1, 0), new Vector2(-40, 30), new Vector2(420, 76), Dorado, Color.black, 30);
        _btnEnviar.onClick.AddListener(EnviarPitch);
        var demo = Boton(p, "Demo", "Ver demo", new Vector2(0, 0), new Vector2(40, 30), new Vector2(220, 76), new Color(0.25f, 0.3f, 0.4f), Color.white, 26);
        demo.onClick.AddListener(() => AlPedirDemo?.Invoke());
        _formEstado = Etiqueta(p, "", new Vector2(290, -800), new Vector2(430, 60), 20, new Color(1, 1, 1, 0.6f));
        _formEstado.alignment = TextAnchor.MiddleLeft;
        _form.SetActive(false);
    }

    void EnviarPitch()
    {
        string nombre = _nombre.text.Trim(), pitch = _pitch.text.Trim();
        if (nombre.Length == 0) { ErrorFormulario("Escribe tu nombre."); return; }
        if (pitch.Length < 10) { ErrorFormulario("Escribe tu pitch (al menos una oración)."); return; }
        if (!Numero(_monto.text, out double monto) || monto <= 0) { ErrorFormulario("El monto debe ser un número mayor que 0."); return; }
        if (!Numero(_porcentaje.text, out double pct) || pct <= 0 || pct > 100) { ErrorFormulario("El porcentaje debe estar entre 0 y 100."); return; }
        ErrorFormulario("");
        _btnEnviar.interactable = false;
        AlEnviarPitch?.Invoke(nombre, pitch, monto, pct);
    }

    static bool Numero(string s, out double v) =>
        double.TryParse((s ?? "").Replace("$", "").Replace(",", "").Replace("%", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    public void MostrarFormulario(bool visible)
    {
        _form.SetActive(visible);
        _btnEnviar.interactable = true;
        if (visible) { _nuevo.SetActive(false); Enfocar(_nombre.text.Length == 0 ? _nombre : _pitch); }
    }

    public void ErrorFormulario(string msg) { _formError.text = msg; _btnEnviar.interactable = true; }
    public void EstadoFormulario(string msg, Color c) { if (_formEstado != null) { _formEstado.text = msg; _formEstado.color = c; } }

    // ================================================================ respuesta a una pregunta

    void ConstruirRespuesta()
    {
        var p = Caja(_root, "Respuesta", new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1500, 360), Panel);
        p.pivot = new Vector2(0.5f, 0); p.anchoredPosition = new Vector2(0, 30);
        Borde(p, Dorado);
        _resp = p.gameObject;

        var titulo = Caja(p, "Titulo", new Vector2(0, 1), new Vector2(28, 22), new Vector2(420, 54), Color.white);
        titulo.pivot = new Vector2(0, 0); titulo.anchorMin = titulo.anchorMax = new Vector2(0, 1); titulo.anchoredPosition = new Vector2(28, -22);
        _respTituloFondo = titulo.GetComponent<Image>();
        _respTitulo = Etiqueta(titulo, "", Vector2.zero, Vector2.zero, 28, Color.black, FontStyle.Bold);
        Llenar(_respTitulo.rectTransform);
        _respTitulo.alignment = TextAnchor.MiddleCenter;

        _respPregunta = Etiqueta(p, "", new Vector2(34, -44), new Vector2(1430, 80), 27, Color.white, FontStyle.Italic);
        _respuesta = Entrada(p, "Texto", new Vector2(34, -130), new Vector2(1432, 140), "Escribe tu respuesta…", true, 25);
        _respError = Etiqueta(p, "", new Vector2(34, -290), new Vector2(900, 30), 22, new Color(1f, 0.45f, 0.4f), FontStyle.Bold);
        Etiqueta(p, "Ctrl o Cmd + Enter para enviar", new Vector2(34, -322), new Vector2(600, 28), 19, new Color(1, 1, 1, 0.5f), FontStyle.Italic);
        _btnResponder = Boton(p, "Responder", "RESPONDER  »", new Vector2(1, 0), new Vector2(-34, 22), new Vector2(330, 66), Dorado, Color.black, 28);
        _btnResponder.onClick.AddListener(EnviarRespuesta);
        _resp.SetActive(false);
    }

    public void MostrarRespuesta(string shark, Color color, string pregunta)
    {
        _resp.SetActive(true);
        _respTitulo.text = "Responde a " + shark;
        _respTituloFondo.color = color;
        _respTitulo.color = color.grayscale > 0.55f ? new Color(0.08f, 0.08f, 0.1f) : Color.white;
        ((RectTransform)_respTituloFondo.transform).sizeDelta = new Vector2(Mathf.Max(300, (12 + shark.Length) * 17f + 50), 54);
        _respPregunta.text = string.IsNullOrEmpty(pregunta) ? "" : "“" + pregunta + "”";
        _respuesta.text = "";
        _respError.text = "";
        _btnResponder.interactable = true;
        Enfocar(_respuesta);
    }

    public void OcultarRespuesta() => _resp.SetActive(false);
    public void ErrorRespuesta(string msg) { _respError.text = msg; _btnResponder.interactable = true; }

    void EnviarRespuesta()
    {
        string t = _respuesta.text.Trim();
        if (t.Length == 0) { ErrorRespuesta("Escribe una respuesta."); return; }
        _btnResponder.interactable = false;
        AlResponder?.Invoke(t);
    }

    // ================================================================ elección de oferta

    void ConstruirEleccion()
    {
        var p = Caja(_root, "Eleccion", new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(980, 560), Panel);
        Borde(p, Dorado);
        _elec = p.gameObject;
        Etiqueta(p, "¿Con quién haces el trato?", new Vector2(40, -28), new Vector2(900, 56), 42, Dorado, FontStyle.Bold);
        var lista = new GameObject("Lista", typeof(RectTransform)).GetComponent<RectTransform>();
        lista.SetParent(p, false);
        lista.anchorMin = lista.anchorMax = new Vector2(0, 1); lista.pivot = new Vector2(0, 1);
        lista.anchoredPosition = new Vector2(40, -100); lista.sizeDelta = new Vector2(900, 440);
        _elecLista = lista;
        _elecError = Etiqueta(p, "", new Vector2(40, -520), new Vector2(900, 30), 22, new Color(1f, 0.45f, 0.4f), FontStyle.Bold);
        _elec.SetActive(false);
    }

    public void MostrarEleccion(IList<Opcion> opciones, Func<string, Color> colorDe)
    {
        foreach (Transform c in _elecLista) Destroy(c.gameObject);
        _elecError.text = "";
        float y = 0, alto = 70;
        foreach (var o in opciones)
        {
            string nombre = o.shark;
            var b = Boton(_elecLista, "Opcion " + nombre, $"{nombre}   ·   {FormatoShow.Dinero(o.monto)} por el {FormatoShow.Porcentaje(o.porcentaje)}   ({o.tipo})",
                          new Vector2(0, 1), new Vector2(0, -y), new Vector2(900, alto - 10), colorDe(nombre), Color.white, 27);
            var t = b.GetComponentInChildren<Text>();
            if (colorDe(nombre).grayscale > 0.55f) t.color = Color.black;
            b.onClick.AddListener(() => Elegir(nombre));
            y += alto;
        }
        var ninguna = Boton(_elecLista, "Ninguna", "No acepto ninguna oferta", new Vector2(0, 1), new Vector2(0, -y - 10), new Vector2(900, alto - 10), new Color(0.3f, 0.33f, 0.4f), Color.white, 26);
        ninguna.onClick.AddListener(() => Elegir(null));
        ((RectTransform)_elec.transform).sizeDelta = new Vector2(980, 160 + y + alto + 40);
        _elecError.rectTransform.anchoredPosition = new Vector2(40, -(110 + y + alto + 10));
        _elec.SetActive(true);
    }

    void Elegir(string shark)
    {
        foreach (var b in _elecLista.GetComponentsInChildren<Button>()) b.interactable = false;
        AlElegir?.Invoke(shark);
    }

    public void OcultarEleccion() => _elec.SetActive(false);

    public void ErrorEleccion(string msg)
    {
        _elecError.text = msg;
        foreach (var b in _elecLista.GetComponentsInChildren<Button>()) b.interactable = true;
    }

    public void MostrarNuevoPitch(bool v) => _nuevo.SetActive(v);

    public void OcultarTodo()
    {
        _form.SetActive(false); _resp.SetActive(false); _elec.SetActive(false); _nuevo.SetActive(false);
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        bool mod = kb.ctrlKey.isPressed || kb.leftCommandKey.isPressed || kb.rightCommandKey.isPressed;
        bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
        if (mod && enter)
        {
            if (RespuestaVisible && _btnResponder.interactable) EnviarRespuesta();
            else if (FormularioVisible && _btnEnviar.interactable) EnviarPitch();
        }
        // Tab para pasar al siguiente campo del formulario
        if (FormularioVisible && kb.tabKey.wasPressedThisFrame)
        {
            var orden = new[] { _nombre, _pitch, _monto, _porcentaje };
            int i = Array.FindIndex(orden, f => f.isFocused);
            if (i >= 0) Enfocar(orden[(i + 1) % orden.Length]);
        }
#endif
    }

    void Enfocar(InputField f)
    {
        if (f == null || EventSystem.current == null) return;
        EventSystem.current.SetSelectedGameObject(f.gameObject);
        f.ActivateInputField();
    }

    // ================================================================ helpers de construcción

    RectTransform Caja(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = color;
        return rt;
    }

    Text Etiqueta(RectTransform parent, string texto, Vector2 pos, Vector2 size, int fontSize, Color color, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject("Etiqueta", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var t = go.GetComponent<Text>();
        t.font = _font; t.fontSize = fontSize; t.color = color; t.fontStyle = style; t.text = texto;
        t.alignment = TextAnchor.UpperLeft;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void Llenar(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    InputField Entrada(RectTransform parent, string name, Vector2 pos, Vector2 size, string placeholder, bool multilinea, int fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = Campo;

        Text Hijo(string n, Color c, FontStyle st)
        {
            var tgo = new GameObject(n, typeof(RectTransform), typeof(Text));
            var trt = (RectTransform)tgo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14, 8); trt.offsetMax = new Vector2(-14, -8);
            var t = tgo.GetComponent<Text>();
            t.font = _font; t.fontSize = fontSize; t.color = c; t.fontStyle = st;
            t.alignment = multilinea ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            t.horizontalOverflow = multilinea ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = false;
            return t;
        }
        var ph = Hijo("Placeholder", new Color(0.1f, 0.1f, 0.15f, 0.4f), FontStyle.Italic);
        ph.text = placeholder;
        var txt = Hijo("Text", new Color(0.06f, 0.07f, 0.1f), FontStyle.Normal);

        var f = go.GetComponent<InputField>();
        f.textComponent = txt;
        f.placeholder = ph;
        f.lineType = multilinea ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        f.characterLimit = multilinea ? 6000 : 120;
        f.caretWidth = 2;
        f.customCaretColor = true;
        f.caretColor = new Color(0.1f, 0.1f, 0.2f);
        f.selectionColor = new Color(0.65f, 0.8f, 1f, 0.75f);
        return f;
    }

    Button Boton(RectTransform parent, string name, string texto, Vector2 anchor, Vector2 pos, Vector2 size, Color fondo, Color colorTexto, int fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = fondo;
        var b = go.GetComponent<Button>();
        var cb = b.colors;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.7f);
        b.colors = cb;
        var t = Etiqueta(rt, texto, Vector2.zero, Vector2.zero, fontSize, colorTexto, FontStyle.Bold);
        Llenar(t.rectTransform);
        t.alignment = TextAnchor.MiddleCenter;
        return b;
    }

    static void Borde(RectTransform rt, Color c)
    {
        var o = rt.gameObject.AddComponent<Outline>();
        o.effectColor = c; o.effectDistance = new Vector2(3, -3);
    }
}
