using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;

public class RadioTuner : MonoBehaviour
{
    [Header("Knob")]
    public RadioKnob volumeKnob;         // knob kiri: volume
    public RadioKnob frequencyKnob;      // knob kanan: frekuensi

    [Header("Frekuensi yang benar (0 sampai 1)")]
    [Range(0f, 1f)] public float targetFrequency = 0.7f;
    public float sweetSpot = 0.02f;      // selisih sekecil ini dianggap pas
    public float tolerance = 0.25f;      // makin kecil, makin sempit area "mendekati"

    [Header("Audio")]
    public AudioSource noiseSource;      // suara statis (loop)
    public AudioSource voiceSource;      // suara hint (loop)
    public AudioLowPassFilter voiceFilter; // bikin suara terdengar kabur

    [Header("Volume awal")]
    public bool forceStartVolume = true;   // true = volume knob otomatis di-set saat game mulai
    [Range(0f, 1f)] public float startVolume = 1f;  // 1 = langsung maksimal

    [Header("Volume")]
    public float noiseMax = 1f;          // desis saat frekuensi salah
    public float noiseMin = 0.08f;       // desis saat frekuensi pas
    public float voiceMax = 0.8f;        // suara hint saat frekuensi pas
    [Range(0f, 1f)]
    public float voiceStart = 0.55f;     // di bawah angka ini suara hint MATI total
    public float cutoffMuffled = 500f;   // suara kabur (Hz)
    public float cutoffClear = 22000f;   // suara jernih (Hz)

    [Header("Saat sudah pas")]
    public float tunedThreshold = 0.97f;
    public float minVolumeForTuned = 0.3f;
    public UnityEvent onTuned;           // dipanggil sekali saat pertama kali pas

    [Header("Kunci keluar dari zoom")]
    public Bookshelf bookshelf;          // kosongkan kalau Bookshelf ada di objek yang sama
    public bool showExitHint = true;

    [Header("Tombol power")]
    public float offFrequency = 105f;    // radio hanya bisa dimatikan di frekuensi ini (MHz)
    public float powerTolerance = 0.05f; // 0.05 = harus pas di angka yang tampil (105.0)
    public bool canTurnOnAgain = true;   // tekan tombol lagi saat OFF = nyalakan lagi
    public bool enterKeyAlsoPresses = true; // tombol Enter juga berfungsi sebagai tombol power

    [Header("Tampilan angka di layar")]
    public bool showDisplay = true;
    public float minFrequency = 88f;     // frekuensi saat knob di 0 (MHz)
    public float maxFrequency = 108f;    // frekuensi saat knob di 1 (MHz)
    public int fontSize = 32;

    [Header("Debug (hanya untuk dilihat)")]
    public float debugCloseness;
    public float debugVoiceAmount;

    private float closeness = 0f;
    private float master = 0f;
    private bool hasTuned = false;
    private Collider knobCollider;
    private bool isOn = true;            // radio selalu mulai dalam keadaan ON
    private string message = "";
    private float messageUntil = 0f;

    void Start()
    {
        if (volumeKnob != null) knobCollider = volumeKnob.GetComponent<Collider>();
        if (bookshelf == null) bookshelf = GetComponent<Bookshelf>();

        // Mulai langsung di volume yang diset knob (tanpa fade-in)
        if (volumeKnob != null && frequencyKnob != null)
        {
            if (forceStartVolume) volumeKnob.SetValue(startVolume);
            master = volumeKnob.value;
            closeness = Closeness(frequencyKnob.value, targetFrequency);

            // Atur volume awal sebelum suara diputar, supaya tidak ada jeda atau lonjakan
            if (noiseSource != null) noiseSource.volume = Mathf.Lerp(noiseMax, noiseMin, closeness) * master;
            if (voiceSource != null)
                voiceSource.volume = Mathf.InverseLerp(voiceStart, 1f, closeness) * voiceMax * master;
        }

        if (noiseSource != null) { noiseSource.loop = true; noiseSource.Play(); }
        if (voiceSource != null) { voiceSource.loop = true; voiceSource.Play(); }
    }

    float CurrentFrequency()
    {
        return Mathf.Lerp(minFrequency, maxFrequency, frequencyKnob.value);
    }

    bool IsZoomed()
    {
        return knobCollider != null && knobCollider.enabled;
    }

