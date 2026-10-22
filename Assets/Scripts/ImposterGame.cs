using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// The town-square imposter game.
// Every outfit appears twice, but outfits mean nothing. One random villager is the imposter. It starts with a couple
// of copies already made, chosen at random. Every so often it secretly converts someone away from you (never right
// beside you); copies keep their looks. Turning a copy back also holds off its next conversion for a bit.
// Walk up to anyone and press Space: copies can't help but nod; everyone else shakes their head, and so does the
// imposter, which lies. Press F twice to accuse. A copy turns back for free, and then turns to stare at the one
// who changed it, the main clue. Accusing the imposter wins; accusing anyone else who shook their head costs one
// of your hunches. If more than half the town is on the imposter's side, you lose; and if it's still free at
// midnight (the round's deadline, shown as the sun going down and a MIDNIGHT IN countdown), you lose too.
// After PLAY, two villagers gossip in speech bubbles to set up the story (skippable); tips then appear the first
// time each thing happens, instead of a wall of rules.
public class ImposterGame : MonoBehaviour
{
    public Transform player;

    [Header("UI")]
    public Font titleFont;
    public Font uiFont;
    public Font strongFont;
    [TextArea] public string title = "IMPOSTER\nIN TOWN";

    [Header("Talking")]
    public float talkRange = 1.1f;
    public float releaseRange = 1.3f;
    [Tooltip("How much closer another villager must be before the conversation switches to them (only while you're moving).")]
    public float switchMargin = 0.15f;
    public int accusations = 3;
    [Tooltip("How long a copy that's been turned back stares at the imposter.")]
    public float staredFor = 5f;
    [Tooltip("How far off a turned-back copy's aim can be, in degrees, so it points at an area, not a person.")]
    public float stareAimError = 30f;

    [Header("Imposter")]
    [Tooltip("Copies the imposter has already made when the game starts, chosen at random.")]
    public int startingCopies = 2;

    [Header("Deadline")]
    [Tooltip("Seconds from the start of play to midnight, when the town is lost if the imposter is still free.")]
    public float roundLength = 240f;
    [Tooltip("A bell tolls every 10 seconds for this long before midnight.")]
    public float finalBells = 30f;

    [Header("Intro")]
    [Tooltip("The villagers' gossip after PLAY. Always skipped by PLAY AGAIN.")]
    public bool playIntro = true;

    [Header("Conversions")]
    public float firstConversionDelay = 30f;
    public float speedUpPerConversion = 2f;
    public float minConversionInterval = 14f;
    [Tooltip("Seconds each copy you turn back pushes the imposter's next conversion away.")]
    public float freedCopyDelay = 6f;
    [Tooltip("Never convert anyone this close to the player.")]
    public float minConvertDistance = 8f;
    public float witnessRadius = 5f;
    public float witnessMemory = 90f;

    static readonly string[] Glitches = { "glitch_001", "glitch_002", "glitch_003" };

    enum Phase { Intro, Playing, Won, Lost }

    List<VillagerAI> villagers;
    VillagerAI imposter;
    int loseAt;
    Phase phase = Phase.Intro;
    VillagerAI focus;
    float armedUntil;
    Camera cam;

    ImposterHud hud;
    PlayerController playerControl;
    bool started;
    DayNight dayNight;
    MusicPlayer music;
    Ambience ambience;
    int startingSide;   // villagers on the imposter's side at the start (the imposter and its first copies)
    bool replay;        // came from PLAY AGAIN: no start screen, no intro
    bool hintedTalk, hintedNod, hintedChange, hintedCopy;   // first-time tips already shown
    float playStartedAt = -1f;   // when the clock started (after the intro)
    float nextConversionAt;      // when the imposter next tries to convert someone
    Vector3 lastPlayerPos;       // to tell whether the player is walking
    int nextBell;                // seconds-before-midnight of the next bell to toll
    AudioSource bellSource;
    AudioClip bell;

    float endedAt = -1f;         // when the round ended (won or lost): the clock stops there
    float Elapsed => playStartedAt < 0f ? 0f : (endedAt >= 0f ? endedAt : Time.time) - playStartedAt;
    float Remaining => Mathf.Max(0f, roundLength - Elapsed);

