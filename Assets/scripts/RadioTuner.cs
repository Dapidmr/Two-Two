using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;

public class RadioTuner : MonoBehaviour
{
    [Header("Knob")]
    public RadioKnob volumeKnob;         // knob kiri: volume
    public RadioKnob frequencyKnob;      // knob kanan: frekuensi

    [Header("Frekuensi (MHz)")]
    public float minMHz = 101.5f;        // frekuensi saat knob di posisi paling kiri
    public float maxMHz = 110.75f;       // frekuensi saat knob di posisi paling kanan
    public float startMHz = 101.5f;      // frekuensi saat game dimulai
    public float stepMHz = 0.25f;        // sekali tekan panah = geser segini

    [Header("Stasiun utama")]
    public float clearMHz = 104.25f;     // titik paling jelas
    public float clearHalfWidth = 0.5f;  // masih jelas sampai sejauh ini ke atas dan ke bawah (MHz)
    [Range(0f, 1f)] public float edgeClarity = 0.85f;  // kejernihan di tepi area jelas (di titik tengah = 1)
    public float nearRange = 1.5f;       // di luar area jelas, suara memudar sejauh ini (MHz)

    [Header("Volume knob")]
    public float volumeStep = 0.05f;     // sekali tekan panah = volume berubah segini (0 sampai 1)
    public bool forceStartVolume = true;
    [Range(0f, 1f)] public float startVolume = 1f;   // 1 = langsung maksimal saat mulai

    [Header("Audio")]
    public AudioSource noiseSource;      // kresek-kresek (loop)
    public AudioSource voiceSource;      // suara utama (loop)
    public AudioLowPassFilter voiceFilter; // bikin suara utama terdengar kabur
    public AudioSource turnSource;       // suara saat memutar (klip 1 detik, diulang)
    public AudioSource settleSource;     // suara saat berhenti memutar (klip 0,6 detik)
    public AudioSource passwordSource;   // memutar klip-klip password satu per satu

    [Header("Suara saat memutar dan berhenti")]
    public float turnVolume = 0.8f;
    public float settleVolume = 0.8f;
    public float stopDelay = 0.25f;      // tidak ada putaran selama ini = dianggap sudah berhenti
    public float settleDuration = 0.6f;  // dipakai kalau Settle Source tidak punya klip
    public float voiceFadeIn = 0.5f;     // lama suara masuk setelah suara berhenti selesai

    [Header("Kresek")]
    public float noiseMax = 1f;          // paling keras (awal game dan setelah berhenti memutar)
    public float noiseSettled = 0.6f;    // level kresek setelah lama kalau frekuensi masih jauh
    public float noiseClear = 0f;        // level kresek kalau frekuensi pas (0 = berhenti total)
    public float noiseFadeTime = 4f;     // lama kresek mengecil kalau frekuensi masih jauh
    public float noiseClearFadeTime = 1.2f; // lama kresek mengecil kalau frekuensi pas

    [Header("Suara utama")]
    public float voiceMax = 1f;          // volume saat frekuensi pas
    public float voiceFar = 0.2f;        // volume saat masih jauh (tertutup kresek)
    public float cutoffMuffled = 500f;   // suara kabur (Hz)
    public float cutoffClear = 22000f;   // suara jernih (Hz)

    [Header("Stasiun password (harus tepat)")]
    public bool passwordEnabled = true;
    public float passwordMHz = 110.75f;  // jelas HANYA di frekuensi ini
    public float passwordWindow = 0.05f; // 0.05 = harus pas di angka yang tampil
    public AudioClip[] passwordClips;    // klip suara, dimainkan berurutan dari atas ke bawah
    public float passwordVolume = 1f;
    public float passwordStartDelay = 1f;   // jeda setelah pas, supaya kresek sempat mengecil sebelum klip pertama
    public float gapBetweenClips = 0.4f;    // jeda antar klip
    public bool loopPassword = true;        // true = urutan diulang terus
    public float pauseBeforeRepeat = 3f;    // jeda sebelum urutan diulang
    public UnityEvent onPasswordTuned;      // dipanggil sekali saat pertama kali pas di stasiun password

