using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

// Simple daily-life brain for a villager: stroll around the plaza, browse market stalls,
// sit on benches, admire the fountain, and stop to chat with a passer-by.
// Animator states used: "Locomotion" (blend tree on Speed), "sit", "interact-left", "interact-right",
// "pick-up", "emote-yes", "emote-no".
// Nodding and head-shaking are reserved for answering the player, so everyday life never uses them.
// While standing still a villager swaps its NavMeshAgent for a NavMeshObstacle that carves a hole in the nav mesh,
// so walkers path around it (rather than only dodging at the last moment, which jams in narrow gaps) instead of
// shoving it out of the way. The player carves one too while standing still.
// ImposterGame drives the rest: Engage/Answer when the player talks to them, Glitch when converted,
// StareAt when a copy is turned back and looks at the one who changed it.
[RequireComponent(typeof(NavMeshAgent), typeof(NavMeshObstacle))]
public class VillagerAI : MonoBehaviour
{
    public Vector2 walkSpeedRange = new Vector2(0.9f, 1.3f);
    public float plazaHalfSize = 7.5f;
    [Tooltip("How high to lift the character onto a bench seat while sitting.")]
    public float sitHeight = 0.36f;

    [Header("How often each activity is chosen")]
    public float strollWeight = 3f;
    public float shopWeight = 2f;
    public float sitWeight = 1.5f;
    public float fountainWeight = 1f;
    public float chatWeight = 1.5f;

    [Header("Imposter game")]
    [Tooltip("Which outfit this villager wears (two villagers share each one).")]
    public int skinType;
    [HideInInspector] public bool onImposterSide;
    // Someone this villager saw get converted; they glance at them before answering.
    [HideInInspector] public VillagerAI witnessed;
    [HideInInspector] public float witnessedUntil;

    public bool Engaged { get; private set; }
    public bool ReadyToAnswer { get; private set; }
    public bool Answering { get; private set; }
    public bool Staring { get; private set; }

    public static readonly List<VillagerAI> All = new List<VillagerAI>();

    const float EmoteLength = 0.67f;   // emote-yes / emote-no / interact-* clip length
    const float PickUpLength = 0.33f;
    const float ArriveSlack = 0.5f;    // how close to a destination counts as there (m)

    NavMeshAgent agent;
    NavMeshObstacle obstacle;
    Animator animator;
    Coroutine brain;
    ActivitySpot heldSpot;
    bool strolling;   // only strolling villagers can be pulled into a conversation
    bool arrived;     // result of the last WalkTo
    bool seated;
    Vector3 seatStandPos;
    float glitch = 1f;
    Transform talkingTo;
    Coroutine engageRoutine, answerRoutine, stareRoutine;

