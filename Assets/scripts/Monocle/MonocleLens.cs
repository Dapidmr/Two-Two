using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Monocle 1: lingkaran yang bisa di-drag. Di dalam lingkaran, halaman buku berubah dari
// foto 1 (texture halaman asli) menjadi foto 2 (texture "readable").
// Isi field Lens UI dengan objek MonocleUI (gambar cincin kamu). Kalau dikosongkan,
// script membuat cincin sederhana sendiri.
public class MonocleLens : MonoBehaviour
{
    [System.Serializable]
    public class PageEntry
    {
        public Renderer page;        // objek halaman (Page / Page.001)
        public Texture readable;     // foto 2: versi yang terbaca
        public bool flipX;           // centang kalau foto 2 tampak terbalik kiri-kanan
        public bool flipY;           // centang kalau foto 2 tampak terbalik atas-bawah
        [HideInInspector] public RawImage image;
        [System.NonSerialized] public Mesh baked;
    }

    [Header("Halaman yang bisa dibaca dengan lensa")]
    public PageEntry[] pages;

    [Header("UI lensa")]
    public RectTransform lensUI;                              // drag objek MonocleUI ke sini
    [Range(0.3f, 1.2f)] public float lensFill = 0.9f;         // ukuran area terbaca relatif ke cincin (kecilkan kalau tumpang tindih cincin)
    public bool bringToFront = true;                          // lensa selalu di atas UI lain saat muncul
    public Vector2 startPos = new Vector2(0.88f, 0.2f);       // posisi awal lensa (0..1 dari layar)

    [Header("Hanya dipakai kalau Lens UI kosong (cincin otomatis)")]
    [Range(0.1f, 0.6f)] public float sizeFraction = 0.28f;
    [Range(0.01f, 0.2f)] public float ringWidth = 0.06f;
    public Color ringColor = new Color(1f, 0.85f, 0.2f);

    [Header("Uji coba")]
    public bool testWithoutMonocle = false;   // lensa muncul tanpa harus mengambil monocle 1
    public bool previewPlacement = false;     // tampilkan foto 2 penuh + semi-transparan untuk cek posisi

    [Header("Lain-lain")]
    public ZoomView zoomView;        // kosong = dicari otomatis
    public Canvas canvas;            // kosong = Canvas tempat Lens UI berada
    public bool debugLog = true;

    private RectTransform root;
    private bool customUI;
    private Mask maskComp;
    private Image maskImg;
    private bool shown;
    private bool dragging;
    private Vector2 grabOffset;

    void Start()
    {
        if (zoomView == null) zoomView = GetComponent<ZoomView>();
        if (zoomView == null) zoomView = GetComponentInParent<ZoomView>();
        if (zoomView == null) zoomView = FindFirstObjectByType<ZoomView>();
        if (zoomView == null) Debug.LogWarning("[MonocleLens] ZoomView tidak ditemukan. Isi field Zoom View.");

        if (canvas == null && lensUI != null) canvas = lensUI.GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) { Debug.LogError("[MonocleLens] tidak ada Canvas di scene"); enabled = false; return; }
        canvas = canvas.rootCanvas;
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            Debug.LogWarning("[MonocleLens] Canvas sebaiknya Screen Space - Overlay, kalau tidak posisi lensa bisa meleset.");

