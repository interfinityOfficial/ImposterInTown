using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Two separate things the player can read from the town's light:
// - The clock (the midnight deadline) moves the sun and sky: afternoon, golden hour, sunset (lanterns come on),
//   night, midnight. It moves steadily, so the sun is a clock you can glance at.
// - Unease (how many are taken) changes the atmosphere without changing the hour: fog closes in, colour drains,
//   a sickly tint creeps in and the lanterns flicker harder. Turning copies back clears it again.
// Catching the imposter sweeps to a warm morning and clears the air; losing settles into a heavy night.
// Night stays a moody blue rather than dark, so villagers' movements stay readable.
// ImposterGame adds this if the scene doesn't have one; it finds the sun, camera and volume on its own.
public class DayNight : MonoBehaviour
{
    [System.Serializable]
    public class Preset
    {
        public float sunPitch = 42f, sunYaw = -35f;
        public Color sunColor = Color.white;
        public float sunIntensity = 1.6f, shadowStrength = 0.75f;
        public Color ambientSky = Color.gray, ambientEquator = Color.gray, ambientGround = Color.gray;
        public Color fog = Color.gray;
        public float fogStart = 28f, fogEnd = 65f;
        public float exposure, saturation, vignette;
        [Range(0f, 1f)] public float lanterns;
    }

    public Light sun;
    public Camera cam;
    public Volume volume;
    [Tooltip("Seconds for unease to ease to a new level after it changes.")]
    public float uneaseTransition = 3f;
    [Tooltip("How quickly the endings (morning or night) arrive.")]
    public float endingTime = 2.5f;

    [Header("Unease (atmosphere)")]
    [Tooltip("At full unease, fog starts and ends at this fraction of its usual distance.")]
    public float uneaseFogScale = 0.55f;
    public float uneaseSaturation = -30f;
    public float uneaseVignette = 0.12f;
    public Color uneaseTint = new Color(0.86f, 0.95f, 0.84f);   // a faint sickly green

    [Header("Lanterns")]
    public Color lanternColor = new Color(1f, 0.72f, 0.4f);
    public float lanternIntensity = 3f;
    public float lanternRange = 5f;
    public float lanternHeight = 1.42f;

    // The sky's keyframes along the clock (0 = start of the round, 1 = midnight).
    public Preset day = new Preset
    {
        sunPitch = 42f, sunYaw = -35f, sunColor = new Color(1f, 0.93f, 0.8f), sunIntensity = 1.6f, shadowStrength = 0.75f,
        ambientSky = new Color(0.7f, 0.78f, 0.92f), ambientEquator = new Color(0.78f, 0.74f, 0.7f), ambientGround = new Color(0.42f, 0.4f, 0.38f),
        fog = new Color(0.68f, 0.82f, 0.95f), fogStart = 28f, fogEnd = 65f, exposure = 0.15f, saturation = 12f, vignette = 0.22f, lanterns = 0f,
    };
    public Preset golden = new Preset
    {
        sunPitch = 24f, sunYaw = -48f, sunColor = new Color(1f, 0.8f, 0.55f), sunIntensity = 1.45f, shadowStrength = 0.72f,
        ambientSky = new Color(0.72f, 0.7f, 0.82f), ambientEquator = new Color(0.86f, 0.72f, 0.6f), ambientGround = new Color(0.4f, 0.35f, 0.32f),
        fog = new Color(0.95f, 0.78f, 0.6f), fogStart = 26f, fogEnd = 60f, exposure = 0.12f, saturation = 16f, vignette = 0.24f, lanterns = 0f,
    };
    public Preset dusk = new Preset
    {
        sunPitch = 12f, sunYaw = -62f, sunColor = new Color(1f, 0.58f, 0.32f), sunIntensity = 1.25f, shadowStrength = 0.7f,
        ambientSky = new Color(0.58f, 0.48f, 0.66f), ambientEquator = new Color(0.8f, 0.55f, 0.45f), ambientGround = new Color(0.32f, 0.27f, 0.28f),
        fog = new Color(0.9f, 0.58f, 0.48f), fogStart = 22f, fogEnd = 55f, exposure = 0.05f, saturation = 18f, vignette = 0.28f, lanterns = 0.6f,
    };
    public Preset night = new Preset
    {
        sunPitch = 38f, sunYaw = -95f, sunColor = new Color(0.55f, 0.66f, 1f), sunIntensity = 0.5f, shadowStrength = 0.55f,
        ambientSky = new Color(0.3f, 0.36f, 0.56f), ambientEquator = new Color(0.28f, 0.3f, 0.45f), ambientGround = new Color(0.16f, 0.16f, 0.22f),
        fog = new Color(0.1f, 0.13f, 0.25f), fogStart = 16f, fogEnd = 46f, exposure = 0f, saturation = -12f, vignette = 0.36f, lanterns = 1f,
    };
    public Preset morning = new Preset
    {
        sunPitch = 24f, sunYaw = -15f, sunColor = new Color(1f, 0.88f, 0.72f), sunIntensity = 1.55f, shadowStrength = 0.7f,
        ambientSky = new Color(0.8f, 0.82f, 0.94f), ambientEquator = new Color(0.92f, 0.8f, 0.7f), ambientGround = new Color(0.46f, 0.42f, 0.38f),
        fog = new Color(0.96f, 0.86f, 0.76f), fogStart = 28f, fogEnd = 65f, exposure = 0.2f, saturation = 16f, vignette = 0.2f, lanterns = 0f,
    };
    // Where each keyframe sits on the clock: afternoon, golden hour (~1:30 of 4:00), sunset (~2:30), night (~3:30).
    static readonly float[] KeyTimes = { 0f, 0.375f, 0.625f, 0.875f };

