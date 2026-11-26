using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

// Puzzle radio. Pasang di GameObject "Radio" (parent dari Keycaps, Tutupan, Up button, dst).
// Alur: dekati + E -> kamera zoom -> drag untuk memutar radio -> klik keycaps (PIN) -> klik Up button (power).
// Memakai Monolog.Instance (script Monolog.cs milikmu) untuk semua teks monolog.
public class RadioPuzzle : MonoBehaviour
{
    public enum State { Idle, Entering, Zoomed, Exiting, Ending }

    [System.Serializable]
    public class KeyEntry
    {
        public Transform key;
        [Tooltip("Angka 0-9. Isi 'C' untuk tombol clear. Selain itu diabaikan (mis. * atau #).")]
        public string value;

        [System.NonSerialized] public Vector3 restLocal;
        [System.NonSerialized] public float amount;   // 0 = diam, 1 = tertekan penuh
        [System.NonSerialized] public float timer;
    }

    [Header("Referensi")]
    public Camera playerCamera;                       // kosong = Camera.main
    [Tooltip("Script player yang harus mati saat zoom (FirstPersonController, dll).")]
    public MonoBehaviour[] disableWhileZoomed;
    [Tooltip("Opsional. Kosong = kamera zoom otomatis dari posisi player.")]
    public Transform zoomPoint;
    [Tooltip("Child kosong di ruang kosong radio untuk menaruh teks PIN. Sumbu BIRU (Z) harus menunjuk MASUK ke radio (menjauhi pemain).")]
    public Transform pinAnchor;
    public AudioSource radioAudio;                    // sound radio yang sedang berbunyi

    [Header("Interaksi")]
    public float interactDistance = 2.5f;
    [Range(0.5f, 1f)] public float lookDotThreshold = 0.9f;
    public float zoomDistance = 0.7f;                 // jarak kamera dari radio saat zoom
    public float zoomTime = 0.8f;
    public float rotateSpeed = 0.3f;                  // sensitivitas drag (derajat per pixel)

    [Header("PIN")]
    public string pin = "162925";
    public List<KeyEntry> keys = new List<KeyEntry>();   // isi 'value' tiap keycap (klik kanan komponen > Auto-fill keycaps)
    public Transform powerButton;                        // "Up button"

    [Header("Gerak tombol (arah dalam ruang lokal Radio, lihat panah Gizmo)")]
    public Vector3 keyPressDirection = new Vector3(0f, 0f, -1f);
    public float keyPressDepth = 0.004f;
    public Vector3 powerPressDirection = new Vector3(0f, -1f, 0f);
    public float powerPressDepth = 0.006f;
    public float pressHold = 0.12f;
    public float pressSpeed = 15f;

    [Header("Penutup keypad (Tutupan)")]
    public Transform cover;
    public Vector3 coverSlideDirection = new Vector3(1f, 0f, 0f);   // arah dari TUTUP -> BUKA (ruang lokal Radio)
    public float coverSlideDistance = 0.08f;
    public bool coverRestIsOpen = true;               // true = posisi Tutupan di scene saat ini adalah posisi TERBUKA
    public float coverTime = 0.5f;

    [Header("Tampilan PIN")]
    public Font pinFont;                              // kosong = font bawaan Unity
    public float pinWidth = 0.08f;                    // lebar teks PIN di dunia (meter)
    public Color displayColor = new Color(0.6f, 1f, 0.6f);
    public string wrongText = "SALAH!";
    public Color wrongColor = new Color(1f, 0.25f, 0.2f);
    public float wrongShowTime = 1.2f;

    [Header("Monolog")]
    [TextArea(2, 4)] public string[] zoomLines;           // pertama kali zoom
    [TextArea(2, 4)] public string[] powerLockedLines;    // power ditekan sebelum PIN benar
    [TextArea(2, 4)] public string[] pinCorrectLines;     // PIN benar, power belum ditekan
    [TextArea(2, 4)] public string[] endingLines;         // power ditekan setelah PIN benar

