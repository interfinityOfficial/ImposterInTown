using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// "Sticker" UI pieces in the style of Kenney's cursor/icon pack: white shapes with a thick black outline,
// rounded corners and a hard black drop shadow straight underneath.
// All sprites are generated in code from signed distance fields (4x resolution, soft edges).
// - Panel: face and shadow as separate images, so the face can move (pressable buttons).
// - FlatPanel / BubbleSprite: face, outline and shadow baked into one image, so the sticker fades as a single
//   piece (with separate layers, the black shadow shows through the half-transparent face while fading).
public static class StickerUI
{
    const int Scale = 4;   // texture pixels per UI unit
    static readonly Dictionary<(float, float), Sprite> rounded = new Dictionary<(float, float), Sprite>();
    static readonly Dictionary<(float, float, float, float), Sprite> flat = new Dictionary<(float, float, float, float), Sprite>();

    // ---------- Layered (pressable) ----------

    // A white rounded panel with a black outline over a black shadow `drop` units below it.
    // Returns the face, which is what moves when pressed and where content goes.
    public static Image Panel(Transform parent, string name, float radius, float outline, float drop, out RectTransform root)
    {
        root = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(parent, false);

        if (drop > 0f)
        {
            var shadow = Img(root, "Shadow", RoundedSprite(radius, 0f));
            shadow.color = Color.black;
            Stretch(shadow.rectTransform);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -drop);
        }

