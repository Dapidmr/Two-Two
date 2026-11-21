using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class MonocleLens : MonoBehaviour
{
    [System.Serializable]
    public class LensSlot
    {
        public int monocleId = 1;
        public RectTransform lensUI;
        public Vector2 startPos = new Vector2(0.9f, 0.18f);
        [HideInInspector] public bool wasActive;
    }

    [Header("Lensa (satu entri per monocle)")]
    public LensSlot[] lenses = new LensSlot[]
    {
        new LensSlot { monocleId = 1, startPos = new Vector2(0.9f, 0.18f) },
        new LensSlot { monocleId = 2, startPos = new Vector2(0.1f, 0.18f) }
    };

    [Header("Umum")]
    public float radiusUV = 0.15f;
    public float grabRadius = 90f;
    [Range(0.3f, 1.5f)] public float lensFill = 0.8f;
    public Transform zoomSource;          // kosong = ZoomView di objek ini

    [Header("Penanda")]
    public bool tintLens = true;
    public bool showNumber = true;
    public Color color1 = new Color(1f, 0.85f, 0.2f);
    public Color color2 = new Color(0.3f, 0.9f, 1f);
    public float numberSize = 36f;

    private PageLensPage[] pages;
    private ZoomView zoomView;
    private int dragIndex = -1;

    bool HasMonocle(int id) { return id == 2 ? GameState.hasMonocle2 : GameState.hasMonocle; }
    Color ColorOf(int id) { return id == 2 ? color2 : color1; }

    void Start()
    {
        pages = GetComponentsInChildren<PageLensPage>(true);
        Debug.Log("[MonocleLens] jumlah halaman: " + pages.Length);

        foreach (LensSlot s in lenses)
        {
            if (s == null || s.lensUI == null) continue;
            if (tintLens)
            {
                Graphic g = s.lensUI.GetComponent<Graphic>();
                if (g != null) g.color = ColorOf(s.monocleId);
            }
            if (showNumber) CreateNumber(s);
            s.lensUI.gameObject.SetActive(false);
        }
    }

    void CreateNumber(LensSlot s)
    {
        GameObject go = new GameObject("Number");
        go.transform.SetParent(s.lensUI, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -8f);
        rt.sizeDelta = new Vector2(200f, 50f);

        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = s.monocleId.ToString();
        t.color = ColorOf(s.monocleId);
        t.fontSize = numberSize;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
    }

    bool SlotActive(LensSlot s)
    {
        return s != null && s.lensUI != null && zoomView != null && zoomView.ContentOpen && HasMonocle(s.monocleId);
    }

    void LateUpdate()
    {
        if (zoomView == null)
        {
            if (zoomSource != null) zoomView = zoomSource.GetComponent<ZoomView>();
            else zoomView = GetComponent<ZoomView>();
        }

        for (int i = 0; i < lenses.Length; i++)
        {
            LensSlot s = lenses[i];
            if (s == null || s.lensUI == null) continue;

            bool active = SlotActive(s);
            if (active && !s.wasActive)
            {
                s.lensUI.gameObject.SetActive(true);
                s.lensUI.position = new Vector2(Screen.width * s.startPos.x, Screen.height * s.startPos.y);
                s.wasActive = true;
            }
            else if (!active && s.wasActive)
            {
                Hide(s);
                if (dragIndex == i) dragIndex = -1;
                s.wasActive = false;
            }
        }

        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 m = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            float best = grabRadius;
            dragIndex = -1;
            for (int i = 0; i < lenses.Length; i++)
            {
                LensSlot s = lenses[i];
                if (s == null || !s.wasActive) continue;
                float d = Vector2.Distance(m, s.lensUI.position);
                if (d < best) { best = d; dragIndex = i; }
            }
        }
        if (mouse.leftButton.wasReleasedThisFrame) dragIndex = -1;
        if (dragIndex >= 0 && lenses[dragIndex] != null) lenses[dragIndex].lensUI.position = m;

        Camera cam = Camera.main;
        foreach (LensSlot s in lenses)
        {
            if (s == null || !s.wasActive) continue;

            Ray ray = cam.ScreenPointToRay(s.lensUI.position);

            PageLensPage front = null;
            Vector2 uv = Vector2.zero;
            float hitDist = 0f, bestDist = float.MaxValue;
            foreach (PageLensPage p in pages)
            {
                if (!p.gameObject.activeInHierarchy) continue;
                Vector2 puv; float pd;
                if (p.RayToUV(ray, out puv, out pd) && pd < bestDist)
                {
                    bestDist = pd; front = p; uv = puv; hitDist = pd;
                }
            }

            PageLensPage target = (front != null && front.monocleId == s.monocleId) ? front : null;

            float radius = radiusUV;
            if (target != null)
            {
                float pxRadius = s.lensUI.rect.width * 0.5f * s.lensUI.lossyScale.x * lensFill;
                float worldPerPixel = 2f * hitDist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
                radius = pxRadius * worldPerPixel / target.WorldHeight();
            }

            foreach (PageLensPage p in pages)
                if (p.monocleId == s.monocleId)
                    p.SetLens(uv, radius, p == target);
        }
    }

    void Hide(LensSlot s)
    {
        s.lensUI.gameObject.SetActive(false);
        foreach (PageLensPage p in pages)
            if (p.monocleId == s.monocleId)
                p.SetLens(Vector2.zero, radiusUV, false);
    }
}