    // Dipanggil oleh RadioPowerButton (atau tombol Enter)
    public void PressPower()
    {
        if (frequencyKnob == null) return;

        if (isOn)
        {
            float f = CurrentFrequency();
            if (Mathf.Abs(f - offFrequency) <= powerTolerance)
            {
                isOn = false;
                ShowMessage("Radio dimatikan");
            }
            else
            {
                ShowMessage("Tidak bisa dimatikan di frekuensi ini");
            }
        }
        else if (canTurnOnAgain)
        {
            isOn = true;
            ShowMessage("Radio menyala");
        }
    }

    void ShowMessage(string text)
    {
        message = text;
        messageUntil = Time.time + 2f;
    }

    void Update()
    {
        if (volumeKnob == null || frequencyKnob == null) return;

        // Tombol Enter sebagai alternatif tombol power (hanya saat zoom)
        if (enterKeyAlsoPresses && IsZoomed() && Keyboard.current != null
            && Keyboard.current.enterKey.wasPressedThisFrame)
            PressPower();

        // Pemain hanya boleh keluar dari zoom kalau radio OFF
        if (bookshelf != null) bookshelf.allowExit = !isOn;

        // Seberapa dekat frekuensi ke titik yang benar
        float targetCloseness = Closeness(frequencyKnob.value, targetFrequency);
        closeness = Mathf.Lerp(closeness, targetCloseness, Time.deltaTime * 8f);

        // Volume utama dari knob kiri (0 kalau radio OFF)
        float targetMaster = isOn ? volumeKnob.value : 0f;
        master = Mathf.Lerp(master, targetMaster, Time.deltaTime * 8f);

        // Suara hint: mati total sampai cukup dekat, lalu naik pelan-pelan
        float voiceAmount = Mathf.InverseLerp(voiceStart, 1f, closeness);
        debugCloseness = closeness;
        debugVoiceAmount = voiceAmount;
        if (voiceSource != null)
            voiceSource.volume = voiceAmount * voiceMax * master;

        // Filter: kabur saat jauh, jernih saat dekat
        if (voiceFilter != null)
            voiceFilter.cutoffFrequency = Mathf.Lerp(cutoffMuffled, cutoffClear, voiceAmount);

        // Desis: keras saat frekuensi salah, kecil saat pas
        if (noiseSource != null)
            noiseSource.volume = Mathf.Lerp(noiseMax, noiseMin, closeness) * master;

        if (!hasTuned && isOn && closeness >= tunedThreshold && volumeKnob.value >= minVolumeForTuned)
        {
            hasTuned = true;
            onTuned.Invoke();
        }
    }

    void OnGUI()
    {
        if (!showDisplay || volumeKnob == null || frequencyKnob == null) return;
        if (!IsZoomed()) return;

        float freq = CurrentFrequency();
        int vol = Mathf.RoundToInt(volumeKnob.value * 100f);

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = fontSize;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.white;

        float h = fontSize * 1.6f;

        // Pesan singkat (misal: gagal mematikan)
        if (Time.time < messageUntil)
        {
            GUIStyle msgStyle = new GUIStyle(style);
            msgStyle.fontSize = Mathf.RoundToInt(fontSize * 0.8f);
            msgStyle.normal.textColor = new Color(1f, 0.9f, 0.4f);
            GUI.Label(new Rect(40, Screen.height - h * 5 - 40, 900, h), message, msgStyle);
        }

        if (showExitHint)
        {
            GUIStyle hintStyle = new GUIStyle(style);
            hintStyle.fontSize = Mathf.RoundToInt(fontSize * 0.7f);
            hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(40, Screen.height - h * 4 - 40, 900, h),
                isOn ? "Matikan radio untuk keluar" : "Tekan E untuk keluar", hintStyle);
        }

        // Indikator ON / OFF
        GUIStyle powerStyle = new GUIStyle(style);
        powerStyle.normal.textColor = isOn ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.35f, 0.3f);
        GUI.Label(new Rect(40, Screen.height - h * 3 - 40, 700, h), isOn ? "RADIO   ON" : "RADIO   OFF", powerStyle);

        GUI.Label(new Rect(40, Screen.height - h * 2 - 40, 700, h), "FREKUENSI   " + freq.ToString("0.0") + " MHz", style);
        GUI.Label(new Rect(40, Screen.height - h - 40, 700, h), "VOLUME   " + vol, style);
    }

    float Closeness(float value, float target)
    {
        float error = Mathf.Max(0f, Mathf.Abs(value - target) - sweetSpot);
        return Mathf.Clamp01(1f - error / tolerance);
    }
}