    [Header("Sound")]
    public AudioClip unlockedClip;                    // sound radio setelah PIN benar
    public AudioClip keyClip;
    public AudioClip wrongClip;
    public AudioClip powerClip;

    [Header("Ending")]
    public float fadeTime = 2f;
    public string endText = "Chapter ended for now, thx for experiencing the development!";
    public int endFontSize = 32;

    [Header("Aktivasi")]
    public bool startActive = false;   // false = radio mati sampai ActivateRadio() dipanggil
    bool active;

    State state = State.Idle;
    Camera cam;
    Transform camT, camParent;
    Vector3 camLocalPos; Quaternion camLocalRot;
    Vector3 radioPos0; Quaternion radioRot0;
    Vector3 localCenter;

    readonly Dictionary<Transform, KeyEntry> map = new Dictionary<Transform, KeyEntry>();
    KeyEntry powerEntry;
    Vector3 coverRestLocal;
    float coverAmount, coverTarget;       // 0 = tertutup, 1 = terbuka

    string entered = "";
    string flash;
    Color flashColor;
    bool unlocked, busy, dragging, introPlayed, looking;

    TextMesh pinText;
    AudioSource sfx;
    float fadeAlpha, textAlpha;
    GUIStyle promptStyle, endStyle;

    // ---------- setup ----------

    void Reset() { AutoFillKeys(); AutoFindRefs(); }

    void Start()
    {
        active = startActive;
        if (radioAudio != null)
        {
            radioAudio.playOnAwake = false;
            if (active) radioAudio.Play();
            else radioAudio.Stop();      // di Start supaya pasti setelah semua Awake
        }
    }

    // Hubungkan ke BookItem.onCollected di Inspector
    public void ActivateRadio()
    {
        if (active) return;
        active = true;
        if (radioAudio != null && !unlocked)
        {
            radioAudio.loop = true;
            radioAudio.Play();
        }
    }

