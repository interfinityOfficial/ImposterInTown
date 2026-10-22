using UnityEngine;

// Background music: "Blue Saga" (Epidemic Sound) as its three stems, kept in sync and mixed live. It's an uneasy
// piece, so it starts low and builds as midnight nears (or as the town is taken, whichever is further along):
// - early: quiet and thin; mostly bass, the accompaniment held back and the melody barely there
// - building: louder, the accompaniment fills in and the melody (the tune) comes forward
// - midnight: everything at full
// All of it is done with volume alone (no filters), since the web build has no audio filters and every version
// should sound the same.
// It starts when PLAY is pressed (straight away on PLAY AGAIN), fading in. Once the round is over, won or lost,
// it fades out together with the square's ambience, leaving the end screen to the jingle.
//
// Looping: the track runs 190.8 s but ends with an outro and silence. It loops at 140.96 s, which is 88 bars
// (11 phrases of 12.81 s, ~150 BPM) from the start: chorus 2 ends and the opening begins again. Two decks of
// the three stems take turns, each starting exactly on the loop point (PlayScheduled, sample accurate), while the
// one before rings on briefly and fades, so the seam carries the old chorus's tail instead of cutting it.
// Clips live in Assets/Resources/Music (imported compressed in memory by MusicImport).
public class MusicPlayer : MonoBehaviour
{
    const double LoopLength = 140.96;
    const float TailFade = 0.6f;       // how long the previous pass rings on after the loop point
    const double ScheduleLead = 0.25;  // seconds ahead the first pass is scheduled, so all stems start together
    static readonly string[] StemNames = { "Music/BlueSaga_Bass", "Music/BlueSaga_Instruments", "Music/BlueSaga_Melody" };
    const int Bass = 0, Instruments = 1, Melody = 2;

    [Range(0f, 1f)] public float volume = 0.35f;
    [Tooltip("How quickly the layers follow the game (fraction of full change per second).")]
    public float followSpeed = 0.35f;
    public float fadeInTime = 2f;

    readonly AudioSource[,] decks = new AudioSource[2, 3];
    readonly float[] deckGain = { 1f, 1f };
    readonly bool[] deckTail = new bool[2];
    int current;
    double nextStart;
    bool started;

    [Tooltip("Loudness at the very start of the round, as a fraction of `volume`; it builds to full by midnight.")]
    [Range(0f, 1f)] public float startLevel = 0.5f;
    [Tooltip("The melody's level at the start, as a fraction of full; it comes forward as the round builds.")]
    [Range(0f, 1f)] public float startMelody = 0.25f;
    [Tooltip("The accompaniment's level at the start, as a fraction of full.")]
    [Range(0f, 1f)] public float startInstruments = 0.5f;

    float unease, clock;                                  // targets from the game
    float build;                                          // 0 = calm start, 1 = midnight / town nearly lost
    float melodyLevel, instrumentLevel;
    float master;                                         // fades in at the start, out at the end
    bool ended;

    // How far the music has built: 0 = calm start (quiet), 1 = midnight (full). Ambience follows this.
    public float Build => build;

    // Unease (0 = calm, 1 = nearly lost) and the clock (0 = start of the round, 1 = midnight).
    public void SetMood(float unease, float clock)
    {
        this.unease = Mathf.Clamp01(unease);
        this.clock = Mathf.Clamp01(clock);
    }

    // Won or lost: fades out alongside the ambience, over the same time.
    public void End() => ended = true;

    // Starts the music (PLAY pressed, or straight away on PLAY AGAIN), fading in from silence.
    public void Begin()
    {
        if (started || decks[0, 0] == null) return;
        double first = AudioSettings.dspTime + ScheduleLead;
        Schedule(0, first);
        current = 0;
        nextStart = first + LoopLength;
        Schedule(1, nextStart);
        started = true;
    }

    void Start()
    {
        var clips = new AudioClip[StemNames.Length];
        for (int s = 0; s < clips.Length; s++)
        {
            clips[s] = Resources.Load<AudioClip>(StemNames[s]);
            if (clips[s] == null) { Debug.LogWarning("Missing music stem " + StemNames[s]); enabled = false; return; }
            clips[s].LoadAudioData();
        }

        for (int d = 0; d < 2; d++)
        {
            var deck = new GameObject("Music Deck " + (d + 1));
            deck.transform.SetParent(transform, false);
            for (int s = 0; s < clips.Length; s++)
            {
                var source = deck.AddComponent<AudioSource>();
                source.clip = clips[s];
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.volume = 0f;
                source.priority = 0; // never the voice dropped when many sounds play at once
                decks[d, s] = source;
            }
        }
        Mix(0f, true);
    }

    void Schedule(int deck, double at)
    {
        deckGain[deck] = 1f;
        deckTail[deck] = false;
        for (int s = 0; s < 3; s++) decks[deck, s].PlayScheduled(at);
    }

    void Update()
    {
        if (!started) return;
        float dt = Time.unscaledDeltaTime;

        // The other deck has just come in on the loop point: this one rings on for a moment and fades.
        if (AudioSettings.dspTime >= nextStart)
        {
            deckTail[current] = true;
            current = 1 - current;
            nextStart += LoopLength;
        }
        for (int d = 0; d < 2; d++)
        {
            if (!deckTail[d]) continue;
            deckGain[d] = Mathf.MoveTowards(deckGain[d], 0f, dt / TailFade);
            if (deckGain[d] <= 0f)
            {
                for (int s = 0; s < 3; s++) decks[d, s].Stop();
                Schedule(d, nextStart); // its next turn
            }
        }

        Mix(dt, false);

        master = Mathf.MoveTowards(master, ended ? 0f : 1f, dt / (ended ? Ambience.EndFadeTime : fadeInTime));

        float[] stem = { 1f, instrumentLevel, melodyLevel };
        for (int d = 0; d < 2; d++)
        {
            for (int s = 0; s < 3; s++)
                decks[d, s].volume = volume * master * Mathf.Lerp(startLevel, 1f, Mathf.SmoothStep(0f, 1f, build)) * deckGain[d] * stem[s];
        }
    }

    // The layers ease towards how far the round has built (instantly when `snap`).
    void Mix(float dt, bool snap)
    {
        build = snap ? Mathf.Max(clock, unease) : Mathf.MoveTowards(build, Mathf.Max(clock, unease), followSpeed * dt);
        melodyLevel = Mathf.Lerp(startMelody, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.8f, build)));
        instrumentLevel = Mathf.Lerp(startInstruments, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.6f, build)));
    }
}