    float clock;                 // 0..1, set by the game every frame
    float unease, uneaseTarget;  // 0..1, eased
    float morningBlend, morningTarget;
    bool ending;
    ColorAdjustments colorAdjustments;
    Vignette vignetteFx;
    readonly List<Light> lanternLights = new List<Light>();
    readonly List<float> flickerSeeds = new List<float>();

    // How far through the round we are: 0 = start, 1 = midnight.
    public void SetClock(float t)
    {
        if (!ending) clock = Mathf.Clamp01(t);
    }

    // 0 = calm, 1 = about to be taken over.
    public void SetUnease(float u)
    {
        if (!ending) uneaseTarget = Mathf.Clamp01(u);
    }

    // Caught the imposter: morning, clear air. Lost the town: heavy night.
    public void ShowEnding(bool won)
    {
        ending = true;
        if (won) { morningTarget = 1f; uneaseTarget = 0f; }
        else { uneaseTarget = 1f; morningTarget = 0f; StartCoroutine(RunClockTo(1f)); }
    }

    // One eased sweep from wherever the clock is to `target`: slow start, slow finish, smooth in between (a loss
    // at 0:30 sweeps through golden hour and sunset to night in one go rather than stopping at each).
    System.Collections.IEnumerator RunClockTo(float target)
    {
        float from = clock;
        float duration = Mathf.Max(0.5f, endingTime * Mathf.Abs(target - from));
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            clock = Mathf.Lerp(from, target, Smooth(t));
            yield return null;
        }
        clock = target;
    }

    void Start()
    {
        if (sun == null) sun = RenderSettings.sun != null ? RenderSettings.sun : FindAnyObjectByType<Light>();
        if (cam == null) cam = Camera.main;
        if (volume == null) volume = FindAnyObjectByType<Volume>();
        // volume.profile makes a runtime copy, so the saved profile asset is left untouched.
        if (volume != null)
        {
            volume.profile.TryGet(out colorAdjustments);
            volume.profile.TryGet(out vignetteFx);
        }
        CreateLanternLights();
        Apply();
    }

    void CreateLanternLights()
    {
        var env = GameObject.Find("Environment");
        if (env == null) return;
        foreach (var t in env.GetComponentsInChildren<Transform>())
        {
            if (t.name != "lantern") continue;
            var light = new GameObject("Lantern Light").AddComponent<Light>();
            light.transform.SetParent(t, false);
            light.transform.localPosition = Vector3.up * lanternHeight;
            light.type = LightType.Point;
            light.color = lanternColor;
            light.range = lanternRange;
            light.shadows = LightShadows.None;
            light.intensity = 0f;
            light.enabled = false;
            lanternLights.Add(light);
            flickerSeeds.Add(Random.value * 100f);
        }
    }

    void Update()
    {
        float uneaseSpeed = 1f / Mathf.Max(0.01f, ending ? endingTime : uneaseTransition);
        unease = Mathf.MoveTowards(unease, uneaseTarget, uneaseSpeed * Time.deltaTime);
        morningBlend = Mathf.MoveTowards(morningBlend, morningTarget, Time.deltaTime / endingTime);
        Apply();
    }

    void Apply()
    {
        // The hour: blend between the two keyframes either side of the clock, then towards morning if won.
        var p = SkyAt(clock);
        if (morningBlend > 0f) p = Blend(p, morning, Smooth(morningBlend));
        float u = Smooth(unease);

        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(p.sunPitch, p.sunYaw, 0f);
            sun.color = p.sunColor;
            sun.intensity = p.sunIntensity;
            sun.shadowStrength = p.shadowStrength;
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = p.ambientSky;
        RenderSettings.ambientEquatorColor = p.ambientEquator;
        RenderSettings.ambientGroundColor = p.ambientGround;

        // The mood: fog closes in, colour drains, a tint creeps in.
        float fogScale = Mathf.Lerp(1f, uneaseFogScale, u);
        RenderSettings.fogColor = p.fog;
        RenderSettings.fogStartDistance = p.fogStart * fogScale;
        RenderSettings.fogEndDistance = p.fogEnd * fogScale;
        if (cam != null) cam.backgroundColor = p.fog;
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.Override(p.exposure);
            colorAdjustments.saturation.Override(p.saturation + uneaseSaturation * u);
            colorAdjustments.colorFilter.Override(Color.Lerp(Color.white, uneaseTint, u));
        }
        if (vignetteFx != null) vignetteFx.intensity.Override(p.vignette + uneaseVignette * u);

        // Lanterns come on at dusk; the more unease, the harder they flicker.
        float flickerAmount = Mathf.Lerp(0.1f, 0.45f, u);
        for (int i = 0; i < lanternLights.Count; i++)
        {
            float flicker = 1f - flickerAmount + flickerAmount * Mathf.PerlinNoise(flickerSeeds[i], Time.time * (3f + 6f * u));
            lanternLights[i].intensity = lanternIntensity * p.lanterns * flicker;
            lanternLights[i].enabled = p.lanterns > 0.01f;
        }
    }

    Preset SkyAt(float t)
    {
        var keys = new[] { day, golden, dusk, night };
        if (t >= KeyTimes[KeyTimes.Length - 1]) return night;
        for (int i = 0; i < KeyTimes.Length - 1; i++)
            if (t < KeyTimes[i + 1])
                // Straight blends between keyframes: easing each stretch would make the sky pause at every
                // keyframe, which shows as steps when the ending runs the clock quickly to midnight.
                return Blend(keys[i], keys[i + 1], Mathf.InverseLerp(KeyTimes[i], KeyTimes[i + 1], t));
        return night;
    }

    static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

    static Preset Blend(Preset a, Preset b, float t) => new Preset
    {
        sunPitch = Mathf.Lerp(a.sunPitch, b.sunPitch, t),
        sunYaw = Mathf.LerpAngle(a.sunYaw, b.sunYaw, t),
        sunColor = Color.Lerp(a.sunColor, b.sunColor, t),
        sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t),
        shadowStrength = Mathf.Lerp(a.shadowStrength, b.shadowStrength, t),
        ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t),
        ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t),
        ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t),
        fog = Color.Lerp(a.fog, b.fog, t),
        fogStart = Mathf.Lerp(a.fogStart, b.fogStart, t),
        fogEnd = Mathf.Lerp(a.fogEnd, b.fogEnd, t),
        exposure = Mathf.Lerp(a.exposure, b.exposure, t),
        saturation = Mathf.Lerp(a.saturation, b.saturation, t),
        vignette = Mathf.Lerp(a.vignette, b.vignette, t),
        lanterns = Mathf.Lerp(a.lanterns, b.lanterns, t),
    };
}
