using System.Collections.Generic;
using UnityEngine;

// Mengubah keycap di radio menjadi tombol yang bisa ditekan untuk memasukkan kode.
// Pasang di objek Radio (induk) yang punya ZoomView dan RadioTuner.
// Collider dan efek tekan ditambahkan otomatis saat game dimulai.
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

    [Header("Peta keycap (atur sendiri di sini)")]
    public KeyEntry[] keyMap;               // tiap baris = satu keycap dan angkanya

    [Header("Isi otomatis (dipakai kalau Key Map kosong)")]
    public string keycapNamePrefix = "Keycaps";   // semua objek berawalan nama ini dianggap keycap
    public string keys = "123456789*0#";          // urut sesuai nama (Keycaps.001, .002, ...)

    [Header("Efek tekan keycap")]
    public Vector3 pressDirection = Vector3.down;  // arah keycap turun (sumbu lokal objek induknya)
    public float pressFraction = 0.5f;
    public float pressSpeed = 25f;

    void Awake()
    {
        if (tuner == null) tuner = GetComponent<RadioTuner>();
        if (zoomView == null) zoomView = GetComponent<ZoomView>();

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

            // Efek tekan + kirim tombol yang ditekan ke RadioTuner
            PressButtonEffect fx = t.gameObject.AddComponent<PressButtonEffect>();
            fx.pressDirection = pressDirection;
            fx.pressFraction = pressFraction;
            fx.pressSpeed = pressSpeed;

            string captured = e.key;
            if (!string.IsNullOrEmpty(captured))
                fx.onPressed.AddListener(() => { if (tuner != null) tuner.EnterKey(captured); });
        }

        // Daftarkan ke ZoomView supaya hanya bisa ditekan saat zoom, dan tidak ikut memutar radio
        if (zoomView != null)
        {
            List<Collider> all = new List<Collider>();
            if (zoomView.controlColliders != null) all.AddRange(zoomView.controlColliders);
            all.AddRange(newColliders);
            zoomView.controlColliders = all.ToArray();
        }
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

    // Saat Radio dipilih: angka tiap keycap muncul di Scene view, plus garis kuning arah turunnya
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

        foreach (KeyEntry e in GetEntries())
        {
            Transform t = e.keycap;
            if (t == null) continue;

            Vector3 center = CenterOf(t);
            Vector3 d = PressButtonEffect.DirWorld(t, pressDirection);
            Gizmos.DrawLine(center, center + d * PressButtonEffect.PressDistance(t, d, pressFraction));

#if UNITY_EDITOR
            UnityEditor.Handles.Label(center, e.key, style);
#endif
        }
    }
}   