    // Footsteps: quiet, from the villager's own position in 3D, heard only nearby (silent past StepsHeard).
    const float StepsVolume = 0.15f, StepsNear = 1f, StepsHeard = 7f;   // well under the player's own
    static readonly string[] Footsteps = { "footstep04", "footstep05", "footstep06", "footstep09" };
    AudioSource steps;
    float stepDistance;
    float stepPitch;   // each villager's own, a little lighter than the player's

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        obstacle = GetComponent<NavMeshObstacle>();
        animator = GetComponent<Animator>();
        UseCarving(obstacle);
    }

    // Standing obstacles cut themselves out of the nav mesh, so paths are planned around them.
    public static void UseCarving(NavMeshObstacle o)
    {
        if (o == null) return;
        o.carving = true;
        o.carveOnlyStationary = true;    // only once they've stopped, so moving doesn't rebuild the mesh every frame
        o.carvingTimeToStationary = 0.3f;
        o.carvingMoveThreshold = 0.1f;
    }

    void OnEnable() => All.Add(this);

    void OnDisable()
    {
        All.Remove(this);
        ReleaseSpot();
    }

    void Start()
    {
        agent.speed = Random.Range(walkSpeedRange.x, walkSpeedRange.y);
        agent.avoidancePriority = Random.Range(30, 70);
        animator.Play("Locomotion", 0, Random.value);
        SetMoving(false);
        brain = StartCoroutine(Brain());

        steps = gameObject.AddComponent<AudioSource>();
        steps.playOnAwake = false;
        steps.spatialBlend = 1f;
        steps.rolloffMode = AudioRolloffMode.Linear;
        steps.minDistance = StepsNear;
        steps.maxDistance = StepsHeard;
        steps.dopplerLevel = 0f;
        steps.priority = 200; // the first to go if too many sounds play at once
        stepPitch = Random.Range(1.15f, 1.4f);
    }

    void Update()
    {
        float speed = agent.enabled ? agent.velocity.magnitude : 0f;
        animator.SetFloat("Speed", speed, 0.1f, Time.deltaTime);
        animator.speed = glitch;
        Step(speed);
    }

    // A footstep every stride (the same stride rule as the player's), only while actually walking.
    void Step(float speed)
    {
        if (steps == null || !Ambience.Live) return;
        if (speed < 0.2f) { stepDistance = 0f; return; }
        stepDistance += speed * Time.deltaTime;
        float stride = 0.45f + 0.13f * speed;
        if (stepDistance < stride) return;
        stepDistance -= stride;
        steps.pitch = stepPitch * Random.Range(0.95f, 1.05f);
        steps.PlayOneShot(GameAudio.Load(Footsteps[Random.Range(0, Footsteps.Length)]), StepsVolume * Ambience.Level);
    }

    // ---------- Talking to the player ----------

    // The player walked up: stop what we're doing (standing up from a bench if needed) and face them.
    public void Engage(Transform player)
    {
        if (Engaged) return;
        Engaged = true;
        talkingTo = player;
        StopBrain();
        engageRoutine = StartCoroutine(EngageRoutine());
    }

    public void Disengage()
    {
        if (!Engaged) return;
        StopTalking();
        if (seated) { transform.position = seatStandPos; seated = false; } // walked off mid-stand-up
        animator.CrossFadeInFixedTime("Locomotion", 0.2f);
        brain = StartCoroutine(Brain());
    }

    // Nod (true) or shake the head (false), with its sound unless `quiet` (the caller plays its own).
    // Returns false if not ready yet.
    public bool Answer(bool nod, bool quiet = false)
    {
        if (!Engaged || !ReadyToAnswer || Answering) return false;
        answerRoutine = StartCoroutine(AnswerRoutine(nod, quiet));
        return true;
    }

    // Talk with the hands for a moment (the intro's gossip). Only while engaged and not already busy.
    public void Gesture()
    {
        if (!Engaged || !ReadyToAnswer || Answering) return;
        answerRoutine = StartCoroutine(GestureRoutine());
    }

    IEnumerator GestureRoutine()
    {
        Answering = true;
        yield return Play(Random.value < 0.5f ? "interact-right" : "interact-left", EmoteLength * 2f);
        Answering = false;
    }

    // Stop living and hold a pose (used when the game ends).
    public void Freeze(string state)
    {
        StopTalking();
        StopBrain();
        StopStaring();
        if (seated) { transform.position = seatStandPos; seated = false; } // off the bench first
        SetMoving(false);
        animator.CrossFadeInFixedTime(state, 0.15f);
    }

    // Just turned back from a copy: turn towards whoever changed them, point, and keep watching that way for a while.
    // aimError: they only roughly know where it is, so they face up to this many degrees to one side of it
    // (the same side for the whole stare), which points at an area rather than a person.
    public void StareAt(Transform target, float seconds, float aimError = 0f)
    {
        StopTalking();
        StopBrain();
        StopStaring();
        if (seated) { transform.position = seatStandPos; seated = false; }
        float miss = Random.Range(0.4f, 1f) * aimError * (Random.value < 0.5f ? -1f : 1f);
        stareRoutine = StartCoroutine(StareRoutine(target, seconds, Quaternion.Euler(0f, miss, 0f)));
    }

    IEnumerator StareRoutine(Transform target, float seconds, Quaternion miss)
    {
        Staring = true;
        SetMoving(false);
        animator.CrossFadeInFixedTime("Locomotion", 0.2f);
        yield return TurnTo(miss * (target.position - transform.position), 0.5f);

        animator.CrossFadeInFixedTime("interact-right", 0.15f); // pointing at them
        float pointUntil = Time.time + EmoteLength;
        bool pointing = true;
        for (float end = Time.time + seconds; Time.time < end;)
        {
            if (pointing && Time.time > pointUntil)
            {
                animator.CrossFadeInFixedTime("Locomotion", 0.2f);
                pointing = false;
            }
            // Roughly follow them as they move.
            Vector3 dir = miss * (target.position - transform.position); dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 180f * Time.deltaTime);
            yield return null;
        }

        Staring = false;
        stareRoutine = null;
        brain = StartCoroutine(Brain());
    }

    void StopStaring()
    {
        if (stareRoutine != null) StopCoroutine(stareRoutine);
        stareRoutine = null;
        Staring = false;
    }

    // A couple of seconds of stuttering animation: the visible trace of being converted.
    public void Glitch(float duration) => StartCoroutine(GlitchRoutine(duration));

    IEnumerator EngageRoutine()
    {
        ReadyToAnswer = false;
        if (seated)
        {
            animator.CrossFadeInFixedTime("Locomotion", 0.25f);
            yield return Slide(transform.position, seatStandPos, 0.35f);
            seated = false;
        }
        SetMoving(false);
        animator.CrossFadeInFixedTime("Locomotion", 0.2f);
        yield return TurnTo(talkingTo.position - transform.position, 0.35f);
        ReadyToAnswer = true;

        // Keep facing the player while they hang around.
        while (true)
        {
            if (!Answering)
            {
                Vector3 dir = talkingTo.position - transform.position; dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 240f * Time.deltaTime);
            }
            yield return null;
        }
    }

    IEnumerator AnswerRoutine(bool nod, bool quiet)
    {
        Answering = true;
        // Only while the one they saw change is still changed (turned back = nothing to worry about any more).
        if (witnessed != null && witnessed.onImposterSide && Time.time < witnessedUntil)
        {
            // A nervous glance at whoever they saw change, before answering.
            yield return TurnTo(witnessed.transform.position - transform.position, 0.3f);
            yield return new WaitForSeconds(1f);
            yield return TurnTo(talkingTo.position - transform.position, 0.3f);
        }
        // The sound goes with the nod or shake itself, after any glance.
        if (!quiet) GameAudio.Play(nod ? "maximize_007" : "minimize_009", 0.45f);
        yield return Play(nod ? "emote-yes" : "emote-no", EmoteLength * 2f);
        Answering = false;
    }

    IEnumerator GlitchRoutine(float duration)
    {
        float end = Time.time + duration;
        while (Time.time < end)
        {
            glitch = Random.value < 0.5f ? 0f : Random.Range(1.8f, 2.6f);
            yield return new WaitForSeconds(Random.Range(0.08f, 0.22f));
        }
        glitch = 1f;
    }

    void StopTalking()
    {
        if (engageRoutine != null) StopCoroutine(engageRoutine);
        if (answerRoutine != null) StopCoroutine(answerRoutine);
        engageRoutine = answerRoutine = null;
        Engaged = ReadyToAnswer = Answering = false;
    }

    void StopBrain()
    {
        if (brain != null) StopCoroutine(brain);
        brain = null;
        strolling = false;
        ReleaseSpot();
    }

    // ---------- Decision loop ----------

    IEnumerator Brain()
    {
        yield return new WaitForSeconds(Random.Range(0f, 3f));
        while (true)
        {
            float[] weights = { strollWeight, shopWeight, sitWeight, fountainWeight, chatWeight };
            switch (WeightedPick(weights))
            {
                case 0: yield return Stroll(); break;
                case 1: yield return Shop(); break;
                case 2: yield return SitOnBench(); break;
                case 3: yield return AdmireFountain(); break;
                case 4: yield return StartChat(); break;
            }
            yield return null;
        }
    }

    static int WeightedPick(float[] weights)
    {
        float r = Random.value * weights.Sum();
        for (int i = 0; i < weights.Length; i++)
        {
            if (r < weights[i]) return i;
            r -= weights[i];
        }
        return 0;
    }

    // ---------- Activities ----------

    IEnumerator Stroll()
    {
        strolling = true;
        yield return WalkTo(RandomPlazaPoint(), 15f);
        yield return TurnTo(Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward, 0.8f); // look around
        yield return new WaitForSeconds(Random.Range(1f, 4f));
        strolling = false;
    }

    IEnumerator Shop()
    {
        var spot = ClaimSpot(ActivitySpot.Kind.Shop);
        if (spot == null) yield break;
        yield return WalkTo(spot.ApproachPoint, 20f);
        if (arrived)
        {
            yield return TurnTo(spot.transform.forward);
            int browses = Random.Range(1, 4);
            for (int i = 0; i < browses; i++)
            {
                yield return Play(Random.value < 0.5f ? "interact-right" : "interact-left", EmoteLength * Random.Range(2, 4));
                yield return new WaitForSeconds(Random.Range(0.3f, 1.2f));
            }
            if (Random.value < 0.5f) yield return Play("pick-up", PickUpLength + 0.4f);
        }
        ReleaseSpot();
    }

    IEnumerator SitOnBench()
    {
        var spot = ClaimSpot(ActivitySpot.Kind.Bench);
        if (spot == null) yield break;
        yield return WalkTo(spot.ApproachPoint, 20f);
        if (arrived)
        {
            yield return TurnTo(spot.transform.forward);

            // Leave the nav mesh and slide back onto the seat.
            Vector3 standPos = transform.position;
            Vector3 seatPos = spot.transform.position + Vector3.up * sitHeight;
            obstacle.enabled = false;
            animator.CrossFadeInFixedTime("sit", 0.25f);
            yield return Slide(standPos, seatPos, 0.35f);
            seated = true;
            seatStandPos = standPos;

            yield return new WaitForSeconds(Random.Range(5f, 12f));

            animator.CrossFadeInFixedTime("Locomotion", 0.25f);
            yield return Slide(seatPos, standPos, 0.35f);
            seated = false;
            obstacle.enabled = true;
        }
        ReleaseSpot();
    }

    IEnumerator AdmireFountain()
    {
        var spot = ClaimSpot(ActivitySpot.Kind.Fountain);
        if (spot == null) yield break;
        yield return WalkTo(spot.ApproachPoint, 20f);
        if (arrived)
        {
            yield return TurnTo(spot.transform.forward);
            yield return new WaitForSeconds(Random.Range(2f, 4f));
            yield return new WaitForSeconds(Random.Range(1f, 3f));
        }
        ReleaseSpot();
    }

    IEnumerator StartChat()
    {
        Vector3 me = transform.position;
        var partner = All
            .Where(v => v != this && v.strolling && (v.transform.position - me).sqrMagnitude < 64f)
            .OrderBy(v => (v.transform.position - me).sqrMagnitude)
            .FirstOrDefault();
        if (partner == null) yield break;

        // Meet halfway, standing a little apart and facing each other.
        Vector3 mid = (me + partner.transform.position) * 0.5f;
        if (NavMesh.SamplePosition(mid, out var hit, 1.5f, NavMesh.AllAreas)) mid = hit.position;
        Vector3 dir = partner.transform.position - me; dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;

        float endTime = Time.time + Random.Range(10f, 16f);
        partner.JoinChat(this, mid + dir * 0.45f, endTime);
        yield return Chat(partner, mid - dir * 0.45f, endTime);
    }

    // Called by another villager who wants to talk: drop what we're doing (only ever a stroll).
    void JoinChat(VillagerAI other, Vector3 spot, float endTime)
    {
        if (brain != null) StopCoroutine(brain);
        strolling = false;
        SetMoving(false);
        animator.CrossFadeInFixedTime("Locomotion", 0.2f);
        brain = StartCoroutine(ChatThenCarryOn(other, spot, endTime));
    }

    IEnumerator ChatThenCarryOn(VillagerAI other, Vector3 spot, float endTime)
    {
        yield return Chat(other, spot, endTime);
        yield return Brain();
    }

    IEnumerator Chat(VillagerAI other, Vector3 spot, float endTime)
    {
        yield return WalkTo(spot, 10f);

        // Wait for the other one to show up.
        while (Time.time < endTime && other != null && (other.transform.position - transform.position).sqrMagnitude > 1.5f * 1.5f)
            yield return null;

        while (Time.time < endTime && other != null)
        {
            yield return TurnTo(other.transform.position - transform.position, 0.3f);
            float r = Random.value;
            if (Random.value < 0.5f) yield return Play(Random.value < 0.5f ? "interact-right" : "interact-left", EmoteLength * Random.Range(1, 3)); // gesturing
            else yield return new WaitForSeconds(Random.Range(0.8f, 2f));                                                                       // listening
        }
    }

    // ---------- Helpers ----------

    IEnumerator WalkTo(Vector3 destination, float timeout)
    {
        arrived = false;
        // Lift our own carved hole first, and give the nav mesh a moment to fill it back in;
        // otherwise the agent would snap to the edge of its own hole.
        obstacle.enabled = false;
        yield return null;
        yield return null;
        SetMoving(true);
        yield return null; // give the agent a frame to attach to the nav mesh after re-enabling
        if (!agent.isOnNavMesh) { SetMoving(false); yield break; }
        agent.SetDestination(destination);

        // Stuck check, once a second: if we haven't got meaningfully closer, try a slightly different spot so a
        // jam can untangle; after a few tries, settle for close enough and move on to something else.
        float checkAt = Time.time + 1f;
        float lastRemaining = float.MaxValue;
        int strikes = 0;
        for (float t = 0f; t < timeout; t += Time.deltaTime)
        {
            if (!agent.pathPending)
            {
                if (agent.pathStatus == NavMeshPathStatus.PathInvalid) break;
                float remaining = agent.remainingDistance;
                if (remaining <= agent.stoppingDistance + 0.1f) { arrived = Near(destination); break; }

                if (Time.time >= checkAt)
                {
                    if (lastRemaining - remaining < 0.2f)
                    {
                        if (++strikes >= 3) { arrived = Near(destination); break; }
                        agent.SetDestination(Nudge(destination, 0.6f * strikes));
                    }
                    else strikes = 0;
                    lastRemaining = remaining;
                    checkAt = Time.time + 1f;
                }
            }
            yield return null;
        }
        SetMoving(false);
    }

    // Really standing by `destination`. The agent's remaining distance isn't enough: when the destination can't be
    // reached it walks a partial path that stops short, its remaining distance reaches zero anyway, and a villager
    // counted as arrived would then slide from wherever it stopped onto a far-off bench.
    bool Near(Vector3 destination)
    {
        Vector3 d = destination - transform.position; d.y = 0f;
        return d.sqrMagnitude < ArriveSlack * ArriveSlack;
    }

    // A walkable point near `point`, up to `radius` away.
    static Vector3 Nudge(Vector3 point, float radius)
    {
        var offset = Random.insideUnitCircle * radius;
        var p = point + new Vector3(offset.x, 0f, offset.y);
        return NavMesh.SamplePosition(p, out var hit, radius + 0.5f, NavMesh.AllAreas) ? hit.position : point;
    }

    // Moving: a NavMeshAgent that steers around others. Standing still: a NavMeshObstacle others steer around.
    // Only one can be enabled at a time, or the agent would try to avoid its own obstacle.
    void SetMoving(bool moving)
    {
        if (moving)
        {
            obstacle.enabled = false;
            agent.enabled = true;
            agent.Warp(transform.position);
        }
        else
        {
            if (agent.enabled && agent.isOnNavMesh) agent.ResetPath();
            agent.enabled = false;
            obstacle.enabled = true;
        }
    }

    IEnumerator TurnTo(Vector3 direction, float duration = 0.4f)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) yield break;
        Quaternion from = transform.rotation, to = Quaternion.LookRotation(direction);
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            transform.rotation = Quaternion.Slerp(from, to, t);
            yield return null;
        }
        transform.rotation = to;
    }

    IEnumerator Slide(Vector3 from, Vector3 to, float duration)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        transform.position = to;
    }

    // Plays an animator state for a while, then blends back to walking/idling.
    IEnumerator Play(string state, float seconds)
    {
        animator.CrossFadeInFixedTime(state, 0.15f);
        yield return new WaitForSeconds(seconds);
        animator.CrossFadeInFixedTime("Locomotion", 0.2f);
    }

    Vector3 RandomPlazaPoint()
    {
        for (int i = 0; i < 10; i++)
        {
            var p = new Vector3(Random.Range(-plazaHalfSize, plazaHalfSize), 0f, Random.Range(-plazaHalfSize, plazaHalfSize));
            if (NavMesh.SamplePosition(p, out var hit, 1f, NavMesh.AllAreas)) return hit.position;
        }
        return transform.position;
    }

    ActivitySpot ClaimSpot(ActivitySpot.Kind kind)
    {
        var free = ActivitySpot.All.Where(s => s.kind == kind && s.occupant == null).ToList();
        if (free.Count == 0) return null;
        heldSpot = free[Random.Range(0, free.Count)];
        heldSpot.occupant = this;
        return heldSpot;
    }

    void ReleaseSpot()
    {
        if (heldSpot != null && heldSpot.occupant == this) heldSpot.occupant = null;
        heldSpot = null;
    }
}