    static readonly string[] IntroLines =
    {
        "DID YOU HEAR? SOMETHING CAME TO TOWN LAST NIGHT.",
        "THEY SAY IT WEARS A NEIGHBOUR'S FACE...",
        "AND IT MAKES MORE OF ITSELF WHEN NO ONE'S LOOKING.",
        "AND IT WILL NEVER ADMIT WHAT IT IS.",
        "YOU'LL HELP US FIND IT, WON'T YOU?",
    };

    int SideCount => villagers.Count(v => v.onImposterSide);

    // ---------- Setup ----------

    void Start()
    {
        cam = Camera.main;
        villagers = FindObjectsByType<VillagerAI>().ToList();

        imposter = villagers[Random.Range(0, villagers.Count)];
        imposter.onImposterSide = true;

        // It got here before you: a few villagers, anyone at all, are already copies.
        foreach (var v in villagers.Where(v => v != imposter).OrderBy(_ => Random.value).Take(startingCopies))
            v.onImposterSide = true;

        loseAt = villagers.Count / 2 + 1; // more than half: with 20 villagers, 11 on its side = 10 taken
        startingSide = SideCount;

        // The light follows the unease: afternoon at the start, night when the town is nearly lost.
        dayNight = FindAnyObjectByType<DayNight>();
        if (dayNight == null) dayNight = gameObject.AddComponent<DayNight>();
        music = FindAnyObjectByType<MusicPlayer>();
        if (music == null) music = gameObject.AddComponent<MusicPlayer>();
        ambience = gameObject.AddComponent<Ambience>();
        ambience.Init(player, cam, music);

        hud = new GameObject("HUD").AddComponent<ImposterHud>();
        hud.Init(titleFont, uiFont, strongFont, cam);
        hud.InitMeters(loseAt - 1, accusations); // every villager it can take, not counting itself
        playerControl = player.GetComponent<PlayerController>();
        // Villagers path around the player when they stand still, instead of piling up behind them.
        VillagerAI.UseCarving(player.GetComponent<UnityEngine.AI.NavMeshObstacle>());
        if (ImposterHud.skipStartScreen)
        {
            // PLAY AGAIN goes straight into a new round.
            ImposterHud.skipStartScreen = false;
            replay = true;
            hud.ShowHudNow();
            started = true;
        }
        else
        {
            if (playerControl != null) playerControl.enabled = false;
            hud.ShowStartScreen(title, () => started = true);
        }
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        yield return new WaitUntil(() => started);
        music.Begin();
        ambience.Begin();
        if (!replay && playIntro) yield return Intro();
        if (!replay) hud.FadeInHud();
        if (playerControl != null) playerControl.enabled = true;
        cam.GetComponent<OrbitCamera>().mouseLook = true; // the mouse turns the camera from here on
        phase = Phase.Playing;
        playStartedAt = Time.time;
        nextBell = Mathf.FloorToInt(finalBells);
        hud.ShowHint("FIND IT BEFORE MIDNIGHT\nOR IT TAKES THE WHOLE TOWN", 3.5f, HintTarget.Clock);

        // A deadline rather than a wait, so turning a copy back can push it further away.
        float interval = firstConversionDelay;
        nextConversionAt = Time.time + interval;
        while (phase == Phase.Playing)
        {
            yield return null;
            if (Time.time < nextConversionAt) continue;
            if (!TryConvert()) { nextConversionAt = Time.time + 2f; continue; } // nobody it can reach yet
            interval = Mathf.Max(minConversionInterval, interval - speedUpPerConversion);
            nextConversionAt = Time.time + interval;
        }
    }

    // ---------- Intro ----------

