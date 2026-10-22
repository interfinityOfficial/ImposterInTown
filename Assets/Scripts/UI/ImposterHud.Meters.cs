using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The HUD: one row across the top, icons instead of labels (Kenney cursor pack, Assets/Resources/Icons):
//   top left:  [hourglass] 02:31                top right:  [eye] [=====------] 2 / 10   [Q][Q][Q]
// - time to midnight; fades in and out for the last stretch, with the bell
// - taken: a sticker bar whose dark inner bar grows as the imposter turns villagers; full = the town is lost
// - hunches: magnifying glasses, spent ones greyed out from the right
// The eye pulses when the town is nearly lost. Each change gives its meter a little pop, so a conversion
// out of view still gets noticed. What each one means is taught by the first-time tips, not by labels.
public partial class ImposterHud
{
    const float BarWidth = 150f, BarHeight = 18f, BarRadius = 8f, BarOutline = 2.5f, BarDrop = 3f;
    const float FillGap = 2f;   // white space between the outline and the dark inner bar
    const float GlassSize = 20f, GlassGap = 3f;
    const float RowIcon = GlassSize, IconGap = 6f;        // the hourglass / eye, the same size as the glasses
    const float RowY = -TopInset - 12f;                   // centre of the row
    const float GroupGap = 18f;                           // space between the taken count and the hunches
    const float CountGap = 10f;                           // space between the bar and the taken count
    const float EyeSize = 24f, EyeGap = 8f;               // the eye before the bar, a touch bigger than the other icons
    const float PopTime = 0.35f;
    const float FillSpeed = 1.5f;                            // bar fill catches up at this many "full bars" per second
    static readonly Color BarInk = new Color(0.13f, 0.11f, 0.16f);

    Text clockText;
    bool clockUrgent;
    RectTransform clockIcon;
    CanvasGroup takenGroup;   // the eye, bar and count, faded out together on the end screen
    bool takenShown = true;
    RectTransform takenBar, eye;
    Text takenCount;
    Image takenFill;
    float fillAmount;
    float fillMaxWidth, fillMinWidth;
    float barPopAt = -10f;
    int takenMax;
    readonly List<Image> glasses = new List<Image>();
    readonly List<float> glassPopAt = new List<float>();
    int shownTaken = -1, shownHunches = -1;

    // The last round's numbers, kept over the scene reload on PLAY AGAIN so the meters carry on from where they
    // were and ease to the new round's values instead of starting blank.
    static int carriedTaken = -1, carriedHunches = -1;
    static bool carriedTakenHidden;

    public void RememberStats()
    {
        carriedTaken = shownTaken;
        carriedHunches = shownHunches;
        carriedTakenHidden = !takenShown;
    }

    // The end screen doesn't need the taken bar (its title says how it went), so it fades out of the title's way.
    public void HideTaken() => takenShown = false;

