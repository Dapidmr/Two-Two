using UnityEngine;
using UnityEngine.Events;

public class RadioTuner : MonoBehaviour
{
    [Header("Knob")]
    public RadioKnob knob1;
    public RadioKnob knob2;

    [Header("Titik yang benar (0 sampai 1)")]
    [Range(0f, 1f)] public float target1 = 0.7f;
    [Range(0f, 1f)] public float target2 = 0.3f;

    [Header("Seberapa toleran")]
    public float sweetSpot = 0.03f;      // selisih sekecil ini dianggap sudah pas
    public float tolerance = 0.35f;      // makin besar, makin gampang mendekati

    [Header("Audio")]
    public AudioSource noiseSource;      // suara statis (loop)
    public AudioSource voiceSource;      // suara hint (loop)
    public AudioLowPassFilter voiceFilter; // filter yang bikin suara kabur (taruh di objek yang sama dengan voiceSource)

    [Header("Volume")]
    public float noiseMax = 0.8f;        // volume statis saat knob jauh dari benar
    public float noiseMin = 0.05f;       // volume statis saat sudah pas
    public float voiceMax = 1f;          // volume suara hint saat sudah pas
    public float cutoffMuffled = 500f;   // suara kabur (Hz)
    public float cutoffClear = 22000f;   // suara jernih (Hz)

    [Header("Saat sudah pas")]
    public float tunedThreshold = 0.97f;
    public UnityEvent onTuned;           // dipanggil sekali saat pertama kali pas

    private float closeness = 0f;
    private bool hasTuned = false;

    void Start()
    {
        if (noiseSource != null) { noiseSource.loop = true; noiseSource.Play(); }
        if (voiceSource != null) { voiceSource.loop = true; voiceSource.Play(); }
    }

    void Update()
    {
        if (knob1 == null || knob2 == null) return;

        float c1 = Closeness(knob1.value, target1);
        float c2 = Closeness(knob2.value, target2);
        float targetCloseness = (c1 + c2) * 0.5f;

        // Perubahan dibuat halus supaya suara tidak melompat
        closeness = Mathf.Lerp(closeness, targetCloseness, Time.deltaTime * 6f);

        // Suara hint: hampir nol kalau jauh, baru jelas kalau sangat dekat
        if (voiceSource != null)
            voiceSource.volume = Mathf.Pow(closeness, 4f) * voiceMax;

        // Filter: kabur saat jauh, jernih saat dekat
        if (voiceFilter != null)
            voiceFilter.cutoffFrequency = Mathf.Lerp(cutoffMuffled, cutoffClear, Mathf.Pow(closeness, 3f));

        // Statis: keras saat jauh, pelan saat dekat
        if (noiseSource != null)
            noiseSource.volume = Mathf.Lerp(noiseMax, noiseMin, closeness);

        if (!hasTuned && closeness >= tunedThreshold)
        {
            hasTuned = true;
            onTuned.Invoke();
        }
    }

    float Closeness(float value, float target)
    {
        float error = Mathf.Max(0f, Mathf.Abs(value - target) - sweetSpot);
        return Mathf.Clamp01(1f - error / tolerance);
    }
}