using UnityEngine;
using UnityEngine.InputSystem;

// WASD / arrow keys to walk relative to the camera (no running: the square is small and it's a game of looking).
// Drives the Animator's "Speed" float (idle -> walk -> sprint blend tree).
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public float walkSpeed = 1.6f;
    public float turnSpeed = 720f;      // degrees per second
    public float acceleration = 10f;
    public float gravity = -20f;

    CharacterController controller;
    Animator animator;
    Transform cam;
    Vector3 velocity;
    float verticalSpeed;
    float stepDistance;   // ground covered since the last footstep

    static readonly string[] Footsteps = { "footstep04", "footstep05", "footstep06", "footstep09" };

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        cam = Camera.main != null ? Camera.main.transform : null;
    }

    // Disabled at the end of a round: stand still instead of staying in mid-walk.
    void OnDisable()
    {
        velocity = Vector3.zero;
        if (animator != null) animator.SetFloat("Speed", 0f);
    }

    void Update()
    {
        Vector2 input = ReadMoveInput();

        // Move relative to where the camera is looking, flattened onto the ground.
        Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 wishDir = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);

        Vector3 targetVelocity = wishDir * walkSpeed;
        velocity = Vector3.MoveTowards(velocity, targetVelocity, acceleration * Time.deltaTime);

        if (wishDir.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(wishDir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        verticalSpeed = controller.isGrounded ? -1f : verticalSpeed + gravity * Time.deltaTime;
        controller.Move((velocity + Vector3.up * verticalSpeed) * Time.deltaTime);

        if (animator != null)
        {
            Vector3 planar = controller.velocity; planar.y = 0f;
            animator.SetFloat("Speed", planar.magnitude, 0.1f, Time.deltaTime);
        }

        // Footsteps by distance, so the steps keep pace with the feet.
        float speed = new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude;
        if (speed < 0.2f) stepDistance = 0f;
        else
        {
            stepDistance += speed * Time.deltaTime;
            float stride = 0.45f + 0.13f * speed;
            if (stepDistance >= stride)
            {
                stepDistance -= stride;
                // Quiet and a little higher: light steps for a small character.
                GameAudio.Play(Footsteps[Random.Range(0, Footsteps.Length)], 0.3f, 0.08f, 1.12f);
            }
        }
    }

    static Vector2 ReadMoveInput()
    {
        var kb = Keyboard.current;
        if (kb == null) return Vector2.zero;
        Vector2 v = Vector2.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
        return v;
    }
}
