using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// Director del show: consulta al backend (GET /eventos?desde=N), encola los eventos y los
/// reproduce uno por uno con cambios de cámara, foco de luz sobre quien habla, desenfoque
/// de fondo, subtítulos, sellos de "dentro / fuera" y la llegada del emprendedor al barco.
[DefaultExecutionOrder(100)]
public class ShowDirector : MonoBehaviour
{
    [Header("Backend")]
    public string host = "127.0.0.1";
    public int puerto = 8765;
    public float intervaloSondeo = 0.35f;

    [Header("Escena")]
    public EscenarioPitch escenario;
    public Camera camara;
    public CinematicCamera orbita;
    public Material materialParticulas;

    [Header("Ritmo")]
    public float letrasPorSegundo = 48f;
    public float lecturaLetrasPorSegundo = 26f;

    // ---- estado de la conexión / cola
    string _sesion;
    int _ultimoSeq;
    bool _conectado, _modoDemo;
    readonly List<Evento> _cola = new();
    Coroutine _show;

    // ---- estado del show
    ShowUI _ui;
    ShowInput _input;
    VocesShow _voces;
    bool _decidirFormulario = true;   // al primer sondeo: mostrar el formulario si no hay sesión en curso
    string _sesionPedida;             // sesión creada por nuestro POST /sesion (no volver a abrir el formulario)
    string _nombreEmprendedor = "Emprendedor";
    readonly Dictionary<int, string> _autorPregunta = new();
    readonly Dictionary<string, int> _preguntas = new();
    List<string> _nombresBackend = new();
    readonly List<ShowUI.Placa> _placas = new();
    bool _llego;
    int _hablante = int.MinValue;        // índice de Shark, Emprendedor, o MinValue = nadie
    bool _hablandoActivo;
    bool _saltar, _rapido, _camLibre;
    float Vel => _rapido ? 2.6f : 1f;
    Quaternion[] _rotBaseShark;

    // ---- cámara
    Func<EscenarioPitch.Plano> _plano;
    Vector3 _desdePos; Quaternion _desdeRot; float _desdeFov;
    float _blendT, _blendDur;
    Light _foco;
    DepthOfField _dof;
    Volume _volumen;

    const int Emprendedor = -100;   // los índices de Shark son >= 0; -1 = Shark desconocido

    // ================================================================ ciclo de vida

    void Start()
    {
        Application.runInBackground = true;   // seguir recibiendo eventos mientras escribes en la terminal
        if (escenario == null) escenario = FindFirstObjectByType<EscenarioPitch>();
        if (camara == null) camara = Camera.main;
        if (orbita == null && camara != null) orbita = camara.GetComponent<CinematicCamera>();
        escenario.Init();

        _ui = gameObject.AddComponent<ShowUI>();
        _ui.Construir();
        _voces = gameObject.AddComponent<VocesShow>();
        _input = gameObject.AddComponent<ShowInput>();
        _input.Construir();
        _input.AlEnviarPitch += (n, p, m, pct) => StartCoroutine(EnviarPitch(n, p, m, pct));
        _input.AlResponder += t => StartCoroutine(EnviarRespuesta(t));
        _input.AlElegir += sh => StartCoroutine(EnviarEleccion(sh));
        _input.AlPedirDemo += () => { _input.MostrarFormulario(false); if (!_modoDemo) AlternarDemo(); };
        _input.AlNuevoPitch += () => { _ui.Resumen(null, null); if (_modoDemo) AlternarDemo(); _input.MostrarFormulario(true); };

        _rotBaseShark = escenario.sharks.Select(s => s.personaje != null ? s.personaje.localRotation : Quaternion.identity).ToArray();
        for (int i = 0; i < escenario.sharks.Length; i++)
        {
            int k = i;
            _placas.Add(_ui.CrearPlaca(() => escenario.CabezaShark(k) + Vector3.up * (0.45f + escenario.sharks[k].altura * 0.22f)));
        }

        CrearFoco();
        CrearDesenfoque();
        CrearColisionadores();
        ReiniciarEscena();
        StartCoroutine(TomarFotos());
        StartCoroutine(Sondear());
        _show = StartCoroutine(Show());
#if UNITY_EDITOR
        if (File.Exists("Temp/op_capturas")) StartCoroutine(CapturasDePrueba());
#endif
    }

#if UNITY_EDITOR
    /// Solo para pruebas en el editor: guarda una captura cada pocos segundos y sale de Play al final.
    IEnumerator CapturasDePrueba()
    {
        string[] cfg = File.ReadAllText("Temp/op_capturas").Split(' ');
        float cada = cfg.Length > 0 && float.TryParse(cfg[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var c) ? c : 3f;
        int total = cfg.Length > 1 && int.TryParse(cfg[1], out var n) ? n : 60;
        File.Delete("Temp/op_capturas");
        if (cfg.Length > 2 && cfg[2].Trim() == "demo") AlternarDemo();
        if (cfg.Length > 2 && cfg[2].Trim().StartsWith("clavado"))
        {
            // prueba del clavado: sin show, solo el Shark indicado (clavado:zoro)
            string quien = cfg[2].Contains(":") ? cfg[2].Split(':')[1].Trim() : "zoro";
            yield return new WaitForSeconds(2.5f);
            _input.MostrarFormulario(false);
            if (_show != null) StopCoroutine(_show);
            int k = Idx(quien);
            if (k >= 0)
            {
                Hablante(k, "interés bajo");
                _ui.TextoSubtitulo("Estoy fuera.", 999);
                _ui.Sello("ESTOY FUERA", FormatoShow.NombreBonito(quien) + "  ·  sin oferta", ShowUI.Rojo, 1.8f);
                StartCoroutine(Clavado(k));
            }
        }
        if (cfg.Length > 3 && cfg[3].Trim() == "rapido") _rapido = true;
        foreach (var f in Directory.GetFiles("Logs", "show_*.png")) File.Delete(f);
        for (int i = 0; i < total; i++)
        {
            yield return new WaitForSeconds(cada);
            ScreenCapture.CaptureScreenshot($"Logs/show_{i:000}.png");
            File.WriteAllText("Logs/show_estado.txt", $"{i} cola={_cola.Count} seq={_ultimoSeq} sesion={_sesion} conectado={_conectado}");
        }
        yield return new WaitForSeconds(0.5f);
        UnityEditor.EditorApplication.isPlaying = false;
    }
#endif

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        bool atajos = !ShowInput.Escribiendo && !_input.FormularioVisible;   // las teclas no cuentan mientras escribes
        if (!atajos) { ActualizarEstado(); return; }
        if (kb.spaceKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) _saltar = true;
        if (kb.fKey.wasPressedThisFrame) _rapido = !_rapido;
        if (kb.dKey.wasPressedThisFrame) AlternarDemo();
        if (kb.vKey.wasPressedThisFrame) _camLibre = !_camLibre;
#endif
        escenario.emprendedor.multiplicadorVelocidad = Vel * (_saltando ? 5f : 1f);
        ActualizarEstado();
    }

    bool _saltando;

    // ================================================================ backend

