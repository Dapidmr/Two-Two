using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Mengubah keycap di radio menjadi tombol yang bisa ditekan untuk memasukkan kode.
// Pasang di objek Radio (induk) yang punya ZoomView dan RadioTuner.
// Collider dan efek tekan ditambahkan otomatis saat game dimulai.
// Tombol bergerak MASUK searah pandangan kamera zoom (Zoom Point), bukan turun ke bawah.
public class RadioKeypad : MonoBehaviour
{
    [System.Serializable]
    public class KeyEntry
    {
        public Transform keycap;    // objek keycap di model
        public string key = "";     // angka atau simbol pada keycap itu (misal 1, 2, *, #)
    }

    public RadioTuner tuner;                // kosongkan kalau ada di objek yang sama
    public ZoomView zoomView;               // kosongkan kalau ada di objek yang sama

    [Header("Tutupan di depan keypad (opsional)")]
    [Tooltip("Kalau diisi, keypad hanya bisa ditekan ketika cover ini terbuka.")]
    public SlidingCover cover;

    [Header("Peta keycap (atur sendiri di sini)")]
    public KeyEntry[] keyMap;               // tiap baris = satu keycap dan angkanya

    [Header("Isi otomatis (dipakai kalau Key Map kosong)")]
    public string keycapNamePrefix = "Keycaps";   // semua objek berawalan nama ini dianggap keycap
    public string keys = "123456789*0#";          // urut sesuai nama (Keycaps.001, .002, ...)

    [Header("Efek tekan keycap")]
    [Tooltip("Arah tekan = arah pandang titik ini. Kosongkan = pakai Zoom Point dari ZoomView.")]
    public Transform directionReference;
    [Tooltip("Centang kalau tombolnya malah maju keluar. Tombol harus bergerak MASUK ke dalam radio.")]
    public bool flipDirection = true;
    public Vector3 fallbackDirection = Vector3.down;     // sumbu lokal radio, kalau tidak ada referensi
    public float pressFraction = 0.5f;
    public float pressSpeed = 25f;

    [Header("Tombol tetap masuk")]
    [Tooltip("false = tombol ditekan lalu naik lagi sendiri. true = tombol tetap masuk sampai kode salah / ditekan *.")]
    public bool latchKeys = false;
    [Tooltip("Tombol dengan simbol ini tidak ikut tertahan (naik lagi setelah ditekan).")]
    public string momentaryKeys = "*#";

    [Header("Suara tombol")]
    public AudioClip pressSound;            // suara saat tombol ditekan
    [Range(0f, 1f)] public float pressSoundVolume = 1f;
    [Tooltip("Variasi nada tiap tekan supaya tidak monoton. 0 = selalu sama.")]
    [Range(0f, 0.3f)] public float pitchVariation = 0.05f;
    [Tooltip("Kosongkan = dibuat otomatis di objek ini.")]
    public AudioSource soundSource;

    private readonly List<KeycapButton> buttons = new List<KeycapButton>();
    private bool locked = false;
    private bool releasing = false;

    Vector3 PressWorldDirection()
    {
        Transform r = directionReference;
        if (r == null && zoomView != null) r = zoomView.zoomPoint;
        if (r == null)
        {
            ZoomView zv = GetComponent<ZoomView>();
            if (zv != null) r = zv.zoomPoint;
        }

        Vector3 d = r != null ? r.forward : transform.TransformDirection(fallbackDirection);
        return flipDirection ? -d : d;
    }

    void Awake()
    {
        if (tuner == null) tuner = GetComponent<RadioTuner>();
        if (zoomView == null) zoomView = GetComponent<ZoomView>();

        // Siapkan AudioSource untuk suara tombol
        if (soundSource == null)
        {
            soundSource = gameObject.AddComponent<AudioSource>();
            soundSource.playOnAwake = false;
            soundSource.loop = false;
            soundSource.spatialBlend = 0f;   // 2D, selalu terdengar jelas
        }

        Vector3 dir = PressWorldDirection();

        List<KeyEntry> entries = GetEntries();
        List<Collider> newColliders = new List<Collider>();

        foreach (KeyEntry e in entries)
        {
            Transform t = e.keycap;

            // Pastikan ada collider. Mati dulu sampai pemain zoom (diatur ZoomView).
            Collider c = t.GetComponent<Collider>();
            if (c == null) c = AddBoxCollider(t);
            c.enabled = false;
            newColliders.Add(c);

            // Angka ikut tertahan kalau latchKeys aktif; * dan # (atau tombol tanpa angka) selalu naik lagi
            bool isMomentary = string.IsNullOrEmpty(e.key) || momentaryKeys.Contains(e.key);
            bool latch = latchKeys && !isMomentary;

            // Efek tekan + kirim tombol yang ditekan ke RadioTuner
            KeycapButton b = t.gameObject.AddComponent<KeycapButton>();
            b.Setup(c, dir, pressFraction, pressSpeed, latch, true);   // true = angka kembar (misal 34511) boleh ditekan lagi
            buttons.Add(b);

            // Suara untuk SEMUA tombol (termasuk * dan #)
            b.onPressed.AddListener(PlayPressSound);

            string captured = e.key;
            if (!string.IsNullOrEmpty(captured))
                b.onPressed.AddListener(() => { if (tuner != null) tuner.EnterKey(captured); });
        }

        // Daftarkan ke ZoomView supaya hanya bisa ditekan saat zoom, dan tidak ikut memutar radio
        if (zoomView != null)
        {
            List<Collider> all = new List<Collider>();
            if (zoomView.controlColliders != null) all.AddRange(zoomView.controlColliders);
            all.AddRange(newColliders);
            zoomView.controlColliders = all.ToArray();
        }

        // Keypad hanya aktif kalau cover (kalau ada) sedang terbuka, dan belum terkunci
        if (cover != null)
        {
            cover.onOpened.AddListener(HandleCoverOpened);
            cover.onClosed.AddListener(HandleCoverClosed);
            SetInteractable(cover.IsOpen && !locked);
        }
        else
        {
            SetInteractable(!locked);
        }
    }

