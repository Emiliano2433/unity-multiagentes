using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// Formato de los eventos que publica el backend (backend/eventos_unity.py) en
/// GET /eventos?desde=N  ->  {"sesion": "...", "eventos": [ {...}, ... ]}
/// JsonUtility ignora los campos que no están aquí (p. ej. preguntas_por_shark).
[Serializable]
public class RespuestaEventos
{
    public string sesion;
    public Evento[] eventos;
}

[Serializable]
public class Evento
{
    public int seq;
    public int id;
    public string tipo;        // inicio_sesion | esperando | pitch | inicio_vuelta | turno | pregunta | pasar | respuesta
                               // inicio_etapa | recomendacion | decision | eleccion | fin
    public string etapa;       // pitch | preguntas | deliberacion | retroalimentacion | ofertas | eleccion
    public string emisor;
    public string contenido;
    public int vuelta;
    public int responde_a;
    public string interes;     // ninguno | bajo | medio | alto
    public bool dentro;
    public Oferta oferta;
    public string shark_elegido;
    public string shark;       // en "esperando" (preguntas): a qué Shark le responde
    public string pregunta;    // en "esperando" (preguntas): el texto de la pregunta
    public Opcion[] opciones;  // en "esperando" (eleccion): ofertas disponibles
    // inicio_sesion
    public string emprendedor;
    public string[] sharks;
    public string modo;
    public float monto;
    public float porcentaje;
    // fin
    public Resumen resumen;
}

[Serializable]
public class Oferta
{
    public string tipo;        // completa | cercana | pequeña
    public float monto;
    public float porcentaje;
}

[Serializable]
public class Opcion
{
    public string shark;
    public string tipo;
    public float monto;
    public float porcentaje;
}

// ---- Peticiones que Unity manda al backend (POST)
[Serializable] public class PeticionSesion { public string emprendedor; public string pitch; public double monto; public double porcentaje; }
[Serializable] public class PeticionRespuesta { public string texto; }
[Serializable] public class PeticionEleccion { public string shark; }
[Serializable] public class RespuestaError { public string error; }

[Serializable]
public class Resumen
{
    public int vueltas_jugadas;
    public bool fin_por_consenso;
    public int total_preguntas;
    public string[] dentro;
    public string[] fuera;
    public string eleccion;
}

/// Archivo de corrida guardado por el backend (backend/corridas/*.json) — se usa para el modo demo.
[Serializable]
public class Corrida
{
    public string emprendedor;
    public string modo_llm;
    public string[] sharks;
    public Evento[] historial;
    public Resumen verificacion;
}

public static class FormatoShow
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Dinero(float monto) => "$" + monto.ToString("N0", Inv);
    public static string Porcentaje(float p) => p.ToString("0.##", Inv) + "%";

    public static string Oferta(Oferta o) =>
        o == null || o.monto <= 0 ? "sin oferta" : $"{Dinero(o.monto)} por el {Porcentaje(o.porcentaje)}  (oferta {o.tipo})";

    /// "luffy", "Whitebeard", "Barbablanca" -> clave comparable sin acentos ni mayúsculas.
    public static string Clave(string nombre)
    {
        if (string.IsNullOrEmpty(nombre)) return "";
        var sb = new StringBuilder();
        foreach (char c in nombre.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        string k = sb.ToString();
        return k == "whitebeard" || k == "barbablanca" || k == "edwardnewgate" ? "barbablanca" : k;
    }

    /// Parte un texto largo en páginas que caben en el cuadro de subtítulos, cortando en
    /// finales de oración o, si no hay, en espacios.
    public static List<string> Paginar(string texto, int maxChars = 330)
    {
        var paginas = new List<string>();
        texto = (texto ?? "").Trim();
        while (texto.Length > maxChars)
        {
            int corte = -1;
            for (int i = maxChars; i > maxChars / 2; i--)
                if (".!?\n".IndexOf(texto[i - 1]) >= 0 && (i == texto.Length || char.IsWhiteSpace(texto[i]))) { corte = i; break; }
            if (corte < 0) corte = texto.LastIndexOf(' ', maxChars);
            if (corte <= 0) corte = maxChars;
            paginas.Add(texto.Substring(0, corte).Trim());
            texto = texto.Substring(corte).Trim();
        }
        if (texto.Length > 0 || paginas.Count == 0) paginas.Add(texto);
        return paginas;
    }
}