    // Two villagers gossip in speech bubbles while the camera frames them; the last line is said to you.
    IEnumerator Intro()
    {
        if (!GossipPair(out var a, out var b, out float viewYaw, out float viewPitch)) yield break;
        var orbit = cam.GetComponent<OrbitCamera>();
        float yaw = orbit.yaw, pitch = orbit.pitch, distance = orbit.distance, screenY = orbit.targetScreenY;

        bool skipped = false;
        hud.BeginIntro();
        a.Engage(b.transform);
        b.Engage(a.transform);

        Vector3 mid = GossipFocus(a, b);
        yield return MoveCamera(orbit, mid, viewYaw, viewPitch, IntroDistance, 0.5f, 1.4f, () => skipped = skipped || SkipPressed());

        for (int i = 0; i < IntroLines.Length && !skipped; i++)
        {
            bool last = i == IntroLines.Length - 1;
            var speaker = last ? b : i % 2 == 0 ? a : b;
            if (last) { b.Disengage(); b.Engage(cam.transform); } // turns to face the camera: talking to you
            hud.Say(speaker.transform, IntroLines[i]);
            StartCoroutine(GestureWhenReady(speaker));
            yield return null; // the press that brought up this line doesn't also skip it

            while (!skipped)
            {
                if (SkipPressed()) { skipped = true; break; }
                bool next = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                            (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !ImposterHud.PointerOverUI);
                if (next)
                {
                    if (!hud.SpeechTyped) hud.FinishTyping();
                    else break;
                }
                yield return null;
            }
        }

        hud.EndIntro();
        a.Disengage();
        b.Disengage();
        yield return MoveCamera(orbit, player.position + orbit.followOffset, yaw, pitch, distance, screenY, 1.0f, null);
        orbit.follow = player;
    }

    static bool SkipPressed() => Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;

    const float IntroDistance = 5.5f;

    // The closest two villagers (never the imposter) that the camera can see clearly, and the angle to see them
    // from. Pairs are tried nearest first; each gets a clear-view search, so the intro never frames a wall.
    bool GossipPair(out VillagerAI a, out VillagerAI b, out float yaw, out float pitch)
    {
        var pool = villagers.Where(v => v != imposter).ToList();
        var pairs = new List<(VillagerAI, VillagerAI, float)>();
        for (int i = 0; i < pool.Count; i++)
            for (int j = i + 1; j < pool.Count; j++)
            {
                float d = Flat(pool[i].transform.position - pool[j].transform.position).sqrMagnitude;
                if (d > 0.36f) pairs.Add((pool[i], pool[j], d));
            }

        var orbit = cam.GetComponent<OrbitCamera>();
        foreach (var (pa, pb, _) in pairs.OrderBy(p => p.Item3).Take(25))
            if (ClearView(pa, pb, orbit.yaw, out yaw, out pitch)) { a = pa; b = pb; return true; }

        // Nothing clear (very crowded): fall back to the nearest pair from high above.
        a = b = null; yaw = orbit.yaw; pitch = 60f;
        if (pairs.Count == 0) return false;
        var nearest = pairs.OrderBy(p => p.Item3).First();
        a = nearest.Item1; b = nearest.Item2;
        return true;
    }

    static Vector3 GossipFocus(VillagerAI a, VillagerAI b) =>
        (a.transform.position + b.transform.position) * 0.5f + Vector3.up * 0.45f;

    // Tries side-on views first (whichever side is nearer `preferredYaw`), then angles further round and higher up,
    // and keeps the first one where nothing (buildings, trees, stalls) sits between the camera and the pair.
    bool ClearView(VillagerAI a, VillagerAI b, float preferredYaw, out float yaw, out float pitch)
    {
        Vector3 focus = GossipFocus(a, b);
        Vector3 axis = Flat(b.transform.position - a.transform.position);
        float side = Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg + 90f;
        if (Mathf.Abs(Mathf.DeltaAngle(side, preferredYaw)) > 90f) side += 180f;

        foreach (float p in new[] { 22f, 35f, 50f })
            foreach (float offset in new[] { 0f, 180f, 30f, -30f, 150f, -150f, 60f, -60f })
            {
                float y = side + offset;
                Vector3 back = Quaternion.Euler(p, y, 0f) * Vector3.back;
                if (Unblocked(focus, back, IntroDistance, a, b))
                {
                    yaw = y; pitch = p;
                    return true;
                }
            }
        yaw = side; pitch = 22f;
        return false;
    }

