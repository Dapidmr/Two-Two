using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Monocle 1: lensa yang bisa di-drag. Di dalam lubang lensa, halaman buku berubah dari
// foto 1 (texture halaman asli) menjadi foto 2 (texture "readable").
// Isi Lens UI dengan objek MonocleUI. Atur Lens Center + Lens Fill sampai lubang lensa
// pas dengan bagian dalam cincin pada gambar UI-mu (bisa diubah langsung saat Play).
public class MonocleLens : MonoBehaviour
{
    [System.Serializable]
    public class PageEntry
    {
        public Renderer page;        // objek halaman (Page.001)
        public Texture readable;     // foto 2: versi yang terbaca
        public bool flipX;           // centang kalau foto 2 tampak terbalik kiri-kanan
        public bool flipY;           // centang kalau foto 2 tampak terbalik atas-bawah
        [HideInInspector] public RawImage image;
        [System.NonSerialized] public Mesh baked;
        [System.NonSerialized] public bool hasPose;
        [System.NonSerialized] public Vector3 lastPos, refPos;
        [System.NonSerialized] public Quaternion lastRot, refRot;
    }

    enum BookState { Opening, Settled, Closing }

    [Header("Halaman yang bisa dibaca dengan lensa")]
    public PageEntry[] pages;
    [Range(0.3f, 1.2f)] public float readableBrightness = 0.85f;   // samakan terang foto 2 dengan halaman

    [Header("UI lensa")]
    public RectTransform lensUI;                                   // drag objek MonocleUI ke sini
    [Tooltip("Pusat lubang lensa di dalam gambar UI. (0,0) = kiri-bawah, (1,1) = kanan-atas")]
    public Vector2 lensCenter = new Vector2(0.5f, 0.5f);
    [Tooltip("Diameter lubang lensa relatif ke lebar gambar UI")]
    [Range(0.05f, 1.2f)] public float lensFill = 0.9f;
    public bool ringOnTop = true;                                  // gambar UI digambar di atas foto 2
    public bool showMaskGuide = false;                             // lingkaran merah = lubang lensa (untuk menyetel)
    public bool bringToFront = true;
    public Vector2 startPos = new Vector2(0.88f, 0.2f);            // posisi awal pusat lensa (0..1 dari layar)

    [Header("Saat buku menutup")]
    [Tooltip("Lensa kembali ke posisi awal dan foto 2 disembunyikan begitu halaman buku bergerak (menutup)")]
    public bool retreatWhenClosing = true;
    public float retreatSpeed = 2500f;         // kecepatan kembali (piksel per detik)

    [Header("Hanya dipakai kalau Lens UI kosong (cincin otomatis)")]
    [Range(0.1f, 0.6f)] public float sizeFraction = 0.28f;
    [Range(0.01f, 0.2f)] public float ringWidth = 0.06f;
    public Color ringColor = new Color(1f, 0.85f, 0.2f);

    [Header("Uji coba")]
    public bool testWithoutMonocle = false;   // lensa muncul tanpa harus mengambil monocle 1
    public bool previewPlacement = false;     // tampilkan foto 2 penuh + semi-transparan untuk cek posisi

    [Header("Lain-lain")]
    public ZoomView zoomView;
    public Canvas canvas;
    public bool debugLog = true;
    public bool showDebugOverlay = true;   // tulisan status kuning di pojok kiri atas layar (matikan kalau sudah beres)

    private RectTransform root;
    private RectTransform maskRt;
    private bool customUI;
    private Mask maskComp;
    private Image maskImg;
    private bool shown;
    private bool dragging;
    private bool retreating;
    private Vector2 grabOffset;

    private BookState state = BookState.Opening;
    private float stillTime;
    private float activeTime;
    private float lastDeviation;

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

        // lapisan pemotong berbentuk lingkaran (posisi dan ukurannya diperbarui tiap frame)
        maskRt = NewRect("LensMask", root);
        UpdateMaskRect();

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