    [Header("Saat sudah pas di stasiun utama")]
    public float minVolumeForTuned = 0.3f;   // volume harus cukup besar supaya dianggap berhasil
    public UnityEvent onTuned;

    [Header("Kunci keluar dari zoom")]
    public ZoomView zoomView;            // kosongkan kalau ZoomView ada di objek yang sama
    public bool showExitHint = true;

    [Header("Password untuk mematikan radio")]
    public bool requirePasswordToTurnOff = true;   // true = radio hanya bisa dimatikan setelah kode benar
    public string passwordCode = "34511";          // kode yang harus dimasukkan lewat keycap (urut)
    public bool requireOffFrequency = false;       // true = radio juga harus berada di Off Frequency saat dimatikan
    public UnityEvent onPasswordAccepted;          // dipanggil sekali saat kode yang dimasukkan benar
    public UnityEvent onPoweredOff;                // dipanggil saat radio benar-benar dimatikan (misal untuk membuat buku jatuh)

    [Header("Tombol power")]
    public float offFrequency = 105f;    // radio hanya bisa dimatikan di frekuensi ini (MHz)
    public float powerTolerance = 0.05f; // 0.05 = harus pas di angka yang tampil
    public bool canTurnOnAgain = true;   // tekan tombol lagi saat OFF = nyalakan lagi
    public bool enterKeyAlsoPresses = false; // tombol Enter juga berfungsi sebagai tombol power

    [Header("Tampilan angka di layar")]
    public bool showDisplay = true;
    public int fontSize = 32;

    [Header("Debug (hanya untuk dilihat)")]
    public float debugFrequency;
    public float debugClarity;
    public float debugPasswordClarity;
    public bool debugTurning;
    public float debugNoiseVolume;
    public float debugVoiceVolume;
    public int debugPasswordIndex;

    private Collider knobCollider;
    private bool isOn = true;            // radio selalu mulai dalam keadaan ON
    private bool hasTuned = false;
    private bool hasPwTuned = false;
    private bool wasTurning = false;
    private string entered = "";           // angka yang sudah ditekan di keypad
    private bool passwordAccepted = false;   // true = kode sudah benar

    private float master = 0f;
    private float clarity = 0f;          // kejernihan stasiun utama (0 sampai 1)
    private float pwClarity = 0f;        // 1 kalau tepat di stasiun password
    private float sinceStop = 1000f;     // detik sejak pemain berhenti memutar (besar = sudah lama)
    private float noiseT = 0f;           // detik sejak kresek kembali di level paling keras
    private float gateSmooth = 1f;       // 0 = kresek dan suara dibungkam, 1 = terbuka

    // Urutan klip password: 0 = belum mulai, 1 = memutar klip, 2 = jeda antar klip, 3 = jeda sebelum ulang, 4 = selesai
    private int pwState = 0;
    private int pwIndex = -1;
    private float pwTimer = 0f;
    private float pwWait = 0f;

    private string message = "";
    private float messageUntil = 0f;

    void Start()
    {
        if (volumeKnob != null) knobCollider = volumeKnob.GetComponent<Collider>();
        if (zoomView == null) zoomView = GetComponent<ZoomView>();

        // Atur langkah knob dan posisi awal
        if (volumeKnob != null)
        {
            volumeKnob.stepSize = volumeStep;
            if (forceStartVolume) volumeKnob.SetValue(startVolume);
        }
        if (frequencyKnob != null)
        {
            frequencyKnob.stepSize = stepMHz / Mathf.Max(maxMHz - minMHz, 0.0001f);
            frequencyKnob.SetValue(Mathf.InverseLerp(minMHz, maxMHz, startMHz));
        }

        if (volumeKnob != null) master = volumeKnob.value;

        if (noiseSource != null) { noiseSource.loop = true; noiseSource.volume = noiseMax * master; noiseSource.Play(); }
        if (voiceSource != null) { voiceSource.loop = true; voiceSource.volume = 0f; voiceSource.Play(); }
        if (turnSource != null) { turnSource.loop = true; turnSource.playOnAwake = false; }
        if (settleSource != null) { settleSource.loop = false; settleSource.playOnAwake = false; }
        if (passwordSource != null) { passwordSource.loop = false; passwordSource.playOnAwake = false; passwordSource.volume = 0f; }
    }

