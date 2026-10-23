using UnityEngine;

// What the web page around a web build needs to know (Assets/Plugins/WebGL/pageBridge.jslib and the ImposterInTown
// web template): whether the game wants the mouse captured, and where the PLAY or PLAY AGAIN button is. And one thing the game
// asks the page: whether the browser's sound is running yet. Browsers only allow
// capturing the mouse, going fullscreen and starting sound inside the page's own click handler, so the page does
// those itself. Does nothing outside a web build.
public static class WebPage
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    static extern void ImposterWantsPointerLock(int on);
    [System.Runtime.InteropServices.DllImport("__Internal")]
    static extern void ImposterPlayButton(float x, float y, float width, float height);
    [System.Runtime.InteropServices.DllImport("__Internal")]
    static extern int ImposterAudioRunning();
#endif

    // Browsers hold sound back until the player first clicks; until then sounds are queued, and the queue doesn't
    // replay scheduled starts correctly (MusicPlayer waits for this). Always true outside a web build.
    public static bool AudioRunning
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ImposterAudioRunning() != 0;
#else
            return true;
#endif
        }
    }

    static bool? toldLock;
    static Rect toldButton;

    public static void WantsPointerLock(bool on)
    {
        if (toldLock == on) return;
        toldLock = on;
#if UNITY_WEBGL && !UNITY_EDITOR
        ImposterWantsPointerLock(on ? 1 : 0);
#endif
    }

    // As fractions of the screen from the bottom left; an empty rect once there's no button to press.
    public static void PlayButton(Rect area)
    {
        if (area == toldButton) return;
        toldButton = area;
#if UNITY_WEBGL && !UNITY_EDITOR
        ImposterPlayButton(area.x, area.y, area.width, area.height);
#endif
    }
}