        if (customUI)
        {
            // gambar UI (cincin + rantai) digambar lagi di paling atas, supaya tidak tertutup foto 2
            Image rootImg = root.GetComponent<Image>();
            if (ringOnTop && rootImg != null && rootImg.sprite != null)
            {
                RectTransform topRt = NewRect("RingTop", root);
                Stretch(topRt);
                Image top = topRt.gameObject.AddComponent<Image>();
                top.sprite = rootImg.sprite;
                top.color = rootImg.color;
                top.material = rootImg.material;
                top.type = rootImg.type;
                top.preserveAspect = rootImg.preserveAspect;
                top.raycastTarget = false;
                rootImg.enabled = false;
            }
        }
        else
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

    // lensFill = diameter relatif ke LEBAR gambar; tinggi dikoreksi supaya lubang tetap bulat
    void UpdateMaskRect()
    {
        if (maskRt == null) return;
        float aspect = root.rect.height > 0.001f ? root.rect.width / root.rect.height : 1f;
        Vector2 half = new Vector2(lensFill * 0.5f, lensFill * 0.5f * aspect);
        maskRt.anchorMin = lensCenter - half;
        maskRt.anchorMax = lensCenter + half;
        maskRt.offsetMin = Vector2.zero;
        maskRt.offsetMax = Vector2.zero;
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

    // Sprite lingkaran (atau cincin) dibuat lewat kode, tidak butuh file gambar.
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

    // jarak (piksel layar) dari pivot gambar UI ke pusat lubang lensa
    Vector2 LensOffsetPx()
    {
        Vector2 local = Vector2.Scale(lensCenter - root.pivot, root.rect.size);
        return local * root.lossyScale.x;
    }

    Vector2 StartRootPos()
    {
        Vector2 target = new Vector2(Screen.width * startPos.x, Screen.height * startPos.y);
        return target - LensOffsetPx();
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
            retreating = false;
            root.gameObject.SetActive(active);
            if (active)
            {
                state = BookState.Opening;
                stillTime = 0f;
                activeTime = 0f;
                foreach (PageEntry e in pages) if (e != null) e.hasPose = false;

                Vector2 p = StartRootPos();
                root.position = new Vector3(p.x, p.y, 0f);
                if (bringToFront) root.SetAsLastSibling();
                if (debugLog) Debug.Log("[MonocleLens] lensa muncul");
            }
        }
        if (!active) return;

        UpdateMaskRect();

        // mode preview: foto 2 penuh. Mode guide: lubang lensa diberi warna merah.
        maskComp.enabled = !previewPlacement;
        maskImg.enabled = !previewPlacement;
        maskComp.showMaskGraphic = showMaskGuide;
        maskImg.color = showMaskGuide ? new Color(1f, 0f, 0f, 0.4f) : Color.white;

        float scale = canvas.scaleFactor;
        if (!customUI) root.sizeDelta = Vector2.one * (Screen.height * sizeFraction / scale);

        float diameterPx = root.rect.width * lensFill * root.lossyScale.x;

        UpdateBookState();
        bool interactive = !retreatWhenClosing || state == BookState.Settled;

        UpdatePages(scale, interactive);

        if (interactive)
        {
            retreating = false;
            HandleDrag(diameterPx);
        }
        else
        {
            retreating = true;
            Retreat();
        }
    }

    // Deteksi buku menutup lewat GERAK halaman (bukan arah halaman):
    //  Opening : menunggu halaman diam 0,4 detik (paling lama 1,5 detik)
    //  Settled : buku terbuka dan diam, lensa boleh dipakai
    //  Closing : halaman bergeser jauh dari pose terbukanya, lensa kembali ke posisi awal
    void UpdateBookState()
    {
        float dt = Time.unscaledDeltaTime;
        activeTime += dt;

        float step = 0f;        // gerak sejak frame lalu
        float deviation = 0f;   // jarak dari pose saat buku terbuka (derajat + sentimeter)
        foreach (PageEntry e in pages)
        {
            if (e == null || e.page == null) continue;
            Transform t = e.page.transform;
            if (!e.hasPose)
            {
                e.lastPos = e.refPos = t.position;
                e.lastRot = e.refRot = t.rotation;
                e.hasPose = true;
            }
            step = Mathf.Max(step, Quaternion.Angle(e.lastRot, t.rotation) + Vector3.Distance(e.lastPos, t.position) * 100f);
            deviation = Mathf.Max(deviation, Quaternion.Angle(e.refRot, t.rotation) + Vector3.Distance(e.refPos, t.position) * 100f);
            e.lastPos = t.position;
            e.lastRot = t.rotation;
        }
        lastDeviation = deviation;

        switch (state)
        {
            case BookState.Opening:
                if (step > 0.05f) stillTime = 0f; else stillTime += dt;
                if (stillTime >= 0.4f || activeTime >= 1.5f) EnterSettled();
                break;

            case BookState.Settled:
                if (deviation > 1.5f)
                {
                    state = BookState.Closing;
                    stillTime = 0f;
                    if (debugLog) Debug.Log("[MonocleLens] halaman bergerak (buku menutup), lensa kembali ke posisi awal");
                }
                break;

            case BookState.Closing:
                if (deviation <= 1.5f && step <= 0.05f) stillTime += dt; else stillTime = 0f;
                if (stillTime >= 0.4f) EnterSettled();
                break;
        }
    }

