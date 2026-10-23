using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Screens and HUD for the town-square imposter game, laid out like Whisper of the Light:
// - Start screen: the title at the top and a PLAY button at the bottom, over the live scene.
// - Intro (ImposterHud.Intro.cs): villagers gossip in speech bubbles to set up the story, then the game starts.
// - HUD (ImposterHud.Meters.cs): TAKEN in the top left, a row of pips filling towards an eye as the imposter
//   spreads; HUNCHES in the top right, pointing hands spent on wrong accusations.
//   Hints (ImposterHud.Hint.cs): first-time tips and what just happened, typed out on a dark sticker card that
//   points at the part of the HUD it's about.
// - Talk prompt: a small "..." speech bubble above the villager you're standing next to, and the controls as
//   keycaps at the bottom centre of the screen ([SPACE] ASK  [F] ACCUSE) while you're next to someone.
// - End screen: the screen darkens, the title at the top, a PLAY AGAIN button at the bottom; the time and hunches
//   stay on top, the taken bar fades out of the title's way.
// Buttons, keycaps and the talk bubble are "stickers" in the style of Kenney's cursor icons (see StickerUI).
// Buttons and the talk bubble pop (scale) in and out; the keycaps fade, and are single baked images so they fade
// cleanly (with separate layers the shadow would show through the face).
// Everything is built in code on an 800 x 600 reference screen that scales with the window width.
[DefaultExecutionOrder(200)] // after the camera has moved, so the talk bubble sits exactly over its villager
public partial class ImposterHud : MonoBehaviour
{
    public const float SideInset = 28f;
    public const float TopInset = 22f;
    public const float FadeDuration = 0.65f;
    const float LabelSize = 13f;
    const float LabelHeight = 18f;
    const float LabelOpacity = 0.6f;
    const float TitleSize = 46f;
    const float HintSize = 15f;

    // Talk bubble over the villager's head, and the key hints at the bottom of the screen.
    const float BubbleWidth = 40f, BubbleHeight = 26f, BubbleRadius = 8f, BubbleOutline = 2.5f, BubbleDrop = 3f;
    const float TailWidth = 18f, TailHeight = 10f, TailTipRadius = 4f, DotSize = 5f, DotGap = 3f;
    // TailHeight counts the bit that merges into the box (its outline and edge margin); this much shows below it.
    const float TailLength = TailHeight - (BubbleOutline + 1.25f);
    const float PromptFade = 0.2f;   // key hints fade
    const float BubblePop = 0.25f;   // the talk bubble pops (scales) in and out, from the tip of its tail
    const float ArmTransition = 0.18f; // the bubble cross-fading to and from its black "!" state, with a little bounce
    const float TypeSpeed = 45f;   // hint characters per second
    const float KeyHeight = 22f, KeyRadius = 6f, KeyOutline = 2.5f, KeyDrop = 3f;
    const float KeyBarTextSize = 14f, KeyTextSize = 11f;

    // Buttons.
    const float ButtonHeight = 44f, ButtonRadius = 12f, ButtonOutline = 3f, ButtonDrop = 5f, ButtonTextSize = 18f;
    const float TintOpacity = 0.75f;

    // Fonts under Assets/Resources: "You're Gone" (Ray Larabie, CC0) for titles only, and National Park
    // (OFL) for the rest of the UI, SemiBold for labels, ExtraBold for hints, values, buttons and bubbles.
    public const string DefaultTitleFont = "Fonts/Youre Gone";
    public const string DefaultUiFont = "Fonts/NationalPark-SemiBold";
    public const string DefaultStrongFont = "Fonts/NationalPark-ExtraBold";

    // Set by PLAY AGAIN, so a restart goes straight into a new round.
    public static bool skipStartScreen;

    Font titleFont;
    Font uiFont;
    Font strongFont;
    Camera cam;
    RectTransform canvasRect;
    CanvasGroup hudGroup;

