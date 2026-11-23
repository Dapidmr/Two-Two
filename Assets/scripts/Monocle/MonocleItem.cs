using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class MonocleItem : MonoBehaviour
{
    public int monocleId = 1;             // 1 atau 2
    public float pickupDistance = 1.5f;
    public Transform player;              // drag Capsule

    [Header("Penanda")]
    public bool tintItem = true;          // warnai objek monocle sesuai nomornya
    public bool showLabel = true;         // label melayang "Monocle 1/2"
    public Color color1 = new Color(1f, 0.85f, 0.2f);   // kuning
    public Color color2 = new Color(0.3f, 0.9f, 1f);    // biru muda
    public float labelSize = 1.5f;        // besar tulisan, sesuaikan dengan skala ruanganmu
    public float labelHeight = 0.3f;      // tinggi label di atas objek

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
        if (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame) return;

        Transform p = player != null ? player : (Camera.main != null ? Camera.main.transform : null);
        if (p == null)
        {
            Debug.LogWarning("[MonocleItem] player kosong dan tidak ada Main Camera");
            return;
        }

        // jarak ke titik terdekat di permukaan model (bukan ke pivot)
        float d = Vector3.Distance(p.position, transform.position);
        Renderer r = GetComponentInChildren<Renderer>();
        if (r != null) d = Vector3.Distance(p.position, r.bounds.ClosestPoint(p.position));

        Debug.Log($"[MonocleItem {monocleId}] E ditekan | pemain={p.name} | jarak={d:F2} | batas={pickupDistance}");

        if (d <= pickupDistance)
        {
            if (monocleId == 2) GameState.SetMonocle2(true);
            else GameState.SetMonocle(true);

            Debug.Log("[MonocleItem] Monocle " + monocleId + " diambil");
            if (labelTf != null) Destroy(labelTf.gameObject);
            gameObject.SetActive(false);
        }
    }
}