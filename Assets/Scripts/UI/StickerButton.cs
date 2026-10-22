using UnityEngine;
using UnityEngine.EventSystems;

// A sticker button that lifts a little on hover and presses down onto its shadow when clicked.
public class StickerButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public RectTransform face;
    public float lift = 2f;
    public float press = 5f;   // the shadow's drop, so a pressed button sits right on it
    public float speed = 20f;

    bool hover, down;

    void Update()
    {
        float target = down ? -press : hover ? lift : 0f;
        var p = face.anchoredPosition;
        p.y = Mathf.Lerp(p.y, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
        face.anchoredPosition = p;
    }

    void OnDisable()
    {
        hover = down = false;
        if (face != null) face.anchoredPosition = Vector2.zero;
    }

    public void OnPointerEnter(PointerEventData e) => hover = true;
    public void OnPointerExit(PointerEventData e) { hover = false; down = false; }
    public void OnPointerDown(PointerEventData e) { down = true; GameAudio.Play("click3", 0.7f); }
    public void OnPointerUp(PointerEventData e) => down = false;
}
