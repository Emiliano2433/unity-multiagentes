using System.IO;
using UnityEditor;

/// Prueba automática del show (solo editor): si existe Temp/op_play, entra en Play cuando
/// terminan la compilación y la reconstrucción de la escena. Con Temp/op_capturas además,
/// el ShowDirector guarda capturas en Logs/show_###.png (ver ShowDirector.CapturasDePrueba).
[InitializeOnLoad]
public static class PruebaShow
{
    const string Marca = "Temp/op_play";

    static double _siguiente;

    static PruebaShow()
    {
        EditorApplication.update += Esperar;   // revisa la marca una vez por segundo
    }

    static void Esperar()
    {
        if (EditorApplication.timeSinceStartup < _siguiente) return;
        _siguiente = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Marca)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || File.Exists("Temp/op_rebuild")) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Marca);
        EditorApplication.isPlaying = true;
    }
}
