using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

// Pasang di objek Pandora (yang punya ZoomView).
// Mengatur zoom level 2: klik section -> kamera zoom ke section itu.
// Kalau semua section selesai: layar memudar ke hitam, objek 3D "Hertz Code" muncul pelan di layar hitam,
// lalu tombol Next di pojok kanan bawah memindahkan ke scene Home dengan transisi halus.
public class PandoraController : MonoBehaviour
{
    public ZoomView zoomView;               // kosongkan kalau ada di objek yang sama
    public PandoraPanel[] panels;           // isi: section 1, (section 2 nanti), kompas

    [Header("UI")]
    public TextMeshProUGUI hintText;        // opsional
    public string overviewHint = "Klik bagian untuk zoom  |  Geser untuk memutar  |  Esc keluar";
    public string focusHint = "Esc kembali";

    [Header("Klik")]
    public float clickMaxPixels = 8f;       // geser lebih dari ini dianggap memutar, bukan klik

    [Header("Objek Hertz Code (3D)")]
    [Tooltip("Prefab / model 'Hertz Code' dari Project. Ditampilkan sendirian di layar hitam.")]
    public GameObject hertzCodePrefab;
    [Tooltip("Otomatis memutar objek supaya sisi yang paling lebar (permukaan lempengan) menghadap layar.")]
    public bool autoFaceCamera = true;
    [Tooltip("Putaran tambahan (derajat) setelah otomatis. Kalau tidak kelihatan, coba Y = 180 untuk membalik sisi.")]
    public Vector3 showcaseRotation = Vector3.zero;
    public float showcaseScale = 1f;
    [Tooltip("Kecepatan putar saat digeser dengan mouse (derajat per piksel), seperti inspect buku.")]
    public float showcaseRotateSpeed = 0.4f;
    public string rotateHint = "Geser untuk memutar";
    [Tooltip("Lebih besar = objek tampak lebih kecil / lebih banyak ruang hitam di sekelilingnya.")]
    public float showcaseMargin = 1.5f;
    public float showcaseFov = 40f;
    public float showcaseLightIntensity = 0.6f;     // turunkan kalau kertas terlalu putih silau
    public float showcaseAmbient = 0.4f;

    [Header("Tulisan di kertas")]
    [TextArea] public string hertzCodeText = "92401";   // isi kodenya di sini (boleh beberapa baris)
    public TMP_FontAsset showcaseFont;              // opsional: font lain (misal tulisan tangan)
    public Color textColor = new Color(0.1f, 0.1f, 0.1f);
    [Range(0.1f, 1f)] public float textAreaScale = 0.6f;   // luas area tulisan terhadap ukuran kertas
    [Tooltip("Geser tulisan di kertas. 0 = tengah, nilai -0.5 sampai 0.5 dari ukuran kertas (X kanan, Y atas).")]
    public Vector2 textOffset = new Vector2(0.05f, 0.08f);

    [Header("Transisi akhir")]
    public string homeSceneName = "Home";   // harus ada di Build Settings
    public string nextButtonLabel = "Next";
    public float endingDelay = 1.5f;        // jeda setelah hijau, sebelum layar memudar
    public float fadeTime = 1.5f;           // memudar ke hitam
    public float revealTime = 2f;           // objek Hertz Code muncul pelan dari hitam
    public float buttonFadeIn = 1f;         // tombol Next muncul pelan
    public float homeFadeOut = 1.8f;        // setelah Next diklik: memudar ke hitam
    public float homeFadeIn = 1.5f;         // di scene Home: memudar dari hitam

    [Header("Tombol Next")]
    public int buttonFontSize = 40;
    public Vector2 buttonSize = new Vector2(320f, 90f);
    public Vector2 buttonMargin = new Vector2(50f, 40f);   // jarak dari pojok kanan bawah
    public Color buttonColor = new Color(0.15f, 0.15f, 0.15f, 1f);

    [Header("Event")]
    public UnityEvent onAllSolved;

    private PandoraPanel current;
    private bool pressing;
    private Vector2 pressPos;
    private bool unlocked;
    private bool finaleStarted;