    // Called once the game knows its numbers: how many can be taken before losing, and how many hunches.
    public void InitMeters(int takenMax, int hunchesMax)
    {
        this.takenMax = Mathf.Max(1, takenMax);
        var parent = canvasRect;

        // Hourglass and time to midnight.
        float x = SideInset;
        var hourglass = IconImage(parent, "Hourglass", Icon("hourglass"), RowIcon);
        clockIcon = hourglass.rectTransform;
        PlaceCentred(clockIcon, new Vector2(x + RowIcon / 2f, RowY));
        x += RowIcon + IconGap;
        clockText = MakeText(parent, "00:00", strongFont, HintSize, TextAnchor.MiddleLeft); // same size as the hints under it
        float clockWidth = clockText.preferredWidth + 2f; // widest it gets, so nothing shifts as it counts down
        Place(clockText.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(clockWidth, 30f));
        clockText.rectTransform.pivot = new Vector2(0f, 0.5f);
        clockText.rectTransform.anchoredPosition = new Vector2(x, RowY);

        // Top right, laid out from the corner inwards: hunches, the taken count, the bar, then the eye.
        var topRight = new Vector2(1f, 1f);
        float glassesWidth = hunchesMax * GlassSize + (hunchesMax - 1) * GlassGap;
        float countEnd = -SideInset - glassesWidth - GroupGap;

        var takenHolder = new GameObject("Taken", typeof(RectTransform)).GetComponent<RectTransform>();
        takenHolder.SetParent(parent, false);
        Stretch(takenHolder);
        takenGroup = takenHolder.gameObject.AddComponent<CanvasGroup>();
        parent = takenHolder;

        takenCount = MakeText(parent, $"{takenMax} / {takenMax}", strongFont, HintSize, TextAnchor.MiddleLeft);
        float countWidth = takenCount.preferredWidth + 2f; // widest it gets, so nothing shifts as it changes
        takenCount.rectTransform.anchorMin = takenCount.rectTransform.anchorMax = topRight;
        takenCount.rectTransform.pivot = new Vector2(1f, 0.5f);
        takenCount.rectTransform.sizeDelta = new Vector2(countWidth, 30f);
        takenCount.rectTransform.anchoredPosition = new Vector2(countEnd, RowY);
        float barEnd = countEnd - countWidth - CountGap;

        var bar = StickerUI.FlatPanel(parent, "Taken Bar", BarRadius, BarOutline, BarDrop, BarHeight, out var barFace);
        takenBar = bar.rectTransform;
        takenBar.anchorMin = takenBar.anchorMax = topRight;
        takenBar.pivot = new Vector2(1f, 0.5f); // pops outward from its right end
        takenBar.sizeDelta = new Vector2(BarWidth, BarHeight + BarDrop);
        takenBar.anchoredPosition = new Vector2(barEnd, RowY - BarDrop / 2f); // the face, not the shadow, centred on the row

        var eyeImage = IconImage(parent, "Eye", Icon("eye"), EyeSize);
        eye = eyeImage.rectTransform;
        PlaceCentred(eye, new Vector2(barEnd - BarWidth - EyeGap - EyeSize / 2f, RowY), topRight);

        // The inner bar: a dark pill inset from the outline, rounded at both ends, growing from the left.
        float inset = BarOutline + 0.5f + FillGap;
        float fillHeight = BarHeight - 2f * inset;
        fillMaxWidth = BarWidth - 2f * inset;
        fillMinWidth = fillHeight; // even one taken shows as a full round dot
        takenFill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
        takenFill.transform.SetParent(barFace, false);
        var fill = takenFill.rectTransform;
        fill.anchorMin = fill.anchorMax = new Vector2(0f, 0.5f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.anchoredPosition = new Vector2(inset, 0f);
        fill.sizeDelta = new Vector2(0f, fillHeight);
        takenFill.sprite = StickerUI.RoundedSprite(fillHeight / 2f, 0f);
        takenFill.type = Image.Type.Sliced;
        takenFill.color = BarInk;
        takenFill.raycastTarget = false;
        takenFill.enabled = false;

        // Hunches to the right of the bar, in the corner; spent ones grey out from the right.
        parent = canvasRect;
        var glassSprite = Icon("zoom");
        for (int i = 0; i < hunchesMax; i++)
        {
            var glass = IconImage(parent, "Hunch", glassSprite, GlassSize);
            float fromCorner = (hunchesMax - 1 - i) * (GlassSize + GlassGap) + GlassSize / 2f;
            PlaceCentred(glass.rectTransform, new Vector2(-SideInset - fromCorner, RowY), topRight);
            glasses.Add(glass);
            glassPopAt.Add(-10f);
        }

        if (carriedTaken >= 0)
        {
            SetStats(carriedTaken, carriedHunches);
            fillAmount = shownTaken / (float)this.takenMax;
            carriedTaken = carriedHunches = -1;
            if (carriedTakenHidden) takenGroup.alpha = 0f; // was hidden by the end screen: fades back in
            carriedTakenHidden = false;
        }
    }

    // Seconds left until midnight; urgent = the final stretch, when it fades in and out.
    public void SetClock(float secondsLeft, bool urgent)
    {
        int s = Mathf.CeilToInt(secondsLeft);
        clockText.text = (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        clockUrgent = urgent;
    }

    public void SetStats(int taken, int hunches)
    {
        taken = Mathf.Clamp(taken, 0, takenMax);
        if (taken != shownTaken)
        {
            takenCount.text = $"{taken} / {takenMax}";
            if (shownTaken >= 0) barPopAt = Time.unscaledTime;
            // A low thump when the imposter takes someone (not when the meters carry over into a new round).
            // Not for the conversion that ends the round: the losing jingle has that moment.
            if (shownTaken >= 0 && taken > shownTaken && Time.timeSinceLevelLoad > 1f && !roundOver) GameAudio.Play("drop_004", 0.7f);
            shownTaken = taken;
        }

        hunches = Mathf.Clamp(hunches, 0, glasses.Count);
        if (hunches != shownHunches)
        {
            // glasses[0] is the leftmost; the ones past the remaining count (from the right) are spent.
            for (int i = 0; i < glasses.Count; i++)
                glasses[i].color = new Color(1f, 1f, 1f, i < hunches ? 1f : 0.28f);
            if (shownHunches > hunches) glassPopAt[hunches] = Time.unscaledTime;
            else if (shownHunches >= 0)
                for (int i = shownHunches; i < hunches; i++) glassPopAt[i] = Time.unscaledTime; // given back
            shownHunches = hunches;
        }
    }

    void UpdateMeters()
    {
        if (takenBar == null) return;

        // Like Whisper's countdown: in the final stretch the time fades out and back in once a second.
        var cc = clockText.color;
        cc.a = clockUrgent ? 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f) : Mathf.MoveTowards(cc.a, 1f, Time.unscaledDeltaTime * 2f);
        clockText.color = cc;

        takenGroup.alpha = Mathf.MoveTowards(takenGroup.alpha, takenShown ? 1f : 0f, Time.unscaledDeltaTime / FadeDuration);

        // The inner bar eases to its new length; the whole bar pops with each change.
        fillAmount = Mathf.MoveTowards(fillAmount, shownTaken / (float)takenMax, FillSpeed * Time.unscaledDeltaTime);
        takenFill.enabled = fillAmount > 0.001f;
        takenFill.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(fillMinWidth, fillMaxWidth, fillAmount), takenFill.rectTransform.sizeDelta.y);
        // A gentle pop: mostly taller, barely wider, so it never grows into the eye beside it.
        takenBar.localScale = new Vector3(Punch(barPopAt, 0.02f), Punch(barPopAt, 0.2f), 1f);

        // When the town is nearly lost the eye keeps a slow, uneasy pulse.
        bool nearlyLost = shownTaken >= takenMax - 2;
        float pulse = nearlyLost ? 1f + 0.1f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 3f) : 1f;
        eye.localScale = Vector3.one * Punch(barPopAt, 0.18f) * pulse;

