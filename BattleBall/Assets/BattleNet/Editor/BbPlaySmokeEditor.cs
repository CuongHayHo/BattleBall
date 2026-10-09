using UnityEditor;
using UnityEditor.SceneManagement;

namespace BattleBall.EditorTools
{
    /// <summary>Starts the scripted Play mode run of BbPlaySmoke (batch mode, without -quit): the first scene of the build is opened and played; the run quits the Editor itself.</summary>
    public static class BbPlaySmokeEditor
    {
        public static void Enter()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length > 0) EditorSceneManager.OpenScene(scenes[0].path);
            EditorApplication.EnterPlaymode();
        }
    }
}
