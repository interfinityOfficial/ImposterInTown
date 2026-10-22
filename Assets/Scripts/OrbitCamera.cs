using UnityEngine;
using UnityEngine.InputSystem;

// Third-person camera that follows a target, turned with the mouse like PUBG-style shooters: while mouseLook is on
// the cursor is hidden and locked, and moving the mouse swings the camera around behind the player (horizontally;
// distance and pitch stay fixed so players can't zoom out to watch the whole town). Esc frees the cursor, a click
// takes it back.
// Also runs in edit mode, so the Game view matches Play mode and Inspector tweaks show up immediately.
[ExecuteAlways]
public class OrbitCamera : MonoBehaviour
{
    public Transform follow;
    public Vector3 followOffset = new Vector3(0f, 0.35f, 0f); // aim at the character's middle, not its feet
    public Vector3 target = Vector3.zero;                     // used when there's nothing to follow
    public float followSmoothing = 12f;

    public float distance = 8.5f;
    public float yaw = 0f;
    public float pitch = 35f;

    [Tooltip("On while playing; off for menus and the intro, which need the cursor.")]
    public bool mouseLook;
    [Tooltip("Degrees of turn per pixel of mouse movement.")]
    public float lookSensitivity = 0.15f;
    [Tooltip("Where the target sits on screen, from 0 (bottom edge) to 1 (top edge).")]
    [Range(0.05f, 0.95f)] public float targetScreenY = 0.33f;

    Vector3 smoothedTarget;
    bool initialized;
    bool cursorFreed;   // Esc pressed: let the cursor go until the next click
    bool wasLocked;

    void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            Apply();
            return;
        }

        bool locked = UpdateCursor();
        // Skip the first frame after locking: the cursor jumping to the centre reads as a big mouse movement.
        if (locked && wasLocked && Mouse.current != null)
            yaw += Mouse.current.delta.ReadValue().x * lookSensitivity;
        wasLocked = locked;

        Vector3 goal = FocusPoint();
        smoothedTarget = initialized ? Vector3.Lerp(smoothedTarget, goal, 1f - Mathf.Exp(-followSmoothing * Time.deltaTime)) : goal;
        initialized = true;
        Place(smoothedTarget);
    }

    // Locks and hides the cursor while mouse-looking (unless freed with Esc). Returns whether it's locked.
    bool UpdateCursor()
    {
        if (mouseLook)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) cursorFreed = true;
            else if (cursorFreed && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) cursorFreed = false;
        }
        bool locked = mouseLook && !cursorFreed && Application.isFocused;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
        // A browser may refuse the lock (it only allows it from a click), so the web page takes it on the next
        // click, including the one after Esc.
        WebPage.WantsPointerLock(mouseLook);
        return locked;
    }

    void OnDisable()
    {
        if (!Application.isPlaying) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        WebPage.WantsPointerLock(false);
    }

    // Snap straight to the current pose (used by the scene builder and on the first frame).
    public void Apply()
    {
        smoothedTarget = FocusPoint();
        Place(smoothedTarget);
    }

    Vector3 FocusPoint() => follow != null ? follow.position + followOffset : target;

    void Place(Vector3 focus)
    {
        transform.position = focus + Quaternion.Euler(pitch, yaw, 0f) * Vector3.back * distance;
        // Tilt the view up just enough that the target lands at targetScreenY.
        var cam = GetComponent<Camera>();
        float halfFov = (cam != null ? cam.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad;
        float tilt = Mathf.Atan((0.5f - targetScreenY) * 2f * Mathf.Tan(halfFov)) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(pitch - tilt, yaw, 0f);
    }
}
