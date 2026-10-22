using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// What the hints point at: the part of the HUD a tip is about, or nothing.
public enum HintTarget { None, Clock, Taken, Hunches }

// Hints: first-time tips and what just happened, typed out on a dark sticker card (the narrator's voice; the
// villagers speak in white bubbles). The card's tail points up at the part of the HUD the hint is about: the
// time, the taken bar or the hunches. Any other hint (including ones about the SPACE / F keys, which come and go
// as you walk about) sits under the time without a tail.
// Cards move like the bubbles: pop in from the tail's tip, swell and shrink away. A new hint never cuts one
// off mid-sentence: it waits until the current card has typed out and been up for a moment, then the old card
// shrinks away and the new one pops up (if several arrive meanwhile, the latest wins).
public partial class ImposterHud
{
    const float HintTextSize = 13f, HintPadX = 12f, HintPadY = 8f, HintGap = 4f;
    const float HintMinShown = 1.5f;   // a hint stays up at least this long after typing out before being replaced
    const float HintFace = 0.2f;    // the card's fill, dark grey, so the black outline and shadow still read

    RectTransform hintCard;
    Image hintImage;
    Text hintText;
    string hintFull = "";
    float hintStart, hintUntil, hintReadableAt;
    bool hintShown;
    float hintT;   // 0 = gone, 1 = full size
    HintTarget hintTarget;
    (string text, float seconds, HintTarget target)? pendingHint;
    readonly Dictionary<(string, int, int), (Sprite sprite, Vector2 size)> hintSprites = new Dictionary<(string, int, int), (Sprite, Vector2)>();

    // A card is up (or on its way in or out), or a hint is waiting for one.
    public bool HintBusy => hintShown || hintT > 0f || pendingHint.HasValue;

    // Types a message out on a card pointing at `target`, keeps it up for `seconds`, then pops it away. If a card
    // is already up, this one takes over once that card has been readable for a moment.
    // Returns roughly how long until it goes.
    public float ShowHint(string text, float seconds, HintTarget target = HintTarget.None)
    {
        float total = text.Length / TypeSpeed + seconds;
        if (hintShown || hintT > 0f)
        {
            pendingHint = (text, seconds, target);
            return total + Mathf.Max(0f, hintReadableAt - Time.time) + BubblePop;
        }
        OpenHint(text, seconds, target);
        return total;
    }

    public void HideHint()
    {
        pendingHint = null;
        hintShown = false;
    }

    void OpenHint(string text, float seconds, HintTarget target)
    {
        if (TargetRect(target) == null) target = HintTarget.None;
        Vector2 tip = TipPoint(target);
        var (sprite, size, tailX) = HintSprite(text, target, tip);

        hintImage.sprite = sprite;
        hintCard.sizeDelta = size;
        hintCard.pivot = sprite.pivot / sprite.rect.size;
        int dir = TailDir(target);
        float tail = dir == 0 ? 0f : TailLength;
        hintText.rectTransform.offsetMin = new Vector2(HintPadX, BubbleDrop + HintPadY);
        hintText.rectTransform.offsetMax = new Vector2(-HintPadX, -(dir > 0 ? tail : 0f) - HintPadY);
        hintCard.SetAsLastSibling();

        hintTarget = target;
        hintFull = text;
        hintStart = Time.time + BubblePop * 0.6f; // typing starts as it lands
        hintUntil = hintStart + text.Length / TypeSpeed + seconds;
        hintReadableAt = Mathf.Min(hintUntil, hintStart + text.Length / TypeSpeed + HintMinShown);
        hintShown = true;
        hintT = 0f;
        PlaceHint();
    }

    void UpdateHint()
    {
        if (hintCard == null) return;
        // Done, or what it points at has gone (the taken bar on the end screen).
        if (hintShown && (Time.time >= hintUntil || hintTarget == HintTarget.Taken && !takenShown))
            hintShown = false;
        // Another hint is waiting and this one has had its moment: make way.
        if (hintShown && pendingHint.HasValue && Time.time >= hintReadableAt)
            hintShown = false;

        hintT = Mathf.MoveTowards(hintT, hintShown ? 1f : 0f, Time.unscaledDeltaTime / BubblePop);
        if (!hintShown && hintT <= 0f && pendingHint.HasValue)
        {
            var next = pendingHint.Value;
            pendingHint = null;
            OpenHint(next.text, next.seconds, next.target);
        }

        float scale = hintShown ? EaseOutBack(hintT) : 1f - EaseInBack(1f - hintT);
        hintCard.localScale = Vector3.one * Mathf.Max(0f, scale);
        hintCard.gameObject.SetActive(hintT > 0f);
        if (hintT <= 0f) return;
        hintText.text = Typed(hintFull, hintStart);
        PlaceHint();
    }