        var face = Img(root, "Face", RoundedSprite(radius, outline));
        Stretch(face.rectTransform);
        return face;
    }

    // ---------- Baked (fade as one piece) ----------

    // A panel and its shadow in one image. Its rect must be `height + drop` tall (it only stretches sideways);
    // content goes in the returned face area, the top `height` units.
    public static Image FlatPanel(Transform parent, string name, float radius, float outline, float drop, float height, out RectTransform faceArea)
    {
        var img = Img(parent, name, FlatPanelSprite(radius, outline, drop, height));
        faceArea = new GameObject("Face Area", typeof(RectTransform)).GetComponent<RectTransform>();
        faceArea.SetParent(img.transform, false);
        Stretch(faceArea);
        faceArea.offsetMin = new Vector2(0f, drop);
        return img;
    }

    // A speech bubble (rounded box plus a tail with a rounded tip) and its shadow, as one sprite pivoted on the
    // tail's tip. Box and tail are one shape, so the outline runs around both without a seam.
    // tailLength: how far the tail reaches below the box. dots: "..." in the middle. dark: all black (armed).
    public static Sprite BubbleSprite(float width, float height, float radius, float outline, float drop,
                                      float tailWidth, float tailLength, float tipRadius,
                                      bool dots, float dotSize, float dotGap, bool dark)
    {
        float totalHeight = height + tailLength + drop;
        int w = Mathf.CeilToInt(width * Scale), h = Mathf.CeilToInt(totalHeight * Scale);
        float r = radius * Scale, o = outline * Scale, dropPx = drop * Scale, inset = 0.5f * Scale;

        // Texture space, y up: the shadow's drop at the bottom, then the tail, then the box.
        float boxBottom = (tailLength + drop) * Scale;
        var boxCenter = new Vector2(w / 2f, (boxBottom + h) / 2f);
        var boxHalf = new Vector2(w / 2f - inset, (h - boxBottom) / 2f - inset);
        // The tail starts just inside the box so the two shapes merge.
        float tailTop = boxBottom + o + inset;
        float tailHeight = tailTop - dropPx;
        float tipR = tipRadius * Scale, dotR = dotSize * Scale / 2f, dotStep = (dotSize + dotGap) * Scale;

        float Shape(Vector2 p) => Mathf.Min(RoundedRect(p, boxCenter, boxHalf, r),
                                            Tail(p, w / 2f, tailTop, tailWidth * Scale / 2f, tailHeight, tipR));

        var tex = NewTexture(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Shape(p);
                float faceA = Mathf.Clamp01(0.5f - d);
                float faceC = dark ? 0f : Mathf.Clamp01(-(d + o) + 0.5f);
                if (dots && !dark)
                    for (int i = -1; i <= 1; i++)
                        faceC *= Mathf.Clamp01(0.5f + (p - (boxCenter + new Vector2(i * dotStep, 0f))).magnitude - dotR);
                float shadowA = Mathf.Clamp01(0.5f - Shape(p + new Vector2(0f, dropPx)));
                Composite(tex, x, y, faceC, faceA, shadowA);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, drop / totalHeight), 100f * Scale);
    }

    // A callout card: a rounded box whose tail points up (tailDir 1), down (-1) or nowhere (0), `tailX` units off
    // the box's centre, and its shadow, as one sprite. Pivoted on the tail's tip, or the box's top centre without
    // one. faceGrey: the fill, from 0 (black) to 1 (white); the outline and shadow are always black.
    public static Sprite CardSprite(float width, float height, float radius, float outline, float drop,
                                    float tailWidth, float tailLength, float tipRadius, float tailX, int tailDir, float faceGrey)
    {
        float tail = tailDir == 0 ? 0f : tailLength;
        float totalHeight = height + tail + drop;
        int w = Mathf.CeilToInt(width * Scale), h = Mathf.CeilToInt(totalHeight * Scale);
        float r = radius * Scale, o = outline * Scale, dropPx = drop * Scale, inset = 0.5f * Scale;

        // Texture space, y up: the shadow's drop at the bottom, then the box with the tail below or above it.
        float boxBottom = (tailDir < 0 ? tail + drop : drop) * Scale;
        float boxTop = boxBottom + height * Scale;
        var boxCenter = new Vector2(w / 2f, (boxBottom + boxTop) / 2f);
        var boxHalf = new Vector2(w / 2f - inset, height * Scale / 2f - inset);
        float cx = w / 2f + tailX * Scale, halfTail = tailWidth * Scale / 2f, tipR = tipRadius * Scale;
        // The tail starts just inside the box so the two shapes merge. An upward tail is the downward one mirrored,
        // with its tip at the top of the texture.
        float downTop = boxBottom + o + inset, upBase = h - (boxTop - o - inset);

        float Shape(Vector2 p)
        {
            float box = RoundedRect(p, boxCenter, boxHalf, r);
            if (tailDir < 0) return Mathf.Min(box, Tail(p, cx, downTop, halfTail, downTop - dropPx, tipR));
            if (tailDir > 0) return Mathf.Min(box, Tail(new Vector2(p.x, h - p.y), cx, upBase, halfTail, upBase, tipR));
            return box;
        }

        var tex = NewTexture(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Shape(p);
                float shadowA = Mathf.Clamp01(0.5f - Shape(p + new Vector2(0f, dropPx)));
                Composite(tex, x, y, faceGrey * Mathf.Clamp01(-(d + o) + 0.5f), Mathf.Clamp01(0.5f - d), shadowA);
            }
        tex.Apply();
        var pivot = tailDir < 0 ? new Vector2(cx / w, drop / totalHeight)
                  : tailDir > 0 ? new Vector2(cx / w, 1f)
                  : new Vector2(0.5f, 1f);
        return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, 100f * Scale);
    }

    // ---------- Helpers ----------

    static Image Img(Transform parent, string name, Sprite sprite)
    {
        var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        return img;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // Rounded square: white inside, black `outline` band at the edge (outline 0 = plain white, for tinting).
    // Nine-sliced, so it stretches to any size and keeps its corners.
    public static Sprite RoundedSprite(float radius, float outline)
    {
        if (rounded.TryGetValue((radius, outline), out var cached)) return cached;
        float r = radius * Scale, o = outline * Scale;
        int size = Mathf.CeilToInt(r * 2f) + 2 * Scale;
        var tex = NewTexture(size, size);
        float half = size / 2f;
        var halfExtent = Vector2.one * (half - 0.5f * Scale);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundedRect(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half), halfExtent, r);
                Composite(tex, x, y, o > 0f ? Mathf.Clamp01(-(d + o) + 0.5f) : 1f, Mathf.Clamp01(0.5f - d), 0f);
            }
        tex.Apply();
        float border = Mathf.Floor(half) - 1f;
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f * Scale, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        rounded[(radius, outline)] = sprite;
        return sprite;
    }

    // Panel + shadow baked together; sliced on the left and right only, so it stretches sideways.
    static Sprite FlatPanelSprite(float radius, float outline, float drop, float height)
    {
        var key = (radius, outline, drop, height);
        if (flat.TryGetValue(key, out var cached)) return cached;
        float r = radius * Scale, o = outline * Scale, dropPx = drop * Scale, inset = 0.5f * Scale;
        int w = Mathf.CeilToInt(r * 2f) + 2 * Scale, h = Mathf.CeilToInt((height + drop) * Scale);
        var faceCenter = new Vector2(w / 2f, (dropPx + h) / 2f);
        var half = new Vector2(w / 2f - inset, (h - dropPx) / 2f - inset);

        var tex = NewTexture(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = RoundedRect(p, faceCenter, half, r);
                float shadowA = Mathf.Clamp01(0.5f - RoundedRect(p + new Vector2(0f, dropPx), faceCenter, half, r));
                Composite(tex, x, y, Mathf.Clamp01(-(d + o) + 0.5f), Mathf.Clamp01(0.5f - d), shadowA);
            }
        tex.Apply();
        float side = Mathf.Floor(w / 2f) - 1f;
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f * Scale, 0,
            SpriteMeshType.FullRect, new Vector4(side, 0f, side, 0f));
        flat[key] = sprite;
        return sprite;
    }

    // A face (grey level c, coverage faceA) over a black shadow (coverage shadowA). Fully clear pixels are stored
    // as clear black: texture filtering blends colour even at zero alpha, and clear white leaves light fringes.
    static void Composite(Texture2D tex, int x, int y, float c, float faceA, float shadowA)
    {
        float a = faceA + shadowA * (1f - faceA);
        float colour = a > 0f ? c * faceA / a : 0f;
        tex.SetPixel(x, y, a > 0f ? new Color(colour, colour, colour, a) : Color.clear);
    }

    // Signed distance (negative inside) to a rounded rectangle.
    static float RoundedRect(Vector2 p, Vector2 center, Vector2 half, float r)
    {
        var q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - half + Vector2.one * r;
        return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
    }

    // Signed distance to a downward triangle (top edge at topY, halfWidth either side of cx) with a rounded tip.
    // Rounding: shrink the triangle by r, then grow it back by r; below the shrunk tip the distance is to that point.
    static float Tail(Vector2 p, float cx, float topY, float halfWidth, float height, float r)
    {
        var topLeft = new Vector2(cx - halfWidth, topY);
        var topRight = new Vector2(cx + halfWidth, topY);
        var tipPoint = new Vector2(cx, topY - height);
        var nLeft = new Vector2(-height, -halfWidth).normalized;
        var nRight = new Vector2(height, -halfWidth).normalized;
        var tip = new Vector2(cx, topY + (-r - halfWidth * nLeft.x) / nLeft.y);
        var dirLeft = (tipPoint - topLeft).normalized;
        var dirRight = (tipPoint - topRight).normalized;

        float shrunk = Vector2.Dot(p - tip, dirLeft) > 0f && Vector2.Dot(p - tip, dirRight) > 0f
            ? (p - tip).magnitude
            : Mathf.Max(Mathf.Max(Vector2.Dot(p - topLeft, nLeft), Vector2.Dot(p - topRight, nRight)) + r, p.y - topY);
        return shrunk - r;
    }

    static Texture2D NewTexture(int w, int h) =>
        new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
}