    void Awake()
    {
        if (zoomView == null) zoomView = GetComponent<ZoomView>();

        foreach (PandoraPanel p in panels)
            if (p != null) p.Init(this);

        zoomView.onEnter.AddListener(HandleEnter);
        zoomView.onExit.AddListener(HandleExit);
        zoomView.Unfocused += HandleUnfocused;

        SetHint(null);
    }

    void OnDestroy()
    {
        if (zoomView != null) zoomView.Unfocused -= HandleUnfocused;
    }

    void HandleEnter()
    {
        SetSelectors(true);
        SetHint(overviewHint);
    }

    void HandleExit()
    {
        SetSelectors(false);
        SetHint(null);
    }

    void HandleUnfocused()
    {
        if (current != null) current.OnUnfocus();
        current = null;
        if (finaleStarted) return;
        SetSelectors(true);
        SetHint(overviewHint);
    }

    void Update()
    {
        if (finaleStarted || Mouse.current == null || zoomView == null) return;

        if (!zoomView.IsZoomed || zoomView.IsFocused)
        {
            pressing = false;
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            pressing = true;
            pressPos = Mouse.current.position.ReadValue();
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame && pressing)
        {
            pressing = false;
            float moved = Vector2.Distance(pressPos, Mouse.current.position.ReadValue());
            if (moved <= clickMaxPixels) TryPick();
        }
    }

