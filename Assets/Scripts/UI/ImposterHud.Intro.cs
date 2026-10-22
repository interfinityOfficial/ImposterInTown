using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The story intro: a speech bubble (same sticker style as the talk bubble, sized to fit its line) pops up over
// whoever is speaking and types the line out. Bubbles move exactly like the talk bubble: they pop in from the tip
// of the tail with a little overshoot; when the next line starts, the old one swells and shrinks away on its
// speaker first, then the new one pops up (two bubbles take turns). [SPACE] NEXT  [F] SKIP sit at the bottom centre.
// As a line types out, its speaker babbles in marimba notes: one every few letters, the letter picking the note
// (so the same words always play the same little tune), at that villager's own pitch.
// ImposterGame runs the conversation and the camera; this only draws it.
// It lives on its own canvas, so it shows while the rest of the HUD is still hidden.
public partial class ImposterHud
{
    const int SpeechMaxLines = 2;
    const float SpeechMaxWidth = 200f, SpeechPadX = 12f, SpeechPadY = 9f, SpeechTextSize = 12f;
    const float SpeechAboveFeet = 1.0f;

    RectTransform introCanvasRect;

    class Speech
    {
        public RectTransform rect;
        public Image image;
        public Text text;
        public Transform target;
        public string full = "";
        public float start;
        public bool shown;
        public float t;   // 0 = gone, 1 = full size
        public float appearAt;   // waits for the previous bubble to finish shrinking
        public float voice = 1f;   // the speaker's pitch
        public int spoken;         // letters already babbled
        public float lastBlip = -1f;
    }

    const float BabbleGap = 0.09f;     // at most one note this often
    const float BabbleDelay = 0.15f;   // the first note waits for the bubble's pop
    readonly Dictionary<Transform, float> voices = new Dictionary<Transform, float>();

    readonly Speech[] speeches = new Speech[2];
    int currentSpeech;
    Speech Current => speeches[currentSpeech];
    CanvasGroup nextHint;
    bool nextHintShown;
    readonly Dictionary<string, (Sprite sprite, Vector2 size, string text)> speechSprites = new Dictionary<string, (Sprite, Vector2, string)>();

    // The current line has finished typing out.
    public bool SpeechTyped => Current == null || Time.time - Current.start >= Current.full.Length / TypeSpeed;

    // Pointer is over a button (so a click there isn't also "next line").
    public static bool PointerOverUI => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    public void BeginIntro()
    {
        if (introCanvasRect == null) BuildIntro();
        nextHintShown = true;
    }

    // Pops a bubble up over `speaker` and types `line` out in it; the previous line's bubble shrinks away.
    public void Say(Transform speaker, string line)
    {
        // The previous bubble shrinks away first; this one pops up once it's gone.
        var previous = Current;
        previous.shown = false;
        float wait = previous.t * BubblePop;
        currentSpeech = 1 - currentSpeech;
        var bubble = Current;

        var (sprite, size, wrapped) = SpeechSprite(line);
        bubble.image.sprite = sprite;
        bubble.rect.sizeDelta = size;
        bubble.rect.pivot = sprite.pivot / sprite.rect.size;
        bubble.text.rectTransform.offsetMin = new Vector2(SpeechPadX, TailLength + BubbleDrop + SpeechPadY);
        bubble.text.rectTransform.offsetMax = new Vector2(-SpeechPadX, -SpeechPadY);
        bubble.rect.SetAsLastSibling(); // the new line on top
        bubble.target = speaker;
        bubble.full = wrapped;
        bubble.appearAt = Time.time + wait;
        bubble.start = bubble.appearAt; // typing starts once it's up
        bubble.shown = true;
        bubble.t = 0f;
        bubble.spoken = 0;
        bubble.voice = VoiceOf(speaker, previous.target);
    }

    // Each speaker keeps one voice; a new one is kept clearly apart from whoever spoke before.
    float VoiceOf(Transform speaker, Transform other)
    {
        if (voices.TryGetValue(speaker, out float v)) return v;
        v = Random.Range(0.75f, 1.25f);
        if (other != null && voices.TryGetValue(other, out float o) && Mathf.Abs(v - o) < 0.2f)
            v = o + (o < 1f ? 0.25f : -0.25f);
        voices[speaker] = v;
        return v;
    }

    // A note for each newly typed letter, spaced out so it's a patter rather than a buzz.
    void Babble(Speech bubble)
    {
        if (Time.time < bubble.start + BabbleDelay) return;
        int typed = Mathf.Clamp(Mathf.FloorToInt((Time.time - bubble.start) * TypeSpeed), 0, bubble.full.Length);
        for (; bubble.spoken < typed; bubble.spoken++)
        {
            char c = char.ToUpperInvariant(bubble.full[bubble.spoken]);
            if (c < 'A' || c > 'Z' || Time.time - bubble.lastBlip < BabbleGap) continue;
            bubble.lastBlip = Time.time;
            GameAudio.Play("babble" + (c - 'A') % 7, 0.35f, 0.02f, bubble.voice);
        }
    }

    // Show the rest of the line straight away.
    public void FinishTyping()
    {
        if (Current != null) Current.start = Time.time - Current.full.Length / TypeSpeed - 0.01f;
    }

    public void EndIntro()
    {
        foreach (var bubble in speeches) if (bubble != null) bubble.shown = false;
        nextHintShown = false;
    }

    void UpdateIntro()
    {
        if (introCanvasRect == null) return;
        nextHint.alpha = Mathf.MoveTowards(nextHint.alpha, nextHintShown ? 1f : 0f, Time.unscaledDeltaTime / PromptFade);
        foreach (var bubble in speeches) UpdateSpeech(bubble);
    }

