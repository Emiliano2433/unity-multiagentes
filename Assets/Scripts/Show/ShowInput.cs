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
/// y elección de la oferta. Todo se construye por código (uGUI) con el estilo de EstiloUI.
/// El director decide cuándo mostrar cada panel y manda los datos al backend.
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

    RectTransform _root;

    // ---- formulario
    GameObject _form, _velo; CanvasGroup _formGrupo; float _formT0;
    InputField _nombre, _pitch, _monto, _porcentaje;
    Text _formError, _formEstado, _contador, _montoBonito; Image _estadoPunto;
    Button _btnEnviar;
    EstiloUI.Foto _fotoEmp;
    readonly List<(EstiloUI.Foto foto, string clave, string nombre, Color color)> _fotosSharks = new();

    // ---- respuesta
    GameObject _resp; CanvasGroup _respGrupo; float _respT0;
    EstiloUI.Foto _respFoto; Image _respNombreFondo; RectTransform _respNombreCaja;
    Text _respNombre, _respPregunta, _respError; InputField _respuesta; Button _btnResponder;

    // ---- elección
    GameObject _elec; RectTransform _elecPanel, _elecLista; Text _elecError;
    GameObject _nuevo;

    // campos con contorno que se ilumina al enfocar
    readonly List<(InputField campo, Image contorno)> _campos = new();
    Dictionary<string, Texture> _fotos = new();

    static readonly Color CampoFondo = new Color(0.11f, 0.13f, 0.21f, 1f);
    static readonly Color CampoBorde = new Color(1f, 1f, 1f, 0.12f);

    public bool FormularioVisible => _form != null && _form.activeSelf;
    public bool RespuestaVisible => _resp != null && _resp.activeSelf;
    public bool EleccionVisible => _elec != null && _elec.activeSelf;

    public void Construir()
    {
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

        var nuevo = Boton(_root, "Nuevo pitch", "NUEVO PITCH  »", true, 28);
        EstiloUI.Ubicar((RectTransform)nuevo.transform, new Vector2(1, 0), new Vector2(-40, 300), new Vector2(330, 74), new Vector2(1, 0));
        nuevo.onClick.AddListener(() => AlNuevoPitch?.Invoke());
        _nuevo = nuevo.gameObject;
        _nuevo.SetActive(false);
    }

    // ================================================================ formulario del pitch

    void ConstruirFormulario()
    {
        var velo = new GameObject("Velo", typeof(RectTransform), typeof(Image));
        var vrt = (RectTransform)velo.transform; vrt.SetParent(_root, false);
        EstiloUI.Llenar(vrt);
        velo.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.05f, 0.66f);
        _velo = velo; velo.SetActive(false);

        var p = EstiloUI.Panel(_root, "Formulario", EstiloUI.Fondo, 32, true);
        EstiloUI.Ubicar(p, new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1600, 860), new Vector2(0.5f, 0.5f));
        EstiloUI.Contorno(p, new Color(1, 0.79f, 0.27f, 0.55f), 32, 2f);
        _form = p.gameObject;
        _formGrupo = _form.AddComponent<CanvasGroup>();

        // ---------- columna izquierda: título, avatar y los Sharks
        var izq = EstiloUI.Panel(p, "Portada", new Color(0.035f, 0.045f, 0.085f, 1f), 26, false);
        EstiloUI.Ubicar(izq, new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(560, 828), new Vector2(0, 0.5f));
        var franja = EstiloUI.Panel(izq, "Franja", EstiloUI.Dorado, 3, false);
        EstiloUI.Ubicar(franja, new Vector2(0, 1), new Vector2(40, -44), new Vector2(64, 6), new Vector2(0, 1));
        var titulo = EstiloUI.Texto(izq, "Titulo", 66, TextAnchor.UpperLeft, EstiloUI.Dorado, FontStyle.Bold);
        EstiloUI.Llenar(titulo.rectTransform, 40, 0, 30, 60);
        titulo.text = "SHARK TANK";
        EstiloUI.SombraTexto(titulo, 3, 0.6f);
        var sub = EstiloUI.Texto(izq, "Sub", 25, TextAnchor.UpperLeft, EstiloUI.TextoClaro, FontStyle.Italic);
        EstiloUI.Llenar(sub.rectTransform, 42, 0, 30, 140);
        sub.text = "a bordo del Going Merry";

        // avatar de quien presenta (solo la foto)
        _fotoEmp = EstiloUI.CrearFoto(izq, "Avatar", 190, 28, 5f);
        EstiloUI.Ubicar(_fotoEmp.rt, new Vector2(0.5f, 1), new Vector2(0, -196), new Vector2(190, 190), new Vector2(0.5f, 1));
        _fotoEmp.Poner(null, "L", new Color(0.15f, 0.78f, 0.85f));

        var sepIzq = EstiloUI.Panel(izq, "Separador", new Color(1, 1, 1, 0.08f), 0, false);
        EstiloUI.Ubicar(sepIzq, new Vector2(0.5f, 1), new Vector2(0, -404), new Vector2(480, 2), new Vector2(0.5f, 1));
        var tEsperan = EstiloUI.Texto(izq, "Esperan", 15, TextAnchor.UpperLeft, EstiloUI.TextoSuave, FontStyle.Bold);
        EstiloUI.Ubicar(tEsperan.rectTransform, new Vector2(0, 1), new Vector2(40, -426), new Vector2(480, 24), new Vector2(0, 1));
        tEsperan.text = "T E   E S P E R A N   L O S   S H A R K S";

        // ---------- columna derecha: el formulario
        float x0 = 616, w = 944;
        Etiqueta(p, "Presenta tu negocio", new Vector2(x0, -40), new Vector2(w, 52), 40, EstiloUI.TextoClaro, FontStyle.Bold);
        Etiqueta(p, "Cuéntales el problema, tu solución, la tracción, tu equipo y para qué es el dinero.", new Vector2(x0, -94), new Vector2(w, 30), 20, EstiloUI.TextoSuave, FontStyle.Italic);

        Etiqueta(p, "TU NOMBRE", new Vector2(x0, -146), new Vector2(w, 24), 16, EstiloUI.Dorado, FontStyle.Bold);
        _nombre = Entrada(p, "Nombre", new Vector2(x0, -174), new Vector2(w, 58), "Ej. Ana", false, 25);

        Etiqueta(p, "TU PITCH", new Vector2(x0, -254), new Vector2(w, 24), 16, EstiloUI.Dorado, FontStyle.Bold);
        _pitch = Entrada(p, "Pitch", new Vector2(x0, -282), new Vector2(w, 300), "Hola Sharks, soy … y les presento …", true, 22);
        _contador = Etiqueta(p, "", new Vector2(x0, -588), new Vector2(w, 22), 15, EstiloUI.TextoSuave);
        _contador.alignment = TextAnchor.UpperRight;
        _pitch.onValueChanged.AddListener(t => _contador.text = $"{t.Length:N0} / 6,000");
        _contador.text = "0 / 6,000";

        float mitad = (w - 24) / 2f;
        Etiqueta(p, "¿CUÁNTO PIDES?", new Vector2(x0, -616), new Vector2(mitad, 24), 16, EstiloUI.Dorado, FontStyle.Bold);
        _monto = Entrada(p, "Monto", new Vector2(x0, -644), new Vector2(mitad, 62), "1,500,000", false, 27, "$");
        _monto.contentType = InputField.ContentType.DecimalNumber;
        _montoBonito = Etiqueta(p, "", new Vector2(x0, -712), new Vector2(mitad, 22), 16, EstiloUI.TextoSuave, FontStyle.Italic);
        _monto.onValueChanged.AddListener(t => _montoBonito.text = Numero(t, out double v) && v > 0 ? FormatoShow.Dinero((float)v) : "");
        Etiqueta(p, "¿QUÉ % DE TU EMPRESA OFRECES?", new Vector2(x0 + mitad + 24, -616), new Vector2(mitad, 24), 16, EstiloUI.Dorado, FontStyle.Bold);
        _porcentaje = Entrada(p, "Porcentaje", new Vector2(x0 + mitad + 24, -644), new Vector2(mitad, 62), "10", false, 27, null, "%");
        _porcentaje.contentType = InputField.ContentType.DecimalNumber;

        _formError = Etiqueta(p, "", new Vector2(x0, -738), new Vector2(w, 28), 20, new Color(1f, 0.5f, 0.45f), FontStyle.Bold);

        // estado del backend (pastilla con punto)
        var est = EstiloUI.Panel(p, "Estado", new Color(1, 1, 1, 0.05f), 16, false);
        EstiloUI.Ubicar(est, new Vector2(0, 0), new Vector2(x0, 38), new Vector2(300, 40), new Vector2(0, 0));
        var punto = new GameObject("Punto", typeof(RectTransform), typeof(Image));
        var prt = (RectTransform)punto.transform; prt.SetParent(est, false);
        EstiloUI.Ubicar(prt, new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(12, 12), new Vector2(0, 0.5f));
        _estadoPunto = punto.GetComponent<Image>(); _estadoPunto.sprite = EstiloUI.Circulo; _estadoPunto.raycastTarget = false;
        _formEstado = EstiloUI.Texto(est, "Texto", 17, TextAnchor.MiddleLeft, EstiloUI.TextoClaro);
        EstiloUI.Llenar(_formEstado.rectTransform, 38, 0, 10, 0);

        var demo = Boton(p, "Demo", "Ver demo", false, 22);
        EstiloUI.Ubicar((RectTransform)demo.transform, new Vector2(1, 0), new Vector2(-40 - 400 - 18, 26), new Vector2(170, 66), new Vector2(1, 0));
        demo.onClick.AddListener(() => AlPedirDemo?.Invoke());
        _btnEnviar = Boton(p, "Enviar", "SUBIR AL BARCO  »", true, 27);
        EstiloUI.Ubicar((RectTransform)_btnEnviar.transform, new Vector2(1, 0), new Vector2(-40, 26), new Vector2(400, 66), new Vector2(1, 0));
        _btnEnviar.onClick.AddListener(EnviarPitch);

        _form.SetActive(false);
    }

    /// Fotos de los personajes y lista de Sharks (nombre, color) para el formulario y los paneles.
    public void Fotos(Dictionary<string, Texture> fotos, IList<(string nombre, Color color)> sharks, Color colorEmprendedor)
    {
        _fotos = fotos ?? new Dictionary<string, Texture>();
        _fotoEmp.Poner(Foto("emprendedor"), "L", colorEmprendedor);

        var izq = (RectTransform)_form.transform.Find("Portada");
        foreach (var f in _fotosSharks) Destroy(f.foto.rt.parent.gameObject);
        _fotosSharks.Clear();
        int n = sharks.Count;
        const float tam = 84f;
        float paso = n > 0 ? 480f / n : 0;
        for (int i = 0; i < n; i++)
        {
            var (nombre, color) = sharks[i];
            var celda = new GameObject("Shark " + nombre, typeof(RectTransform)).GetComponent<RectTransform>();
            celda.SetParent(izq, false);
            EstiloUI.Ubicar(celda, new Vector2(0, 1), new Vector2(40 + paso * i, -466), new Vector2(paso, 300), new Vector2(0, 1));
            var foto = EstiloUI.CrearFoto(celda, "Foto", tam, 22, 4f);
            EstiloUI.Ubicar(foto.rt, new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(tam, tam), new Vector2(0.5f, 1));
            string clave = FormatoShow.Clave(nombre);
            foto.Poner(Foto(clave), nombre, color);
            var t = EstiloUI.Texto(celda, "Nombre", 17, TextAnchor.UpperCenter, EstiloUI.TextoClaro, FontStyle.Bold);
            EstiloUI.Ubicar(t.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -tam - 8), new Vector2(paso + 6, 46), new Vector2(0.5f, 1));
            t.text = FormatoShow.NombreBonito(nombre).Replace(" D. ", "\nD. ").Replace("Roronoa ", "Roronoa\n");
            t.lineSpacing = 0.9f;
            var a = EstiloUI.Texto(celda, "Apodo", 14, TextAnchor.UpperCenter, color, FontStyle.Italic);
            EstiloUI.Ubicar(a.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -tam - 54), new Vector2(paso + 4, 40), new Vector2(0.5f, 1));
            a.text = FormatoShow.Apodo(nombre);
            // Especialidad: va debajo del apodo (que puede ocupar 2 líneas) como una "etiqueta"
            // en mayúsculas y dorado, para que se lea como dato de negocio y no como otro apodo.
            var esp = EstiloUI.Texto(celda, "Especialidad", 12, TextAnchor.UpperCenter, EstiloUI.Dorado, FontStyle.Bold);
            EstiloUI.Ubicar(esp.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -tam - 98), new Vector2(paso + 4, 36), new Vector2(0.5f, 1));
            esp.text = FormatoShow.Especialidad(nombre).ToUpperInvariant();
            esp.lineSpacing = 0.9f;
            _fotosSharks.Add((foto, clave, nombre, color));
        }
    }

    Texture Foto(string clave) => clave != null && _fotos.TryGetValue(clave, out var t) ? t : null;

    void EnviarPitch()
    {
        string nombre = _nombre.text.Trim(), pitch = _pitch.text.Trim();
        if (nombre.Length == 0) { ErrorFormulario("Escribe tu nombre."); Enfocar(_nombre); return; }
        if (pitch.Length < 10) { ErrorFormulario("Escribe tu pitch (al menos una oración)."); Enfocar(_pitch); return; }
        if (!Numero(_monto.text, out double monto) || monto <= 0) { ErrorFormulario("El monto debe ser un número mayor que 0."); Enfocar(_monto); return; }
        if (!Numero(_porcentaje.text, out double pct) || pct <= 0 || pct > 100) { ErrorFormulario("El porcentaje debe estar entre 0 y 100."); Enfocar(_porcentaje); return; }
        ErrorFormulario("");
        _btnEnviar.interactable = false;
        AlEnviarPitch?.Invoke(nombre, pitch, monto, pct);
    }

    static bool Numero(string s, out double v) =>
        double.TryParse((s ?? "").Replace("$", "").Replace(",", "").Replace("%", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    public void MostrarFormulario(bool visible)
    {
        if (visible && !_form.activeSelf) _formT0 = Time.unscaledTime;
        _form.SetActive(visible);
        _velo.SetActive(visible);
        _btnEnviar.interactable = true;
        if (visible) { _nuevo.SetActive(false); Enfocar(_nombre.text.Length == 0 ? _nombre : _pitch); }
    }

    public void ErrorFormulario(string msg) { _formError.text = msg; _btnEnviar.interactable = true; }

    public void EstadoFormulario(string msg, Color c)
    {
        if (_formEstado == null) return;
        _formEstado.text = (msg ?? "").TrimStart('●', '○', ' ');
        _estadoPunto.color = c;
    }

    // ================================================================ respuesta a una pregunta

    void ConstruirRespuesta()
    {
        var p = EstiloUI.Panel(_root, "Respuesta", EstiloUI.Fondo, 28, true);
        EstiloUI.Ubicar(p, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1540, 370), new Vector2(0.5f, 0));
        EstiloUI.Contorno(p, new Color(1, 0.79f, 0.27f, 0.5f), 28, 2f);
        _resp = p.gameObject;
        _respGrupo = _resp.AddComponent<CanvasGroup>();

        _respFoto = EstiloUI.CrearFoto(p, "Foto", 150, 24, 5f);
        EstiloUI.Ubicar(_respFoto.rt, new Vector2(0, 1), new Vector2(26, -26), new Vector2(150, 150), new Vector2(0, 1));

        _respNombreCaja = EstiloUI.Panel(p, "Nombre", Color.white, 18, true, out _respNombreFondo);
        EstiloUI.Ubicar(_respNombreCaja, new Vector2(0, 1), new Vector2(200, 26), new Vector2(360, 52), new Vector2(0, 1));
        _respNombre = EstiloUI.Texto(_respNombreCaja, "Texto", 25, TextAnchor.MiddleCenter, Color.black, FontStyle.Bold);

        _respPregunta = Etiqueta(p, "", new Vector2(200, -46), new Vector2(1310, 110), 26, EstiloUI.TextoClaro, FontStyle.Italic);
        _respuesta = Entrada(p, "Texto", new Vector2(200, -166), new Vector2(1312, 120), "Escribe tu respuesta…", true, 23);
        _respError = Etiqueta(p, "", new Vector2(200, -296), new Vector2(800, 28), 20, new Color(1f, 0.5f, 0.45f), FontStyle.Bold);
        Etiqueta(p, "Ctrl / ⌘ + Enter para enviar", new Vector2(200, -326), new Vector2(600, 26), 17, EstiloUI.TextoSuave, FontStyle.Italic);
        _btnResponder = Boton(p, "Responder", "RESPONDER  »", true, 25);
        EstiloUI.Ubicar((RectTransform)_btnResponder.transform, new Vector2(1, 0), new Vector2(-28, 22), new Vector2(300, 62), new Vector2(1, 0));
        _btnResponder.onClick.AddListener(EnviarRespuesta);
        _resp.SetActive(false);
    }

    public void MostrarRespuesta(string shark, Color color, string pregunta)
    {
        if (!_resp.activeSelf) _respT0 = Time.unscaledTime;
        _resp.SetActive(true);
        string bonito = FormatoShow.NombreBonito(shark);
        _respFoto.Poner(Foto(FormatoShow.Clave(shark)), bonito, color);
        _respNombre.text = "Responde a " + bonito;
        _respNombreFondo.color = color;
        _respNombre.color = EstiloUI.TextoSobre(color);
        _respNombreCaja.sizeDelta = new Vector2(Mathf.Clamp(_respNombre.preferredWidth + 56, 200, 760), 52);
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
        var velo = new GameObject("Velo eleccion", typeof(RectTransform), typeof(Image));
        var vrt = (RectTransform)velo.transform; vrt.SetParent(_root, false);
        EstiloUI.Llenar(vrt);
        velo.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.05f, 0.45f);

        _elecPanel = EstiloUI.Panel(velo.transform, "Eleccion", EstiloUI.Fondo, 30, true);
        EstiloUI.Ubicar(_elecPanel, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1060, 600), new Vector2(0.5f, 0.5f));
        EstiloUI.Contorno(_elecPanel, new Color(1, 0.79f, 0.27f, 0.55f), 30, 2f);
        _elec = velo;
        Etiqueta(_elecPanel, "¿Con quién haces el trato?", new Vector2(44, -34), new Vector2(980, 56), 42, EstiloUI.Dorado, FontStyle.Bold);
        Etiqueta(_elecPanel, "Elige una de las ofertas o recházalas todas.", new Vector2(46, -92), new Vector2(980, 30), 20, EstiloUI.TextoSuave, FontStyle.Italic);
        var lista = new GameObject("Lista", typeof(RectTransform)).GetComponent<RectTransform>();
        lista.SetParent(_elecPanel, false);
        EstiloUI.Ubicar(lista, new Vector2(0, 1), new Vector2(44, -142), new Vector2(972, 420), new Vector2(0, 1));
        _elecLista = lista;
        _elecError = Etiqueta(_elecPanel, "", new Vector2(44, -540), new Vector2(970, 30), 20, new Color(1f, 0.5f, 0.45f), FontStyle.Bold);
        _elec.SetActive(false);
    }

    public void MostrarEleccion(IList<Opcion> opciones, Func<string, Color> colorDe)
    {
        foreach (Transform c in _elecLista) Destroy(c.gameObject);
        _elecError.text = "";
        float y = 0; const float alto = 96;
        foreach (var o in opciones)
        {
            string nombre = o.shark;
            Color color = colorDe(nombre);
            var b = Boton(_elecLista, "Opcion " + nombre, "", false, 24);
            var brt = (RectTransform)b.transform;
            EstiloUI.Ubicar(brt, new Vector2(0, 1), new Vector2(0, -y), new Vector2(972, alto - 12), new Vector2(0, 1));
            var foto = EstiloUI.CrearFoto(brt, "Foto", 66, 18, 3.5f);
            EstiloUI.Ubicar(foto.rt, new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(66, 66), new Vector2(0, 0.5f));
            foto.Poner(Foto(FormatoShow.Clave(nombre)), nombre, color);
            var n = EstiloUI.Texto(brt, "Nombre", 26, TextAnchor.MiddleLeft, EstiloUI.TextoClaro, FontStyle.Bold);
            EstiloUI.Llenar(n.rectTransform, 96, 0, 560, 0);
            n.text = FormatoShow.NombreBonito(nombre);
            var d = EstiloUI.Texto(brt, "Oferta", 26, TextAnchor.MiddleRight, EstiloUI.Dorado, FontStyle.Bold);
            EstiloUI.Llenar(d.rectTransform, 400, 0, 26, 0);
            d.text = $"{FormatoShow.Dinero(o.monto)} <color=#{EstiloUI.Hex(EstiloUI.TextoSuave)}>por el</color> {FormatoShow.Porcentaje(o.porcentaje)}  <size=18><color=#{EstiloUI.Hex(color)}>({o.tipo})</color></size>";
            b.onClick.AddListener(() => Elegir(nombre));
            y += alto;
        }
        var ninguna = Boton(_elecLista, "Ninguna", "No acepto ninguna oferta", false, 22);
        EstiloUI.Ubicar((RectTransform)ninguna.transform, new Vector2(0, 1), new Vector2(0, -y - 6), new Vector2(972, 64), new Vector2(0, 1));
        ninguna.onClick.AddListener(() => Elegir(null));
        _elecPanel.sizeDelta = new Vector2(1060, 142 + y + 64 + 90);
        _elecError.rectTransform.anchoredPosition = new Vector2(44, -(142 + y + 80));
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
        _form.SetActive(false); _velo.SetActive(false); _resp.SetActive(false); _elec.SetActive(false); _nuevo.SetActive(false);
    }

    // ================================================================ ciclo

    void Update()
    {
        float t = Time.unscaledTime;
        // entrada suave de los paneles
        if (_form.activeSelf)
        {
            float k = Mathf.Clamp01((t - _formT0) / 0.35f);
            _formGrupo.alpha = k;
            _form.transform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, EstiloUI.EaseOut(k));
        }
        if (_resp.activeSelf)
        {
            float k = Mathf.Clamp01((t - _respT0) / 0.3f);
            _respGrupo.alpha = k;
            ((RectTransform)_resp.transform).anchoredPosition = new Vector2(0, 30 - 30 * (1 - EstiloUI.EaseOut(k)));
        }
        // contorno dorado en el campo con foco
        foreach (var (campo, contorno) in _campos)
            contorno.color = Color.Lerp(contorno.color, campo.isFocused ? EstiloUI.Dorado : CampoBorde, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));

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

    Text Etiqueta(RectTransform parent, string texto, Vector2 pos, Vector2 size, int fontSize, Color color, FontStyle style = FontStyle.Normal)
    {
        var t = EstiloUI.Texto(parent, "Etiqueta", fontSize, TextAnchor.UpperLeft, color, style);
        EstiloUI.Ubicar(t.rectTransform, new Vector2(0, 1), pos, size, new Vector2(0, 1));
        t.text = texto;
        return t;
    }

    /// Campo de texto oscuro con esquinas redondeadas; opcionalmente con prefijo ("$") o sufijo ("%").
    InputField Entrada(RectTransform parent, string name, Vector2 pos, Vector2 size, string placeholder, bool multilinea, int fontSize,
                       string prefijo = null, string sufijo = null)
    {
        var caja = EstiloUI.Panel(parent, name, CampoFondo, 14, false, out var fondo);
        EstiloUI.Ubicar(caja, new Vector2(0, 1), pos, size, new Vector2(0, 1));
        var contorno = EstiloUI.Contorno(caja, CampoBorde, 14, 2f);
        fondo.raycastTarget = true;

        float izq = 18, der = 18;
        if (prefijo != null)
        {
            var pre = EstiloUI.Texto(caja, "Prefijo", fontSize + 2, TextAnchor.MiddleLeft, EstiloUI.Dorado, FontStyle.Bold);
            EstiloUI.Llenar(pre.rectTransform, 18, 0, 0, 0);
            pre.text = prefijo; izq = 44;
        }
        if (sufijo != null)
        {
            var suf = EstiloUI.Texto(caja, "Sufijo", fontSize + 2, TextAnchor.MiddleRight, EstiloUI.Dorado, FontStyle.Bold);
            EstiloUI.Llenar(suf.rectTransform, 0, 0, 20, 0);
            suf.text = sufijo; der = 50;
        }

        // InputField crea el cursor como primer hijo del padre del texto. Un contenedor
        // separado lo mantiene encima del fondo opaco y debajo del texto.
        var contenido = new GameObject("Contenido", typeof(RectTransform)).GetComponent<RectTransform>();
        contenido.SetParent(caja, false);
        EstiloUI.Llenar(contenido);

        Text Hijo(string n, Color c, FontStyle st)
        {
            var t = EstiloUI.Texto(contenido, n, fontSize, multilinea ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, c, st);
            EstiloUI.Llenar(t.rectTransform, izq, multilinea ? 12 : 4, der, multilinea ? 12 : 4);
            t.horizontalOverflow = multilinea ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = false;
            t.lineSpacing = 1.08f;
            return t;
        }
        var ph = Hijo("Placeholder", new Color(1, 1, 1, 0.3f), FontStyle.Italic);
        ph.text = placeholder;
        var txt = Hijo("Text", EstiloUI.TextoClaro, FontStyle.Normal);

        var f = fondo.gameObject.AddComponent<InputField>();
        f.targetGraphic = fondo;
        f.textComponent = txt;
        f.placeholder = ph;
        f.lineType = multilinea ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        f.characterLimit = multilinea ? 6000 : 120;
        f.caretWidth = 3;
        f.customCaretColor = true;
        f.caretColor = EstiloUI.Dorado;
        f.selectionColor = new Color(1f, 0.79f, 0.27f, 0.35f);
        var cb = f.colors; cb.highlightedColor = new Color(1.08f, 1.08f, 1.12f, 1f); cb.selectedColor = Color.white; f.colors = cb;
        _campos.Add((f, contorno));
        return f;
    }

    /// Botón redondeado: primario (dorado) o secundario (oscuro con borde).
    Button Boton(RectTransform parent, string name, string texto, bool primario, int fontSize)
    {
        var caja = EstiloUI.Panel(parent, name, primario ? EstiloUI.Dorado : new Color(0.16f, 0.19f, 0.29f), 18, primario, out var fondo);
        if (!primario) EstiloUI.Contorno(caja, new Color(1, 1, 1, 0.16f), 18, 2f);
        fondo.raycastTarget = true;
        var b = caja.gameObject.AddComponent<Button>();
        b.targetGraphic = fondo;
        var cb = b.colors;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.7f);
        b.colors = cb;
        var t = EstiloUI.Texto(caja, "Texto", fontSize, TextAnchor.MiddleCenter, primario ? new Color(0.09f, 0.07f, 0.03f) : EstiloUI.TextoClaro, FontStyle.Bold);
        t.text = texto;
        return b;
    }
}
