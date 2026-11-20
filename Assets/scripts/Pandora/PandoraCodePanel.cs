using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

// Section 1: tekan semua tombol dalam urutan yang benar.
// Tombol yang ditekan tetap masuk.
// Salah -> merah, lalu semua tombol naik lagi.
// Benar -> hijau, semua tetap masuk, kamera kembali ke zoom utama dan section ini terkunci.
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

    [Header("UI")]
    public TextMeshProUGUI display;
    public Color normalColor = Color.white;
    public Color correctColor = Color.green;
    public Color wrongColor = Color.red;
    public float wrongResetDelay = 1f;
    public float returnDelay = 1.2f;        // jeda sebelum kembali ke zoom utama setelah benar

    [Header("Efek tekan keycap")]
    [Tooltip("Arah tekan = arah pandang titik ini. Kosongkan = pakai Focus Point.")]
    public Transform directionReference;
    public bool flipDirection = false;      // centang kalau tombolnya bergerak ke arah sebaliknya
    public Vector3 fallbackDirection = Vector3.forward;   // sumbu lokal objek ini, kalau tidak ada referensi
    public float pressFraction = 0.5f;
    public float pressSpeed = 25f;

    private readonly List<Collider> keyColliders = new List<Collider>();
    private readonly List<KeycapButton> buttons = new List<KeycapButton>();
    private readonly List<string> entered = new List<string>();
    private bool locked = false;

    Vector3 PressWorldDirection()
    {
        Transform r = directionReference != null ? directionReference : focusPoint;
        Vector3 d = r != null ? r.forward : transform.TransformDirection(fallbackDirection);
        return flipDirection ? -d : d;
    }

    void Awake()
    {
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

            string captured = e.label;
            b.onPressed.AddListener(() => Press(captured));
        }

        if (display != null) display.gameObject.SetActive(false);
    }

    public override void OnFocus()
    {
        foreach (Collider c in keyColliders) c.enabled = !solved;
        if (display != null) display.gameObject.SetActive(true);
        Refresh(solved ? correctColor : normalColor);
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
        }

        if (display != null) display.gameObject.SetActive(false);
    }

    void Press(string label)
    {
        if (solved || locked) return;

        entered.Add(label);
        Refresh(normalColor);

        if (entered.Count >= correctSequence.Length) Check();
    }

    void Check()
    {
        bool ok = true;
        for (int i = 0; i < correctSequence.Length; i++)
            if (entered[i] != correctSequence[i]) { ok = false; break; }

        if (ok)
        {
            Refresh(correctColor);
            foreach (Collider c in keyColliders) c.enabled = false;
            SetSolved();
            StartCoroutine(ReturnAfterDelay());
        }
        else
        {
            Refresh(wrongColor);
            StartCoroutine(ResetAfterDelay());
        }
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
        Refresh(normalColor);
    }

    void ReleaseAll()
    {
        foreach (KeycapButton b in buttons) b.Release();
    }

    void SetButtonsInteractable(bool value)
    {
        foreach (KeycapButton b in buttons) b.interactable = value;
    }

    void Refresh(Color color)
    {
        if (display == null) return;
        display.text = string.Join(" ", entered);
        display.color = color;
    }

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

    // Scene view: angka tiap keycap (cyan) dan garis kuning = arah tombol masuk
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