    IEnumerator Sondear()
    {
        while (true)
        {
            if (_modoDemo) { yield return new WaitForSeconds(0.5f); continue; }
            string url = $"http://{host}:{puerto}/eventos?desde={_ultimoSeq}";
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 3;
                yield return req.SendWebRequest();
                if (_modoDemo) continue;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    _conectado = true;
                    RespuestaEventos r = null;
                    try { r = JsonUtility.FromJson<RespuestaEventos>(req.downloadHandler.text); }
                    catch (Exception ex) { Debug.LogWarning("[Show] JSON inválido del backend: " + ex.Message); }
                    if (r != null)
                    {
                        if (r.sesion != _sesion)
                        {
                            bool pedirDeNuevo = _ultimoSeq > 0;   // pedimos desde un seq de otra sesión: volver a 0
                            NuevaSesion(r.sesion);
                            if (pedirDeNuevo) continue;
                            // backend reiniciado (sesión vacía que no pedimos nosotros): volver al formulario
                            if ((r.eventos == null || r.eventos.Length == 0) && r.sesion != _sesionPedida && !_decidirFormulario)
                                _input.MostrarFormulario(true);
                        }
                        if (r.eventos != null)
                            foreach (var e in r.eventos.OrderBy(e => e.seq))
                                if (e.seq > _ultimoSeq) { _cola.Add(e); _ultimoSeq = e.seq; }
                        if (_decidirFormulario)
                        {
                            // Si el backend ya trae una sesión a medias (Unity se reinició), se reproduce; si no, formulario
                            _decidirFormulario = false;
                            var ult = r.eventos != null && r.eventos.Length > 0 ? r.eventos[^1].tipo : null;
                            if (ult == null || ult == "fin" || ult == "error")
                            {
                                _cola.Clear();   // sesión vieja ya terminada: no se repite detrás del formulario
                                _input.MostrarFormulario(true);
                            }
                        }
                    }
                }
                else
                {
                    _conectado = false;
                    if (_decidirFormulario) { _decidirFormulario = false; _input.MostrarFormulario(true); }
                }
            }
            yield return new WaitForSeconds(intervaloSondeo);
        }
    }

    void NuevaSesion(string sesion)
    {
        Debug.Log($"[Show] Nueva sesión del backend: {sesion}");
        _sesion = sesion;
        _ultimoSeq = 0;
        _cola.Clear();
        if (_show != null) StopCoroutine(_show);
        if (_input != null) { _input.OcultarRespuesta(); _input.OcultarEleccion(); _input.MostrarNuevoPitch(false); }
        ReiniciarEscena();
        _show = StartCoroutine(Show());
    }

    /// El backend está esperando al usuario ahora mismo (no es una repetición de eventos viejos).
    bool EnVivoYPendiente() => !_modoDemo && _cola.Count == 0;

    // ================================================================ peticiones al backend (POST)

    IEnumerator Post(string ruta, string json, Action<string> ok, Action<long, string> fallo)
    {
        using (var req = new UnityWebRequest($"http://{host}:{puerto}{ruta}", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            req.timeout = 15;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) { ok?.Invoke(req.downloadHandler.text); yield break; }
            string msg = null;
            try { msg = JsonUtility.FromJson<RespuestaError>(req.downloadHandler.text)?.error; } catch { }
            if (req.result == UnityWebRequest.Result.ConnectionError)
                msg = $"No se pudo conectar con el backend en {host}:{puerto}. ¿Está corriendo  python main.py ?";
            fallo?.Invoke(req.responseCode, string.IsNullOrEmpty(msg) ? req.error : msg);
        }
    }

    IEnumerator EnviarPitch(string nombre, string pitch, double monto, double pct)
    {
        if (_modoDemo) AlternarDemo();
        var cuerpo = JsonUtility.ToJson(new PeticionSesion { emprendedor = nombre, pitch = pitch, monto = monto, porcentaje = pct });
        yield return Post("/sesion", cuerpo,
            txt =>
            {
                try { _sesionPedida = JsonUtility.FromJson<RespuestaEventos>(txt)?.sesion; } catch { }
                _input.MostrarFormulario(false);
            },
            (code, msg) => _input.ErrorFormulario(msg));
    }

    IEnumerator EnviarRespuesta(string texto)
    {
        var cuerpo = JsonUtility.ToJson(new PeticionRespuesta { texto = texto });
        yield return Post("/respuesta", cuerpo,
            _ => _input.OcultarRespuesta(),
            (code, msg) => _input.ErrorRespuesta(code == 409 ? "El backend todavía no espera esta respuesta; intenta de nuevo en un momento." : msg));
    }

    IEnumerator EnviarEleccion(string shark)
    {
        // JsonUtility no escribe null, así que "ninguna" se manda a mano
        string cuerpo = shark == null ? "{\"shark\": null}" : JsonUtility.ToJson(new PeticionEleccion { shark = shark });
        yield return Post("/eleccion", cuerpo,
            _ => _input.OcultarEleccion(),
            (code, msg) => _input.ErrorEleccion(code == 409 ? "El backend todavía no espera la elección; intenta de nuevo." : msg));
    }

    void ActualizarEstado()
    {
        string modo = _rapido ? "   ·   RÁPIDO x2.6" : "";
        _input.EstadoFormulario(_conectado ? "● Backend conectado" : $"○ Backend no conectado — corre:  python main.py", _conectado ? ShowUI.Verde : new Color(1f, 0.6f, 0.45f));
        if (_modoDemo) _ui.Estado("● DEMO (corrida guardada) — D para salir" + modo, ShowUI.Dorado);
        else if (_conectado) _ui.Estado($"● Conectado al backend {host}:{puerto}   ·   en cola: {_cola.Count}" + modo, ShowUI.Verde);
        else _ui.Estado($"○ Esperando backend en {host}:{puerto}  (python main.py)   ·   D: demo" + modo, new Color(1f, 0.6f, 0.45f));
    }

    // ================================================================ demo (corrida guardada)

    void AlternarDemo()
    {
        if (_modoDemo)
        {
            _modoDemo = false;
            _sesion = null; _ultimoSeq = 0;
            NuevaSesion(null);
            _input.MostrarFormulario(true);
            return;
        }
        string ruta = Path.Combine(Application.streamingAssetsPath, "demo_corrida.json");
        if (!File.Exists(ruta)) { Debug.LogWarning("[Show] No existe " + ruta); return; }
        Corrida c;
        try { c = JsonUtility.FromJson<Corrida>(File.ReadAllText(ruta)); }
        catch (Exception ex) { Debug.LogWarning("[Show] Demo inválida: " + ex.Message); return; }
        _modoDemo = true;
        NuevaSesion("demo");
        _cola.AddRange(EventosDeCorrida(c));
    }

    /// Convierte el historial guardado en la misma secuencia de eventos que manda el backend en vivo.
    static List<Evento> EventosDeCorrida(Corrida c)
    {
        var lista = new List<Evento> { new Evento { tipo = "inicio_sesion", emprendedor = c.emprendedor, sharks = c.sharks } };
        string etapa = null; int vuelta = 0;
        foreach (var m in c.historial)
        {
            if (m.etapa == "preguntas" && m.vuelta != vuelta)
            {
                vuelta = m.vuelta;
                lista.Add(new Evento { tipo = "inicio_vuelta", vuelta = vuelta });
            }
            if (m.etapa != etapa)
            {
                if (m.etapa == "retroalimentacion") lista.Add(new Evento { tipo = "inicio_etapa", etapa = "deliberacion" });
                if (m.etapa == "retroalimentacion" || m.etapa == "ofertas" || m.etapa == "eleccion")
                    lista.Add(new Evento { tipo = "inicio_etapa", etapa = m.etapa });
                etapa = m.etapa;
            }
            if (m.tipo == "pregunta") lista.Add(new Evento { tipo = "turno", emisor = m.emisor, etapa = "preguntas", vuelta = m.vuelta });
            lista.Add(m);
        }
        lista.Add(new Evento { tipo = "fin", resumen = c.verificacion });
        return lista;
    }

    // ================================================================ reproducción

    IEnumerator Show()
    {
        while (true)
        {
            if (_cola.Count == 0) { yield return null; continue; }
            var e = _cola[0];
            _cola.RemoveAt(0);
            _saltar = false;
            yield return Reproducir(e);
            Callar();
        }
    }

    IEnumerator Reproducir(Evento e)
    {
        switch (e.tipo)
        {
            case "inicio_sesion":
                _input.MostrarFormulario(false);   // empezó una sesión: fuera el formulario
                ConfigurarSesion(e.emprendedor, e.sharks);
                yield return Llegada();
                break;

            case "esperando":
                yield return Llegada();
                if (e.etapa == "pitch")
                {
                    Etapa("1 · PITCH");
                    Plano(escenario.PlanoEmprendedor);
                    yield return Esperando(Emprendedor, "se prepara", "Preparando el pitch");
                }
                else if (e.etapa == "eleccion")
                {
                    Etapa("5 · ELECCIÓN");
                    Plano(escenario.PlanoGrupo, 1f);
                    if (EnVivoYPendiente() && e.opciones != null && e.opciones.Length > 0)
                    {
                        _ui.OcultarSubtitulo();
                        _input.MostrarEleccion(e.opciones, n => { int k = Idx(n); return k >= 0 ? escenario.sharks[k].color : Color.white; });
                        while (_cola.Count == 0) yield return null;
                        _input.OcultarEleccion();
                    }
                    else yield return Esperando(Emprendedor, "decide", "Eligiendo una oferta", minimo: 0.6f);
                }
                else
                {
                    int s = Idx(e.shark);
                    Plano(s >= 0 ? () => escenario.PlanoContraplano(s) : escenario.PlanoEmprendedor);
                    if (EnVivoYPendiente())
                    {
                        _ui.OcultarSubtitulo();
                        _hablante = int.MinValue;
                        _input.MostrarRespuesta(e.shark ?? "el Shark", s >= 0 ? escenario.sharks[s].color : Color.white, e.pregunta);
                        while (_cola.Count == 0) yield return null;
                        _input.OcultarRespuesta();
                    }
                    else yield return Esperando(Emprendedor, "responde a " + e.shark, "Escribiendo su respuesta", minimo: 0.5f);
                }
                break;

            case "error":
                Etapa("ERROR DEL BACKEND");
                Orbita();
                _ui.Hablante("Backend", ShowUI.Rojo, "la sesión se detuvo");
                _ui.TextoSubtitulo(e.contenido ?? "Error desconocido", 9999);
                _input.MostrarNuevoPitch(true);
                while (_cola.Count == 0) yield return null;
                break;

            case "pitch":
            {
                yield return Llegada();
                Etapa("1 · PITCH");
                Tarjeta("EL PITCH", _nombreEmprendedor + " presenta su negocio", 2.2f);
                string texto = e.contenido ?? "";
                int i = texto.IndexOf("[Solicitud:", StringComparison.Ordinal);
                if (i >= 0) texto = texto.Substring(0, i).Trim();
                if (e.monto > 0)
                    _ui.Solicitud(_nombreEmprendedor, e.monto, e.porcentaje);
                Plano(escenario.PlanoGrupo, 0.9f);
                yield return Esperar(1.6f);
                var planos = new Func<EscenarioPitch.Plano>[] { escenario.PlanoEmprendedor, escenario.PlanoMesa, escenario.PlanoEmprendedor, escenario.PlanoGrupo };
                yield return Hablar(Emprendedor, texto, "pitch", planos);
                if (e.monto > 0)
                    yield return Hablar(Emprendedor, $"Les pido {FormatoShow.Dinero(e.monto)} por el {FormatoShow.Porcentaje(e.porcentaje)} de mi empresa.", "la solicitud",
                                        new Func<EscenarioPitch.Plano>[] { escenario.PlanoEmprendedor });
                break;
            }

            case "inicio_vuelta":
                Etapa($"2 · PREGUNTAS   ·   Vuelta {e.vuelta}");
                if (e.vuelta <= 1) Tarjeta("RONDA DE PREGUNTAS", "Cada Shark pregunta o pasa", 2.4f);
                else Tarjeta($"VUELTA {e.vuelta}", "Siguen las preguntas", 1.8f);
                Plano(escenario.PlanoGrupo, 0.9f);
                yield return Esperar(_cola.Count > 3 ? 1.4f : 2.2f);
                break;

            case "turno":
            {
                int s = Idx(e.emisor);
                if (s < 0) break;
                Resaltar(s);
                _voces.Efecto("turno", 0.6f);
                Plano(() => escenario.PlanoShark(s));
                yield return Esperando(s, "su turno", "Pensando");
                break;
            }

            case "pregunta":
            {
                yield return Llegada();
                int s = Idx(e.emisor);
                _autorPregunta[e.id] = e.emisor;
                _preguntas[FormatoShow.Clave(e.emisor)] = Preguntas(e.emisor) + 1;
                MarcadorFila(e.emisor, $"preguntó ({Preguntas(e.emisor)})", Color.white);
                Resaltar(s);
                yield return Hablar(s, e.contenido, "pregunta", s >= 0 ? new Func<EscenarioPitch.Plano>[] { () => escenario.PlanoShark(s) } : null);
                break;
            }

            case "pasar":
            {
                int s = Idx(e.emisor);
                Resaltar(s);
                MarcadorFila(e.emisor, Preguntas(e.emisor) > 0 ? $"pasa  ·  {Preguntas(e.emisor)} pregunta(s)" : "pasa", ShowUI.Gris);
                yield return Hablar(s, "Paso.", "pasa", s >= 0 ? new Func<EscenarioPitch.Plano>[] { () => escenario.PlanoShark(s) } : null,
                                    lecturaMin: _cola.Count > 2 ? 0.6f : 1.1f);
                break;
            }

            case "respuesta":
            {
                _autorPregunta.TryGetValue(e.responde_a, out string autor);
                int s = Idx(autor);
                Resaltar(s);
                var planos = s >= 0
                    ? new Func<EscenarioPitch.Plano>[] { () => escenario.PlanoContraplano(s), escenario.PlanoEmprendedor }
                    : new Func<EscenarioPitch.Plano>[] { escenario.PlanoEmprendedor };
                yield return Hablar(Emprendedor, e.contenido, autor != null ? "responde a " + autor : "respuesta", planos);
                break;
            }

            case "inicio_etapa":
                yield return InicioEtapa(e.etapa);
                break;

            case "recomendacion":
            {
                int s = Idx(e.emisor);
                Etapa("3 · RETROALIMENTACIÓN");
                Resaltar(s);
                yield return Hablar(s, e.contenido, "retroalimentación", s >= 0 ? new Func<EscenarioPitch.Plano>[] { () => escenario.PlanoShark(s) } : null);
                break;
            }

            case "decision":
                yield return Decision(e);
                break;

            case "eleccion":
                yield return Eleccion(e);
                break;

            case "fin":
                yield return Fin(e.resumen);
                break;

            default:
                Debug.Log($"[Show] Evento no manejado: {e.tipo}");
                break;
        }
    }

    IEnumerator InicioEtapa(string etapa)
    {
        switch (etapa)
        {
            case "deliberacion":
                _ui.MarcadorResaltar(-1);
                Etapa("LOS SHARKS DELIBERAN");
                Tarjeta("DELIBERACIÓN", "Cada Shark decide sin ver a los demás", 2.4f);
                Plano(escenario.PlanoMesa, 1.2f);
                yield return Esperando(int.MinValue, "", "Los Sharks están deliberando", "Sharks", ShowUI.Dorado);
                break;
            case "retroalimentacion":
                Etapa("3 · RETROALIMENTACIÓN");
                Tarjeta("RETROALIMENTACIÓN", "Lo que cada Shark le recomienda al emprendedor", 2.4f);
                Plano(escenario.PlanoGrupo, 1f);
                yield return Esperar(2.2f);
                break;
            case "ofertas":
                Etapa("4 · OFERTAS");
                Tarjeta("¿DENTRO O FUERA?", "Llegó el momento de las ofertas", 2.6f);
                Plano(escenario.PlanoGrupo, 1f);
                yield return Esperar(2.4f);
                break;
            case "eleccion":
                Etapa("5 · ELECCIÓN");
                _ui.MarcadorResaltar(-1);
                Tarjeta("LA ELECCIÓN", _nombreEmprendedor + " decide con quién hace el trato", 2.6f);
                Plano(escenario.PlanoEmprendedor, 1f);
                yield return Esperando(Emprendedor, "decide", "Eligiendo una oferta", minimo: 2.4f);
                break;
            default:
                Etapa(etapa?.ToUpperInvariant());
                yield return Esperar(1f);
                break;
        }
    }

    IEnumerator Decision(Evento e)
    {
        int s = Idx(e.emisor);
        Etapa("4 · OFERTAS");
        Resaltar(s);
        string etiqueta = "interés " + (e.interes ?? "?");
        yield return Hablar(s, e.contenido, etiqueta, s >= 0 ? new Func<EscenarioPitch.Plano>[] { () => escenario.PlanoShark(s) } : null);

        bool dentro = e.dentro && e.oferta != null && e.oferta.monto > 0;
        Color c = dentro ? ShowUI.Verde : ShowUI.Rojo;
        _voces.Efecto(dentro ? "dentro" : "fuera");
        _ui.Sello(dentro ? "¡ESTOY DENTRO!" : "ESTOY FUERA", (e.emisor ?? "") + (dentro ? "  ·  " + FormatoShow.Oferta(e.oferta) : "  ·  sin oferta"), c, 2.6f / Vel);
        MarcadorFila(e.emisor, dentro ? "DENTRO  ·  " + FormatoShow.Dinero(e.oferta.monto) + " x " + FormatoShow.Porcentaje(e.oferta.porcentaje) : "FUERA", c);
        if (s >= 0) _placas[s].Mostrar(dentro ? "DENTRO" : "FUERA", c);
        if (dentro && s >= 0) Confeti(escenario.CabezaShark(s), 60);
        Hablante(s, etiqueta);
        _ui.TextoSubtitulo(dentro ? "Oferta: " + FormatoShow.Oferta(e.oferta) : "Sin oferta.", 999);
        if (!dentro && s >= 0)
        {
            yield return Esperar(1.2f);
            yield return Clavado(s);
        }
        else yield return Esperar(2.6f);
    }

    IEnumerator Eleccion(Evento e)
    {
        Etapa("5 · ELECCIÓN");
        yield return Hablar(Emprendedor, e.contenido, "elección", new Func<EscenarioPitch.Plano>[] { escenario.PlanoEmprendedor });
        int s = Idx(e.shark_elegido);
        if (s >= 0)
        {
            Resaltar(s);
            Plano(() => escenario.PlanoShark(s), 0.8f);
            _hablante = s;
            _voces.Efecto("trato");
            _ui.Sello("¡TRATO HECHO!", _nombreEmprendedor + " + " + e.shark_elegido, ShowUI.Dorado, 3.2f / Vel);
            _placas[s].Mostrar("TRATO", ShowUI.Dorado);
            Confeti(escenario.CabezaEmprendedor(), 220);
            Confeti(escenario.CabezaShark(s), 160);
            yield return Esperar(3.2f);
        }
        else
        {
            _voces.Efecto("fuera", 0.7f);
            _ui.Sello("SIN TRATO", _nombreEmprendedor + " no aceptó ninguna oferta", new Color(0.35f, 0.38f, 0.45f), 2.8f / Vel);
            yield return Esperar(2.8f);
        }
    }

    IEnumerator Fin(Resumen r)
    {
        _ui.MarcadorResaltar(-1);
        _ui.OcultarSubtitulo();
        Etapa("FIN DEL PITCH");
        float t0 = Time.time;
        Plano(() => escenario.PlanoFinal(Time.time - t0), 1.6f);
        string Lista(string[] xs) => xs == null || xs.Length == 0 ? "nadie" : string.Join(", ", xs);
        string trato = r != null && !string.IsNullOrEmpty(r.eleccion) ? $"<color=#FFD045><b>Trato con {r.eleccion}</b></color>" : "<b>Sin trato</b>";
        string cuerpo = r == null ? "" :
            $"{trato}\n\n" +
            $"<color=#4CD964>Dentro:</color>  {Lista(r.dentro)}\n" +
            $"<color=#FF5A4F>Fuera:</color>  {Lista(r.fuera)}\n\n" +
            $"Preguntas: {r.total_preguntas}   ·   Vueltas: {r.vueltas_jugadas}" +
            (r.fin_por_consenso ? "   ·   todos pasaron" : "   ·   máximo de vueltas");
        _ui.Resumen("RESULTADO · " + _nombreEmprendedor, cuerpo);
        _input.MostrarNuevoPitch(true);
        while (_cola.Count == 0) yield return null;
        _ui.Resumen(null, null);
        _input.MostrarNuevoPitch(false);
    }

    // ================================================================ llegada del emprendedor

    IEnumerator Llegada()
    {
        if (_llego) yield break;
        _llego = true;
        var emp = escenario.emprendedor;
        var ruta = escenario.rutaSubida;
        emp.Colocar(escenario.puntoBote, ruta.Length > 0 ? ruta[0] : escenario.puntoBote + Vector3.right);

        Etapa("LLEGADA AL GOING MERRY");
        Tarjeta("SHARK TANK", "a bordo del Going Merry", 3f);
        Plano(escenario.PlanoSubida, 0.01f);
        Hablante(Emprendedor, "llega al barco");
        _ui.TextoSubtitulo(_nombreEmprendedor + " sube a bordo para presentar su negocio a los Sharks.", 999, "", true);
        yield return Esperar(1.6f);

        _saltando = false;
        emp.StartCoroutine(emp.Recorrer(ruta));   // en el emprendedor, para poder detenerlo al reiniciar
        // La cámara sigue al emprendedor por la escalera; cuando entra a la cubierta, plano de conjunto
        bool enCubierta = false;
        float yCubierta = escenario.PuntoPitch.y;
        yield return null;
        while (emp.Caminando || !enCubierta)
        {
            if (_saltar) { _saltando = true; _saltar = false; }
            if (!enCubierta && emp.transform.localPosition.y >= yCubierta + 0.5f)
            {
                enCubierta = true;
                Plano(escenario.PlanoMesa, 1.4f);
            }
            if (!emp.Caminando && !enCubierta) { enCubierta = true; Plano(escenario.PlanoMesa, 1.2f); }
            yield return null;
        }
        _saltando = false;
        yield return emp.Voltear(escenario.barco.InverseTransformPoint(escenario.CentroGrupo()));
        _ui.OcultarSubtitulo();
        Plano(escenario.PlanoGrupo, 1f);
        yield return Esperar(1f);
    }

    // ================================================================ hablar / esperar

    /// Muestra el texto de quien habla, con efecto de máquina de escribir y paginado,
    /// moviendo la cámara entre los planos indicados (uno por página, en ciclo).
    IEnumerator Hablar(int quien, string texto, string etiqueta, Func<EscenarioPitch.Plano>[] planos, float lecturaMin = 1.8f)
    {
        var paginas = FormatoShow.Paginar(string.IsNullOrWhiteSpace(texto) ? "…" : texto);
        for (int p = 0; p < paginas.Count; p++)
        {
            string pag = paginas[p];
            if (planos != null && planos.Length > 0 && (p == 0 || planos.Length > 1))
                Plano(planos[p % planos.Length], p == 0 ? 0.6f : 1.1f);
            Hablante(quien, etiqueta);
            _hablandoActivo = true;
            string numero = paginas.Count > 1 ? $"{p + 1} / {paginas.Count}" : "";

            float visibles = 0;
            int mostradas = 0;
            string voz = VozDe(quien);
            _voces.Efecto(p == 0 ? "abrir" : "pagina", p == 0 ? 0.55f : 0.8f);
            while (visibles < pag.Length)
            {
                bool salto = false;
                if (_saltar) { _saltar = false; visibles = pag.Length; salto = true; }
                visibles += Time.deltaTime * letrasPorSegundo * Vel * (_cola.Count > 6 ? 1.8f : 1f);
                int ahora = Mathf.Min((int)visibles, pag.Length);
                if (!salto) for (int k = mostradas; k < ahora; k++) _voces.Letra(voz, pag[k]);   // blip estilo Celeste
                mostradas = ahora;
                _ui.TextoSubtitulo(pag, ahora, numero);
                yield return null;
            }
            _ui.TextoSubtitulo(pag, pag.Length, numero);
            _hablandoActivo = false;
            float lectura = Mathf.Clamp(pag.Length / lecturaLetrasPorSegundo, lecturaMin, 9f);
            yield return Esperar(lectura);
        }
    }

    /// Estado de espera (alguien piensa o escribe): dura hasta que llegue el siguiente evento.
    IEnumerator Esperando(int quien, string etiqueta, string texto, string nombre = null, Color? color = null, float minimo = 0.9f)
    {
        if (nombre != null) { _ui.Hablante(nombre, color ?? Color.white, etiqueta); _hablante = int.MinValue; }
        else Hablante(quien, etiqueta);
        float t = 0;
        float min = _cola.Count > 0 ? Mathf.Min(minimo, 0.5f) / Vel : minimo / Vel;
        while (t < min || _cola.Count == 0)
        {
            if (_saltar && _cola.Count > 0) break;
            _saltar = false;
            int puntos = 1 + (int)(Time.time * 2.5f) % 3;
            _ui.TextoSubtitulo(texto + new string('.', puntos), 999, "", true);
            t += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator Esperar(float segundos)
    {
        float t = 0;
        while (t < segundos)
        {
            if (_saltar) { _saltar = false; yield break; }
            t += Time.deltaTime * Vel * (_cola.Count > 6 ? 1.6f : 1f);
            yield return null;
        }
    }

    void Hablante(int quien, string etiqueta)
    {
        _hablante = quien;
        if (quien == Emprendedor) _ui.Hablante(_nombreEmprendedor, escenario.colorEmprendedor, etiqueta, RetratoDe(quien), "emprendedor");
        else if (quien >= 0) _ui.Hablante(NombreVisible(quien), escenario.sharks[quien].color, etiqueta, RetratoDe(quien), FormatoShow.Clave(escenario.sharks[quien].nombre));
        else _ui.Hablante("", Color.gray, etiqueta);
    }

    /// Cámara del retrato: cabeza de quien habla, hacia dónde mira y su altura (para el encuadre).
    Func<(Vector3, Vector3, float)> RetratoDe(int quien)
    {
        if (quien == Emprendedor)
            return () => (escenario.CabezaEmprendedor(), escenario.emprendedor.transform.forward, 1.78f);
        if (quien >= 0 && quien < escenario.sharks.Length)
        {
            int k = quien;
            return () =>
            {
                Vector3 c = escenario.CabezaShark(k);
                return (c, escenario.CabezaEmprendedor() - c, escenario.sharks[k].altura);
            };
        }
        return null;
    }

    string VozDe(int quien) =>
        quien == Emprendedor ? "emprendedor" : quien >= 0 && quien < escenario.sharks.Length ? VocesShow.Clave(escenario.sharks[quien].nombre) : "_";

    void Tarjeta(string grande, string sub, float segundos)
    {
        _ui.Tarjeta(grande, sub, segundos);
        _voces.Efecto("tarjeta", 0.8f);
    }

    void Callar()
    {
        _hablandoActivo = false;
        escenario.emprendedor.hablando = false;
    }

    // ================================================================ sesión

    void ConfigurarSesion(string emprendedor, string[] sharks)
    {
        _nombreEmprendedor = string.IsNullOrWhiteSpace(emprendedor) ? "Emprendedor" : emprendedor.Trim();
        _nombresBackend = (sharks != null && sharks.Length > 0 ? sharks : SharksPorDefecto).ToList();
        var colores = _nombresBackend.Select(n => { int i = Idx(n); return i >= 0 ? escenario.sharks[i].color : Color.white; }).ToList();
        _ui.ConfigurarMarcador(_nombresBackend, colores);
    }

    void ReiniciarEscena()
    {
        RegresarDelAgua();
        _autorPregunta.Clear();
        _preguntas.Clear();
        _llego = false;
        _hablante = int.MinValue;
        _ui.LimpiarTodo();
        ConfigurarSesion(null, null);
        var ruta = escenario.rutaSubida;
        escenario.emprendedor.StopAllCoroutines();
        escenario.emprendedor.Colocar(escenario.puntoBote, ruta != null && ruta.Length > 0 ? ruta[0] : escenario.puntoBote + Vector3.right);
        Orbita();
    }

    /// Resalta en el marcador la fila del Shark (índice de escena -> fila en el orden del backend).
    void Resaltar(int idxEscena) => _ui.MarcadorResaltar(idxEscena < 0 ? -1 : _nombresBackend.FindIndex(n => Idx(n) == idxEscena));

    static readonly string[] SharksPorDefecto = { "Luffy", "Shanks", "Zoro", "Whitebeard", "Arlong" };

    int Idx(string nombre) => string.IsNullOrEmpty(nombre) ? -1 : escenario.IndiceShark(nombre);

    string NombreVisible(int i) => FormatoShow.NombreBonito(escenario.sharks[i].nombre);

    int Preguntas(string shark) => _preguntas.TryGetValue(FormatoShow.Clave(shark), out int n) ? n : 0;

    void MarcadorFila(string shark, string estado, Color color)
    {
        int fila = _nombresBackend.FindIndex(n => FormatoShow.Clave(n) == FormatoShow.Clave(shark));
        _ui.Marcador(fila, estado, color);
    }

    void Etapa(string t) => _ui.Etapa(t);

    // ================================================================ cámara, foco y desenfoque

    void Plano(Func<EscenarioPitch.Plano> plano, float mezcla = 0.6f)
    {
        _plano = plano;
        _despejeHasta = 0f;
        _desdePos = camara.transform.position;
        _desdeRot = camara.transform.rotation;
        _desdeFov = camara.fieldOfView;
        _blendT = 0;
        _blendDur = Mathf.Max(0.01f, mezcla);
    }

    void Orbita() { _plano = null; }

    void LateUpdate()
    {
        if (camara == null) return;
        bool dirigida = _plano != null && !_camLibre;
        if (orbita != null) orbita.enabled = !dirigida;

        Vector3 foco = escenario.CentroGrupo();
        if (dirigida)
        {
            var p = _plano();
            p.pos = Despejar(p.mira, p.pos);
            var oc = OceanWaves.Instance;
            if (oc != null) p.pos.y = Mathf.Max(p.pos.y, oc.HeightAt(p.pos) + 0.9f);
            // pequeño movimiento de cámara en mano
            float t = Time.time;
            p.pos += new Vector3(Mathf.PerlinNoise(t * 0.3f, 0) - 0.5f, Mathf.PerlinNoise(0, t * 0.3f) - 0.5f, Mathf.PerlinNoise(t * 0.3f, 5) - 0.5f) * 0.12f;
            Quaternion rot = Quaternion.LookRotation(p.mira - p.pos);

            _blendT += Time.deltaTime;
            if (_blendT < _blendDur)
            {
                float k = Mathf.SmoothStep(0, 1, _blendT / _blendDur);
                camara.transform.SetPositionAndRotation(Vector3.Lerp(_desdePos, p.pos, k), Quaternion.Slerp(_desdeRot, rot, k));
                camara.fieldOfView = Mathf.Lerp(_desdeFov, p.fov, k);
            }
            else
            {
                float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
                camara.transform.SetPositionAndRotation(Vector3.Lerp(camara.transform.position, p.pos, k), Quaternion.Slerp(camara.transform.rotation, rot, k));
                camara.fieldOfView = Mathf.Lerp(camara.fieldOfView, p.fov, k);
            }
            foco = p.mira;
        }
        else if (orbita != null && orbita.enabled)
            camara.fieldOfView = Mathf.Lerp(camara.fieldOfView, 45f, Time.deltaTime * 3f);

        ActualizarHablante();
        ActualizarFoco(dirigida);
        if (_dof != null)
        {
            float d = Vector3.Distance(camara.transform.position, foco);
            _volumen.weight = Mathf.MoveTowards(_volumen.weight, dirigida ? 1f : 0f, Time.deltaTime * 2f);
            _dof.gaussianStart.Override(d + 1.2f);
            _dof.gaussianEnd.Override(d + 10f);
        }
    }

    /// Quien habla se mueve un poco (asiente) y el emprendedor gesticula.
    void ActualizarHablante()
    {
        escenario.emprendedor.hablando = _hablante == Emprendedor && _hablandoActivo;
        float t = Time.time;
        for (int i = 0; i < escenario.sharks.Length; i++)
        {
            var tr = escenario.sharks[i].personaje;
            if (tr == null || _alAgua.ContainsKey(i)) continue;
            Quaternion objetivo = _rotBaseShark[i];
            if (i == _hablante && _hablandoActivo)
                objetivo *= Quaternion.Euler(Mathf.Sin(t * 7f) * 2.2f + 1.5f, Mathf.Sin(t * 2.1f) * 4f, Mathf.Sin(t * 3.3f) * 1.2f);
            tr.localRotation = Quaternion.Slerp(tr.localRotation, objetivo, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }
    }

    void ActualizarFoco(bool dirigida)
    {
        Vector3? sobre = null;
        if (dirigida && _hablante == Emprendedor) sobre = escenario.CabezaEmprendedor();
        else if (dirigida && _hablante >= 0) sobre = escenario.CabezaShark(_hablante);

        float objetivo = sobre.HasValue ? 9f : 0f;
        _foco.intensity = Mathf.MoveTowards(_foco.intensity, objetivo, Time.deltaTime * 25f);
        if (sobre.HasValue)
        {
            Vector3 haciaCam = camara.transform.position - sobre.Value; haciaCam.y = 0;
            Vector3 pos = sobre.Value + Vector3.up * 4.5f + haciaCam.normalized * 1.6f;
            float k = _foco.intensity < 0.5f ? 1f : 1f - Mathf.Exp(-6f * Time.deltaTime);
            _foco.transform.position = Vector3.Lerp(_foco.transform.position, pos, k);
            _foco.transform.rotation = Quaternion.Slerp(_foco.transform.rotation, Quaternion.LookRotation(sobre.Value - Vector3.up * 0.6f - pos), k);
        }
        _foco.enabled = _foco.intensity > 0.01f;
    }

    void CrearFoco()
    {
        var go = new GameObject("Foco del hablante");
        go.transform.SetParent(transform, false);
        _foco = go.AddComponent<Light>();
        _foco.type = LightType.Spot;
        _foco.color = new Color(1f, 0.93f, 0.78f);
        _foco.range = 14f;
        _foco.spotAngle = 34f;
        _foco.innerSpotAngle = 18f;
        _foco.shadows = LightShadows.Soft;
        _foco.intensity = 0;
    }

    void CrearDesenfoque()
    {
        var go = new GameObject("Desenfoque (show)");
        go.transform.SetParent(transform, false);
        _volumen = go.AddComponent<Volume>();
        _volumen.isGlobal = true;
        _volumen.priority = 10;
        _volumen.weight = 0;
        var perfil = ScriptableObject.CreateInstance<VolumeProfile>();
        _dof = perfil.Add<DepthOfField>(true);
        _dof.mode.Override(DepthOfFieldMode.Gaussian);
        _dof.gaussianMaxRadius.Override(1.2f);
        _dof.highQualitySampling.Override(true);
        _volumen.profile = perfil;
    }

    // ================================================================ confeti

    void Confeti(Vector3 donde, int cantidad)
    {
        var go = new GameObject("Confeti");
        go.transform.position = donde + Vector3.up * 1.2f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        main.gravityModifier = 0.55f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(ShowUI.Dorado, 0), new GradientColorKey(ShowUI.Rojo, 0.33f), new GradientColorKey(new Color(0.3f, 0.6f, 1f), 0.66f), new GradientColorKey(ShowUI.Verde, 1) },
                     new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        main.startColor = new ParticleSystem.MinMaxGradient(grad) { mode = ParticleSystemGradientMode.RandomColor };
        var em = ps.emission;
        em.rateOverTime = 0;
        em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)cantidad) });
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 35f;
        sh.rotation = new Vector3(-90, 0, 0);
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (materialParticulas != null) r.sharedMaterial = materialParticulas;
        main.stopAction = ParticleSystemStopAction.Destroy;
        ps.Play();
    }

    // ================================================================ fotos de los personajes

    readonly Dictionary<string, Texture> _fotos = new();

    /// Retrato fijo de cada personaje (una sola vez al iniciar) para el marcador y los menús.
    IEnumerator TomarFotos()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        var go = new GameObject("Camara fotos");
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.fieldOfView = 22f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 400f;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