    // Keeps the tail's tip on its target (the window may have been resized).
    void PlaceHint() => hintCard.anchoredPosition = TipPoint(hintTarget);

    void BuildHint(Transform parent)
    {
        hintCard = new GameObject("Hint Card", typeof(RectTransform)).GetComponent<RectTransform>();
        hintCard.SetParent(parent, false);
        hintCard.anchorMin = hintCard.anchorMax = new Vector2(0.5f, 0.5f);
        hintImage = hintCard.gameObject.AddComponent<Image>();
        hintImage.raycastTarget = false;
        hintText = MakeText(hintCard, "", strongFont, HintTextSize, TextAnchor.MiddleCenter);
        Stretch(hintText.rectTransform);
        hintText.lineSpacing = 1.1f;
        hintCard.gameObject.SetActive(false);
    }

    // What a hint points at, or null for nothing.
    RectTransform TargetRect(HintTarget target)
    {
        switch (target)
        {
            case HintTarget.Clock: return clockIcon;
            case HintTarget.Taken: return takenBar;
            case HintTarget.Hunches: return glasses.Count > 0 ? glasses[glasses.Count / 2].rectTransform : null;
            default: return null;
        }
    }

    // The targets are all across the top, so their cards hang below them with the tail up.
    static int TailDir(HintTarget target) => target == HintTarget.None ? 0 : 1;

    // Where the tail's tip goes, in canvas units from the centre: just off the target's edge. Without a target,
    // the card's top centre, under the time on the left (filled in once the card's width is known).
    Vector2 TipPoint(HintTarget target)
    {
        var rect = TargetRect(target);
        if (rect == null)
        {
            var half = canvasRect.rect.size / 2f;
            return new Vector2(-half.x + SideInset + hintCard.sizeDelta.x / 2f, half.y + RowY - 16f);
        }
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = canvasRect.InverseTransformPoint(corners[0]), max = canvasRect.InverseTransformPoint(corners[2]);
        return new Vector2((min.x + max.x) / 2f, min.y - HintGap);
    }

    // A card sized to the hint's lines, kept inside the screen's side margins, with its tail under (or over) `tip`.
    // Made once per hint, target and tail position.
    (Sprite, Vector2, float) HintSprite(string text, HintTarget target, Vector2 tip)
    {
        float width = 0f;
        foreach (var line in text.Split('\n')) width = Mathf.Max(width, Measure(line, hintText));
        var settings = hintText.GetGenerationSettings(Vector2.zero);
        float height = hintText.cachedTextGeneratorForLayout.GetPreferredHeight(text, settings) / hintText.pixelsPerUnit;
        float boxW = width + 2f + 2f * HintPadX, boxH = Mathf.Max(BubbleHeight, height + 2f * HintPadY);

        int dir = TailDir(target);
        float tailX = 0f;
        if (dir != 0)
        {
            float half = canvasRect.rect.width / 2f;
            float centre = Mathf.Clamp(tip.x, -half + SideInset + boxW / 2f, half - SideInset - boxW / 2f);
            float limit = boxW / 2f - BubbleRadius - TailWidth / 2f;
            tailX = Mathf.Round(Mathf.Clamp(tip.x - centre, -limit, limit));
        }

        var key = (text, (int)target, (int)tailX);
        if (!hintSprites.TryGetValue(key, out var made))
        {
            var sprite = StickerUI.CardSprite(boxW, boxH, BubbleRadius, BubbleOutline, BubbleDrop,
                TailWidth, TailLength, TailTipRadius, tailX, dir, HintFace);
            made = (sprite, new Vector2(boxW, boxH + (dir == 0 ? 0f : TailLength) + BubbleDrop));
            hintSprites[key] = made;
        }
        return (made.sprite, made.size, tailX);
    }
}