    void TryPick()
    {
        Camera cam = Camera.main;
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

        PandoraPanel best = null;
        float bestDist = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            foreach (PandoraPanel p in panels)
            {
                if (p == null || p.selector != h.collider) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = p;
                }
            }
        }

        if (best != null) Focus(best);
    }

    void Focus(PandoraPanel p)
    {
        if (p.focusPoint == null)
        {
            Debug.LogWarning("[Pandora] Focus Point belum diisi di " + p.name);
            return;
        }

        current = p;
        SetSelectors(false);
        zoomView.FocusOn(p.focusPoint, p.focusFov);
        p.OnFocus();
        SetHint(focusHint);
    }

    // Kembali ke zoom utama dari dalam section (dipanggil panel, misal saat kode benar)
    public void LeaveFocus()
    {
        if (zoomView != null) zoomView.Unfocus();
    }

    void SetSelectors(bool value)
    {
        foreach (PandoraPanel p in panels)
            if (p != null && p.selector != null)
                p.selector.enabled = value && !p.solved;    // section yang sudah solved tidak bisa di-zoom lagi
    }

    void SetHint(string text)
    {
        if (hintText == null) return;
        hintText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        if (!string.IsNullOrEmpty(text)) hintText.text = text;
    }

    // Dipakai kompas: benar/salahnya baru boleh diketahui kalau section lain sudah beres
    public bool OthersSolved(PandoraPanel except)
    {
        foreach (PandoraPanel p in panels)
            if (p != null && p != except && !p.solved) return false;
        return true;
    }

    public void NotifySolved(PandoraPanel p)
    {
        if (unlocked) return;
        foreach (PandoraPanel x in panels)
            if (x != null && !x.solved) return;

        unlocked = true;
        Debug.Log("[Pandora] Semua section selesai");
        onAllSolved.Invoke();
        StartCoroutine(Finale());
    }

    // ================= Akhir: layar hitam, Hertz Code, tombol Next =================

    IEnumerator Finale()
    {
        finaleStarted = true;
        if (zoomView != null) zoomView.allowExit = false;   // pemain tidak bisa keluar dari zoom
        SetHint(null);

        yield return new WaitForSeconds(endingDelay);

        // 1. Layar memudar ke hitam
        CanvasGroup black = CreateBlackOverlay();
        yield return Fade(black, 0f, 1f, fadeTime);

        // 2. Di belakang layar hitam: sembunyikan UI game, siapkan panggung objek Hertz Code
        HideOtherCanvases(black.GetComponent<Canvas>());

        Transform holder = null;
        Vector3 baseScale = Vector3.one;
        if (hertzCodePrefab != null)
        {
            holder = BuildShowcase();
            baseScale = holder.localScale;
        }
        else
        {
            Debug.LogWarning("[Pandora] Hertz Code Prefab belum diisi di PandoraController.");
        }

        // 3. Layar hitam menghilang pelan, objek Hertz Code muncul sambil membesar sedikit
        float t = 0f;
        while (t < revealTime)
        {
            t += Time.deltaTime;
            float e = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(revealTime, 0.01f));
            black.alpha = 1f - e;
            if (holder != null) holder.localScale = Vector3.Lerp(baseScale * 0.85f, baseScale, e);
            yield return null;
        }
        black.alpha = 0f;
        if (holder != null) holder.localScale = baseScale;
        Destroy(black.gameObject);

        if (holder != null) StartCoroutine(DragRotate(holder));

        // 4. Tombol Next muncul pelan di pojok kanan bawah
        EnsureEventSystem();
        CanvasGroup buttonGroup = CreateNextButton();
        yield return Fade(buttonGroup, 0f, 1f, buttonFadeIn);
    }

    IEnumerator Fade(CanvasGroup g, float from, float to, float time)
    {
        float t = 0f;
        while (t < time)
        {
            t += Time.deltaTime;
            g.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(time, 0.01f)));
            yield return null;
        }
        g.alpha = to;
    }

    // Putar objek dengan menggeser mouse (360 derajat, ada sedikit momentum), seperti inspect buku
    IEnumerator DragRotate(Transform holder)
    {
        Vector2 vel = Vector2.zero;
        bool dragging = false;

        while (holder != null)
        {
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    // Klik di tombol Next tidak memutar objek
                    bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                    dragging = !overUI;
                }
                if (Mouse.current.leftButton.wasReleasedThisFrame) dragging = false;

                float dt = Time.deltaTime;
                Vector2 input = dragging ? Mouse.current.delta.ReadValue() * showcaseRotateSpeed : Vector2.zero;

                Vector2 applied;
                if (input.sqrMagnitude > 0f)
                {
                    applied = input;
                    vel = input / Mathf.Max(dt, 0.0001f) * 0.35f;
                }
                else
                {
                    vel = Vector2.Lerp(vel, Vector2.zero, dt * 5f);
                    applied = vel * dt;
                }

                // Kamera panggung menghadap +Z tanpa putaran: atas = Y dunia, kanan = X dunia
                if (applied.sqrMagnitude > 0.00001f)
                {
                    holder.Rotate(Vector3.up, -applied.x, Space.World);
                    holder.Rotate(Vector3.right, applied.y, Space.World);
                }
            }
            yield return null;
        }
    }

    // Menyembunyikan UI game (hint, tulisan derajat, dst.) supaya yang terlihat hanya objek Hertz Code
    void HideOtherCanvases(Canvas keep)
    {
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == keep || !c.isRootCanvas) continue;
            if (c.renderMode == RenderMode.ScreenSpaceOverlay) c.enabled = false;
        }
    }

    // Panggung objek: kamera hitam sendiri + lampu, jauh di bawah dunia game supaya tidak ada yang ikut terlihat.
    // Mengembalikan wadah (holder) yang dipakai untuk animasi membesar dan berputar.
    Transform BuildShowcase()
    {
        Vector3 basePos = new Vector3(0f, -10000f, 0f);

        GameObject holder = new GameObject("HertzCodeShowcase");
        holder.transform.position = basePos;

        GameObject inst = Instantiate(hertzCodePrefab, holder.transform);
        inst.SetActive(true);
        inst.transform.localPosition = Vector3.zero;
        Quaternion prefabRot = hertzCodePrefab.transform.localRotation;
        inst.transform.localRotation = prefabRot;
        inst.transform.localScale = hertzCodePrefab.transform.localScale * showcaseScale;

        // Hadapkan sisi terlebar (permukaan lempengan) ke kamera. Kamera memandang ke arah +Z dari sisi -Z.
        if (autoFaceCamera)
        {
            Vector3 s = GetBounds(inst).size;
            Quaternion auto = Quaternion.identity;
            if (s.y <= s.x && s.y <= s.z) auto = Quaternion.Euler(-90f, 0f, 0f);       // berbaring datar -> berdiri
            else if (s.x <= s.y && s.x <= s.z) auto = Quaternion.Euler(0f, 90f, 0f);   // menghadap samping -> hadap depan
            inst.transform.localRotation = auto * prefabRot;
        }
        inst.transform.localRotation = Quaternion.Euler(showcaseRotation) * inst.transform.localRotation;

        foreach (Rigidbody rb in inst.GetComponentsInChildren<Rigidbody>())
            rb.isKinematic = true;

        MakeDoubleSided(inst);                      // kertas terlihat dari kedua sisi saat diputar

        // Pusatkan objek di titik tengah wadah
        Bounds b = GetBounds(inst);
        inst.transform.position += basePos - b.center;
        b = GetBounds(inst);
        float radius = Mathf.Max(b.extents.magnitude, 0.01f);

        CreatePaperText(holder.transform, b);

        // Kamera: latar hitam, di atas semua kamera lain
        GameObject camGo = new GameObject("HertzCodeCamera");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.fieldOfView = showcaseFov;
        cam.depth = 100f;

        float dist = radius / Mathf.Sin(showcaseFov * 0.5f * Mathf.Deg2Rad) * showcaseMargin;
        cam.transform.position = basePos + new Vector3(0f, 0f, -dist);
        cam.transform.rotation = Quaternion.identity;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = dist + radius * 4f;

        // Cahaya sekitar rata supaya objek tidak hitam di latar hitam
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(showcaseAmbient, showcaseAmbient, showcaseAmbient);

        Debug.Log("[Pandora] Showcase: ukuran objek " + b.size + ", jarak kamera " + dist.ToString("0.00"));

        // Lampu searah pandangan kamera (sedikit miring supaya ada bayangan bentuk)
        GameObject lightGo = new GameObject("HertzCodeLight");
        lightGo.transform.SetParent(camGo.transform, false);
        lightGo.transform.localRotation = Quaternion.Euler(20f, -20f, 0f);
        Light l = lightGo.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = showcaseLightIntensity;
        l.shadows = LightShadows.None;

        return holder.transform;
    }

    // Tulisan kode di permukaan kertas (ikut berputar bersama kertas, hanya terlihat dari sisi depan)
    void CreatePaperText(Transform holder, Bounds b)
    {
        if (string.IsNullOrEmpty(hertzCodeText)) return;

        GameObject go = new GameObject("HertzCodeText");
        go.transform.SetParent(holder, false);
        float depth = Mathf.Max(0.002f, b.size.z * 0.5f + 0.002f);       // sedikit di depan permukaan kertas
        go.transform.localPosition = new Vector3(b.size.x * textOffset.x, b.size.y * textOffset.y, -depth);
        go.transform.localRotation = Quaternion.identity;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        if (showcaseFont != null) tmp.font = showcaseFont;
        tmp.text = hertzCodeText;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;                    // ukuran huruf menyesuaikan area di kertas
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 20f;
        tmp.rectTransform.sizeDelta = new Vector2(b.size.x * textAreaScale, b.size.y * textAreaScale);

        // Tulisan hanya digambar dari sisi depan (dari belakang kertas kosong)
        Material m = tmp.fontMaterial;
        if (m != null && m.HasProperty("_CullMode")) m.SetFloat("_CullMode", 2f);
    }

    // Membuat kertas terlihat dari dua sisi
    void MakeDoubleSided(GameObject root)
    {
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
        {
            Renderer r = mf.GetComponent<Renderer>();
            if (r == null || mf.sharedMesh == null) continue;

            bool allSet = true;
            foreach (Material m in r.materials)          // salinan material, aset asli tidak berubah
            {
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                else if (m.HasProperty("_CullMode")) m.SetFloat("_CullMode", 0f);
                else allSet = false;
            }

            if (!allSet) AddBackFaces(mf, r);
        }
    }

    // Cadangan kalau shader tidak punya pengaturan cull: tambahkan salinan mesh dengan sisi terbalik
    void AddBackFaces(MeshFilter mf, Renderer r)
    {
        Mesh src = mf.sharedMesh;
        if (!src.isReadable)
        {
            Debug.LogWarning("[Pandora] Mesh '" + src.name + "' tidak bisa dibaca (Read/Write mati), sisi belakang kertas tidak ditambahkan.");
            return;
        }

        Mesh flipped = Instantiate(src);
        for (int sub = 0; sub < flipped.subMeshCount; sub++)
        {
            int[] tris = flipped.GetTriangles(sub);
            for (int i = 0; i < tris.Length; i += 3)
            {
                int tmp = tris[i];
                tris[i] = tris[i + 2];
                tris[i + 2] = tmp;
            }
            flipped.SetTriangles(tris, sub);
        }
        Vector3[] normals = flipped.normals;
        for (int i = 0; i < normals.Length; i++) normals[i] = -normals[i];
        flipped.normals = normals;

        GameObject back = new GameObject("BackFaces");
        back.transform.SetParent(mf.transform, false);
        back.AddComponent<MeshFilter>().sharedMesh = flipped;
        back.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
    }

    Bounds GetBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    CanvasGroup CreateBlackOverlay()
    {
        GameObject root = new GameObject("FinaleBlack");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        root.AddComponent<GraphicRaycaster>();

        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = true;

        GameObject black = new GameObject("Black", typeof(RectTransform));
        black.transform.SetParent(root.transform, false);
        Image img = black.AddComponent<Image>();
        img.color = Color.black;
        RectTransform rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        return group;
    }

    CanvasGroup CreateNextButton()
    {
        GameObject canvasGo = new GameObject("FinaleButtonCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1001;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        CanvasGroup group = canvasGo.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        GameObject btnGo = new GameObject("NextButton", typeof(RectTransform));
        btnGo.transform.SetParent(canvasGo.transform, false);
        Image btnImg = btnGo.AddComponent<Image>();
        btnImg.color = Color.white;                 // warna sebenarnya diatur lewat Button.colors

        RectTransform brt = btnImg.rectTransform;
        brt.anchorMin = new Vector2(1f, 0f);
        brt.anchorMax = new Vector2(1f, 0f);
        brt.pivot = new Vector2(1f, 0f);
        brt.sizeDelta = buttonSize;
        brt.anchoredPosition = new Vector2(-buttonMargin.x, buttonMargin.y);

        Button btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.transition = Selectable.Transition.ColorTint;

        ColorBlock cb = btn.colors;
        cb.normalColor = buttonColor;
        cb.highlightedColor = Color.Lerp(buttonColor, Color.white, 0.25f);
        cb.pressedColor = Color.Lerp(buttonColor, Color.black, 0.3f);
        cb.selectedColor = buttonColor;
        cb.disabledColor = buttonColor;             // setelah diklik tidak berubah abu-abu
        cb.fadeDuration = 0.15f;
        btn.colors = cb;

        btn.onClick.AddListener(() =>
        {
            btn.interactable = false;               // cegah klik dua kali
            SceneFader.LoadWithFade(homeSceneName, homeFadeOut, homeFadeIn);
        });

        GameObject labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(btnGo.transform, false);
        RectTransform lrt = (RectTransform)labelGo.transform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = nextButtonLabel;
        label.fontSize = buttonFontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;

        // Petunjuk kecil di tengah bawah
        if (!string.IsNullOrEmpty(rotateHint))
        {
            GameObject hintGo = new GameObject("RotateHint", typeof(RectTransform));
            hintGo.transform.SetParent(canvasGo.transform, false);
            RectTransform hrt = (RectTransform)hintGo.transform;
            hrt.anchorMin = new Vector2(0.5f, 0f);
            hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(800f, 60f);
            hrt.anchoredPosition = new Vector2(0f, 40f);

            TextMeshProUGUI hint = hintGo.AddComponent<TextMeshProUGUI>();
            hint.text = rotateHint;
            hint.fontSize = 30;
            hint.alignment = TextAlignmentOptions.Center;
            hint.color = new Color(1f, 1f, 1f, 0.6f);
            hint.raycastTarget = false;
        }

        return group;
    }

    void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();
    }
}