        for (int i = 0; i < glasses.Count; i++)
        {
            float p = (Time.unscaledTime - glassPopAt[i]) / PopTime;
            // A spent hunch shakes as it greys out.
            glasses[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, p < 1f ? Mathf.Sin(p * Mathf.PI * 5f) * 18f * (1f - p) : 0f);
            glasses[i].rectTransform.localScale = Vector3.one * Punch(glassPopAt[i], 0.3f);
        }
    }

    // 1 normally; a quick swell to 1 + amount right after `since`.
    static float Punch(float since, float amount)
    {
        float p = (Time.unscaledTime - since) / PopTime;
        return p >= 0f && p < 1f ? 1f + amount * Mathf.Sin(p * Mathf.PI) : 1f;
    }

    // Anchored to a corner (top left unless given), pivoted on its centre so pops and shakes happen in place.
    static void PlaceCentred(RectTransform rect, Vector2 centre, Vector2? corner = null)
    {
        rect.anchorMin = rect.anchorMax = corner ?? new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = centre;
    }

    static Sprite Icon(string name)
    {
        var tex = Resources.Load<Texture2D>("Icons/" + name);
        if (tex == null) { Debug.LogWarning("Missing icon " + name); return null; }
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

    static Image IconImage(Transform parent, string name, Sprite sprite, float size)
    {
        var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.rectTransform.sizeDelta = Vector2.one * size;
        return img;
    }
}