    // Same motion as the talk bubble: pop in with an overshoot, swell then shrink out, following its speaker.
    void UpdateSpeech(Speech bubble)
    {
        bool waiting = bubble.shown && Time.time < bubble.appearAt;
        if (bubble.shown && !waiting && bubble.t <= 0f) GameAudio.Play("drop_003", 0.5f); // starting to pop up
        bubble.t = Mathf.MoveTowards(bubble.t, bubble.shown && !waiting ? 1f : 0f, Time.unscaledDeltaTime / BubblePop);
        float scale = bubble.shown ? EaseOutBack(bubble.t) : 1f - EaseInBack(1f - bubble.t);
        bubble.rect.localScale = Vector3.one * Mathf.Max(0f, scale);
        bool visible = bubble.t > 0f && bubble.target != null;
        bubble.rect.gameObject.SetActive(visible);
        if (!visible) return;

        bubble.text.text = Typed(bubble.full, bubble.start);
        if (bubble.shown) Babble(bubble);
        Vector3 screen = cam.WorldToScreenPoint(bubble.target.position + Vector3.up * SpeechAboveFeet);
        if (screen.z > 0f)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(introCanvasRect, screen, null, out var local);
            bubble.rect.anchoredPosition = local;
        }
    }

    void BuildIntro()
    {
        var root = new GameObject("Intro", typeof(RectTransform));
        var canvas = MakeCanvas(root, 25);
        introCanvasRect = (RectTransform)canvas.transform;

        for (int i = 0; i < speeches.Length; i++)
        {
            var bubble = new Speech();
            bubble.rect = new GameObject("Speech Bubble", typeof(RectTransform)).GetComponent<RectTransform>();
            bubble.rect.SetParent(introCanvasRect, false);
            bubble.rect.anchorMin = bubble.rect.anchorMax = new Vector2(0.5f, 0.5f);
            bubble.image = bubble.rect.gameObject.AddComponent<Image>();
            bubble.image.raycastTarget = false;
            bubble.text = MakeText(bubble.rect, "", strongFont, SpeechTextSize, TextAnchor.MiddleCenter);
            Stretch(bubble.text.rectTransform);
            bubble.text.color = Color.black;
            bubble.text.lineSpacing = 1.05f;
            bubble.rect.gameObject.SetActive(false);
            speeches[i] = bubble;
        }

        // [SPACE] NEXT  [F] SKIP at the bottom centre, like the talk controls.
        var bar = new GameObject("Next Hint", typeof(RectTransform)).GetComponent<RectTransform>();
        bar.SetParent(introCanvasRect, false);
        Place(bar, new Vector2(0.5f, 0f), new Vector2(0f, SideInset), new Vector2(600f, KeyHeight + KeyDrop));
        nextHint = bar.gameObject.AddComponent<CanvasGroup>();
        nextHint.alpha = 0f;
        nextHint.blocksRaycasts = false;
        BuildRow(bar, null, ("SPACE", "NEXT"), ("F", "SKIP"));
    }

    // A bubble that fits its line: wrapped into as few lines as fit SpeechMaxWidth, but never more than
    // SpeechMaxLines (a long line widens the bubble instead), those lines balanced to about the same length, and
    // the box as wide as the longest. Made once per line.
    (Sprite, Vector2, string) SpeechSprite(string line)
    {
        if (speechSprites.TryGetValue(line, out var cached)) return cached;
        float maxInner = SpeechMaxWidth - 2f * SpeechPadX;
        var words = line.Split(' ');

        // Fewest lines at the widest allowed (at most SpeechMaxLines), then the narrowest width that still
        // needs only that many.
        int lineCount = Mathf.Min(Wrap(words, maxInner).Count, SpeechMaxLines);
        float lo = 0f, hi = Mathf.Max(maxInner, Measure(line));
        foreach (var w in words) lo = Mathf.Max(lo, Measure(w));
        for (int i = 0; i < 12; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Wrap(words, mid).Count <= lineCount) hi = mid; else lo = mid;
        }
        var lines = Wrap(words, hi);
        string wrapped = string.Join("\n", lines);
        float width = 0f;
        foreach (var l in lines) width = Mathf.Max(width, Measure(l));

        var settings = Measurer.GetGenerationSettings(Vector2.zero);
        float height = Measurer.cachedTextGeneratorForLayout.GetPreferredHeight(wrapped, settings) / Measurer.pixelsPerUnit;

        float boxW = width + 2f + 2f * SpeechPadX, boxH = Mathf.Max(BubbleHeight, height + 2f * SpeechPadY);
        var sprite = StickerUI.BubbleSprite(boxW, boxH, BubbleRadius, BubbleOutline, BubbleDrop,
            TailWidth, TailLength, TailTipRadius, false, DotSize, DotGap, false);
        var result = (sprite, new Vector2(boxW, boxH + TailLength + BubbleDrop), wrapped);
        speechSprites[line] = result;
        return result;
    }

    // Text used to measure lines (same font and size as every bubble).
    Text Measurer => speeches[0].text;

    // Greedy word wrap at `width` (UI units).
    List<string> Wrap(string[] words, float width)
    {
        var lines = new List<string>();
        string current = "";
        foreach (var word in words)
        {
            string attempt = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && Measure(attempt) > width)
            {
                lines.Add(current);
                current = word;
            }
            else current = attempt;
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    float Measure(string text) => Measure(text, Measurer);

    static float Measure(string text, Text with)
    {
        var settings = with.GetGenerationSettings(Vector2.zero);
        return with.cachedTextGeneratorForLayout.GetPreferredWidth(text, settings) / with.pixelsPerUnit;
    }
}
