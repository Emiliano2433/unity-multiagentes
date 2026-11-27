using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// Interfaz del show construida por código (uGUI, sin assets extra):
/// cuadro de diálogo estilo Celeste con retrato en vivo de quien habla, barra de etapas,
/// tarjeta de la solicitud, marcador de Sharks, tarjetas de transición, sello de
/// "dentro / fuera", resumen final y placas sobre cada Shark.
public class ShowUI : MonoBehaviour
{
    RectTransform _root;

    // ---- Diálogo
    RectTransform _dlg; CanvasGroup _dlgGrupo; float _dlgT0 = -10f;
    Image _dlgContorno, _nombreFondo, _retratoMarco, _indicador;
    RectTransform _nombreCaja, _etiquetaCaja;
    Text _nombre, _etiqueta, _texto, _pagina, _inicial;
    RawImage _retratoImg;
    bool _paginaCompleta;
    string _hablanteActual;

    // ---- Retrato en vivo
    Camera _retCam; RenderTexture _retRT;
    Func<(Vector3 cabeza, Vector3 frente, float altura)> _retFuente;
    string _retClave;

    // ---- Arriba
    RectTransform _etapas; readonly List<(Image fondo, Text texto)> _pasos = new(); int _pasoActivo = -1;
    RectTransform _detalleCaja; Text _detalle;
    RectTransform _solicitud; Text _solNombre, _solMonto;
    Text _estado, _ayuda; Image _estadoPunto;

    // ---- Marcador
    RectTransform _marcador;
    readonly List<Fila> _filas = new();
    class Fila { public RectTransform rt; public Image contorno, franja; public EstiloUI.Foto foto; public Text nombre, estado; public string clave; public Color color; public float x; }
    Dictionary<string, Texture> _fotos = new();

    // ---- Centro
    RectTransform _tarjetaRT; CanvasGroup _tarjeta; Text _tarjetaGrande, _tarjetaSub; float _tarjetaT0 = -10f, _tarjetaHasta;
    CanvasGroup _sello; Text _selloTitulo, _selloDetalle; Image _selloFondo; float _selloT0 = -10f, _selloHasta;
    GameObject _resumen; Text _resumenTitulo, _resumenCuerpo;

    // ---- Placas en el mundo
    readonly List<Placa> _placas = new();

    public static readonly Color Verde = EstiloUI.Verde;
    public static readonly Color Rojo = EstiloUI.Rojo;
    public static readonly Color Gris = new Color(0.68f, 0.7f, 0.76f);
    public static readonly Color Dorado = EstiloUI.Dorado;

    static readonly string[] NombresPasos = { "Pitch", "Preguntas", "Retroalimentación", "Ofertas", "Elección" };

    public void Construir()
    {
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

        ConstruirBarrasCine();
        ConstruirDialogo();
        ConstruirArriba();
        ConstruirMarcador();
        ConstruirCentro();
        ConstruirRetrato();
    }

    // ================================================================ construcción

