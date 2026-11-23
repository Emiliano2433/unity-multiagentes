using System.IO;
using UnityEditor;

/// Prueba automática del show (solo editor): si existe Temp/op_play, entra en Play cuando
/// terminan la compilación y la reconstrucción de la escena. Con Temp/op_capturas además,
/// el ShowDirector guarda capturas en Logs/show_###.png (ver ShowDirector.CapturasDePrueba).
[InitializeOnLoad]
public static class PruebaShow
{
    const string Marca = "Temp/op_play";

    static PruebaShow()
    {
        if (File.Exists(Marca)) EditorApplication.update += Esperar;
    }

    static void Esperar()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || File.Exists("Temp/op_rebuild")) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.update -= Esperar; return; }
        EditorApplication.update -= Esperar;
        File.Delete(Marca);
        EditorApplication.isPlaying = true;
    }
}