    // Frekuensi sekarang (MHz), selalu dibulatkan ke kelipatan langkah
    float CurrentFrequency()
    {
        float raw = Mathf.Lerp(minMHz, maxMHz, frequencyKnob.value);
        float steps = Mathf.Round((raw - minMHz) / Mathf.Max(stepMHz, 0.0001f));
        return minMHz + steps * stepMHz;
    }

    bool IsZoomed()
    {
        if (zoomView != null) return zoomView.IsZoomed;
        return knobCollider != null && knobCollider.enabled;
    }

    // Dipanggil oleh RadioPowerButton (atau tombol Enter kalau diaktifkan).
    // Mengembalikan true kalau status radio benar-benar berubah.
    public bool PressPower()
    {
        if (frequencyKnob == null) return false;

        if (isOn)
        {
            if (requirePasswordToTurnOff && !passwordAccepted)
            {
                ShowMessage("Masukkan password terlebih dahulu untuk mematikan radio", 3f);
                return false;
            }

            if (requireOffFrequency && Mathf.Abs(CurrentFrequency() - offFrequency) > powerTolerance)
            {
                ShowMessage("Tidak bisa dimatikan di frekuensi ini");
                return false;
            }

            isOn = false;
            ShowMessage("Radio dimatikan");
            onPoweredOff.Invoke();
            return true;
        }

        if (canTurnOnAgain)
        {
            isOn = true;
            ShowMessage("Radio menyala");
            return true;
        }

        return false;
    }

    // Dipanggil oleh RadioKeypad saat sebuah keycap ditekan
    public void EnterKey(string key)
    {
        if (!isOn || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(passwordCode)) return;

        if (passwordAccepted)
        {
            ShowMessage("Kode sudah benar. Radio bisa dimatikan");
            return;
        }

        if (key == "*") { entered = ""; return; }   // * = hapus yang sudah diketik
        if (key == "#") return;                      // # tidak dipakai

        entered += key;

        if (entered.Length >= passwordCode.Length)
        {
            if (entered == passwordCode)
            {
                passwordAccepted = true;
                ShowMessage("Kode benar. Radio sekarang bisa dimatikan", 3f);
                onPasswordAccepted.Invoke();
            }
            else
            {
                entered = "";
                ShowMessage("Kode salah");
            }
        }
    }

    // Tampilan kode di layar, misal "3 4 _ _ _"
    string CodeDisplay()
    {
        if (passwordAccepted) return "BENAR";

        string result = "";
        for (int i = 0; i < passwordCode.Length; i++)
            result += (i < entered.Length ? entered[i].ToString() : "_") + " ";
        return result.Trim();
    }

    void ShowMessage(string text, float seconds = 2f)
    {
        message = text;
        messageUntil = Time.time + seconds;
    }

    float SettleLength()
    {
        if (settleSource != null && settleSource.clip != null) return settleSource.clip.length;
        return settleDuration;
    }

    // Kejernihan stasiun: 1 di titik tengah, turun sedikit sampai tepi area jelas, lalu memudar ke 0
    float StationClarity(float err)
    {
        if (err <= clearHalfWidth + 0.0001f)
        {
            float t = clearHalfWidth > 0.0001f ? err / clearHalfWidth : 0f;
            return Mathf.Lerp(1f, edgeClarity, t);
        }

        float f = 1f - Mathf.Clamp01((err - clearHalfWidth) / Mathf.Max(nearRange, 0.0001f));
        return edgeClarity * f * f;
    }