    // Nothing solid between `focus` and a camera `distance` away along `back`, ignoring characters and benches.
    bool Unblocked(Vector3 focus, Vector3 back, float distance, VillagerAI a, VillagerAI b)
    {
        foreach (var hit in Physics.SphereCastAll(focus, 0.35f, back, distance, ~0, QueryTriggerInteraction.Ignore))
            if (!SeeThrough(hit.collider)) return false;
        // And both speakers themselves are in sight from there (not hidden behind something next to them).
        Vector3 camPos = focus + back * distance;
        foreach (var v in new[] { a, b })
        {
            Vector3 head = v.transform.position + Vector3.up * 0.5f;
            Vector3 dir = head - camPos;
            foreach (var hit in Physics.RaycastAll(camPos, dir.normalized, dir.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!SeeThrough(hit.collider)) return false;
        }
        return true;
    }

    // Characters, and benches: a bench's collider stands taller than the bench so the player can't climb it,
    // but the bench itself is low enough to see over.
    static bool SeeThrough(Collider c) =>
        c.GetComponentInParent<VillagerAI>() != null || c.GetComponentInParent<CharacterController>() != null ||
        c.name == "stall-bench";

    IEnumerator GestureWhenReady(VillagerAI speaker)
    {
        for (float t = 0f; t < 1.5f && !speaker.ReadyToAnswer; t += Time.deltaTime) yield return null;
        speaker.Gesture();
    }

    // Eases the camera to look at `focus` from yaw/pitch/distance, with the focus at `screenY` on screen.
    IEnumerator MoveCamera(OrbitCamera orbit, Vector3 focus, float yaw, float pitch, float distance, float screenY,
                           float duration, System.Func<bool> abort)
    {
        Vector3 fromFocus = orbit.follow != null ? orbit.follow.position + orbit.followOffset : orbit.target;
        float fromYaw = orbit.yaw, fromPitch = orbit.pitch, fromDistance = orbit.distance, fromScreenY = orbit.targetScreenY;
        orbit.follow = null;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            if (abort != null && abort()) yield break;
            float k = Mathf.SmoothStep(0f, 1f, t);
            orbit.target = Vector3.Lerp(fromFocus, focus, k);
            orbit.yaw = Mathf.LerpAngle(fromYaw, yaw, k);
            orbit.pitch = Mathf.Lerp(fromPitch, pitch, k);
            orbit.distance = Mathf.Lerp(fromDistance, distance, k);
            orbit.targetScreenY = Mathf.Lerp(fromScreenY, screenY, k);
            yield return null;
        }
        orbit.target = focus;
        orbit.yaw = yaw;
        orbit.pitch = pitch;
        orbit.distance = distance;
        orbit.targetScreenY = screenY;
    }

    // ---------- Conversions ----------

    bool TryConvert()
    {
        var candidates = villagers.Where(v => !v.onImposterSide && !v.Engaged && !v.Staring &&
            Flat(v.transform.position - player.position).magnitude > minConvertDistance).ToList();
        if (candidates.Count == 0) return false;

        var target = candidates[Random.Range(0, candidates.Count)];
        target.onImposterSide = true;
        target.witnessed = null; // a copy isn't itself any more: it forgets anything it saw
        target.Glitch(2.5f);

        foreach (var w in villagers)
            if (w != target && !w.onImposterSide && Flat(w.transform.position - target.transform.position).magnitude < witnessRadius)
            {
                w.witnessed = target;
                w.witnessedUntil = Time.time + witnessMemory;
            }

        if (SideCount >= loseAt) { Lose("TOO MANY FACES NOW SMILE THE SAME SMILE"); return true; }

        // Nothing says who, just that it happened.
        if (!hintedChange)
        {
            hintedChange = true;
            hud.ShowHint("SOMEONE JUST CHANGED, SOMEWHERE IN TOWN\nIF THE BAR FILLS, THE TOWN IS ITS", 5f, HintTarget.Taken);
        }
        return true;
    }

    // ---------- Talking and accusing ----------

    void Update()
    {
        if (phase == Phase.Playing)
        {
            HandleTalking();
            UpdateDeadline();
        }
        else if ((phase == Phase.Won || phase == Phase.Lost) && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            ImposterHud.skipStartScreen = true;
            Restart();
        }
        UpdateHud();
    }