    RectTransform prompt;
    CanvasGroup bubbleGroup;
    Image bubble;
    Sprite bubbleSprite, bubbleArmedSprite;
    CanvasGroup armedGroup;   // the black "!" bubble, laid over the normal one and faded in when armed
    Text alert;
    bool bubbleArmed;
    float armedT;
    float armedChangedAt = -10f;
    bool bubbleShown;
    float bubbleT;   // 0 = gone, 1 = full size
    Transform bubbleTarget;   // the villager the bubble belongs to; it stays on them while fading out
    const float BubbleAboveFeet = 0.95f;
    CanvasGroup keyBar;
    GameObject askRow, armedRow;
    bool keyBarShown;
    bool roundOver;           // the end screen is up
    float lastPopAt = -10f;   // the talk bubble's pop sound, rate-limited
    const float PopSoundGap = 0.3f;

    public void Init(Font titleFont, Font uiFont, Font strongFont, Camera cam)
    {
        // Falls back to loading the default fonts directly, so the HUD keeps its fonts even if the scene
        // was built without them assigned.
        this.titleFont = titleFont != null ? titleFont : Resources.Load<Font>(DefaultTitleFont);
        this.uiFont = uiFont != null ? uiFont : Resources.Load<Font>(DefaultUiFont);
        this.strongFont = strongFont != null ? strongFont : Resources.Load<Font>(DefaultStrongFont);
        this.cam = cam;
        EnsureEventSystem();

        // Drawn above the end screen's dark tint.
        var canvas = MakeCanvas(gameObject, 20);
        canvasRect = (RectTransform)canvas.transform;
        hudGroup = gameObject.AddComponent<CanvasGroup>();
        hudGroup.interactable = false;
        hudGroup.blocksRaycasts = false;
        hudGroup.alpha = 0f;

        BuildPrompt(canvas.transform);
        BuildKeyBar(canvas.transform);
        BuildHint(canvas.transform);
    }

    // ---------- Start and end screens ----------

    public void ShowStartScreen(string title, System.Action onPlay)
    {
        var screen = new GameObject("Start Screen", typeof(RectTransform));
        var canvas = MakeCanvas(screen, 30);
        var group = screen.AddComponent<CanvasGroup>();

        float width = AddTitle(canvas.transform, title);
        RectTransform play = null;
        bool pressed = false;
        // Pressed in the game, or by the web page when it's clicked over the button (see BeginFromPage).
        System.Action begin = () =>
        {
            if (pressed) return;
            pressed = true;
            playButton = null;
            group.interactable = false;
            group.blocksRaycasts = false;
            onPlay();
            StartCoroutine(Pop(play, false, 0f));
            StartCoroutine(FadeOutAndDestroy(group, screen));
        };
        play = AddButton(canvas.transform, "PLAY", width, () => begin());
        playButton = play;
        beginFromPage = begin;
    }

    RectTransform playButton;       // the start screen's PLAY or the end screen's PLAY AGAIN, until it's pressed
    System.Action beginFromPage;

    // In a web build the page presses PLAY (or PLAY AGAIN) itself when it's clicked over the button: for PLAY it goes
    // fullscreen and wakes the sound there, which browsers only allow inside their own click handler. Sent by the
    // ImposterInTown template.
    // Only ever called by name from the page, so code stripping is told to keep it.
    [UnityEngine.Scripting.Preserve]
    public void BeginFromPage() => beginFromPage?.Invoke();

    // Where PLAY or PLAY AGAIN is, for the web page: as fractions of the screen from the bottom left, or nowhere once pressed.
    void TellPagePlayButton()
    {
        var area = new Rect();
        if (playButton != null)
        {
            var corners = new Vector3[4];
            playButton.GetWorldCorners(corners); // screen pixels, on a screen overlay canvas
            area = Rect.MinMaxRect(corners[0].x / Screen.width, corners[0].y / Screen.height,
                                   corners[2].x / Screen.width, corners[2].y / Screen.height);
        }
        WebPage.PlayButton(area);
    }

