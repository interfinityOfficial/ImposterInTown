using UnityEngine;

// Sounds of the square itself, heard from where the player stands:
// - The town (Epidemic Sound "Town Centre, Old Town Square, Distant Crowd", a seamless 60 s stereo loop in
//   Assets/Resources/Ambience) plays everywhere as a bed. It's the everyday square, so it gives way to the
//   music: full while the music is quiet at the start, sinking as the music builds towards midnight.
// - The fountain (Epidemic Sound "Metal Water Fountain, Continuous Flow", cut to a seamless 30 s mono loop in
//   Assets/Resources/Ambience) plays from the fountain in 3D: soft even beside it, and gone a few steps away.
//   The fade with distance is worked out here rather than left to the AudioSource's rolloff: the web build plays
//   through the browser's audio, which only has its own few fall-off shapes, so a custom curve wouldn't hold
//   there. The source keeps 3D on for left/right panning, with its own fall-off pushed out of reach.
// All of it (town, fountain and the villagers' footsteps) starts when PLAY is pressed (straight away on PLAY
// AGAIN), fading in with the music, and fades out together on either end screen.
// - The ears move from the camera to the player's head (turned the way the camera looks), so walking up to
//   something brings its sound close; the camera trails 8.5 m behind, which would flatten every distance.
public class Ambience : MonoBehaviour
{
    [Range(0f, 1f)] public float fountainVolume = 0.1f;
    [Tooltip("Full volume within this distance of the fountain (m).")]
    public float fountainNear = 1.5f;
    [Tooltip("Silent beyond this distance (m); it falls away quickly after fountainNear.")]
    public float fountainFar = 10f;
    public float fadeInTime = 2f;
    public float earHeight = 1f;
    [Range(0f, 1f)] public float townVolume = 0.12f;
    [Tooltip("The town's level once the music is at full, as a fraction of townVolume.")]
    [Range(0f, 1f)] public float townUnderMusic = 0.15f;

    Transform player, view, ears, fountain;
    MusicPlayer music;
    AudioSource town, fountainSource;
    float startFade;       // 0 until PLAY, then fades in
    float endFade = 1f;    // fades to 0 once the round is over
    bool started, ended;
    public const float EndFadeTime = 3f;   // the music fades out over the same time

    // The square's sounds have started (villagers' footsteps wait for this too).
    public static bool Live { get; private set; }
    // The shared fade-in / fade-out every ambient sound is multiplied by (villagers' footsteps use it too).
    public static float Level { get; private set; }

    // Won or lost: every ambient sound fades out together.
    public void End() => ended = true;

    // PLAY pressed (or straight into a round on PLAY AGAIN): the square's sounds fade in.
    public void Begin()
    {
        if (started) return;
        started = true;
        Live = true;
        if (town != null) town.Play();
        if (fountainSource != null) fountainSource.Play();
    }

    public void Init(Transform player, Camera cam, MusicPlayer music)
    {
        this.player = player;
        this.music = music;
        Live = false; // a new scene: quiet until PLAY
        Level = 0f;
        view = cam.transform;

        var townClip = Resources.Load<AudioClip>("Ambience/Town");
        if (townClip != null)
        {
            town = gameObject.AddComponent<AudioSource>();
            town.clip = townClip;
            town.loop = true;
            town.spatialBlend = 0f;
            town.priority = 10;
            town.time = Random.Range(0f, townClip.length);
            town.volume = 0f;
        }
        else Debug.LogWarning("Town ambience is missing");

        var cameraEars = cam.GetComponent<AudioListener>();
        if (cameraEars != null) cameraEars.enabled = false;
        ears = new GameObject("Ears").transform;
        ears.gameObject.AddComponent<AudioListener>();
        LateUpdate();

        var fountainObject = GameObject.Find("Fountain");
        var clip = Resources.Load<AudioClip>("Ambience/Fountain");
        if (fountainObject == null || clip == null) { Debug.LogWarning("Fountain or its sound is missing"); return; }
        fountain = fountainObject.transform;
        var source = fountainSource = fountainObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.volume = 0f;
        source.spatialBlend = 1f; // for panning only: the engine's own fall-off starts far beyond the square
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 500f;
        source.maxDistance = 1000f;
        source.dopplerLevel = 0f;
        source.time = Random.Range(0f, clip.length); // not the same splash every round
    }

    void Update()
    {
        if (!started) return;
        startFade = Mathf.MoveTowards(startFade, 1f, Time.unscaledDeltaTime / fadeInTime);
        endFade = Mathf.MoveTowards(endFade, ended ? 0f : 1f, Time.unscaledDeltaTime / EndFadeTime);
        Level = startFade * endFade;
        if (fountainSource != null) fountainSource.volume = fountainVolume * FountainFalloff() * Level;
        if (town == null) return;
        float underMusic = music != null ? Mathf.Lerp(1f, townUnderMusic, Mathf.SmoothStep(0f, 1f, music.Build)) : 1f;
        town.volume = townVolume * underMusic * Level;
    }

    // Full up to fountainNear, then a steep fall: about a third left at 40% of the way out, silent at fountainFar.
    float FountainFalloff()
    {
        if (ears == null) return 0f;
        float t = Vector3.Distance(ears.position, fountain.position) / fountainFar; // 0 at the fountain, 1 at fountainFar
        return t < 0.4f ? Mathf.Lerp(1f, 0.3f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fountainNear / fountainFar, 0.4f, t)))
                        : Mathf.Lerp(0.3f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1f, t)));
    }

    void LateUpdate()
    {
        if (ears == null || player == null) return;
        ears.SetPositionAndRotation(player.position + Vector3.up * earHeight, view.rotation);
    }
}