    void Update()
    {
        if (volumeKnob == null || frequencyKnob == null) return;
        float dt = Time.deltaTime;

        if (enterKeyAlsoPresses && IsZoomed() && Keyboard.current != null
            && Keyboard.current.enterKey.wasPressedThisFrame)
            PressPower();

        // Pemain hanya boleh keluar dari zoom kalau radio OFF
        if (zoomView != null) zoomView.allowExit = !isOn;

        // --- Posisi frekuensi ---
        float freq = CurrentFrequency();

        float mainErr = Mathf.Abs(freq - clearMHz);
        bool inMainWindow = mainErr <= clearHalfWidth + 0.0001f;
        clarity = Mathf.Lerp(clarity, StationClarity(mainErr), dt * 10f);

        bool exactPassword = passwordEnabled && Mathf.Abs(freq - passwordMHz) <= passwordWindow;
        pwClarity = Mathf.Lerp(pwClarity, exactPassword ? 1f : 0f, dt * 10f);

        // Kresek mengecil kalau salah satu stasiun jelas
        float bestClarity = Mathf.Max(clarity, pwClarity);

        // --- Sedang memutar atau sudah berhenti ---
        bool turning = Time.time - frequencyKnob.LastChangeTime < stopDelay;
        if (turning)
        {
            sinceStop = 0f;
            noiseT = 0f;
        }
        else
        {
            if (wasTurning && isOn && settleSource != null)
            {
                settleSource.volume = settleVolume * master;
                settleSource.Play();
            }
            sinceStop += dt;

            // Kresek baru mulai mengecil setelah suara berhenti selesai
            if (sinceStop > SettleLength()) noiseT += dt;
        }
        wasTurning = turning;

        // --- Volume utama dari knob kiri (0 kalau radio OFF) ---
        float targetMaster = isOn ? volumeKnob.value : 0f;
        master = Mathf.Lerp(master, targetMaster, dt * 8f);

        // --- Pintu suara: tertutup saat memutar dan saat suara berhenti berbunyi, lalu terbuka ---
        float gateTarget = turning ? 0f : Mathf.Clamp01((sinceStop - SettleLength()) / Mathf.Max(voiceFadeIn, 0.01f));
        gateSmooth = Mathf.MoveTowards(gateSmooth, gateTarget, dt * 8f);
        float gate = gateSmooth;

        // --- Kresek ---
        float settledNoise = Mathf.Lerp(noiseSettled, noiseClear, bestClarity);
        float fadeTime = Mathf.Lerp(noiseFadeTime, noiseClearFadeTime, bestClarity);
        float noiseFade = Mathf.Clamp01(noiseT / Mathf.Max(fadeTime, 0.01f));
        float noiseLevel = Mathf.Lerp(noiseMax, settledNoise, noiseFade);
        if (noiseSource != null)
            noiseSource.volume = noiseLevel * gate * master;

        // --- Suara utama (dibungkam kalau sedang di stasiun password) ---
        float voiceLevel = Mathf.Lerp(voiceFar, voiceMax, clarity) * (1f - pwClarity);
        if (voiceSource != null)
            voiceSource.volume = voiceLevel * gate * master;
        if (voiceFilter != null)
            voiceFilter.cutoffFrequency = Mathf.Lerp(cutoffMuffled, cutoffClear, clarity * clarity);

        // --- Stasiun password ---
        UpdatePassword(dt, exactPassword, gate >= 0.99f);
        if (passwordSource != null)
            passwordSource.volume = passwordVolume * pwClarity * gate * master;

        // --- Suara memutar (diulang selama memutar) ---
        if (turnSource != null)
        {
            if (turning && isOn)
            {
                if (!turnSource.isPlaying) turnSource.Play();
            }
            else if (turnSource.isPlaying)
            {
                turnSource.Stop();
            }
            turnSource.volume = turnVolume * master;
        }

        debugFrequency = freq;
        debugClarity = clarity;
        debugPasswordClarity = pwClarity;
        debugTurning = turning;
        debugNoiseVolume = noiseSource != null ? noiseSource.volume : 0f;
        debugVoiceVolume = voiceSource != null ? voiceSource.volume : 0f;
        debugPasswordIndex = pwIndex;

        if (!hasTuned && isOn && inMainWindow && gate >= 1f && volumeKnob.value >= minVolumeForTuned)
        {
            hasTuned = true;
            onTuned.Invoke();
        }
    }

    // ---------- Urutan klip password ----------