    // Midnight ends the round; the last stretch is counted down by a tolling bell.
    void UpdateDeadline()
    {
        float remaining = Remaining;
        if (nextBell > 0 && remaining <= nextBell)
        {
            Toll(0.6f);
            nextBell -= 10;
        }
        if (remaining <= 0f)
        {
            Toll(1f);
            Lose("MIDNIGHT CAME BEFORE YOU FOUND IT", 1.2f); // the jingle waits for the last bell to ring out
        }
    }

    void Toll(float volume)
    {
        if (bellSource == null)
        {
            bellSource = gameObject.AddComponent<AudioSource>();
            bell = BellClip();
        }
        bellSource.PlayOneShot(bell, volume);
    }

    // A church-bell-ish tone made in code: a low strike with a few inharmonic partials that ring out.
    static AudioClip BellClip()
    {
        const int rate = 44100;
        const float length = 3.5f, fundamental = 196f;
        float[] partials = { 0.5f, 1f, 1.19f, 1.5f, 2f, 2.74f };
        float[] levels = { 0.5f, 1f, 0.6f, 0.4f, 0.3f, 0.15f };
        int samples = (int)(rate * length);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / rate, v = 0f;
            for (int k = 0; k < partials.Length; k++)
                v += levels[k] * Mathf.Sin(2f * Mathf.PI * fundamental * partials[k] * t) * Mathf.Exp(-t * (0.8f + k * 0.6f));
            data[i] = v * 0.18f * Mathf.Min(1f, i / 200f);
        }
        var clip = AudioClip.Create("Bell", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void UpdateHud()
    {
        // TAKEN: everyone on its side apart from itself (never who). HUNCHES: accusations left.
        int side = SideCount;
        hud.SetStats(side - 1, Mathf.Max(accusations, 0));
        // The clock: MIDNIGHT IN, and the sun's position. Unease (how many are taken) sets the atmosphere.
        hud.SetClock(Remaining, phase == Phase.Playing && Remaining <= finalBells);
        if (phase == Phase.Intro || phase == Phase.Playing)
        {
            float unease = (side - startingSide) / (float)Mathf.Max(1, loseAt - 1 - startingSide);
            dayNight.SetClock(Elapsed / roundLength);
            dayNight.SetUnease(unease);
            music.SetMood(unease, Elapsed / roundLength);
        }

        if (phase == Phase.Playing && focus != null && focus.ReadyToAnswer && !focus.Answering)
        {
            bool armed = Time.time < armedUntil;
            hud.ShowPrompt(focus.transform, armed);
        }
        else hud.HidePrompt();
    }

    // Fades through black, so the end screen's lighting doesn't cut straight to the new round's daylight.
    void Restart()
    {
        ScreenFade.Through(() =>
        {
            hud.RememberStats();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        });
    }

    void HandleTalking()
    {
        // Talk to whoever is closest. Once you're talking to someone it stays locked on them while you stand
        // still, however close others wander; it only switches to someone clearly closer while you're walking
        // (the margin stops flicker between two equally close villagers), and never cuts anyone off mid-answer.
        bool walking = Time.deltaTime > 0f && Flat(player.position - lastPlayerPos).magnitude / Time.deltaTime > 0.3f;
        lastPlayerPos = player.position;
        var nearest = villagers.Where(v => !v.Staring)
            .OrderBy(v => Flat(v.transform.position - player.position).sqrMagnitude).FirstOrDefault();
        float nearestDist = nearest != null ? Flat(nearest.transform.position - player.position).magnitude : float.MaxValue;
        if (focus != null)
        {
            float focusDist = Flat(focus.transform.position - player.position).magnitude;
            bool tooFar = focusDist > releaseRange;
            bool someoneCloser = walking && nearest != focus && nearestDist < talkRange && nearestDist < focusDist - switchMargin && !focus.Answering;
            if (tooFar || someoneCloser)
            {
                focus.Disengage();
                focus = null;
            }
        }
        if (focus == null && nearestDist < talkRange)
        {
            focus = nearest;
            focus.Engage(player);
            armedUntil = 0f;
            // A first-time tip: only when nothing else is on screen, or it waits for the next person you walk up to.
            if (!hintedTalk && !hud.HintBusy)
            {
                hintedTalk = true;
                hud.ShowHint("ASK IF THEY FEEL STRANGE", 3f);
            }
        }
        if (focus == null || Keyboard.current == null) return;

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            // Copies nod; the imposter lies and shakes its head like everyone else.
            if (focus.Answer(IsCopy(focus)))
            {
                armedUntil = 0f;
                if (IsCopy(focus) && !hintedNod)
                {
                    hintedNod = true;
                    hud.ShowHint("THE CHANGED ONES CAN'T HELP BUT NOD\nACCUSE THEM TO TURN THEM BACK", 5f);
                }
            }
        }

        if (Keyboard.current.fKey.wasPressedThisFrame && focus.ReadyToAnswer && !focus.Answering)
        {
            if (Time.time < armedUntil) Accuse(focus);
            else armedUntil = Time.time + 3f;
        }
    }

