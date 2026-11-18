using UnityEditor;
using UnityEditor.SceneManagement;

/// Abre la escena principal (Going Merry) al iniciar el editor.
/// Solo actúa una vez por sesión y solo si la escena actual está vacía (sin guardar)
/// o es la SampleScene, para no cerrarte otra escena en la que estés trabajando.
[InitializeOnLoad]
public static class AbrirEscenaPrincipal
{
    const string EscenaPrincipal = "Assets/Scenes/GoingMerry_Arlong.unity";
    const string SampleScene     = "Assets/Scenes/SampleScene.unity";
    const string Flag            = "OP_EscenaPrincipalAbierta";

    static AbrirEscenaPrincipal()
    {
        EditorApplication.delayCall += Abrir;
    }

    static void Abrir()
    {
        if (SessionState.GetBool(Flag, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Abrir;
            return;
        }
        SessionState.SetBool(Flag, true);

        var actual = EditorSceneManager.GetActiveScene();
        if (actual.path == EscenaPrincipal) return;
        if (!string.IsNullOrEmpty(actual.path) && actual.path != SampleScene) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(EscenaPrincipal) == null) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EditorSceneManager.OpenScene(EscenaPrincipal);
    }
}
