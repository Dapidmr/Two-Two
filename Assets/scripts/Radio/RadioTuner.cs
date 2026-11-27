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
    public float maxMHz = 110.75f;      // frekuensi saat knob di posisi paling kanan
    public float startMHz = 101.5f;      // frekuensi saat game dimulai
    public float stepMHz = 0.25f;        // sekali tekan panah = geser segini

    [Header("Stasiun utama")]
    public float clearMHz = 104.25f;     // titik paling jelas
    public float clearHalfWidth = 0f;    // 0 = harus tepat di clearMHz
    [Range(0f, 1f)] public float edgeClarity = 1f;  // kejernihan di tepi area jelas (di titik tengah = 1)
    public float nearRange = 0f;         // 0 = di luar area jelas langsung senyap

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

    [Header("Musik latar (BGM)")]
    public AudioSource bgmSource;                  // musik yang looping terus
    [Range(0f, 1f)] public float bgmNormalVolume = 0.5f;      // sebelum pemain memakai radio
    [Range(0f, 1f)] public float bgmDuringRadioVolume = 0.1f; // saat zoom ke radio dan radio ON (pelan)
    [Range(0f, 1f)] public float bgmAfterRadioVolume = 0.3f;  // setelah radio dimatikan (naik sedikit)
    public float bgmFadeSpeed = 0.5f;              // kecepatan volume berubah (makin besar makin cepat)

    [Header("Suara saat memutar dan berhenti")]
    public float turnVolume = 0.8f;
    public float settleVolume = 0.8f;
    public float stopDelay = 0.25f;      // tidak ada putaran selama ini = dianggap sudah berhenti
    public float settleDuration = 0.6f;  // dipakai kalau Settle Source tidak punya klip
    public float voiceFadeIn = 0.5f;     // lama suara masuk setelah suara berhenti selesai

    [Header("Kresek")]
    public bool noiseOnlyAtStart = true; // true = kresek bunyi terus dari awal game sampai frekuensi diganti, lalu senyap
    public float noiseAfterChange = 0f;  // level kresek setelah frekuensi diganti (dipakai kalau Noise Only At Start = true)
    public float noiseMax = 1f;          // paling keras (awal game dan setelah berhenti memutar)
    public float noiseSettled = 0.6f;    // level kresek setelah lama kalau frekuensi masih jauh (dipakai kalau Noise Only At Start = false)
    public float noiseClear = 0f;        // level kresek kalau frekuensi pas (0 = berhenti total)
    public float noiseFadeTime = 4f;     // lama kresek mengecil kalau frekuensi masih jauh
    public float noiseClearFadeTime = 1.2f; // lama kresek mengecil kalau frekuensi pas

    [Header("Suara utama")]
    public float voiceMax = 1f;          // volume saat frekuensi pas (AudioSource.volume maksimal 1)
    public float voiceFar = 0f;          // volume saat masih jauh (0 = senyap)
    public float cutoffMuffled = 500f;   // suara kabur (Hz)
    public float cutoffClear = 22000f;   // suara jernih (Hz)

    [Header("Teks stasiun utama (click for text)")]
    public bool textEnabled = true;
    public float textDelay = 3f;                   // detik setelah frekuensi pas sampai tulisan muncul
    public string promptLabel = "click for text";  // tulisan kecil di kanan layar
    public Key toggleTextKey = Key.T;              // alternatif kalau kursor tidak bisa dipakai
    [TextArea(8, 40)] public string stationText = "";   // teks panjang yang ditampilkan
    [Range(0f, 1f)] public float textBgAlpha = 0.75f;   // transparansi latar gelap (0 = bening, 1 = pekat)
    [Range(0.2f, 0.9f)] public float panelWidthRatio = 0.4f;   // lebar panel (porsi lebar layar)
    [Range(0.2f, 0.95f)] public float panelHeightRatio = 0.75f; // tinggi panel (porsi tinggi layar)
    public int textFontSize = 24;
    public bool typewriterEffect = true;           // true = teks muncul seperti diketik
    public float charsPerSecond = 40f;             // kecepatan mengetik (huruf per detik)

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
    public UnityEvent onPoweredOff;                // dipanggil saat radio benar-benar dimatikan

    [Header("Keypad (tombol masuk)")]
    public RadioKeypad keypad;                     // kosongkan kalau RadioKeypad ada di objek yang sama
    public float wrongReleaseDelay = 0.5f;         // kode salah: tombol tetap masuk sebentar, lalu naik lagi

    [Header("Tombol power")]
    public float offFrequency = 105f;    // radio hanya bisa dimatikan di frekuensi ini (MHz)
    public float powerTolerance = 0.05f; // 0.05 = harus pas di angka yang tampil
    public bool canTurnOnAgain = true;   // tekan tombol lagi saat OFF = nyalakan lagi
    public bool enterKeyAlsoPresses = false; // tombol Enter juga berfungsi sebagai tombol power

    [Header("Tampilan angka di layar")]
    public bool showDisplay = true;
    public int fontSize = 32;
    public string tuneHint = "Use the LEFT and RIGHT arrow keys to tune the frequency";   // menggantikan tulisan VOLUME
    public string dragHint = "Click and drag to rotate the radio";   // petunjuk kedua di bawahnya

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
    private bool wasInMainWindow = false;  // untuk deteksi "baru saja masuk" area jelas
    private float inWindowTime = 0f;       // detik berada pas di stasiun utama (suara sudah terbuka)
    private bool textAvailable = false;    // true = tulisan "click for text" boleh muncul
    private bool textOpen = false;         // true = panel teks sedang terbuka
    private Vector2 textScroll = Vector2.zero;
    private bool usedRadio = false;        // true = radio sudah pernah dimatikan (untuk volume BGM)
    private float typedChars = 0f;         // jumlah huruf yang sudah "diketik"
    private bool typingStarted = false;    // true = animasi mengetik sudah pernah mulai
    private bool typingConsumed = false;   // true = animasi sudah terpakai, teks langsung penuh
    private Texture2D bgTex;
    private float bgTexAlpha = -1f;
    private float initialFreq = 0f;        // frekuensi saat game dimulai
    private bool freqChanged = false;      // true = pemain sudah pernah mengganti frekuensi
    private string entered = "";           // angka yang sudah ditekan di keypad
    private bool passwordAccepted = false;   // true = kode sudah benar

    private float master = 0f;
    private float clarity = 0f;          // kejernihan stasiun utama (0 sampai 1)
    private float pwClarity = 0f;        // 1 kalau tepat di stasiun password
    private float sinceStop = 1000f;     // detik sejak pemain berhenti memutar
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
        if (keypad == null) keypad = GetComponent<RadioKeypad>();

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
        if (frequencyKnob != null) initialFreq = CurrentFrequency();

        if (bgmSource != null)
        {
            bgmSource.loop = true;
            bgmSource.volume = bgmNormalVolume;
            if (!bgmSource.isPlaying) bgmSource.Play();
        }

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

        if (key == "*")
        {
            // * = hapus yang sudah diketik, semua tombol naik lagi
            entered = "";
            if (keypad != null) keypad.ReleaseAll();
            return;
        }
        if (key == "#") return;                      // # tidak dipakai

        entered += key;

        if (entered.Length >= passwordCode.Length)
        {
            if (entered == passwordCode)
            {
                passwordAccepted = true;
                ShowMessage("Kode benar. Radio sekarang bisa dimatikan", 3f);
                if (keypad != null) keypad.LockKeys();   // semua tombol tetap masuk dan terkunci
                onPasswordAccepted.Invoke();
            }
            else
            {
                entered = "";
                ShowMessage("Kode salah");
                if (keypad != null) keypad.ReleaseAllAfter(wrongReleaseDelay);   // tombol naik lagi
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

    // Kejernihan stasiun: 1 di titik tengah, turun sedikit sampai tepi area jelas.
    // Di luar area jelas: memudar sejauh nearRange, atau langsung 0 kalau nearRange = 0.
    float StationClarity(float err)
    {
        if (err <= clearHalfWidth + 0.0001f)
        {
            float t = clearHalfWidth > 0.0001f ? err / clearHalfWidth : 0f;
            return Mathf.Lerp(1f, edgeClarity, t);
        }

        if (nearRange <= 0.0001f) return 0f;   // tidak ada pemudaran: di luar titik = senyap

        float f = 1f - Mathf.Clamp01((err - clearHalfWidth) / nearRange);
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
        if (!freqChanged && Mathf.Abs(freq - initialFreq) > 0.001f) freqChanged = true;

        float mainErr = Mathf.Abs(freq - clearMHz);
        bool inMainWindow = mainErr <= clearHalfWidth + 0.0001f;
        clarity = Mathf.Lerp(clarity, StationClarity(mainErr), dt * 10f);

        // Restart suara utama dari awal setiap kali BARU SAJA masuk ke area jelas
        if (isOn && inMainWindow && !wasInMainWindow && voiceSource != null)
        {
            voiceSource.Stop();
            voiceSource.Play();
        }
        wasInMainWindow = inMainWindow;

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
        if (noiseOnlyAtStart)
            noiseLevel = freqChanged ? noiseAfterChange : noiseMax;   // bunyi terus sampai frekuensi diganti
        if (noiseSource != null)
            noiseSource.volume = noiseLevel * gate * master;

        // --- Suara utama: HANYA bunyi kalau berada di area jelas, dan dibungkam di stasiun password ---
        float voiceLevel = Mathf.Lerp(voiceFar, voiceMax, clarity) * (1f - pwClarity);
        if (!inMainWindow) voiceLevel = 0f;   // potong langsung, tanpa "ekor" suara
        if (voiceSource != null)
            voiceSource.volume = voiceLevel * gate * master;
        if (voiceFilter != null)
            voiceFilter.cutoffFrequency = Mathf.Lerp(cutoffMuffled, cutoffClear, clarity * clarity);

        // --- Teks "click for text": muncul beberapa detik setelah pas di stasiun utama ---
        if (!isOn || !inMainWindow)
        {
            inWindowTime = 0f;
            textOpen = false;
            textScroll = Vector2.zero;
            if (typingStarted) typingConsumed = true;   // frekuensi diganti: lain kali teks langsung penuh
        }
        else if (gate >= 1f)
        {
            inWindowTime += dt;
        }
        textAvailable = textEnabled && !string.IsNullOrEmpty(stationText) && inWindowTime >= textDelay;
        if (!textAvailable) textOpen = false;

        // Animasi mengetik: jalan hanya saat panel terbuka
        if (textOpen && typewriterEffect && !typingConsumed)
        {
            typingStarted = true;
            typedChars += charsPerSecond * dt;
        }

        if (textAvailable && IsZoomed() && Keyboard.current != null
            && Keyboard.current[toggleTextKey].wasPressedThisFrame)
            textOpen = !textOpen;

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

        // --- Musik latar: pelan saat memakai radio, naik sedikit setelah radio dimatikan ---
        if (bgmSource != null)
        {
            if (!isOn) usedRadio = true;                       // radio sudah pernah dimatikan
            float bgmTarget;
            if (isOn && IsZoomed()) bgmTarget = bgmDuringRadioVolume;       // sedang memakai radio
            else if (usedRadio) bgmTarget = bgmAfterRadioVolume;            // setelah radio dimatikan
            else bgmTarget = bgmNormalVolume;                               // sebelum memakai radio
            bgmSource.volume = Mathf.MoveTowards(bgmSource.volume, bgmTarget, bgmFadeSpeed * dt);
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

    // ---------- Teks "click for text" ----------

    Texture2D DarkBg()
    {
        if (bgTex == null || !Mathf.Approximately(bgTexAlpha, textBgAlpha))
        {
            if (bgTex == null)
            {
                bgTex = new Texture2D(1, 1);
                bgTex.wrapMode = TextureWrapMode.Repeat;
            }
            bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, textBgAlpha));
            bgTex.Apply();
            bgTexAlpha = textBgAlpha;
        }
        return bgTex;
    }

    void DrawTextPanel()
    {
        if (!textAvailable) return;

        Texture2D bg = DarkBg();

        if (!textOpen)
        {
            // Tulisan kecil di kanan layar
            GUIStyle btn = new GUIStyle(GUI.skin.button);
            btn.fontSize = Mathf.RoundToInt(textFontSize * 0.9f);
            btn.normal.background = bg;
            btn.hover.background = bg;
            btn.active.background = bg;
            btn.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            btn.hover.textColor = Color.white;
            btn.active.textColor = Color.white;

            float bw = textFontSize * 9f;
            float bh = textFontSize * 2f;
            if (GUI.Button(new Rect(Screen.width - bw - 30, Screen.height * 0.5f - bh * 0.5f, bw, bh), promptLabel, btn))
                textOpen = true;
            return;
        }

        // Panel teks: latar gelap transparan, bisa di-scroll
        float pw = Screen.width * panelWidthRatio;
        float ph = Screen.height * panelHeightRatio;
        Rect panel = new Rect(Screen.width - pw - 30, (Screen.height - ph) * 0.5f, pw, ph);

        GUIStyle box = new GUIStyle(GUI.skin.box);
        box.normal.background = bg;
        GUI.Box(panel, GUIContent.none, box);

        float pad = 20f;
        float closeH = textFontSize * 1.8f;

        GUIStyle closeStyle = new GUIStyle(GUI.skin.button);
        closeStyle.fontSize = Mathf.RoundToInt(textFontSize * 0.8f);
        if (GUI.Button(new Rect(panel.xMax - pad - closeH * 3f, panel.y + pad * 0.5f, closeH * 3f, closeH), "tutup", closeStyle))
            textOpen = false;

        GUIStyle body = new GUIStyle(GUI.skin.label);
        body.fontSize = textFontSize;
        body.wordWrap = true;
        body.richText = true;
        body.normal.textColor = Color.white;

        Rect view = new Rect(panel.x + pad, panel.y + pad + closeH, panel.width - pad * 2f, panel.height - pad * 2f - closeH);
        float contentW = view.width - 24f;   // sisakan ruang untuk scrollbar
        // Tinggi dihitung dari teks penuh supaya tata letak tidak lompat-lompat saat mengetik
        float contentH = body.CalcHeight(new GUIContent(stationText), contentW);

        string shown = stationText;
        bool typing = typewriterEffect && !typingConsumed && typedChars < VisibleLength(stationText);
        if (typing)
        {
            shown = TypedSubstring(stationText, Mathf.FloorToInt(typedChars));

            // Gulung otomatis ke bawah kalau teks yang diketik sudah melewati batas panel
            float shownH = body.CalcHeight(new GUIContent(shown), contentW);
            if (shownH > view.height + textScroll.y) textScroll.y = shownH - view.height;
        }

        textScroll = GUI.BeginScrollView(view, textScroll, new Rect(0, 0, contentW, contentH));
        GUI.Label(new Rect(0, 0, contentW, contentH), shown, body);
        GUI.EndScrollView();
    }

    // Jumlah huruf yang terlihat (tag rich text seperti <b> tidak dihitung)
    int VisibleLength(string s)
    {
        int n = 0;
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '<')
            {
                int end = s.IndexOf('>', i);
                if (end > i) { i = end + 1; continue; }
            }
            n++;
            i++;
        }
        return n;
    }

    // Ambil teks sampai 'count' huruf terlihat, tag rich text ikut dibawa utuh
    string TypedSubstring(string s, int count)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int shownCount = 0;
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '<')
            {
                int end = s.IndexOf('>', i);
                if (end > i)
                {
                    sb.Append(s, i, end - i + 1);
                    i = end + 1;
                    continue;
                }
            }
            if (shownCount >= count) break;
            sb.Append(s[i]);
            shownCount++;
            i++;
        }
        return sb.ToString();
    }

    // ---------- Tampilan di layar ----------

    void OnGUI()
    {
        if (!showDisplay || volumeKnob == null || frequencyKnob == null) return;
        if (!IsZoomed()) return;

        float freq = CurrentFrequency();

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = fontSize;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.white;

        float h = fontSize * 1.6f;

        DrawTextPanel();

        if (Time.time < messageUntil)
        {
            GUIStyle msgStyle = new GUIStyle(style);
            msgStyle.fontSize = Mathf.RoundToInt(fontSize * 0.8f);
            msgStyle.normal.textColor = new Color(1f, 0.9f, 0.4f);
            GUI.Label(new Rect(40, Screen.height - h * 6 - 40, 900, h), message, msgStyle);
        }

        if (requirePasswordToTurnOff && (entered.Length > 0 || passwordAccepted))
        {
            GUIStyle codeStyle = new GUIStyle(style);
            codeStyle.normal.textColor = passwordAccepted ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.9f, 0.4f);
            GUI.Label(new Rect(40, Screen.height - h * 7 - 40, 900, h), "KODE   " + CodeDisplay(), codeStyle);
        }

        if (showExitHint)
        {
            string exitText = isOn ? "Matikan radio untuk keluar" : "Tekan E untuk keluar";

            GUIStyle hintStyle = new GUIStyle(style);
            hintStyle.fontSize = Mathf.RoundToInt(fontSize * 0.8f);
            hintStyle.normal.textColor = isOn ? new Color(1f, 0.9f, 0.4f) : Color.white;   // kuning = menonjol

            // Latar hitam transparan di belakang tulisan supaya mudah dibaca
            Vector2 sz = hintStyle.CalcSize(new GUIContent(exitText));
            float padX = 16f, padY = 6f;
            float rowY = Screen.height - h * 5 - 40;
            Rect hintBox = new Rect(40, rowY + (h - sz.y) * 0.5f - padY, sz.x + padX * 2f, sz.y + padY * 2f);

            GUIStyle hintBg = new GUIStyle(GUI.skin.box);
            hintBg.normal.background = DarkBg();
            GUI.Box(hintBox, GUIContent.none, hintBg);
            GUI.Label(new Rect(hintBox.x + padX, hintBox.y + padY, sz.x, sz.y), exitText, hintStyle);
        }

        GUIStyle powerStyle = new GUIStyle(style);
        powerStyle.normal.textColor = isOn ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.35f, 0.3f);
        GUI.Label(new Rect(40, Screen.height - h * 4 - 40, 700, h), isOn ? "RADIO   ON" : "RADIO   OFF", powerStyle);

        GUI.Label(new Rect(40, Screen.height - h * 3 - 40, 700, h), "FREKUENSI   " + freq.ToString("0.00") + " MHz", style);
        GUI.Label(new Rect(40, Screen.height - h * 2 - 40, 1400, h), tuneHint, style);
        GUI.Label(new Rect(40, Screen.height - h - 40, 1400, h), dragHint, style);
    }
}