    void Accuse(VillagerAI v)
    {
        armedUntil = 0f;
        if (v == imposter)
        {
            Win();
        }
        else if (v.onImposterSide)
        {
            // A copy: it snaps back to normal, at no cost, and turns to stare at the one who changed it.
            // The imposter is set back too: its next conversion comes a little later.
            v.onImposterSide = false;
            nextConversionAt += freedCopyDelay;
            if (focus == v) { focus.Disengage(); focus = null; }
            v.Glitch(1.5f);
            GameAudio.Burst(Glitches, 5, 0.06f, 0.35f);
            GameAudio.PlayAfter(0.3f, "jingles_PIZZI09", 0.6f, 0f); // the stutter first, then the relief
            v.StareAt(imposter.transform, staredFor, stareAimError);
            if (!hintedCopy)
            {
                hintedCopy = true;
                hud.ShowHint("THEY'RE THEMSELVES AGAIN\nAND THEY'RE STARING AT WHOEVER DID IT", 6f);
            }
            else hud.ShowHint("THEY'RE THEMSELVES AGAIN\nWHO ARE THEY LOOKING AT?", 3f);
        }
        else
        {
            accusations--;
            v.Answer(false, quiet: true);
            GameAudio.Play("error_005", 0.5f);
            if (accusations <= 0) Lose("YOU RAN OUT OF HUNCHES", 0.6f); // after the "wrong" sound
            else hud.ShowHint($"THEY JUST LOOK HURT\n{(accusations == 1 ? "ONE HUNCH" : accusations + " HUNCHES")} LEFT", 4f, HintTarget.Hunches);
        }
    }

    void Win()
    {
        phase = Phase.Won;
        endedAt = Time.time;
        EndConversation();
        foreach (var v in villagers.Where(v => v.onImposterSide && v != imposter)) v.onImposterSide = false;
        imposter.Freeze("die");
        dayNight.ShowEnding(true);
        GameAudio.Play("jingles_PIZZI10", 0.8f, 0f);
        music.End();
        ambience.End();
        hud.ShowEndScreen("IMPOSTER\nFOUND", "THE TOWN WAKES UP AS ITSELF AGAIN", Restart);
    }

    // `jingleDelay`: lets the sound of what ended the round (the bell, a wrong accusation) be heard first.
    void Lose(string reason, float jingleDelay = 0f)
    {
        dayNight.ShowEnding(false);
        GameAudio.PlayAfter(jingleDelay, "jingles_PIZZI07", 0.8f, 0f);
        music.End();
        ambience.End();
        phase = Phase.Lost;
        endedAt = Time.time;
        EndConversation();
        // Everyone else falls; the imposter is left standing, nodding.
        foreach (var v in villagers.Where(v => v != imposter)) v.Freeze("die");
        imposter.Freeze("emote-yes");
        hud.ShowEndScreen("THE TOWN\nIS LOST", reason, Restart);
    }

    void EndConversation()
    {
        if (focus != null) focus.Disengage();
        focus = null;
        if (playerControl != null) playerControl.enabled = false; // the round is over; leave the mouse for PLAY AGAIN
        cam.GetComponent<OrbitCamera>().mouseLook = false;
    }

    bool IsCopy(VillagerAI v) => v.onImposterSide && v != imposter;

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