    // Already showing (PLAY AGAIN): the meters just ease from the last round's values to the new ones.
    public void ShowHudNow() => hudGroup.alpha = 1f;

    // The HUD fades in when play begins after the intro.
    public void FadeInHud() => StartCoroutine(FadeIn(hudGroup));

    public void ShowEndScreen(string title, string subtitle, System.Action onPlayAgain)
    {
        roundOver = true;
        HidePrompt();
        HideHint();
        HideTaken();

        // Sits under the HUD, so the unease and accusations stay visible on top of the tint.
        var screen = new GameObject("End Screen", typeof(RectTransform));
        var canvas = MakeCanvas(screen, 10);
        var group = screen.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        var tint = new GameObject("Tint", typeof(RectTransform)).AddComponent<Image>();
        tint.transform.SetParent(canvas.transform, false);
        Stretch(tint.rectTransform);
        tint.color = new Color(0f, 0f, 0f, TintOpacity);
        tint.raycastTarget = false;

        float width = AddTitle(canvas.transform, title, subtitle);
        RectTransform playAgain = null;
        bool pressed = false;
        // Pressed in the game, or by the web page (see BeginFromPage): the browser has just let go of the mouse,
        // and can miss the game's first click after that.
        System.Action again = () =>
        {
            if (pressed) return;
            pressed = true;
            playButton = null;
            skipStartScreen = true;
            onPlayAgain();
        };
        playAgain = AddButton(canvas.transform, "PLAY AGAIN", width, () => again());
        playButton = playAgain;
        beginFromPage = again;
        playAgain.localScale = Vector3.zero;
        StartCoroutine(FadeIn(group));
        StartCoroutine(Pop(playAgain, true, FadeDuration * 0.5f));
    }

    // Title at the top centre (each line centred), with an optional faded line under it.
    // Returns the width the buttons should match.
    float AddTitle(Transform parent, string title, string subtitle = null)
    {
        var titleText = MakeText(parent, title, titleFont, TitleSize, TextAnchor.UpperCenter);
        titleText.lineSpacing = 0.95f;
        float width = Mathf.Max(220f, titleText.preferredWidth);
        float height = Mathf.Max(56f, titleText.preferredHeight);
        Place(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -TopInset - 4f), new Vector2(width, height));