#if UNITY_EDITOR
        // En el editor los shaders se compilan en segundo plano: la primera vez que se abre el proyecto
        // (o tras importar modelos nuevos) las fotos salían vacías. Se compilan de forma síncrona aquí.
        bool asincrono = UnityEditor.ShaderUtil.allowAsyncCompilation;
        UnityEditor.ShaderUtil.allowAsyncCompilation = false;
#endif
        Vector3 pitch = escenario.barco.TransformPoint(escenario.PuntoPitch) + Vector3.up * 1.5f;
        for (int i = 0; i < escenario.sharks.Length; i++)
        {
            var sh = escenario.sharks[i];
            if (sh.personaje == null) continue;
            Vector3 c = escenario.CabezaShark(i);
            Fotografiar(cam, FormatoShow.Clave(sh.nombre), c, pitch - c, sh.altura);
        }
        Fotografiar(cam, "emprendedor", escenario.CabezaEmprendedor(), escenario.emprendedor.transform.forward, 1.78f);
#if UNITY_EDITOR
        UnityEditor.ShaderUtil.allowAsyncCompilation = asincrono;
#endif
        Destroy(go);
        _ui.Fotos(_fotos);
        var orden = escenario.sharks.Where(x => x.personaje != null)
            .OrderBy(x => { int k = Array.FindIndex(SharksPorDefecto, n => FormatoShow.Clave(n) == FormatoShow.Clave(x.nombre)); return k < 0 ? 99 : k; })
            .Select(x => (x.nombre, x.color)).ToList();
        _input.Fotos(_fotos, orden, escenario.colorEmprendedor);
    }

    void Fotografiar(Camera cam, string clave, Vector3 cabeza, Vector3 frente, float altura)
    {
        var rt = new RenderTexture(384, 384, 24, RenderTextureFormat.ARGB32) { name = "Foto " + clave, antiAliasing = 4 };
        rt.Create();
        var (pos, rot) = ShowUI.EncuadreRetrato(cabeza, frente, altura, clave);
        cam.transform.SetPositionAndRotation(pos, rot);
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;
        _fotos[clave] = rt;
    }

    // ================================================================ cámaras que no se quedan tapadas

    int _mascaraOclusion = ~0;
    Quaternion _despejeGiro = Quaternion.identity;
    float _despejeSubir, _despejeAcercar = 1f, _despejeHasta;

    /// Colisionadores (solo para las cámaras) en el barco, las sillas y los Sharks. El barco lleva un
    /// Rigidbody cinemático para que moverlo con las olas no sea costoso.
    void CrearColisionadores()
    {
        const int capa = 31;   // capa propia solo para estas colisiones
        _mascaraOclusion = 1 << capa;
        // (en Unity "??" no sirve con componentes: un componente faltante no es null de C#)
        if (!escenario.barco.TryGetComponent<Rigidbody>(out var rb)) rb = escenario.barco.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.None;

        Transform empTr = escenario.emprendedor != null ? escenario.emprendedor.transform : null;
        foreach (var mf in escenario.barco.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || (empTr != null && mf.transform.IsChildOf(empTr))) continue;
            if (mf.GetComponent<ParticleSystem>() != null) continue;
            AgregarColision(mf.gameObject, mf.sharedMesh, capa);
        }
        // modelos con esqueleto (Arlong): una malla horneada en su pose actual
        foreach (var smr in escenario.barco.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null || (empTr != null && smr.transform.IsChildOf(empTr))) continue;
            var m = new Mesh { name = smr.sharedMesh.name + " (colision)" };
            smr.BakeMesh(m, true);
            AgregarColision(smr.gameObject, m, capa);
        }
    }

    static void AgregarColision(GameObject go, Mesh malla, int capa)
    {
        // en un hijo propio, para no cambiar la capa del objeto que se dibuja
        var c = new GameObject("Colision camara");
        c.layer = capa;
        c.transform.SetParent(go.transform, false);
        var mc = c.AddComponent<MeshCollider>();
        mc.sharedMesh = malla;
    }

    /// Si algo tapa la vista entre la cámara y lo que enfoca, prueba otros ángulos alrededor;
    /// si ninguno está libre, acerca la cámara. La decisión se mantiene un rato para no titubear.
    Vector3 Despejar(Vector3 foco, Vector3 deseada)
    {
        Vector3 d = deseada - foco;
        float dist = d.magnitude;
        if (dist < 0.3f) return deseada;
        if (Time.time >= _despejeHasta)
        {
            _despejeHasta = Time.time + 0.5f;
            _despejeGiro = Quaternion.identity; _despejeSubir = 0f; _despejeAcercar = 1f;
            if (Tapado(foco, deseada))
            {
                bool listo = false;
                foreach (float subir in new[] { 0f, 0.35f, 0.7f })
                {
                    foreach (float ang in new[] { 20f, -20f, 40f, -40f, 65f, -65f, 95f, -95f })
                    {
                        var q = Quaternion.AngleAxis(ang, Vector3.up);
                        Vector3 cand = foco + q * d + Vector3.up * dist * subir;
                        if (!Tapado(foco, cand)) { _despejeGiro = q; _despejeSubir = subir; listo = true; break; }
                    }
                    if (listo) break;
                }
                if (!listo)
                {
                    Vector3 dir = d / dist;
                    if (Physics.SphereCast(foco + dir * 0.35f, 0.14f, dir, out var hit, dist - 0.35f, _mascaraOclusion, QueryTriggerInteraction.Ignore))
                        _despejeAcercar = Mathf.Clamp01((hit.distance + 0.35f - 0.25f) / dist);
                }
            }
        }
        Vector3 res = foco + _despejeGiro * d * _despejeAcercar + Vector3.up * dist * _despejeSubir;
        return res;
    }

    bool Tapado(Vector3 foco, Vector3 cam)
    {
        Vector3 d = cam - foco;
        float dist = d.magnitude;
        if (dist < 0.5f) return false;
        Vector3 dir = d / dist;
        // empieza un poco fuera de la cabeza de quien se enfoca para no chocar con su propio pelo
        return Physics.SphereCast(foco + dir * 0.35f, 0.14f, dir, out _, dist - 0.35f, _mascaraOclusion, QueryTriggerInteraction.Ignore);
    }

    // ================================================================ clavado de los que están "fuera"

    struct PoseOriginal { public Transform padre; public Vector3 pos, escala; public Quaternion rot; }
    readonly Dictionary<int, PoseOriginal> _alAgua = new();
    float _medioManga = -1f;

    /// Media manga del barco (en su espacio local), para saber cuánto hay que saltar para librar el casco.
    float MedioManga()
    {
        if (_medioManga > 0) return _medioManga;
        _medioManga = 5f;
        var modelo = escenario.barco.Find("Modelo Going Merry");
        if (modelo == null) return _medioManga;
        float max = 0;
        foreach (var r in modelo.GetComponentsInChildren<Renderer>())
        {
            var b = r.bounds;
            for (int k = 0; k < 8; k++)
            {
                var esquina = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                max = Mathf.Max(max, Mathf.Abs(escenario.barco.InverseTransformPoint(esquina).z));
            }
        }
        if (max > 0.5f) _medioManga = max * 0.8f;   // sin contar velas/vergas que sobresalen
        return _medioManga;
    }

    /// El Shark se levanta, corre hacia la borda y se avienta un clavado al mar.
    IEnumerator Clavado(int s)
    {
        var sh = escenario.sharks[s];
        var tr = sh.personaje;
        if (tr == null || _alAgua.ContainsKey(s)) yield break;
        _alAgua[s] = new PoseOriginal { padre = tr.parent, pos = tr.localPosition, rot = tr.localRotation, escala = tr.localScale };

        var barco = escenario.barco;
        Vector3 inicioLocal = barco.InverseTransformPoint(tr.position);
        float lado = inicioLocal.z >= 0 ? 1f : -1f;
        if (Mathf.Abs(inicioLocal.z) < 0.6f) lado = -Mathf.Sign(escenario.ladoEscalera.z == 0 ? -1f : escenario.ladoEscalera.z);   // el del centro salta por el lado sin escalera
        Vector3 haciaFuera = barco.TransformDirection(new Vector3(0, 0, lado)).normalized;
        Vector3 proa = barco.TransformDirection(Vector3.right).normalized;

        tr.SetParent(null, true);
        Vector3 inicio = tr.position;
        float alto = Mathf.Max(1f, sh.altura);
        Vector3 destinoLocal = new Vector3(inicioLocal.x, inicioLocal.y, lado * (MedioManga() + 2.5f + alto * 0.5f));
        Vector3 destino = barco.TransformPoint(destinoLocal);
        var oc = OceanWaves.Instance;
        float agua = oc != null ? oc.HeightAt(destino) : 0f;
        destino.y = agua;
        Quaternion mirarFuera = Quaternion.LookRotation(haciaFuera, Vector3.up);
        Quaternion rot0 = tr.rotation;

        // cámara fija desde el mar: encuadra la borda y el punto del chapuzón, y sigue un poco al que salta
        Vector3 borda = barco.TransformPoint(new Vector3(inicioLocal.x, inicioLocal.y + 1.2f, lado * MedioManga()));
        Vector3 centro = Vector3.Lerp(borda, destino, 0.45f) + Vector3.up * 0.5f;
        float lejos = 8f + alto * 2f;
        Vector3 camPos = centro + haciaFuera * lejos + proa * lejos * 0.6f;
        camPos.y = Mathf.Max(camPos.y + 2f, borda.y + 1.2f);   // por encima de la borda para ver la salida
        _ui.OcultarSubtitulo();
        _hablante = int.MinValue;
        Plano(() =>
        {
            Vector3 m = Vector3.Lerp(centro, tr.position + Vector3.up * alto * 0.4f, 0.65f);
            m.y = Mathf.Max(m.y, agua + 1.5f);
            return new EscenarioPitch.Plano(camPos, m, 40f);
        }, 0.6f);

        float T(float seg) => seg / Vel;
        // 1) se levanta y gira hacia el mar
        float t = 0, dur = T(0.55f);
        while (t < dur)
        {
            t += Time.deltaTime; float k = Mathf.SmoothStep(0, 1, t / dur);
            tr.position = inicio + Vector3.up * 0.35f * k;
            tr.rotation = Quaternion.Slerp(rot0, mirarFuera, k);
            yield return null;
        }
        // 2) salto en arco con voltereta de cabeza
        _voces.Efecto("silbido", 0.9f);
        Vector3 a = tr.position;
        float apice = Mathf.Max(a.y, barco.TransformPoint(new Vector3(inicioLocal.x, inicioLocal.y + 1.5f, lado * MedioManga())).y) + 2.2f + alto * 0.3f;
        Vector3 eje = Vector3.Cross(Vector3.up, haciaFuera).normalized;
        t = 0; dur = T(1.35f + alto * 0.12f);
        bool saltado = false;
        while (t < dur)
        {
            if (_saltar) { _saltar = false; saltado = true; t = dur; }
            t += Time.deltaTime; float k = Mathf.Clamp01(t / dur);
            Vector3 horiz = Vector3.Lerp(new Vector3(a.x, 0, a.z), new Vector3(destino.x, 0, destino.z), k);
            // parábola por tres alturas: salida, ápice y agua
            float y = (1 - k) * (1 - k) * a.y + 2 * (1 - k) * k * (2 * apice - 0.5f * (a.y + destino.y)) + k * k * (destino.y - alto * 0.3f);
            tr.position = new Vector3(horiz.x, y, horiz.z);
            float giro = Mathf.SmoothStep(0, 1, Mathf.Clamp01((k - 0.1f) / 0.85f)) * 160f;   // termina de cabeza
            tr.rotation = Quaternion.AngleAxis(giro, eje) * mirarFuera;
            yield return null;
        }
        // 3) chapuzón y se hunde
        destino.y = oc != null ? oc.HeightAt(destino) : destino.y;
        _voces.Efecto("splash");
        Salpicadura(destino, alto);
        Vector3 b0 = tr.position;
        t = 0; dur = saltado ? 0.01f : T(0.7f);
        while (t < dur)
        {
            t += Time.deltaTime; float k = Mathf.Clamp01(t / dur);
            tr.position = b0 + Vector3.down * (alto * 1.4f) * k;
            yield return null;
        }
        tr.gameObject.SetActive(false);
        yield return Esperar(0.9f);
    }

    void Salpicadura(Vector3 donde, float alto)
    {
        var go = new GameObject("Salpicadura");
        go.transform.position = donde + Vector3.up * 0.1f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.3f; main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f + alto, 7f + alto * 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.5f + alto * 0.1f);
        main.gravityModifier = 1.1f;
        main.startColor = new Color(0.92f, 0.97f, 1f, 0.9f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var em = ps.emission; em.rateOverTime = 0;
        em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)(140 + alto * 40)) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 22f; sh.radius = 0.35f + alto * 0.12f;
        sh.rotation = new Vector3(-90, 0, 0);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.8f, 0.9f, 1f), 1) },
                  new[] { new GradientAlphaKey(0.95f, 0), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (materialParticulas != null) r.sharedMaterial = materialParticulas;
        ps.Play();
    }

    /// Regresa a su silla a los Sharks que se aventaron al agua (al empezar otra sesión).
    void RegresarDelAgua()
    {
        foreach (var kv in _alAgua)
        {
            var tr = escenario.sharks[kv.Key].personaje;
            if (tr == null) continue;
            tr.gameObject.SetActive(true);
            tr.SetParent(kv.Value.padre, false);
            tr.localPosition = kv.Value.pos;
            tr.localRotation = kv.Value.rot;
            tr.localScale = kv.Value.escala;
        }
        _alAgua.Clear();
    }
}