        BuildUI();
    }

    void BuildUI()
    {
        customUI = lensUI != null;

        if (customUI)
        {
            root = lensUI;
        }
        else
        {
            root = NewRect("MonocleLens_UI", canvas.transform);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
        }

        // lapisan pemotong berbentuk lingkaran, sedikit lebih kecil dari cincin (lensFill)
        RectTransform maskRt = NewRect("LensMask", root);
        float inset = (1f - lensFill) * 0.5f;
        maskRt.anchorMin = new Vector2(inset, inset);
        maskRt.anchorMax = new Vector2(1f - inset, 1f - inset);
        maskRt.offsetMin = Vector2.zero;
        maskRt.offsetMax = Vector2.zero;

        maskImg = maskRt.gameObject.AddComponent<Image>();
        maskImg.sprite = MakeSprite(false, 0f);
        maskImg.raycastTarget = false;
        maskComp = maskRt.gameObject.AddComponent<Mask>();
        maskComp.showMaskGraphic = false;

        // foto 2 per halaman, di dalam pemotong
        foreach (PageEntry e in pages)
        {
            if (e == null || e.page == null) continue;
            RectTransform rt = NewRect("Foto2_" + e.page.name, maskRt);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            RawImage img = rt.gameObject.AddComponent<RawImage>();
            img.texture = e.readable;
            img.raycastTarget = false;
            e.image = img;
            if (e.readable == null) Debug.LogError("[MonocleLens] texture readable belum di-assign untuk " + e.page.name);
        }

        // cincin otomatis hanya kalau tidak memakai MonocleUI sendiri
        if (!customUI)
        {
            RectTransform ringRt = NewRect("Ring", root);
            Stretch(ringRt);
            Image ringImg = ringRt.gameObject.AddComponent<Image>();
            ringImg.sprite = MakeSprite(true, ringWidth);
            ringImg.color = ringColor;
            ringImg.raycastTarget = false;
        }

        root.gameObject.SetActive(false);
    }

    static RectTransform NewRect(string n, Transform parent)
    {
        GameObject go = new GameObject(n, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // Membuat sprite lingkaran (atau cincin) lewat kode, jadi tidak butuh file gambar.
    static Sprite MakeSprite(bool ring, float ringRatio)
    {
        const int N = 256;
        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color32[] px = new Color32[N * N];
        float c = (N - 1) * 0.5f;
        float R = N * 0.5f;
        float rin = R * (1f - ringRatio);

        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                float a = Mathf.Clamp01(R - d);
                if (ring) a *= Mathf.Clamp01(d - rin);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    void LateUpdate()
    {
        if (root == null) return;

        bool hasMonocle = testWithoutMonocle || GameState.hasMonocle;
        bool active = zoomView != null && zoomView.ContentOpen && hasMonocle;
        if (active != shown)
        {
            shown = active;
            dragging = false;
            root.gameObject.SetActive(active);
            if (active)
            {
                root.position = new Vector3(Screen.width * startPos.x, Screen.height * startPos.y, 0f);
                if (bringToFront) root.SetAsLastSibling();
                if (debugLog) Debug.Log("[MonocleLens] lensa muncul");
            }
        }
        if (!active) return;

        // mode preview: matikan pemotong lingkaran supaya foto 2 terlihat penuh
        maskComp.enabled = !previewPlacement;
        maskImg.enabled = !previewPlacement;

        float scale = canvas.scaleFactor;
        if (!customUI) root.sizeDelta = Vector2.one * (Screen.height * sizeFraction / scale);

        float diameterPx = root.rect.width * root.lossyScale.x;

        HandleDrag(diameterPx);
        UpdatePages(scale);
    }

    void HandleDrag(float diameterPx)
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 m = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 center = root.position;
            if (Vector2.Distance(m, center) <= diameterPx * 0.5f)
            {
                dragging = true;
                grabOffset = center - m;
            }
        }
        if (mouse.leftButton.wasReleasedThisFrame) dragging = false;

        if (dragging)
        {
            Vector2 p = m + grabOffset;
            p.x = Mathf.Clamp(p.x, 0f, Screen.width);
            p.y = Mathf.Clamp(p.y, 0f, Screen.height);
            root.position = new Vector3(p.x, p.y, 0f);
        }
    }

    // Foto 2 dipasang tepat di atas halaman (ukuran halaman di layar), tidak ikut bergerak bersama lensa.
    void UpdatePages(float scale)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        foreach (PageEntry e in pages)
        {
            if (e == null || e.page == null || e.image == null) continue;

            Rect r;
            if (!e.page.gameObject.activeInHierarchy || !TryGetScreenRect(e, cam, out r))
            {
                e.image.enabled = false;
                continue;
            }

            e.image.enabled = true;
            e.image.color = new Color(1f, 1f, 1f, previewPlacement ? 0.6f : 1f);

            RectTransform rt = e.image.rectTransform;
            rt.position = new Vector3(r.center.x, r.center.y, 0f);
            rt.sizeDelta = r.size / scale;
            e.image.uvRect = new Rect(e.flipX ? 1f : 0f, e.flipY ? 1f : 0f, e.flipX ? -1f : 1f, e.flipY ? -1f : 1f);

            if (debugLog && Time.frameCount % 180 == 0)
                Debug.Log($"[MonocleLens] {e.page.name} di layar: pos={r.position} ukuran={r.size}");
        }
    }

    // Kotak halaman di layar: 8 sudut bounding box mesh diproyeksikan ke layar.
    // Untuk Skinned Mesh, mesh di-bake dulu supaya bentuknya sesuai pose saat ini.
    static bool TryGetScreenRect(PageEntry e, Camera cam, out Rect rect)
    {
        rect = default;
        Renderer r = e.page;

        Bounds b;
        bool local = true;
        SkinnedMeshRenderer sk = r as SkinnedMeshRenderer;
        MeshFilter mf = r.GetComponent<MeshFilter>();

        if (sk != null && sk.sharedMesh != null)
        {
            if (e.baked == null) e.baked = new Mesh();
            sk.BakeMesh(e.baked, false);
            e.baked.RecalculateBounds();
            b = e.baked.bounds;
        }
        else if (mf != null && mf.sharedMesh != null)
        {
            b = mf.sharedMesh.bounds;
        }
        else
        {
            b = r.bounds;
            local = false;
        }

        Transform t = r.transform;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

        for (int i = 0; i < 8; i++)
        {
            Vector3 sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
            Vector3 corner = b.center + Vector3.Scale(b.extents, sign);
            Vector3 w = local ? t.TransformPoint(corner) : corner;
            Vector3 s = cam.WorldToScreenPoint(w);
            if (s.z <= 0f) return false;

            minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
            minY = Mathf.Min(minY, s.y); maxY = Mathf.Max(maxY, s.y);
        }

        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }
}