    void ConstruirBarrasCine()
    {
        // Viñetas suaves arriba y abajo (oscurecen el borde y ayudan a leer la UI)
        Image Barra(string n, bool arriba, float alto, float alpha)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)go.transform; rt.SetParent(_root, false);
            rt.anchorMin = new Vector2(0, arriba ? 1 : 0); rt.anchorMax = new Vector2(1, arriba ? 1 : 0);
            rt.pivot = new Vector2(0.5f, arriba ? 1 : 0);
            rt.sizeDelta = new Vector2(0, alto);
            var img = go.GetComponent<RawImage>();
            img.texture = EstiloUI.Degradado.texture;          // alfa 0 abajo → 1 arriba
            img.uvRect = arriba ? new Rect(0, 0, 1, 1) : new Rect(0, 1, 1, -1);
            img.color = new Color(0, 0, 0, alpha);
            img.raycastTarget = false;
            return null;
        }
        Barra("Viñeta arriba", true, 170, 0.5f);
        Barra("Viñeta abajo", false, 340, 0.6f);
    }

    void ConstruirDialogo()
    {
        _dlg = EstiloUI.Panel(_root, "Dialogo", EstiloUI.Fondo, 26, true);
        EstiloUI.Ubicar(_dlg, new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(1520, 252), new Vector2(0.5f, 0));
        _dlgContorno = EstiloUI.Contorno(_dlg, new Color(1, 1, 1, 0.14f), 26, 2f);
        _dlgGrupo = _dlg.gameObject.AddComponent<CanvasGroup>();

        // Retrato (marco con el color de quien habla, imagen redondeada dentro)
        var marco = EstiloUI.Panel(_dlg, "Retrato", Color.white, 22, false, out _retratoMarco);
        EstiloUI.Ubicar(marco, new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(216, 216), new Vector2(0, 0.5f));
        var mascara = EstiloUI.Panel(marco, "Mascara", new Color(0.1f, 0.12f, 0.2f), 18, false, out var fondoMascara);
        EstiloUI.Llenar(mascara, 6, 6, 6, 6);
        fondoMascara.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var raw = new GameObject("Imagen", typeof(RectTransform), typeof(RawImage));
        var rawRt = (RectTransform)raw.transform; rawRt.SetParent(fondoMascara.transform, false);
        EstiloUI.Llenar(rawRt);
        _retratoImg = raw.GetComponent<RawImage>(); _retratoImg.raycastTarget = false;
        _inicial = EstiloUI.Texto(fondoMascara.transform, "Inicial", 110, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

        // Nombre (pastilla que sobresale arriba) + etiqueta
        _nombreCaja = EstiloUI.Panel(_dlg, "Nombre", Color.white, 20, true, out _nombreFondo);
        EstiloUI.Ubicar(_nombreCaja, new Vector2(0, 1), new Vector2(252, 26), new Vector2(300, 54), new Vector2(0, 1));
        _nombre = EstiloUI.Texto(_nombreCaja, "Texto", 29, TextAnchor.MiddleCenter, Color.black, FontStyle.Bold);

        _etiquetaCaja = EstiloUI.Panel(_dlg, "Etiqueta", new Color(1, 1, 1, 0.08f), 14, false);
        EstiloUI.Ubicar(_etiquetaCaja, new Vector2(0, 1), new Vector2(570, 14), new Vector2(240, 34), new Vector2(0, 1));
        _etiqueta = EstiloUI.Texto(_etiquetaCaja, "Texto", 19, TextAnchor.MiddleCenter, EstiloUI.TextoSuave, FontStyle.Italic);

        // Texto
        _texto = EstiloUI.Texto(_dlg, "Texto", 31, TextAnchor.UpperLeft, EstiloUI.TextoClaro);
        _texto.lineSpacing = 1.1f;
        // si una página no cabe, la letra se achica en vez de salirse del cuadro
        _texto.resizeTextForBestFit = true;
        _texto.resizeTextMinSize = 21;
        _texto.resizeTextMaxSize = 31;
        _texto.verticalOverflow = VerticalWrapMode.Truncate;
        EstiloUI.Llenar(_texto.rectTransform, 262, 34, 54, 46);

        // Página e indicador de "siguiente" (rombo que rebota)
        _pagina = EstiloUI.Texto(_dlg, "Pagina", 18, TextAnchor.LowerRight, new Color(1, 1, 1, 0.45f));
        EstiloUI.Ubicar(_pagina.rectTransform, new Vector2(1, 0), new Vector2(-58, 14), new Vector2(160, 26), new Vector2(1, 0));
        var ind = new GameObject("Siguiente", typeof(RectTransform), typeof(Image));
        var indRt = (RectTransform)ind.transform; indRt.SetParent(_dlg, false);
        EstiloUI.Ubicar(indRt, new Vector2(1, 0), new Vector2(-32, 24), new Vector2(15, 15), new Vector2(0.5f, 0.5f));
        indRt.localRotation = Quaternion.Euler(0, 0, 45);
        _indicador = ind.GetComponent<Image>(); _indicador.sprite = EstiloUI.Redondo(3); _indicador.type = Image.Type.Sliced;
        _indicador.color = EstiloUI.Dorado; _indicador.raycastTarget = false;

        _dlg.gameObject.SetActive(false);
    }

    void ConstruirArriba()
    {
        // ---- Barra de etapas
        float ancho = 0; var anchos = new List<float>();
        foreach (var n in NombresPasos) { float w = 44 + AnchoTexto($"{NombresPasos.Length}  {n}", 21, FontStyle.Bold); anchos.Add(w); ancho += w; }
        ancho += 8 * (NombresPasos.Length - 1) + 16;
        _etapas = EstiloUI.Panel(_root, "Etapas", new Color(0.04f, 0.05f, 0.09f, 0.82f), 26, true);
        EstiloUI.Ubicar(_etapas, new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(ancho, 56), new Vector2(0.5f, 1));
        float x = 8;
        for (int i = 0; i < NombresPasos.Length; i++)
        {
            var p = EstiloUI.Panel(_etapas, "Paso " + (i + 1), new Color(1, 1, 1, 0), 20, false, out var fondo);
            EstiloUI.Ubicar(p, new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(anchos[i], 42), new Vector2(0, 0.5f));
            var t = EstiloUI.Texto(p, "Texto", 21, TextAnchor.MiddleCenter, EstiloUI.TextoSuave, FontStyle.Bold);
            t.text = $"{i + 1}  {NombresPasos[i]}";
            _pasos.Add((fondo, t));
            x += anchos[i] + 8;
        }
        _detalleCaja = EstiloUI.Panel(_root, "Detalle etapa", new Color(0.55f, 0.08f, 0.1f, 0.92f), 16, true);
        EstiloUI.Ubicar(_detalleCaja, new Vector2(0.5f, 1), new Vector2(0, -86), new Vector2(420, 38), new Vector2(0.5f, 1));
        _detalle = EstiloUI.Texto(_detalleCaja, "Texto", 20, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        _etapas.gameObject.SetActive(false);
        _detalleCaja.gameObject.SetActive(false);

        // ---- Solicitud (arriba izquierda)
        _solicitud = EstiloUI.Panel(_root, "Solicitud", EstiloUI.Fondo, 22, true);
        EstiloUI.Ubicar(_solicitud, new Vector2(0, 1), new Vector2(26, -22), new Vector2(430, 132), new Vector2(0, 1));
        var franja = EstiloUI.Panel(_solicitud, "Franja", EstiloUI.Dorado, 4, false);
        EstiloUI.Ubicar(franja, new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(6, 96), new Vector2(0, 0.5f));
        var titulo = EstiloUI.Texto(_solicitud, "Titulo", 16, TextAnchor.UpperLeft, EstiloUI.TextoSuave, FontStyle.Bold);
        EstiloUI.Llenar(titulo.rectTransform, 36, 0, 16, 16);
        titulo.text = "L A   S O L I C I T U D";
        _solMonto = EstiloUI.Texto(_solicitud, "Monto", 36, TextAnchor.MiddleLeft, EstiloUI.Dorado, FontStyle.Bold);
        EstiloUI.Llenar(_solMonto.rectTransform, 36, 30, 16, 36);
        _solNombre = EstiloUI.Texto(_solicitud, "Nombre", 19, TextAnchor.LowerLeft, EstiloUI.TextoClaro);
        EstiloUI.Llenar(_solNombre.rectTransform, 36, 14, 16, 0);
        _solicitud.gameObject.SetActive(false);

        // ---- Estado y ayuda (arriba derecha)
        var est = EstiloUI.Panel(_root, "Estado", new Color(0.04f, 0.05f, 0.09f, 0.75f), 16, false);
        EstiloUI.Ubicar(est, new Vector2(1, 1), new Vector2(-26, -22), new Vector2(430, 36), new Vector2(1, 1));
        var punto = new GameObject("Punto", typeof(RectTransform), typeof(Image));
        var pRt = (RectTransform)punto.transform; pRt.SetParent(est, false);
        EstiloUI.Ubicar(pRt, new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(12, 12), new Vector2(0, 0.5f));
        _estadoPunto = punto.GetComponent<Image>(); _estadoPunto.sprite = EstiloUI.Circulo; _estadoPunto.raycastTarget = false;
        _estado = EstiloUI.Texto(est, "Texto", 17, TextAnchor.MiddleLeft, EstiloUI.TextoClaro);
        EstiloUI.Llenar(_estado.rectTransform, 38, 0, 12, 0);

        _ayuda = EstiloUI.Texto(_root, "Ayuda", 16, TextAnchor.UpperRight, new Color(1, 1, 1, 0.6f));
        EstiloUI.Ubicar(_ayuda.rectTransform, new Vector2(1, 1), new Vector2(-30, -66), new Vector2(700, 24), new Vector2(1, 1));
        _ayuda.text = "<b>Espacio</b> siguiente   ·   <b>F</b> rápido   ·   <b>V</b> cámara libre   ·   <b>D</b> demo";
        EstiloUI.SombraTexto(_ayuda);
    }

    void ConstruirMarcador()
    {
        _marcador = new GameObject("Marcador", typeof(RectTransform)).GetComponent<RectTransform>();
        _marcador.SetParent(_root, false);
        EstiloUI.Ubicar(_marcador, new Vector2(1, 1), new Vector2(-26, -104), new Vector2(372, 480), new Vector2(1, 1));
        _marcador.gameObject.SetActive(false);
    }

    void ConstruirCentro()
    {
        // ---- Tarjeta de transición: banda ancha con líneas doradas
        _tarjetaRT = EstiloUI.Panel(_root, "Tarjeta", new Color(0.02f, 0.03f, 0.06f, 0.86f), 0, false);
        _tarjetaRT.anchorMin = new Vector2(0, 0.5f); _tarjetaRT.anchorMax = new Vector2(1, 0.5f);
        _tarjetaRT.pivot = new Vector2(0.5f, 0.5f); _tarjetaRT.anchoredPosition = new Vector2(0, 150); _tarjetaRT.sizeDelta = new Vector2(0, 200);
        foreach (float y in new[] { 1f, 0f })
        {
            var l = new GameObject("Linea", typeof(RectTransform), typeof(Image));
            var lr = (RectTransform)l.transform; lr.SetParent(_tarjetaRT, false);
            lr.anchorMin = new Vector2(0, y); lr.anchorMax = new Vector2(1, y); lr.pivot = new Vector2(0.5f, y);
            lr.sizeDelta = new Vector2(0, 4);
            var li = l.GetComponent<Image>(); li.color = EstiloUI.Dorado; li.raycastTarget = false;
        }
        _tarjeta = _tarjetaRT.gameObject.AddComponent<CanvasGroup>();
        _tarjetaGrande = EstiloUI.Texto(_tarjetaRT, "Grande", 70, TextAnchor.MiddleCenter, EstiloUI.Dorado, FontStyle.Bold);
        EstiloUI.Llenar(_tarjetaGrande.rectTransform, 40, 66, 40, 18);
        EstiloUI.SombraTexto(_tarjetaGrande, 3, 0.8f);
        _tarjetaSub = EstiloUI.Texto(_tarjetaRT, "Sub", 28, TextAnchor.MiddleCenter, EstiloUI.TextoClaro);
        EstiloUI.Llenar(_tarjetaSub.rectTransform, 40, 22, 40, 128);
        _tarjeta.alpha = 0;

        // ---- Sello dentro/fuera
        var sello = EstiloUI.Panel(_root, "Sello", Verde, 30, true, out _selloFondo);
        EstiloUI.Ubicar(sello, new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(760, 186), new Vector2(0.5f, 0.5f));
        EstiloUI.Contorno(sello, Color.white, 30, 6f);
        _sello = sello.gameObject.AddComponent<CanvasGroup>();
        _selloTitulo = EstiloUI.Texto(sello, "Titulo", 72, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        EstiloUI.Llenar(_selloTitulo.rectTransform, 10, 62, 10, 6);
        EstiloUI.SombraTexto(_selloTitulo, 3, 0.5f);
        _selloDetalle = EstiloUI.Texto(sello, "Detalle", 27, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        EstiloUI.Llenar(_selloDetalle.rectTransform, 10, 16, 10, 120);
        _sello.alpha = 0;

        // ---- Resumen final
        var res = EstiloUI.Panel(_root, "Resumen", EstiloUI.Fondo, 26, true);
        EstiloUI.Ubicar(res, new Vector2(0, 0.5f), new Vector2(46, 40), new Vector2(780, 420), new Vector2(0, 0.5f));
        EstiloUI.Contorno(res, EstiloUI.Dorado, 26, 3f);
        _resumen = res.gameObject;
        _resumenTitulo = EstiloUI.Texto(res, "Titulo", 40, TextAnchor.UpperLeft, EstiloUI.Dorado, FontStyle.Bold);
        EstiloUI.Llenar(_resumenTitulo.rectTransform, 44, 0, 30, 30);
        _resumenCuerpo = EstiloUI.Texto(res, "Cuerpo", 28, TextAnchor.UpperLeft, EstiloUI.TextoClaro);
        _resumenCuerpo.lineSpacing = 1.15f;
        EstiloUI.Llenar(_resumenCuerpo.rectTransform, 44, 28, 36, 104);
        _resumen.SetActive(false);
    }

    void ConstruirRetrato()
    {
        _retRT = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32) { name = "Retrato", antiAliasing = 2 };
        _retRT.Create();
        var go = new GameObject("Camara retrato");
        go.transform.SetParent(transform, false);
        _retCam = go.AddComponent<Camera>();
        _retCam.targetTexture = _retRT;
        _retCam.fieldOfView = 24f;
        _retCam.nearClipPlane = 0.05f;
        _retCam.farClipPlane = 400f;
        _retCam.depth = -10;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        _retCam.enabled = false;
        _retratoImg.texture = _retRT;
    }

    // ================================================================ animación

    void Update()
    {
        float t = Time.unscaledTime;

        // Diálogo: entra con un pequeño rebote cuando cambia quien habla
        if (_dlg.gameObject.activeSelf)
        {
            float k = Mathf.Clamp01((t - _dlgT0) / 0.28f);
            _dlgGrupo.alpha = Mathf.Clamp01(k * 1.6f);
            float e = EstiloUI.EaseOutBack(k);
            _dlg.anchoredPosition = new Vector2(0, 34 - 26 * (1 - e));
            _nombreCaja.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, EstiloUI.EaseOutBack(Mathf.Clamp01((t - _dlgT0 - 0.05f) / 0.3f)));
            bool mostrar = _paginaCompleta;
            _indicador.enabled = mostrar;
            if (mostrar) ((RectTransform)_indicador.transform).anchoredPosition = new Vector2(-32, 24 + Mathf.Abs(Mathf.Sin(t * 5f)) * 7f);
        }

        // Tarjeta: entra deslizándose y sale desvaneciéndose
        {
            bool vis = t < _tarjetaHasta;
            float k = Mathf.Clamp01((t - _tarjetaT0) / 0.45f);
            _tarjeta.alpha = Mathf.MoveTowards(_tarjeta.alpha, vis ? 1f : 0f, Time.unscaledDeltaTime * (vis ? 5f : 3f));
            _tarjetaGrande.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-140f, 0f, EstiloUI.EaseOut(k)), 0);
            _tarjetaSub.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(140f, 0f, EstiloUI.EaseOut(k)), 0);
            _tarjetaRT.localScale = new Vector3(1, Mathf.Lerp(0.2f, 1f, EstiloUI.EaseOut(Mathf.Clamp01((t - _tarjetaT0) / 0.25f))), 1);
        }

        // Sello: golpe con rebote
        {
            float k = t - _selloT0;
            _sello.alpha = Mathf.MoveTowards(_sello.alpha, t < _selloHasta ? 1f : 0f, Time.unscaledDeltaTime * (t < _selloHasta ? 8f : 2.5f));
            float pop = k < 0.4f ? Mathf.Lerp(1.7f, 1f, EstiloUI.EaseOutBack(k / 0.4f)) : 1f;
            _sello.transform.localScale = Vector3.one * pop;
            _sello.transform.localRotation = Quaternion.Euler(0, 0, k < 0.4f ? Mathf.Lerp(-12f, -4f, k / 0.4f) : -4f);
        }

        // Marcador: la fila activa se adelanta un poco
        foreach (var f in _filas)
        {
            var p = f.rt.anchoredPosition;
            p.x = Mathf.Lerp(p.x, f.x, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            f.rt.anchoredPosition = p;
        }
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        foreach (var p in _placas) p.Actualizar(cam);
        ActualizarRetrato();
    }

    /// Encuadre de retrato (busto, de 3/4) para una cabeza que mira hacia 'frente'.
    public static (Vector3 pos, Quaternion rot) EncuadreRetrato(Vector3 cabeza, Vector3 frente, float altura, string clave = null)
    {
        frente.y = 0;
        if (frente.sqrMagnitude < 1e-4f) frente = Vector3.forward;
        frente.Normalize();
        float escala = Mathf.Clamp(altura / 1.6f, 0.7f, 2.2f);
        // ajustes por personaje: lado de la cámara (para que no estorben armas), subir el encuadre, distancia
        float lado = 22f, subir = 0f, dist = 1f;
        switch (clave)
        {
            case "arlong": lado = -34f; subir = 0.05f; break;            // la Kiribachi queda del otro lado
            case "barbablanca": lado = -18f; subir = 0.42f; dist = 0.9f; break;
            case "zoro": lado = -24f; subir = 0.02f; break;               // del lado iluminado
            case "shanks": dist = 1.1f; break;
        }
        cabeza += Vector3.up * subir;
        Vector3 dir = Quaternion.AngleAxis(lado, Vector3.up) * frente;
        Vector3 pos = cabeza + dir * (0.55f + 0.5f * escala) * dist + Vector3.up * 0.06f * escala;
        return (pos, Quaternion.LookRotation(cabeza - Vector3.up * 0.05f * escala - pos));
    }

    /// Fotos de cada personaje (clave = FormatoShow.Clave del nombre, "emprendedor" para quien presenta).
    public void Fotos(Dictionary<string, Texture> fotos)
    {
        _fotos = fotos ?? new Dictionary<string, Texture>();
        foreach (var f in _filas) f.foto.Poner(Foto(f.clave), f.nombre.text, f.color);
    }

    public Texture Foto(string clave) => clave != null && _fotos.TryGetValue(clave, out var t) ? t : null;

    void ActualizarRetrato()
    {
        bool activo = _dlg.gameObject.activeSelf && _retFuente != null;
        _retCam.enabled = activo;
        _retratoImg.enabled = activo;
        _inicial.enabled = !activo;
        if (!activo) return;
        var (cabeza, frente, altura) = _retFuente();
        var (pos, rot) = EncuadreRetrato(cabeza, frente, altura, _retClave);
        _retCam.transform.SetPositionAndRotation(pos, rot);
    }

    /// Ancho real (en px de referencia) que ocupa un texto con la fuente de la UI.
    static float AnchoTexto(string texto, int tam, FontStyle estilo)
    {
        var gen = new TextGenerator();
        var cfg = new TextGenerationSettings
        {
            font = EstiloUI.Fuente, fontSize = tam, fontStyle = estilo, scaleFactor = 1f, lineSpacing = 1f,
            richText = true, horizontalOverflow = HorizontalWrapMode.Overflow, verticalOverflow = VerticalWrapMode.Overflow,
            generationExtents = new Vector2(4000, 200), pivot = Vector2.zero, textAnchor = TextAnchor.MiddleLeft, color = Color.white
        };
        return gen.GetPreferredWidth(texto, cfg);
    }

    // ================================================================ API

    public void Estado(string texto, Color color)
    {
        texto = (texto ?? "").TrimStart('●', '○', ' ');   // el punto de color ya lo dibuja la UI
        _estado.text = texto;
        _estadoPunto.color = color;
        float ancho = Mathf.Clamp(_estado.preferredWidth + 56, 160, 900);
        ((RectTransform)_estado.transform.parent).sizeDelta = new Vector2(ancho, 36);
    }

    /// "2 · PREGUNTAS · Vuelta 3" → paso 2 activo y detalle "Vuelta 3". Sin número → solo el detalle.
    public void Etapa(string texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            _etapas.gameObject.SetActive(false); _detalleCaja.gameObject.SetActive(false); _pasoActivo = -1;
            return;
        }
        _etapas.gameObject.SetActive(true);
        string detalle = texto;
        var partes = texto.Split('·');
        if (partes.Length > 0 && int.TryParse(partes[0].Trim(), out int n) && n >= 1 && n <= NombresPasos.Length)
        {
            _pasoActivo = n - 1;
            detalle = partes.Length > 2 ? string.Join(" · ", partes, 2, partes.Length - 2).Trim() : "";
        }
        for (int i = 0; i < _pasos.Count; i++)
        {
            bool act = i == _pasoActivo, hecho = _pasoActivo >= 0 && i < _pasoActivo;
            _pasos[i].fondo.color = act ? EstiloUI.Dorado : hecho ? new Color(1, 0.79f, 0.27f, 0.16f) : new Color(1, 1, 1, 0);
            _pasos[i].texto.color = act ? new Color(0.08f, 0.07f, 0.05f) : hecho ? EstiloUI.Dorado : EstiloUI.TextoSuave;
        }
        _detalleCaja.gameObject.SetActive(detalle.Length > 0);
        _detalle.text = detalle.ToUpperInvariant();
        _detalleCaja.sizeDelta = new Vector2(Mathf.Max(160, _detalle.preferredWidth + 48), 38);
    }

    public void Solicitud(string nombre, float monto, float porcentaje)
    {
        bool vis = monto > 0;
        _solicitud.gameObject.SetActive(vis);
        if (!vis) return;
        _solMonto.text = $"{FormatoShow.Dinero(monto)}  <size=26><color=#{EstiloUI.Hex(EstiloUI.TextoClaro)}>por el</color></size>  {FormatoShow.Porcentaje(porcentaje)}";
        _solNombre.text = $"<b>{nombre}</b> lo pide por su empresa";
    }

    public void Solicitud(string texto) { if (string.IsNullOrEmpty(texto)) _solicitud.gameObject.SetActive(false); }

    public void Tarjeta(string grande, string sub, float segundos)
    {
        _tarjetaGrande.text = grande;
        _tarjetaSub.text = sub;
        _tarjetaT0 = Time.unscaledTime;
        _tarjetaHasta = Time.unscaledTime + segundos;
    }

    /// Prepara el diálogo para quien habla. El retrato en vivo se toma de 'retrato' (o una inicial si es null).
    public void Hablante(string nombre, Color color, string etiqueta, Func<(Vector3, Vector3, float)> retrato = null, string claveRetrato = null)
    {
        _retClave = claveRetrato;
        bool nuevo = !_dlg.gameObject.activeSelf || _hablanteActual != nombre;
        _dlg.gameObject.SetActive(true);
        if (nuevo) _dlgT0 = Time.unscaledTime;
        _hablanteActual = nombre;
        _retFuente = retrato;

        _nombre.text = string.IsNullOrEmpty(nombre) ? "…" : nombre;
        _nombreFondo.color = color;
        _nombre.color = EstiloUI.TextoSobre(color);
        float ancho = Mathf.Clamp(_nombre.preferredWidth + 56f, 140f, 640f);
        _nombreCaja.sizeDelta = new Vector2(ancho, 54);
        _retratoMarco.color = color;
        _inicial.text = string.IsNullOrEmpty(nombre) ? "?" : nombre.Substring(0, 1).ToUpperInvariant();
        _inicial.color = color;
        _dlgContorno.color = new Color(color.r, color.g, color.b, 0.45f);

        _etiqueta.text = etiqueta ?? "";
        _etiquetaCaja.gameObject.SetActive(!string.IsNullOrEmpty(etiqueta));
        _etiquetaCaja.anchoredPosition = new Vector2(252 + ancho + 14, 14);
        _etiquetaCaja.sizeDelta = new Vector2(Mathf.Clamp(_etiqueta.preferredWidth + 36, 80, 640), 34);
        _texto.text = "";
        _pagina.text = "";
        _paginaCompleta = false;
    }

    /// Muestra 'visibles' caracteres; el resto se escribe transparente para que el texto no salte.
    public void TextoSubtitulo(string completo, int visibles, string pagina = "", bool cursiva = false)
    {
        visibles = Mathf.Clamp(visibles, 0, completo.Length);
        string a = Escapar(completo.Substring(0, visibles)), b = Escapar(completo.Substring(visibles));
        string s = b.Length > 0 ? a + "<color=#00000000>" + b + "</color>" : a;
        _texto.text = cursiva ? $"<i><color=#{EstiloUI.Hex(EstiloUI.TextoSuave)}>{s}</color></i>" : s;
        _pagina.text = pagina;
        _paginaCompleta = !cursiva && visibles >= completo.Length;
    }

    public void OcultarSubtitulo() { _dlg.gameObject.SetActive(false); _hablanteActual = null; }

    static string Escapar(string s) => s.Replace("<", "‹").Replace(">", "›");

    public void ConfigurarMarcador(IList<string> nombres, IList<Color> colores)
    {
        foreach (Transform c in _marcador) Destroy(c.gameObject);
        _filas.Clear();
        _marcador.gameObject.SetActive(nombres.Count > 0);
        const float alto = 86f, ancho = 372f;
        _marcador.sizeDelta = new Vector2(ancho, nombres.Count * (alto + 10));
        for (int i = 0; i < nombres.Count; i++)
        {
            var color = colores[i];
            var fila = EstiloUI.Panel(_marcador, "Fila " + nombres[i], EstiloUI.Fondo, 20, true);
            EstiloUI.Ubicar(fila, new Vector2(1, 1), new Vector2(0, -i * (alto + 10)), new Vector2(ancho, alto), new Vector2(1, 1));
            var contorno = EstiloUI.Contorno(fila, new Color(1, 1, 1, 0.07f), 20, 2f);

            // franja de color a la izquierda
            var franja = EstiloUI.Panel(fila, "Franja", color, 3, false, out var franjaImg);
            EstiloUI.Ubicar(franja, new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(6, alto - 28), new Vector2(0, 0.5f));

            // nombre completo + estado
            string bonito = FormatoShow.NombreBonito(nombres[i]);
            var n = EstiloUI.Texto(fila, "Nombre", 25, TextAnchor.LowerLeft, EstiloUI.TextoClaro, FontStyle.Bold);
            EstiloUI.Llenar(n.rectTransform, 30, alto * 0.48f, 96, 8);
            n.text = bonito;
            n.horizontalOverflow = HorizontalWrapMode.Overflow;
            var e = EstiloUI.Texto(fila, "Estado", 17, TextAnchor.UpperLeft, Gris);
            EstiloUI.Llenar(e.rectTransform, 31, 6, 96, alto * 0.56f);
            e.text = "en la mesa";

            // foto a la derecha
            var foto = EstiloUI.CrearFoto(fila, "Foto", 70, 18, 3.5f);
            EstiloUI.Ubicar(foto.rt, new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(70, 70), new Vector2(1, 0.5f));
            string clave = FormatoShow.Clave(nombres[i]);
            foto.Poner(Foto(clave), bonito, color);

            _filas.Add(new Fila { rt = fila, contorno = contorno, franja = franjaImg, foto = foto, nombre = n, estado = e, clave = clave, color = color, x = 0 });
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
        {
            bool a = i == activo;
            _filas[i].x = a ? -24f : 0f;
            _filas[i].contorno.color = a ? EstiloUI.Dorado : new Color(1, 1, 1, 0.07f);
            _filas[i].nombre.color = a ? EstiloUI.Dorado : EstiloUI.TextoClaro;
            _filas[i].foto.anillo.color = a ? EstiloUI.Dorado : _filas[i].color;
        }
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

    void OnDestroy()
    {
        if (_retRT != null) { _retRT.Release(); Destroy(_retRT); }
    }

    // ---------------------------------------------------------------- placas sobre los Sharks

    public Placa CrearPlaca(Func<Vector3> posicion)
    {
        var p = new Placa(posicion, transform);
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

        public Placa(Func<Vector3> pos, Transform padre)
        {
            _pos = pos;
            _go = new GameObject("Placa", typeof(Canvas));
            _go.transform.SetParent(padre, false);
            _grupo = _go.AddComponent<CanvasGroup>();
            var c = _go.GetComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)_go.transform;
            rt.sizeDelta = new Vector2(360, 96);
            var panel = EstiloUI.Panel(rt, "Panel", Color.white, 30, true, out _fondo);
            EstiloUI.Llenar(panel);
            EstiloUI.Contorno(panel, Color.white, 30, 5f);
            _texto = EstiloUI.Texto(panel, "Texto", 52, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            EstiloUI.SombraTexto(_texto, 2, 0.4f);
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
}
