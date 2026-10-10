using UnityEngine;

namespace BattleBall.Net
{
    /// <summary>
    /// Device settings that must be made in code (owner report 2026-10-10, Android: "fps thấp loanh quanh 30"): Unity runs a mobile game at 30 frames a second unless
    /// Application.targetFrameRate says otherwise (vSyncCount is ignored on Android), so the frame rate is set here, once, before the first scene. Also: the screen stays
    /// on while the game runs, and the game lies on its side (landscape, either way up): the whole interface is laid out for 1280 x 720.
    /// </summary>
    public static class BbPerformance
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = BbConfig.Current.targetFrameRate;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }
    }
}