    void PlayPressSound()
    {
        if (pressSound == null || soundSource == null) return;
        soundSource.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        soundSource.PlayOneShot(pressSound, pressSoundVolume);
    }

    void HandleCoverOpened()
    {
        if (!locked && !releasing) SetInteractable(true);
    }

    void HandleCoverClosed()
    {
        SetInteractable(false);
    }

    // Naikkan semua tombol sekarang juga (dipanggil RadioTuner saat * ditekan)
    public void ReleaseAll()
    {
        foreach (KeycapButton b in buttons) b.Release();
    }

    // Tombol terkunci sebentar, lalu semua naik lagi (dipanggil RadioTuner saat kode salah)
    public void ReleaseAllAfter(float delay)
    {
        if (releasing) return;
        StartCoroutine(ReleaseRoutine(delay));
    }

    IEnumerator ReleaseRoutine(float delay)
    {
        releasing = true;
        SetInteractable(false);
        yield return new WaitForSeconds(delay);
        ReleaseAll();
        releasing = false;
        if (!locked && (cover == null || cover.IsOpen)) SetInteractable(true);
    }

    // Kode benar: tombol tetap seperti sekarang dan tidak bisa ditekan lagi
    public void LockKeys()
    {
        locked = true;
        SetInteractable(false);
    }

    public void SetInteractable(bool value)
    {
        foreach (KeycapButton b in buttons) b.interactable = value;
    }

    // Daftar keycap yang dipakai: dari Key Map kalau diisi, kalau kosong dicari otomatis dari nama
    List<KeyEntry> GetEntries()
    {
        List<KeyEntry> list = new List<KeyEntry>();

        if (keyMap != null && keyMap.Length > 0)
        {
            foreach (KeyEntry e in keyMap)
                if (e != null && e.keycap != null) list.Add(e);
            return list;
        }

        List<Transform> found = FindKeycapsByName();
        for (int i = 0; i < found.Count; i++)
        {
            KeyEntry e = new KeyEntry();
            e.keycap = found[i];
            e.key = i < keys.Length ? keys[i].ToString() : "";
            list.Add(e);
        }
        return list;
    }

    List<Transform> FindKeycapsByName()
    {
        List<Transform> list = new List<Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t != transform && t.name.StartsWith(keycapNamePrefix))
                list.Add(t);

        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }

    // Klik kanan judul komponen ini di Inspector > "Isi Key Map Otomatis"
    [ContextMenu("Isi Key Map Otomatis")]
    void FillKeyMap()
    {
        List<Transform> found = FindKeycapsByName();
        keyMap = new KeyEntry[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            keyMap[i] = new KeyEntry();
            keyMap[i].keycap = found[i];
            keyMap[i].key = i < keys.Length ? keys[i].ToString() : "";
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    BoxCollider AddBoxCollider(Transform t)
    {
        BoxCollider box = t.gameObject.AddComponent<BoxCollider>();
        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            box.center = mf.sharedMesh.bounds.center;
            box.size = mf.sharedMesh.bounds.size;
        }
        return box;
    }

    Vector3 CenterOf(Transform t)
    {
        Renderer r = t.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.center : t.position;
    }

    // Saat Radio dipilih: angka tiap keycap muncul di Scene view, plus garis kuning arah masuknya
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

#if UNITY_EDITOR
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.cyan;
        style.fontSize = 22;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
#endif

        Vector3 d = PressWorldDirection().normalized;

        foreach (KeyEntry e in GetEntries())
        {
            Transform t = e.keycap;
            if (t == null) continue;

            Vector3 center = CenterOf(t);
            Gizmos.DrawLine(center, center + d * 0.1f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(center, e.key, style);
#endif
        }
    }
}