    void EnterSettled()
    {
        foreach (PageEntry e in pages)
        {
            if (e == null || e.page == null) continue;
            e.refPos = e.page.transform.position;
            e.refRot = e.page.transform.rotation;
        }
        state = BookState.Settled;
        stillTime = 0f;
        if (debugLog) Debug.Log("[MonocleLens] buku terbuka dan diam, lensa siap dipakai");
    }

    void OnGUI()
    {
        if (!showDebugOverlay || root == null) return;

        GUIStyle st = new GUIStyle(GUI.skin.label);
        st.fontSize = 18;
        st.normal.textColor = Color.yellow;

        string mouseTxt = Mouse.current != null ? Mouse.current.position.ReadValue().ToString() : "tidak ada mouse";
        Vector2 center = (Vector2)root.position + LensOffsetPx();
        string t = $"MonocleLens | aktif={shown} | buku={state} | deviasi={lastDeviation:F2} | mundur={retreating} | tarik={dragging}\n" +
                   $"mouse={mouseTxt} | pusat lensa={center}";
        GUI.Label(new Rect(10f, 10f, 1400f, 70f), t, st);
    }

    // lensa meluncur kembali ke posisi awal (pojok layar), jauh dari buku
    void Retreat()
    {
        dragging = false;
        Vector2 p = Vector2.MoveTowards((Vector2)root.position, StartRootPos(), retreatSpeed * Time.unscaledDeltaTime);
        root.position = new Vector3(p.x, p.y, 0f);
    }

    void HandleDrag(float diameterPx)
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 m = mouse.position.ReadValue();

        Vector2 off = LensOffsetPx();
        Vector2 center = (Vector2)root.position + off;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            // lensa bisa ditarik dari lubangnya ATAU dari mana saja pada gambar monocle (cincin, rantai)
            float dist = Vector2.Distance(m, center);
            bool inHole = dist <= diameterPx * 0.5f;
            bool onImage = RectTransformUtility.RectangleContainsScreenPoint(root, m, null);
            if (debugLog)
                Debug.Log($"[MonocleLens] klik di {m} | pusat lensa {center} | jarak={dist:F0}px | radius lubang={diameterPx * 0.5f:F0}px | di gambar UI={onImage}");
            if (inHole || onImage)
            {
                dragging = true;
                grabOffset = (Vector2)root.position - m;
            }
        }
        if (mouse.leftButton.wasReleasedThisFrame) dragging = false;

        if (dragging)
        {
            Vector2 c = m + grabOffset + off;
            c.x = Mathf.Clamp(c.x, 0f, Screen.width);
            c.y = Mathf.Clamp(c.y, 0f, Screen.height);
            Vector2 p = c - off;
            root.position = new Vector3(p.x, p.y, 0f);
        }
    }

    // Foto 2 dipasang tepat di atas halaman (ukuran halaman di layar), tidak ikut bergerak bersama lensa.
    // Saat buku belum diam atau sedang menutup (showPhotos = false), foto 2 disembunyikan.
    void UpdatePages(float scale, bool showPhotos)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float b = readableBrightness;

        foreach (PageEntry e in pages)
        {
            if (e == null || e.page == null || e.image == null) continue;

            Rect r;
            if (!showPhotos || !e.page.gameObject.activeInHierarchy || !TryGetScreenRect(e, cam, out r))
            {
                e.image.enabled = false;
                continue;
            }

            e.image.enabled = true;
            e.image.color = new Color(b, b, b, previewPlacement ? 0.6f : 1f);

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