    [ContextMenu("Auto-fill keycaps")]
    void AutoFillKeys()
    {
        keys.Clear();
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("Keycaps")) keys.Add(new KeyEntry { key = t, value = "" });
        keys.Sort((a, b) => string.CompareOrdinal(a.key.name, b.key.name));
    }

    void AutoFindRefs()
    {
        if (powerButton == null) powerButton = FindDeep(transform, "Up button");
        if (cover == null) cover = FindDeep(transform, "Tutupan");
    }

    static Transform FindDeep(Transform root, string n)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == n) return t;
        return null;
    }

    void Awake()
    {
        cam = playerCamera != null ? playerCamera : Camera.main;
        AutoFindRefs();
        if (keys.Count == 0) AutoFillKeys();

        // pusat radio (sebelum teks PIN dibuat)
        var rs = GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            localCenter = transform.InverseTransformPoint(b.center);
        }

        foreach (var k in keys)
        {
            if (k.key == null) continue;
            k.restLocal = k.key.localPosition;
            EnsureCollider(k.key);
            map[k.key] = k;
        }
        if (powerButton != null)
        {
            powerEntry = new KeyEntry { key = powerButton, restLocal = powerButton.localPosition };
            EnsureCollider(powerButton);
            map[powerButton] = powerEntry;
        }
        if (cover != null)
        {
            coverRestLocal = cover.localPosition;
            EnsureCollider(cover);
        }

        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;

        CreatePinText();
        UpdateDisplay();
    }

    static void EnsureCollider(Transform t)
    {
        if (t.GetComponent<Collider>() == null && t.GetComponent<MeshFilter>() != null)
            t.gameObject.AddComponent<BoxCollider>();   // otomatis pas ke mesh
    }

    void CreatePinText()
    {
        if (pinAnchor == null)
            Debug.LogWarning("RadioPuzzle: pinAnchor belum diisi, teks PIN ditaruh di tengah radio.");

        var go = new GameObject("PinText");
        go.transform.SetParent(pinAnchor != null ? pinAnchor : transform, false);
        pinText = go.AddComponent<TextMesh>();
        pinText.anchor = TextAnchor.MiddleCenter;
        pinText.alignment = TextAlignment.Center;
        pinText.fontSize = 64;
        pinText.characterSize = 0.1f;

        Font f = pinFont;
        if (f == null)
        {
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
        }
        pinText.font = f;
        go.GetComponent<MeshRenderer>().sharedMaterial = f.material;
        StartCoroutine(FitPinText());
    }

    // sesuaikan ukuran teks supaya lebarnya = pinWidth (dalam meter, apa pun scale import modelnya)
    IEnumerator FitPinText()
    {
        yield return null;
        var mr = pinText.GetComponent<MeshRenderer>();
        pinText.transform.localScale = Vector3.one;
        string keep = pinText.text;
        pinText.text = "0 0 0 0 0 0";
        yield return null;
        float w = mr.bounds.size.magnitude;   // aman terhadap rotasi anchor
        pinText.text = keep;
        if (w > 0.0001f)
        {
            // lebar teks sampel ~ 6 angka + spasi; magnitude ~ lebar karena tinggi kecil
            pinText.transform.localScale = Vector3.one * (pinWidth / w);
        }
    }

    // ---------- loop ----------

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;

        if (state == State.Idle)
        {
            looking = CanInteract();
            if (looking && kb != null && kb.eKey.wasPressedThisFrame) StartCoroutine(Enter());
        }
        else if (state == State.Zoomed)
        {
            if (kb != null && (kb.qKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
            {
                StartCoroutine(Exit());
                return;
            }
            if (mouse != null) HandleMouse(mouse);
        }
    }

    bool CanInteract()
    {
        if (!active || cam == null) return false;
        if (cam == null) return false;
        Vector3 to = WorldCenter() - cam.transform.position;
        float d = to.magnitude;
        if (d > interactDistance || d < 0.001f) return false;
        return Vector3.Dot(cam.transform.forward, to / d) > lookDotThreshold;
    }

    Vector3 WorldCenter() { return transform.TransformPoint(localCenter); }

    void HandleMouse(Mouse mouse)
    {
        if (mouse.leftButton.wasPressedThisFrame)
        {
            dragging = false;
            KeyEntry hit;
            if (TryPick(mouse.position.ReadValue(), out hit)) Click(hit);
            else dragging = true;
        }
        if (!mouse.leftButton.isPressed) dragging = false;

        if (dragging || mouse.rightButton.isPressed)
        {
            Vector2 d = mouse.delta.ReadValue();
            Vector3 pivot = WorldCenter();
            transform.RotateAround(pivot, cam.transform.up, -d.x * rotateSpeed);
            transform.RotateAround(pivot, cam.transform.right, d.y * rotateSpeed);
        }
    }

    bool TryPick(Vector2 screenPos, out KeyEntry entry)
    {
        entry = null;
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore)) return false;

        Transform t = hit.collider.transform;
        while (t != null && t != transform.parent)
        {
            if (map.TryGetValue(t, out entry)) return true;
            t = t.parent;
        }
        entry = null;
        return false;
    }

    // ---------- klik tombol ----------

    void Click(KeyEntry e)
    {
        if (busy || coverAmount < 0.95f) return;
        e.timer = pressHold;

        if (e == powerEntry) { PressPower(); return; }

        Play(keyClip);
        if (unlocked || flash != null) return;

        string v = e.value == null ? "" : e.value.Trim();
        if (v == "C" || v == "c")
        {
            entered = "";
        }
        else if (v.Length == 1 && char.IsDigit(v[0]))
        {
            entered += v;
            UpdateDisplay();
            if (entered.Length >= pin.Length) StartCoroutine(CheckPin());
            return;
        }
        UpdateDisplay();
    }

    IEnumerator CheckPin()
    {
        yield return new WaitForSeconds(0.25f);

        if (entered == pin)
        {
            unlocked = true;
            UpdateDisplay();
            if (radioAudio != null && unlockedClip != null)
            {
                radioAudio.clip = unlockedClip;
                radioAudio.loop = true;
                radioAudio.volume = 1f;
                radioAudio.Play();
            }
            Say(pinCorrectLines);
        }
        else
        {
            Play(wrongClip);
            flash = wrongText;
            flashColor = wrongColor;
            UpdateDisplay();
            yield return new WaitForSeconds(wrongShowTime);
            flash = null;
            entered = "";
            UpdateDisplay();
        }
    }

    void PressPower()
    {
        Play(powerClip);
        if (!unlocked)
        {
            Say(powerLockedLines);
            return;
        }
        StartCoroutine(Ending());
    }

    IEnumerator Ending()
    {
        state = State.Ending;
        busy = true;
        dragging = false;


        yield return new WaitForSeconds(0.3f);
        Say(endingLines);
        yield return null;
        while (Monolog.Instance != null && Monolog.Instance.IsPlaying) yield return null;
        yield return new WaitForSeconds(0.5f);

        for (float t = 0f; t < 1f;)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, fadeTime);
            fadeAlpha = Mathf.Clamp01(t);
            yield return null;
        }
        fadeAlpha = 1f;
        yield return new WaitForSeconds(0.6f);
        for (float t = 0f; t < 1f;)
        {
            t += Time.deltaTime / 1.5f;
            textAlpha = Mathf.Clamp01(t);
            yield return null;
        }
        textAlpha = 1f;
    }

    

    // ---------- zoom ----------

    IEnumerator Enter()
    {
        state = State.Entering;
        camT = cam.transform;
        camParent = camT.parent;
        camLocalPos = camT.localPosition;
        camLocalRot = camT.localRotation;
        radioPos0 = transform.position;
        radioRot0 = transform.rotation;

        foreach (var b in disableWhileZoomed) if (b != null) b.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Vector3 toPos; Quaternion toRot;
        if (zoomPoint != null) { toPos = zoomPoint.position; toRot = zoomPoint.rotation; }
        else
        {
            Vector3 c = WorldCenter();
            Vector3 dir = (camT.position - c).normalized;
            toPos = c + dir * zoomDistance;
            toRot = Quaternion.LookRotation(-dir, Vector3.up);
        }

        Vector3 p0 = camT.position; Quaternion r0 = camT.rotation;
        for (float t = 0f; t < 1f;)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, zoomTime);
            float s = Mathf.SmoothStep(0f, 1f, t);
            camT.SetPositionAndRotation(Vector3.Lerp(p0, toPos, s), Quaternion.Slerp(r0, toRot, s));
            yield return null;
        }
        camT.SetPositionAndRotation(toPos, toRot);

        state = State.Zoomed;
        coverTarget = 1f;
        if (!introPlayed) { introPlayed = true; Say(zoomLines); }
    }

    IEnumerator Exit()
    {
        state = State.Exiting;
        dragging = false;
        coverTarget = 0f;

        Vector3 p0 = camT.position; Quaternion r0 = camT.rotation;
        Vector3 rp0 = transform.position; Quaternion rr0 = transform.rotation;
        for (float t = 0f; t < 1f;)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, zoomTime);
            float s = Mathf.SmoothStep(0f, 1f, t);
            Vector3 toP = camParent != null ? camParent.TransformPoint(camLocalPos) : camLocalPos;
            Quaternion toR = camParent != null ? camParent.rotation * camLocalRot : camLocalRot;
            camT.SetPositionAndRotation(Vector3.Lerp(p0, toP, s), Quaternion.Slerp(r0, toR, s));
            transform.SetPositionAndRotation(Vector3.Lerp(rp0, radioPos0, s), Quaternion.Slerp(rr0, radioRot0, s));
            yield return null;
        }
        camT.localPosition = camLocalPos;
        camT.localRotation = camLocalRot;
        transform.SetPositionAndRotation(radioPos0, radioRot0);

        foreach (var b in disableWhileZoomed) if (b != null) b.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        state = State.Idle;
    }

    // ---------- animasi tombol & penutup ----------

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        Vector3 keyWorld = transform.TransformDirection(keyPressDirection).normalized * keyPressDepth;
        foreach (var k in keys) AnimatePress(k, dt, keyWorld);
        if (powerEntry != null)
            AnimatePress(powerEntry, dt, transform.TransformDirection(powerPressDirection).normalized * powerPressDepth);

        if (cover != null)
        {
            coverAmount = Mathf.MoveTowards(coverAmount, coverTarget, dt / Mathf.Max(0.01f, coverTime));
            float s = Mathf.SmoothStep(0f, 1f, coverAmount);
            Vector3 slide = transform.TransformDirection(coverSlideDirection).normalized * coverSlideDistance;
            Vector3 world = coverRestIsOpen ? -slide * (1f - s) : slide * s;
            cover.localPosition = coverRestLocal + ToParentLocal(cover, world);
        }
        else coverAmount = coverTarget;
    }

    void AnimatePress(KeyEntry e, float dt, Vector3 fullWorldOffset)
    {
        if (e.key == null) return;
        bool held = e.timer > 0f;
        if (held) e.timer -= dt;
        e.amount = Mathf.MoveTowards(e.amount, held ? 1f : 0f, dt * pressSpeed);
        e.key.localPosition = e.restLocal + ToParentLocal(e.key, fullWorldOffset * e.amount);
    }

    static Vector3 ToParentLocal(Transform t, Vector3 worldVec)
    {
        return t.parent != null ? t.parent.InverseTransformVector(worldVec) : worldVec;
    }

    // ---------- util ----------

    void Say(string[] lines)
    {
        if (lines == null || lines.Length == 0) return;
        if (Monolog.Instance == null) { Debug.LogWarning("RadioPuzzle: tidak ada Monolog di scene"); return; }
        Monolog.Instance.Play(lines);
    }

    void Play(AudioClip c) { if (c != null && sfx != null) sfx.PlayOneShot(c); }

    void UpdateDisplay()
    {
        if (pinText == null) return;
        if (!string.IsNullOrEmpty(flash))
        {
            pinText.text = flash;
            pinText.color = flashColor;
            return;
        }
        var sb = new StringBuilder();
        for (int i = 0; i < pin.Length; i++)
        {
            sb.Append(i < entered.Length ? entered[i] : '_');
            if (i < pin.Length - 1) sb.Append(' ');
        }
        pinText.text = sb.ToString();
        pinText.color = displayColor;
    }

    // ---------- GUI ----------

    void OnGUI()
    {
        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold };
            endStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }

        if (state == State.Idle && looking)
            DrawCentered("[E] Gunakan radio", Screen.height * 0.62f);
        else if (state == State.Zoomed)
            DrawCentered("Drag: putar   |   Klik tombol   |   Q / Esc: keluar", 24f);

        if (fadeAlpha > 0f)
        {
            GUI.depth = -800;   // di atas Monolog (-500), di bawah crosshair (-1000)
            GUI.color = new Color(0f, 0f, 0f, fadeAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (textAlpha > 0f)
            {
                endStyle.fontSize = endFontSize;
                endStyle.normal.textColor = new Color(1f, 1f, 1f, textAlpha);
                float w = Screen.width * 0.8f;
                GUI.Label(new Rect((Screen.width - w) * 0.5f, 0, w, Screen.height), endText, endStyle);
            }
        }
    }

    void DrawCentered(string s, float y)
    {
        var shadow = new Rect(1f, y + 1f, Screen.width, 30f);
        promptStyle.normal.textColor = Color.black;
        GUI.Label(shadow, s, promptStyle);
        promptStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(0f, y, Screen.width, 30f), s, promptStyle);
    }

    // panah bantu di Scene view saat Radio dipilih
    void OnDrawGizmosSelected()
    {
        Vector3 o = transform.position;
        Gizmos.color = Color.red;
        Gizmos.DrawRay(o, transform.TransformDirection(keyPressDirection).normalized * 0.2f);     // arah keycaps masuk
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(o, transform.TransformDirection(powerPressDirection).normalized * 0.2f);   // arah power turun
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(o, transform.TransformDirection(coverSlideDirection).normalized * 0.2f);   // arah Tutupan membuka
    }
}