    void UpdatePassword(float dt, bool exact, bool gateOpen)
    {
        if (passwordSource == null) return;

        // Keluar dari frekuensi password: hentikan dan ulang dari awal nanti
        if (!exact)
        {
            StopPassword();
            return;
        }

        // Tunggu sampai kresek dan suara berhenti selesai
        if (!gateOpen) return;

        if (pwState == 0)
        {
            // Jeda sebentar supaya kresek sempat mengecil sebelum klip pertama
            pwWait += dt;
            if (pwWait < passwordStartDelay) return;

            if (!hasPwTuned)
            {
                hasPwTuned = true;
                onPasswordTuned.Invoke();
            }

            pwIndex = -1;
            PlayNextClip();
            return;
        }

        switch (pwState)
        {
            case 1:   // klip sedang diputar
                if (!passwordSource.isPlaying)
                {
                    pwState = 2;
                    pwTimer = gapBetweenClips;
                }
                break;

            case 2:   // jeda antar klip
                pwTimer -= dt;
                if (pwTimer <= 0f) PlayNextClip();
                break;

            case 3:   // jeda sebelum urutan diulang
                pwTimer -= dt;
                if (pwTimer <= 0f)
                {
                    pwIndex = -1;
                    PlayNextClip();
                }
                break;
        }
    }

    void PlayNextClip()
    {
        pwIndex++;
        while (passwordClips != null && pwIndex < passwordClips.Length && passwordClips[pwIndex] == null)
            pwIndex++;

        if (passwordClips == null || pwIndex >= passwordClips.Length)
        {
            // Semua klip sudah diputar
            if (loopPassword && HasAnyClip())
            {
                pwState = 3;
                pwTimer = pauseBeforeRepeat;
            }
            else
            {
                pwState = 4;
            }
            return;
        }

        passwordSource.clip = passwordClips[pwIndex];
        passwordSource.loop = false;
        passwordSource.Play();
        pwState = 1;
    }

    bool HasAnyClip()
    {
        if (passwordClips == null) return false;
        foreach (var c in passwordClips)
            if (c != null) return true;
        return false;
    }

    void StopPassword()
    {
        if (pwState == 0 && pwWait <= 0f) return;   // sudah dalam keadaan diam

        if (passwordSource != null && passwordSource.isPlaying) passwordSource.Stop();
        pwState = 0;
        pwIndex = -1;
        pwTimer = 0f;
        pwWait = 0f;
    }

    // ---------- Tampilan di layar ----------

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

        if (Time.time < messageUntil)
        {
            GUIStyle msgStyle = new GUIStyle(style);
            msgStyle.fontSize = Mathf.RoundToInt(fontSize * 0.8f);
            msgStyle.normal.textColor = new Color(1f, 0.9f, 0.4f);
            GUI.Label(new Rect(40, Screen.height - h * 5 - 40, 900, h), message, msgStyle);
        }

        if (requirePasswordToTurnOff && (entered.Length > 0 || passwordAccepted))
        {
            GUIStyle codeStyle = new GUIStyle(style);
            codeStyle.normal.textColor = passwordAccepted ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.9f, 0.4f);
            GUI.Label(new Rect(40, Screen.height - h * 6 - 40, 900, h), "KODE   " + CodeDisplay(), codeStyle);
        }

        if (showExitHint)
        {
            GUIStyle hintStyle = new GUIStyle(style);
            hintStyle.fontSize = Mathf.RoundToInt(fontSize * 0.7f);
            hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(40, Screen.height - h * 4 - 40, 900, h),
                isOn ? "Matikan radio untuk keluar" : "Tekan E untuk keluar", hintStyle);
        }

        GUIStyle powerStyle = new GUIStyle(style);
        powerStyle.normal.textColor = isOn ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.35f, 0.3f);
        GUI.Label(new Rect(40, Screen.height - h * 3 - 40, 700, h), isOn ? "RADIO   ON" : "RADIO   OFF", powerStyle);

        GUI.Label(new Rect(40, Screen.height - h * 2 - 40, 700, h), "FREKUENSI   " + freq.ToString("0.00") + " MHz", style);
        GUI.Label(new Rect(40, Screen.height - h - 40, 700, h), "VOLUME   " + vol, style);
    }
}