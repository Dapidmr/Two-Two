using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class MonocleItem : MonoBehaviour
{
    public int monocleId = 1;             // 1 atau 2

    [Header("Interaksi")]
    public float pickupDistance = 3f;     // jarak maksimal dari kamera ke monocle
    public float aimPadding = 0.1f;       // area bidik diperbesar sedikit supaya benda kecil mudah diarahkan
    public string takeText = "Press E to Take";

    [Header("Penanda")]
    public bool tintItem = true;          // warnai objek monocle sesuai nomornya
    public bool showLabel = true;         // label melayang "Monocle 1/2"
    public Color color1 = new Color(1f, 0.85f, 0.2f);   // kuning
    public Color color2 = new Color(0.3f, 0.9f, 1f);    // biru muda
    public float labelSize = 1.5f;        // besar tulisan, sesuaikan dengan skala ruanganmu
    public float labelHeight = 0.3f;      // tinggi label di atas objek

    [Header("Event")]
    public UnityEvent onPickedUp;         // BARU: nyala saat monocle diambil (untuk monolog)

    private Transform labelTf;

    Color MyColor { get { return monocleId == 2 ? color2 : color1; } }

    void Start()
    {
        if (tintItem) Tint();
        if (showLabel) CreateLabel();
    }

    void Tint()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            // r.materials = salinan semua slot material, tidak mengubah asset aslinya
            foreach (Material m in r.materials)
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", MyColor);
                else if (m.HasProperty("_Color")) m.SetColor("_Color", MyColor);
            }
        }
    }

    void CreateLabel()
    {
        Renderer r = GetComponentInChildren<Renderer>();
        Vector3 top = r != null ? r.bounds.center + Vector3.up * (r.bounds.extents.y + labelHeight)
                                : transform.position + Vector3.up * labelHeight;

        GameObject go = new GameObject("Label_Monocle" + monocleId);
        labelTf = go.transform;
        labelTf.position = top;

        TextMeshPro t = go.AddComponent<TextMeshPro>();
        t.text = "Monocle " + monocleId;
        t.color = MyColor;
        t.fontSize = labelSize;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
    }

    void LateUpdate()
    {
        // Label selalu menghadap kamera
        if (labelTf != null && Camera.main != null)
            labelTf.rotation = Camera.main.transform.rotation;
    }

    void Update()
    {
        var c = Camera.main;
        if (c == null || Mouse.current == null) return;

        // kursor terkunci -> tengah layar, kursor bebas -> posisi mouse
        Vector2 screenPos = Cursor.lockState == CursorLockMode.Locked
            ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            : Mouse.current.position.ReadValue();

        Ray ray = c.ScreenPointToRay(screenPos);

        // area bidik = gabungan semua renderer di objek ini, diperbesar sedikit
        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        b.Expand(aimPadding * 2f);

        if (!b.IntersectRay(ray, out float dist)) return;
        if (dist > pickupDistance) return;

        // cek tidak ada benda lain yang menghalangi (selain player dan objek ini sendiri)
        var hits = Physics.RaycastAll(ray, dist, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (IsPlayer(hit.transform)) continue;
            if (hit.transform.IsChildOf(transform)) continue;
            if (hit.distance < dist - 0.05f) return;   // ada penghalang di depan
        }

        HintUI.ShowAim(takeText);

        var kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
            Pickup();
    }

    void Pickup()
    {
        if (monocleId == 2) GameState.SetMonocle2(true);
        else GameState.SetMonocle(true);

        Debug.Log("[MonocleItem] Monocle " + monocleId + " diambil");
        if (labelTf != null) Destroy(labelTf.gameObject);

        onPickedUp.Invoke();   // BARU: panggil monolog sebelum objek dimatikan

        gameObject.SetActive(false);
    }

    static bool IsPlayer(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.CompareTag("Player")) return true;
        return false;
    }
}