        if (!string.IsNullOrEmpty(subtitle))
        {
            var sub = MakeText(parent, subtitle, strongFont, HintSize, TextAnchor.UpperCenter);
            sub.color = new Color(1f, 1f, 1f, LabelOpacity);
            Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -TopInset - 4f - height - 10f), new Vector2(600f, 60f));
        }
        return width;
    }

    IEnumerator FadeOutAndDestroy(CanvasGroup group, GameObject screen)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / FadeDuration)
        {
            group.alpha = 1f - t;
            yield return null;
        }
        Destroy(screen);
    }

    // Buttons pop instead of fading: in with a little overshoot, out with a little swell first.
    IEnumerator Pop(RectTransform button, bool appear, float delay)
    {
        var group = button.GetComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        const float duration = 0.3f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            float s = appear ? EaseOutBack(t) : 1f - EaseInBack(t);
            button.localScale = Vector3.one * Mathf.Max(0f, s);
            yield return null;
        }
        button.localScale = appear ? Vector3.one : Vector3.zero;
        group.interactable = group.blocksRaycasts = appear;
    }

    static float EaseOutBack(float t) { const float c = 1.7f; t -= 1f; return 1f + (c + 1f) * t * t * t + c * t * t; }
    static float EaseInBack(float t) { const float c = 1.7f; return (c + 1f) * t * t * t - c * t * t; }

    IEnumerator FadeIn(CanvasGroup group)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / FadeDuration)
        {
            group.alpha = t;
            yield return null;
        }
        group.alpha = 1f;
    }

    // ---------- HUD ----------

    // A small "..." bubble pops up above the villager's head, and the key hints fade in at the bottom of the screen.
    // Armed (about to accuse): the bubble turns black with a "!", and the hint asks to press F again.
    public void ShowPrompt(Transform villager, bool armed)
    {
        // Pops up over someone new; not more often than PopSoundGap, so weaving through a crowd doesn't tick.
        if (!bubbleShown && Time.unscaledTime - lastPopAt >= PopSoundGap)
        {
            lastPopAt = Time.unscaledTime;
            GameAudio.Play("drop_003", 0.5f);
        }
        if (armed && !bubbleArmed) GameAudio.Play("switch_002", 0.7f);
        bubbleTarget = villager;
        if (armed != bubbleArmed) armedChangedAt = Time.unscaledTime;
        bubbleArmed = armed;
        bubbleShown = keyBarShown = true;
        askRow.SetActive(!armed);
        armedRow.SetActive(armed);
    }

    // The key hints fade out and the bubble shrinks away, following its villager until it's gone.
    public void HidePrompt()
    {
        bubbleShown = keyBarShown = false;
    }

    void Update()
    {
        TellPagePlayButton();
        if (keyBar == null) return;
        keyBar.alpha = Mathf.MoveTowards(keyBar.alpha, keyBarShown ? 1f : 0f, Time.unscaledDeltaTime / PromptFade);
        // Pops like the buttons: in with a little overshoot, out with a little swell first.
        bubbleT = Mathf.MoveTowards(bubbleT, bubbleShown ? 1f : 0f, Time.unscaledDeltaTime / BubblePop);
        float scale = bubbleShown ? EaseOutBack(bubbleT) : 1f - EaseInBack(1f - bubbleT);
        // Arming cross-fades to the black "!" bubble with a quick bounce, rather than swapping instantly.
        armedT = Mathf.MoveTowards(armedT, bubbleArmed ? 1f : 0f, Time.unscaledDeltaTime / ArmTransition);
        armedGroup.alpha = armedT;
        float since = (Time.unscaledTime - armedChangedAt) / (ArmTransition * 1.5f);
        float bounce = since < 1f ? 1f + 0.18f * Mathf.Sin(since * Mathf.PI) : 1f;
        prompt.localScale = Vector3.one * Mathf.Max(0f, scale) * bounce;
        bubbleGroup.alpha = bubbleT > 0f ? 1f : 0f;
        FollowBubbleTarget();

        UpdateHint();
        UpdateMeters();
        UpdateIntro();
    }

    // Typewriter: the rest of the message is there but clear, so the lines don't shift as it types.
    static string Typed(string full, float startedAt)
    {
        int shown = Mathf.Clamp(Mathf.FloorToInt((Time.time - startedAt) * TypeSpeed), 0, full.Length);
        return shown < full.Length ? full.Substring(0, shown) + "<color=#00000000>" + full.Substring(shown) + "</color>" : full;
    }

    void FollowBubbleTarget()
    {
        if (bubbleTarget == null) return;
        if (!bubbleShown && bubbleT <= 0f)
        {
            // Gone: the next bubble starts fresh in its normal state.
            bubbleTarget = null;
            bubbleArmed = false;
            armedT = 0f;
            return;
        }
        Vector3 screen = cam.WorldToScreenPoint(bubbleTarget.position + Vector3.up * BubbleAboveFeet);
        if (screen.z <= 0f) { bubbleGroup.alpha = 0f; return; } // behind the camera
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
        prompt.anchoredPosition = local;
    }

    // A small label in a top corner.
    Text AddLabel(Transform parent, string label, Vector2 corner, TextAnchor alignment, float x)
    {
        var labelText = MakeText(parent, label, uiFont, LabelSize, alignment);
        Place(labelText.rectTransform, corner, new Vector2(x, -TopInset), new Vector2(220f, LabelHeight));
        return labelText;
    }

    void BuildPrompt(Transform parent)
    {
        bubbleSprite = StickerUI.BubbleSprite(BubbleWidth, BubbleHeight, BubbleRadius, BubbleOutline, BubbleDrop,
            TailWidth, TailLength, TailTipRadius, true, DotSize, DotGap, false);
        bubbleArmedSprite = StickerUI.BubbleSprite(BubbleWidth, BubbleHeight, BubbleRadius, BubbleOutline, BubbleDrop,
            TailWidth, TailLength, TailTipRadius, false, DotSize, DotGap, true);

        prompt = new GameObject("Talk Bubble", typeof(RectTransform)).GetComponent<RectTransform>();
        prompt.SetParent(parent, false);
        prompt.anchorMin = prompt.anchorMax = new Vector2(0.5f, 0.5f);
        prompt.pivot = bubbleSprite.pivot / bubbleSprite.rect.size; // the tail's tip sits on the point it's given
        prompt.sizeDelta = new Vector2(BubbleWidth, BubbleHeight + TailLength + BubbleDrop);
        bubbleGroup = prompt.gameObject.AddComponent<CanvasGroup>();
        bubbleGroup.alpha = 0f;
        bubbleGroup.blocksRaycasts = false;

        bubble = new GameObject("Bubble", typeof(RectTransform)).AddComponent<Image>();
        bubble.transform.SetParent(prompt, false);
        Stretch(bubble.rectTransform);
        bubble.sprite = bubbleSprite;
        bubble.raycastTarget = false;

        // The armed version on top: black bubble with a white "!" in the box part, faded in when armed.
        var armed = new GameObject("Armed", typeof(RectTransform)).AddComponent<Image>();
        armed.transform.SetParent(prompt, false);
        Stretch(armed.rectTransform);
        armed.sprite = bubbleArmedSprite;
        armed.raycastTarget = false;
        armedGroup = armed.gameObject.AddComponent<CanvasGroup>();
        armedGroup.alpha = 0f;
        alert = MakeText(armed.transform, "!", strongFont, 18f, TextAnchor.MiddleCenter);
        Stretch(alert.rectTransform);
        alert.rectTransform.offsetMin = new Vector2(0f, TailLength + BubbleDrop);
    }

    // The controls at the bottom centre, shown while you're next to a villager.
    void BuildKeyBar(Transform parent)
    {
        var bar = new GameObject("Key Hints", typeof(RectTransform)).GetComponent<RectTransform>();
        bar.SetParent(parent, false);
        Place(bar, new Vector2(0.5f, 0f), new Vector2(0f, SideInset), new Vector2(600f, KeyHeight + KeyDrop));
        keyBar = bar.gameObject.AddComponent<CanvasGroup>();
        keyBar.alpha = 0f;

        askRow = BuildRow(bar, null, ("SPACE", "ASK"), ("F", "ACCUSE"));

        armedRow = BuildRow(bar, null, ("F", "AGAIN TO ACCUSE"));
        armedRow.SetActive(false);
    }

    // A centred row of [KEY] action pairs: keycap stickers with white labels.
    GameObject BuildRow(Transform parent, System.Collections.Generic.List<(RectTransform root, RectTransform face, float width, Text label)> keysOut,
                        params (string key, string action)[] items)
    {
        const float keyGap = 7f, itemGap = 22f;
        var row = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(parent, false);
        Stretch(row);

        var parts = new System.Collections.Generic.List<(RectTransform key, float keyWidth, Text label)>();
        float width = 0f;
        foreach (var (key, action) in items)
        {
            var keyImage = StickerUI.FlatPanel(row, "Key " + key, KeyRadius, KeyOutline, KeyDrop, KeyHeight, out var keyFace);
            var keyRoot = keyImage.rectTransform;
            var keyText = MakeText(keyFace, key, strongFont, KeyTextSize, TextAnchor.MiddleCenter);
            keyText.color = Color.black;
            Stretch(keyText.rectTransform);
            float keyWidth = Mathf.Max(KeyHeight, keyText.preferredWidth + 14f);
            CenterGlyphs(keyText, new Vector2(keyWidth, KeyHeight)); // in the white of the cap

            var label = MakeText(row, action, strongFont, KeyBarTextSize, TextAnchor.MiddleLeft);
            parts.Add((keyRoot, keyWidth, label));
            keysOut?.Add((keyRoot, keyFace, keyWidth, label));
            width += (width > 0f ? itemGap : 0f) + keyWidth + keyGap + label.preferredWidth;
        }

        float x = -width / 2f;
        foreach (var (keyRoot, keyWidth, label) in parts)
        {
            Place(keyRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(keyWidth, KeyHeight + KeyDrop));
            keyRoot.pivot = new Vector2(0f, 0.5f);
            keyRoot.anchoredPosition = new Vector2(x, 0f); // face centred at KeyDrop/2, level with the labels
            x += keyWidth + keyGap;
            float labelWidth = label.preferredWidth;
            Place(label.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(labelWidth + 2f, KeyHeight));
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.anchoredPosition = new Vector2(x, KeyDrop / 2f);
            CenterGlyphs(label, new Vector2(labelWidth + 2f, KeyHeight)); // level with the key's letters
            x += labelWidth + itemGap;
        }
        return row.gameObject;
    }

    // ---------- Shared helpers ----------

    // Legacy Text centres the whole line (room for ascenders and descenders), so all-caps words sit low in their
    // box. Moves `text` so its letters themselves are centred in a box of `extents`.
    static void CenterGlyphs(Text text, Vector2 extents)
    {
        var generator = new TextGenerator();
        generator.Populate(text.text, text.GetGenerationSettings(extents));
        float min = float.MaxValue, max = float.MinValue;
        var verts = generator.verts; // four corners per character, relative to the text's pivot
        for (int i = 0; i + 3 < verts.Count; i += 4)
        {
            float bottom = Mathf.Min(verts[i].position.y, verts[i + 2].position.y);
            float top = Mathf.Max(verts[i].position.y, verts[i + 2].position.y);
            if (top - bottom < 0.01f) continue; // spaces
            min = Mathf.Min(min, bottom);
            max = Mathf.Max(max, top);
        }
        if (min > max) return;
        text.rectTransform.anchoredPosition -= new Vector2(0f, (min + max) / 2f / text.pixelsPerUnit);
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    // Screen space canvas that scales like Whisper of the Light's.
    static Canvas MakeCanvas(GameObject owner, int sortingOrder)
    {
        var canvas = owner.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = owner.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 600);
        scaler.matchWidthOrHeight = 0f;
        owner.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    // Plain white text, no shadow.
    static Text MakeText(Transform parent, string content, Font font, float size, TextAnchor alignment)
    {
        var text = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
        text.transform.SetParent(parent, false);
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = Mathf.RoundToInt(size);
        text.color = Color.white;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    // A white sticker button with black text (at the bottom centre unless told otherwise); lifts on hover,
    // presses onto its shadow.
    RectTransform AddButton(Transform parent, string content, float width, UnityEngine.Events.UnityAction onClick,
                            Vector2? anchor = null, Vector2? position = null)
    {
        var face = StickerUI.Panel(parent, content + " Button", ButtonRadius, ButtonOutline, ButtonDrop, out var root);
        Place(root, anchor ?? new Vector2(0.5f, 0f), position ?? new Vector2(0f, SideInset + ButtonDrop), new Vector2(width, ButtonHeight));
        face.raycastTarget = true;

        var button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = face;
        button.onClick.AddListener(onClick);

        var label = MakeText(face.transform, content, strongFont, ButtonTextSize, TextAnchor.MiddleCenter);
        label.color = Color.black;
        Stretch(label.rectTransform);

        var style = root.gameObject.AddComponent<StickerButton>();
        style.face = face.rectTransform;
        style.press = ButtonDrop;

        // Not faded with its screen: face and shadow are separate layers, so it pops in and out instead.
        root.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
        return root;
    }

    // Anchor and pivot at the same point.
    static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
