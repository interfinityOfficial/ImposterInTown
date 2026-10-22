using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// A full-screen fade through black, kept across scene loads, so PLAY AGAIN doesn't jump from the end screen's
// lighting to a fresh daytime square: the screen fades to black, `whileBlack` runs (the scene reload), then it
// fades back in on the new round. All sound fades with it (the listener's volume), so the end screen's music and
// jingle go down with the picture instead of cutting off when the scene reloads, and the new round fades up.
public class ScreenFade : MonoBehaviour
{
    const float OutTime = 0.45f, InTime = 0.7f;

    static ScreenFade instance;
    CanvasGroup group;

    public static bool Busy => instance != null && instance.running;
    bool running;

    public static void Through(System.Action whileBlack)
    {
        if (Busy) return;
        if (instance == null) instance = Create();
        instance.StartCoroutine(instance.Run(whileBlack));
    }

    IEnumerator Run(System.Action whileBlack)
    {
        running = true;
        group.blocksRaycasts = true; // no clicks or second presses mid-fade
        for (float t = group.alpha; t < 1f; t += Time.unscaledDeltaTime / OutTime)
        {
            group.alpha = Smooth(t);
            AudioListener.volume = 1f - group.alpha;
            yield return null;
        }
        group.alpha = 1f;
        AudioListener.volume = 0f;

        // A scene load only happens at the end of the frame, and its first frames are long (loading, baking
        // sprites). Wait until it has actually loaded and settled, then fade in with capped steps, so a long
        // frame can't skip the fade and cut straight to the scene.
        bool loaded = false;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (scene, mode) => loaded = true;
        SceneManager.sceneLoaded += onLoaded;
        whileBlack();
        for (float waited = 0f; !loaded && waited < 5f; waited += Time.unscaledDeltaTime) yield return null;
        SceneManager.sceneLoaded -= onLoaded;
        for (int i = 0; i < 3; i++) yield return null;

        for (float t = 0f; t < 1f; t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f) / InTime)
        {
            group.alpha = 1f - Smooth(t);
            AudioListener.volume = 1f - group.alpha;
            yield return null;
        }
        group.alpha = 0f;
        AudioListener.volume = 1f;
        group.blocksRaycasts = false;
        running = false;
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    static ScreenFade Create()
    {
        var go = new GameObject("Screen Fade");
        DontDestroyOnLoad(go);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // above every other screen
        go.AddComponent<GraphicRaycaster>();

        var black = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
        black.transform.SetParent(go.transform, false);
        var rect = black.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        black.color = Color.black;

        var fade = go.AddComponent<ScreenFade>();
        fade.group = go.AddComponent<CanvasGroup>();
        fade.group.alpha = 0f;
        fade.group.blocksRaycasts = false;
        return fade;
    }
}
