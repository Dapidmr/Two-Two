using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Section 1: tekan semua tombol dalam urutan yang benar.
// - Tombol yang ditekan tetap masuk dan diberi TANDA (titik berwarna) supaya jelas terlihat.
// - Setelah semua tombol ditekan baru dicek, ditampilkan lewat lingkaran indikator:
//     MERAH = salah (lalu semua tombol naik lagi), HIJAU = benar (kembali ke zoom utama, section terkunci).
// Tombol dengan label yang sama dianggap sama (misal 3 tombol berlabel "I").
public class PandoraCodePanel : PandoraPanel
{
    [System.Serializable]
    public class KeyEntry
    {
        public Transform keycap;
        public string label = "";
    }

    [Header("Peta tombol (atur sendiri di sini)")]
    public KeyEntry[] keyMap;

    [Header("Isi otomatis (dipakai kalau Key Map kosong)")]
    public string keycapNamePrefix = "Plane";
    public string[] autoLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };

    [Header("Kode")]
    public string[] correctSequence = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };

    [Header("Indikator lingkaran")]
    [Tooltip("Posisi di layar (0-1). Default: tengah atas.")]
    public Vector2 indicatorPosition = new Vector2(0.5f, 0.9f);
    public float indicatorSize = 90f;
    public Color waitColor = new Color(1f, 1f, 1f, 0.35f);
    public Color correctColor = new Color(0.2f, 1f, 0.3f, 1f);
    public Color wrongColor = new Color(1f, 0.15f, 0.15f, 1f);
    public Color rimColor = new Color(0f, 0f, 0f, 0.6f);
    public float wrongResetDelay = 1f;      // lama merah sebelum tombol naik lagi
    public float returnDelay = 1.2f;        // jeda sebelum kembali ke zoom utama setelah benar

    [Header("Tanda tombol ditekan")]
    public bool showPressMarker = true;
    public Color markerColor = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("Ukuran titik, sebagai bagian dari sisi terkecil tombol.")]
    [Range(0.1f, 0.8f)] public float markerSize = 0.3f;
    [Tooltip("Letak titik di tombol: X kanan, Y atas, dari tengah (1 = tepi tombol).")]
    public Vector2 markerCorner = new Vector2(0.6f, 0.6f);
    [Tooltip("Jarak titik di depan permukaan tombol (satuan dunia).")]
    public float markerLift = 0.003f;

    [Header("Efek tekan keycap")]
    [Tooltip("Arah tekan = arah pandang titik ini. Kosongkan = pakai Focus Point.")]
    public Transform directionReference;
    public bool flipDirection = false;      // centang kalau tombolnya bergerak ke arah sebaliknya
    public Vector3 fallbackDirection = Vector3.forward;   // sumbu lokal objek ini, kalau tidak ada referensi
    public float pressFraction = 0.5f;
    public float pressSpeed = 25f;

    private readonly List<Collider> keyColliders = new List<Collider>();
    private readonly List<KeycapButton> buttons = new List<KeycapButton>();
    private readonly List<SpriteRenderer> markers = new List<SpriteRenderer>();
    private readonly List<string> entered = new List<string>();
    private PandoraIndicator ind;
    private Color markerCurrent;
    private bool locked = false;

    Vector3 PressWorldDirection()
    {
        Transform r = directionReference != null ? directionReference : focusPoint;
        Vector3 d = r != null ? r.forward : transform.TransformDirection(fallbackDirection);
        return flipDirection ? -d : d;
    }

    void Awake()
    {
        markerCurrent = markerColor;
        Vector3 dir = PressWorldDirection();

        foreach (KeyEntry e in GetEntries())
        {
            Transform t = e.keycap;

            Collider c = t.GetComponent<Collider>();
            if (c == null) c = AddBoxCollider(t);
            c.enabled = false;              // baru aktif saat fokus ke section ini
            keyColliders.Add(c);

            KeycapButton b = t.gameObject.AddComponent<KeycapButton>();
            b.Setup(c, dir, pressFraction, pressSpeed, true, false);   // tetap masuk, tidak bisa ditekan dua kali
            buttons.Add(b);

            markers.Add(CreateMarker(t));

            string captured = e.label;
            b.onPressed.AddListener(() => Press(captured));
        }
    }

    void OnDestroy()
    {
        if (ind != null) ind.Destroy();
    }

    void Update()
    {
        if (ind != null) ind.Tick();

        // Titik penanda hanya tampil di tombol yang sedang masuk
        for (int i = 0; i < markers.Count; i++)
        {
            SpriteRenderer m = markers[i];
            if (m == null) continue;

            bool on = showPressMarker && buttons[i].IsPressed;
            if (m.gameObject.activeSelf != on) m.gameObject.SetActive(on);
            if (on) m.color = markerCurrent;
        }
    }

    public override void OnFocus()
    {
        foreach (Collider c in keyColliders) c.enabled = !solved;

        if (ind == null) ind = new PandoraIndicator();
        if (!ind.IsBuilt) ind.Build("CodeIndicatorCanvas", indicatorPosition, indicatorSize, waitColor, rimColor);
        ind.Show(true);
        ind.Set(solved ? correctColor : waitColor, false, true);
        markerCurrent = solved ? correctColor : markerColor;
    }

    public override void OnUnfocus()
    {
        foreach (Collider c in keyColliders) c.enabled = false;
        StopAllCoroutines();
        locked = false;
        SetButtonsInteractable(true);

        if (!solved)
        {
            entered.Clear();
            ReleaseAll();
            markerCurrent = markerColor;
        }

        if (ind != null) ind.Show(false);
    }

    void Press(string label)
    {
        if (solved || locked) return;

        entered.Add(label);
        if (entered.Count >= correctSequence.Length) Check();
    }

    void Check()
    {
        bool ok = true;
        for (int i = 0; i < correctSequence.Length; i++)
            if (entered[i] != correctSequence[i]) { ok = false; break; }

        if (ok)
        {
            ShowResult(correctColor);
            foreach (Collider c in keyColliders) c.enabled = false;
            SetSolved();
            StartCoroutine(ReturnAfterDelay());
        }
        else
        {
            ShowResult(wrongColor);
            StartCoroutine(ResetAfterDelay());
        }
    }

    void ShowResult(Color c)
    {
        markerCurrent = c;
        if (ind != null) ind.Set(c, true);
    }

    IEnumerator ReturnAfterDelay()
    {
        yield return new WaitForSeconds(returnDelay);
        if (owner != null) owner.LeaveFocus();
    }

    IEnumerator ResetAfterDelay()
    {
        locked = true;
        SetButtonsInteractable(false);

        yield return new WaitForSeconds(wrongResetDelay);

        entered.Clear();
        ReleaseAll();                       // semua tombol naik lagi
        SetButtonsInteractable(true);
        locked = false;

        markerCurrent = markerColor;
        if (ind != null) ind.Set(waitColor, false);
    }

    void ReleaseAll()
    {
        foreach (KeycapButton b in buttons) b.Release();
    }

    void SetButtonsInteractable(bool value)
    {
        foreach (KeycapButton b in buttons) b.interactable = value;
    }

    // ---------- Tanda di tombol ----------

    // Titik berwarna kecil di sudut permukaan tombol, ikut bergerak masuk bersama tombol
    SpriteRenderer CreateMarker(Transform key)
    {
        Renderer r = key.GetComponentInChildren<Renderer>();
        if (r == null) return null;

        Transform rf = directionReference != null ? directionReference : focusPoint;
        Vector3 viewDir = rf != null ? rf.forward : PressWorldDirection().normalized;
        Vector3 right = rf != null ? rf.right : Vector3.Cross(Vector3.up, viewDir).normalized;
        Vector3 up = rf != null ? rf.up : Vector3.Cross(viewDir, right).normalized;

        Vector3 e = r.bounds.extents;
        float halfW = Proj(right, e);
        float halfH = Proj(up, e);
        float halfD = Proj(viewDir, e);

        GameObject go = new GameObject("PressMarker");
        go.transform.SetParent(key, true);
        go.transform.position = r.bounds.center
                                - viewDir * (halfD + markerLift)
                                + right * (halfW * markerCorner.x)
                                + up * (halfH * markerCorner.y);
        go.transform.rotation = Quaternion.LookRotation(viewDir, up);

        // Ukuran dunia: diameter = sisi terkecil tombol x markerSize (sprite 128 px pada 100 px per unit = 1,28 unit)
        float diameter = Mathf.Min(halfW, halfH) * 2f * markerSize;
        float s = diameter / 1.28f;
        go.transform.localScale = Vector3.one;
        Vector3 ls = go.transform.lossyScale;
        go.transform.localScale = new Vector3(
            s / Mathf.Max(Mathf.Abs(ls.x), 0.00001f),
            s / Mathf.Max(Mathf.Abs(ls.y), 0.00001f),
            s / Mathf.Max(Mathf.Abs(ls.z), 0.00001f));

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = PandoraIndicator.GetCircle();
        sr.color = markerColor;
        sr.sortingOrder = 10;

        go.SetActive(false);
        return sr;
    }

    float Proj(Vector3 axis, Vector3 e)
    {
        return Mathf.Abs(axis.x) * e.x + Mathf.Abs(axis.y) * e.y + Mathf.Abs(axis.z) * e.z;
    }

    // ---------- Daftar tombol ----------

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
            e.label = i < autoLabels.Length ? autoLabels[i] : (i + 1).ToString();
            list.Add(e);
        }
        return list;
    }

    List<Transform> FindKeycapsByName()
    {
        List<Transform> list = new List<Transform>();
        foreach (Transform t in transform.root.GetComponentsInChildren<Transform>(true))
            if (t != transform && t.name.StartsWith(keycapNamePrefix))
                list.Add(t);

        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }

    [ContextMenu("Isi Key Map Otomatis")]
    void FillKeyMap()
    {
        List<Transform> found = FindKeycapsByName();
        keyMap = new KeyEntry[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            keyMap[i] = new KeyEntry();
            keyMap[i].keycap = found[i];
            keyMap[i].label = i < autoLabels.Length ? autoLabels[i] : (i + 1).ToString();
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

    // Scene view: label tiap keycap (cyan) dan garis kuning = arah tombol masuk
    void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.cyan;
        style.fontSize = 22;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;

        Gizmos.color = Color.yellow;
        Vector3 dir = PressWorldDirection().normalized;

        foreach (KeyEntry e in GetEntries())
        {
            if (e.keycap == null) continue;
            Renderer r = e.keycap.GetComponentInChildren<Renderer>();
            Vector3 center = r != null ? r.bounds.center : e.keycap.position;
            Gizmos.DrawLine(center, center + dir * 0.1f);
            UnityEditor.Handles.Label(center, e.label, style);
        }
#endif
    }
}