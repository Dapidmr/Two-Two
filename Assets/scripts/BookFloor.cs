using UnityEngine;
using UnityEngine.InputSystem;

// Buku yang tergeletak di lantai (tanpa parent, tanpa rak atau laci).
// Arahkan crosshair ke buku -> "E to inspect" -> langsung di-inspect.
// Saat inspect: player berhenti, kursor bebas, layar meredup (lewat Bookshelf).
// Esc = buku kembali ke lantai dan player bisa bergerak lagi.
[RequireComponent(typeof(Collider))]
public class BookFloor : MonoBehaviour
{
    [Header("Referensi")]
    public Bookshelf bookshelf;          // dipakai untuk layar redup dan teks hint. Kosong = dicari otomatis

    [Header("Interaksi")]
    public float interactDistance = 2.5f;     // jarak maksimal crosshair bisa mengenai buku
    public string promptText = "E to inspect";

    [Header("Inspect")]
    public float inspectDistance = 0.5f;   // jarak pusat buku dari kamera
    public float inspectMoveSpeed = 8f;
    public float rotateSpeed = 0.4f;       // derajat per pixel drag
    public float inspectRadius = 0.2f;     // ukuran buku saat inspect (radius ujung terjauh, world unit)

    [Header("Zoom Inspect")]
    public bool canZoom = false;           // centang agar scroll bisa zoom saat buku terbuka
    public float zoomStep = 0.05f;
    public float minInspectDistance = 0.25f;
    public float maxInspectDistance = 0.9f;
    public float zoomSmooth = 10f;

    [Header("Hadap kamera")]
    public Vector3 faceLocalAxis = Vector3.up;     // sumbu lokal buku yang menghadap kamera (rebah: 0,1,0)
    public Vector3 faceLocalUp = Vector3.right;    // sumbu lokal buku (arah spine) yang diarahkan ke atas layar
    public bool faceCameraOnInspect = true;        // buku di lantai biasanya rebah, jadi default nyala

    [Header("Buka Buku (E to read)")]
    public bool canOpen = false;
    public AnimationClip[] openClips;        // clip animasi buka buku
    public float openDuration = 1.2f;
    public float openUp = 0.08f;             // geser ke atas, menyisakan ruang untuk teks bawah
    public float openScaleMul = 1.3f;        // diperbesar saat terbuka
    public float openRoll = 0f;              // putar searah jarum jam bila halaman miring (derajat)
    public bool notBook = false;             // true = setelah ditutup kembali ke rotasi saat "E to open"
    public float panSensitivity = 1f;
    public Vector2 panLimitAbs = new Vector2(0.3f, 0.2f);
    public float openBlendSpeed = 3f;
    public float closeSpeedMul = 1.5f;
    [Range(0.1f, 0.9f)] public float openPeak = 0.48f;
    public float closeSkip = 0.07f;

    [Header("Monolog saat buku ditutup")]
    public bool monologOnClose = true;           // centang = aktif, hapus centang = mati
    [TextArea(2, 4)] public string[] closeLines; // satu elemen = satu baris monolog
    public bool closeMonologOnce = false;        // true = hanya saat pertama kali ditutup
    private bool closeMonologPlayed;

    public static bool AnyInspecting => activeBook != null;

    // ===== state =====
    private static BookFloor activeBook;

    private Vector3 originalPos;
    private Quaternion originalRot;
    private Vector3 originalScale;

    private bool hovered = false;
    private bool inspecting = false;
    private bool returning = false;

    private Vector3 localCenter;
    private float bookRadius = 0f;
    private Renderer[] allRends;
    private Vector3 targetScale;
    private Vector3 viewCenter;

    private float openT = 0f;
    private float openTarget = 0f;
    private bool readShown = false;
    private bool openedHintShown = false;
    private Quaternion openRot;
    private float phase = 0f;
    private int bookState = 0;      // 0 tertutup, 1 membuka, 2 terbuka, 3 menutup
    private Quaternion inspectTargetRot;
    private bool settlingRot = false;
    private Quaternion preOpenRot;
    private Vector2 panOffset;
    private float targetInspectDistance;
    private float currentInspectDistance;

    private MonoBehaviour playerController;
    private Rigidbody playerRb;
    private Rigidbody bookRb;
    private bool bookRbWasKinematic;

    private readonly System.Collections.Generic.List<Material> queueMats = new System.Collections.Generic.List<Material>();
    private readonly System.Collections.Generic.List<int> queueOrig = new System.Collections.Generic.List<int>();

    private GUIStyle style, shadow;

    void Start()
    {
        originalScale = transform.localScale;
        originalPos = transform.position;
        originalRot = transform.rotation;
        activeBook = null;

        if (bookshelf == null) bookshelf = FindFirstObjectByType<Bookshelf>();
        if (bookshelf == null) Debug.LogWarning("BookFloor " + name + ": Bookshelf tidak ditemukan, layar redup dan teks hint tidak akan tampil");

        bookRb = GetComponent<Rigidbody>();

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            playerController = p.GetComponent<FirstPersonController>();
            playerRb = p.GetComponent<Rigidbody>();
        }

        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            localCenter = transform.InverseTransformPoint(b.center);
            bookRadius = b.extents.magnitude;
            allRends = rends;
        }
    }

    void Update()
    {
        if (Camera.main == null || Mouse.current == null || Keyboard.current == null) return;

        if (inspecting) { UpdateInspect(); return; }
        if (returning) { UpdateReturn(); return; }

        // Hover: crosshair di tengah layar, hanya saat mode first person (kursor terkunci)
        hovered = false;
        bool free = activeBook == null
                    && Cursor.lockState == CursorLockMode.Locked
                    && (bookshelf == null || !bookshelf.IsInspecting);

        if (free)
        {
            Ray ray = Camera.main.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            var hits = Physics.RaycastAll(ray, interactDistance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (IsPlayer(hit.transform)) continue;
                hovered = hit.collider.GetComponentInParent<BookFloor>() == this && IsVisuallyHit(hit);
                break;   // benda pertama selain player
            }
        }

        // E dibaca di sini lalu langsung return di frame berikutnya, jadi E yang sama tidak ikut membuka buku
        if (hovered && Keyboard.current.eKey.wasPressedThisFrame)
            StartInspect();
    }

    static bool IsPlayer(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.CompareTag("Player")) return true;
        return false;
    }
    // Terlihat secara visual: renderer aktif, masuk frustum kamera, dan titik kena berada di badan mesh
    bool IsVisuallyHit(RaycastHit hit)
    {
        if (allRends == null) return true;

        var planes = GeometryUtility.CalculateFrustumPlanes(Camera.main);
        foreach (var r in allRends)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;

            Bounds b = r.bounds;
            b.Expand(0.02f);
            if (b.Contains(hit.point)) return true;
        }
        return false;
    }

    void OnGUI()
    {
        if (!hovered || inspecting || returning) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
            shadow = new GUIStyle(style);
            shadow.normal.textColor = Color.black;
        }

        var rect = new Rect(Screen.width * 0.5f - 100, Screen.height * 0.5f + 25, 200, 40);
        GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), promptText, shadow);
        GUI.Label(rect, promptText, style);
    }

    // Buku digambar SETELAH layar redup (queue 2450, tetap opaque), jadi tidak ikut gelap
    void SetAboveDim(bool above)
    {
        if (above)
        {
            if (queueMats.Count > 0 || allRends == null) return;
            foreach (var r in allRends)
            {
                if (r == null) continue;
                foreach (var m in r.materials)
                {
                    queueMats.Add(m);
                    queueOrig.Add(m.renderQueue);
                    if (m.renderQueue < 2450) m.renderQueue = 2450;
                }
            }
        }
        else
        {
            for (int i = 0; i < queueMats.Count; i++)
                if (queueMats[i] != null) queueMats[i].renderQueue = queueOrig[i];
            queueMats.Clear();
            queueOrig.Clear();
        }
    }

    // Pusat bounds dalam ruang lokal buku, tidak terpengaruh rotasi buku saat ini
    Vector3 CurrentLocalCenter()
    {
        if (allRends == null) return localCenter;
        Matrix4x4 w2l = transform.worldToLocalMatrix;
        bool first = true;
        Bounds lb = new Bounds();
        foreach (var r in allRends)
        {
            if (r == null) continue;
            Bounds b = r.localBounds;
            Matrix4x4 m = w2l * r.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));
                Vector3 p = m.MultiplyPoint3x4(c);
                if (first) { lb = new Bounds(p, Vector3.zero); first = false; }
                else lb.Encapsulate(p);
            }
        }
        return first ? localCenter : lb.center;
    }

    Quaternion FaceRot(Transform cam)
    {
        Quaternion local = Quaternion.LookRotation(faceLocalAxis.normalized, faceLocalUp.normalized);
        Quaternion view = Quaternion.LookRotation(-cam.forward, cam.up);
        return view * Quaternion.Inverse(local);
    }

    void ApplyOpenPose()
    {
        if (openClips == null) return;
        foreach (var c in openClips)
            if (c != null) c.SampleAnimation(gameObject, phase * c.length);
    }

    void FreezePlayer()
    {
        if (playerController != null) playerController.enabled = false;   // OnDisable melepas kursor
        if (playerRb != null)
        {
            playerRb.linearVelocity = Vector3.zero;
            playerRb.isKinematic = true;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ResumePlayer()
    {
        if (playerRb != null) playerRb.isKinematic = false;
        if (playerController != null) playerController.enabled = true;    // OnEnable mengunci kursor lagi
    }

    void PlayCloseMonolog()
    {
        if (!monologOnClose || closeLines == null || closeLines.Length == 0) return;
        if (closeMonologOnce && closeMonologPlayed) return;
        if (Monolog.Instance == null) return;

        closeMonologPlayed = true;
        Monolog.Instance.Play(closeLines);
    }

    void StartInspect()
    {
        activeBook = this;
        inspecting = true;
        hovered = false;

        FreezePlayer();

        if (bookRb != null)
        {
            bookRbWasKinematic = bookRb.isKinematic;
            bookRb.isKinematic = true;
        }

        float k = bookRadius > 0.0001f ? inspectRadius / bookRadius : 1f;
        targetScale = originalScale * k;
        targetInspectDistance = inspectDistance;
        currentInspectDistance = inspectDistance;
        panOffset = Vector2.zero;

        // Reset state buka buku
        openT = 0f;
        openTarget = 0f;
        phase = 0f;
        bookState = 0;
        viewCenter = localCenter;
        ApplyOpenPose();
        readShown = false;
        openedHintShown = false;

        // Hadap kamera
        settlingRot = false;
        if (faceCameraOnInspect)
        {
            inspectTargetRot = FaceRot(Camera.main.transform);
            settlingRot = true;
        }

        SetAboveDim(true);
        if (bookshelf != null) bookshelf.BeginInspect(inspectDistance, inspectRadius, true);
    }

    void UpdateInspect()
    {
        Transform cam = Camera.main.transform;
        bool ePressed = Keyboard.current.eKey.wasPressedThisFrame;
        bool escPressed = Keyboard.current.escapeKey.wasPressedThisFrame;

        if (canOpen)
        {
            if (escPressed)
            {
                if (bookState == 2)
                {
                    bookState = 3;
                    phase = Mathf.Min(1f, phase + closeSkip);
                    if (notBook) openRot = preOpenRot;
                }
                if (bookState != 0) escPressed = false;     // selama buku terbuka/animasi, jangan keluar inspect
            }
            else if (ePressed && bookState == 0)
            {
                preOpenRot = transform.rotation;
                bookState = 1;
                openRot = Quaternion.AngleAxis(openRoll, -cam.forward) * FaceRot(cam);
            }

            float step = Time.deltaTime / Mathf.Max(0.01f, openDuration) * 0.5f;
            float prevPhase = phase;

            if (bookState == 1)
            {
                phase = Mathf.MoveTowards(phase, openPeak, step);
                if (phase >= openPeak) bookState = 2;
            }
            else if (bookState == 3)
            {
                phase = Mathf.MoveTowards(phase, 1f, step * closeSpeedMul);
                if (phase >= 1f)
                {
                    phase = 1f;
                    ApplyOpenPose();
                    Vector3 pivot = transform.TransformPoint(CurrentLocalCenter());
                    if (!notBook) transform.RotateAround(pivot, cam.up, 180f);

                    phase = 0f;
                    bookState = 0;
                    settlingRot = notBook;
                    if (notBook) inspectTargetRot = preOpenRot;
                    ApplyOpenPose();

                    Vector3 newCenterLocal = CurrentLocalCenter();
                    transform.position += pivot - transform.TransformPoint(newCenterLocal);
                    viewCenter = newCenterLocal;

                    prevPhase = phase;
                    PlayCloseMonolog();
                }
            }

            if (phase != prevPhase) ApplyOpenPose();

            openTarget = bookState == 0 ? 0f : 1f;
            openT = Mathf.MoveTowards(openT, openTarget, Time.deltaTime * openBlendSpeed);

            bool showRead = bookState == 0;
            if (showRead != readShown)
            {
                readShown = showRead;
                if (bookshelf != null) bookshelf.SetReadHint(showRead);
            }

            bool showOpened = bookState != 0;
            if (showOpened != openedHintShown)
            {
                openedHintShown = showOpened;
                if (bookshelf != null) bookshelf.SetOpenedHint(showOpened);
            }
        }

        if (bookState != 0)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, openRot, Time.deltaTime * inspectMoveSpeed);

            if (Mouse.current.leftButton.isPressed)
            {
                Camera c = Camera.main;
                Vector2 d = Mouse.current.delta.ReadValue();
                float halfH = currentInspectDistance * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float wpp = (2f * halfH / Screen.height) * panSensitivity;
                panOffset += d * wpp;
                panOffset.x = Mathf.Clamp(panOffset.x, -panLimitAbs.x, panLimitAbs.x);
                panOffset.y = Mathf.Clamp(panOffset.y, -panLimitAbs.y, panLimitAbs.y);
            }
        }
        else if (Mouse.current.leftButton.isPressed)
        {
            settlingRot = false;
            Vector2 d = Mouse.current.delta.ReadValue();
            transform.Rotate(cam.up, -d.x * rotateSpeed, Space.World);
            transform.Rotate(cam.right, d.y * rotateSpeed, Space.World);
        }
        else if (settlingRot)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, inspectTargetRot, Time.deltaTime * inspectMoveSpeed);
        }

        float o = Mathf.SmoothStep(0f, 1f, openT);
        Vector3 scaleT = targetScale * Mathf.Lerp(1f, openScaleMul, o);
        transform.localScale = Vector3.Lerp(transform.localScale, scaleT, Time.deltaTime * inspectMoveSpeed);

        if (canZoom && bookState != 0)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                targetInspectDistance -= Mathf.Sign(scroll) * zoomStep;
                targetInspectDistance = Mathf.Clamp(targetInspectDistance, minInspectDistance, maxInspectDistance);
            }
        }
        else
        {
            targetInspectDistance = inspectDistance;
        }
        currentInspectDistance = Mathf.Lerp(currentInspectDistance, targetInspectDistance, Time.deltaTime * zoomSmooth);

        if (bookState == 0)
            panOffset = Vector2.Lerp(panOffset, Vector2.zero, Time.deltaTime * inspectMoveSpeed);

        Vector3 center = cam.position + cam.forward * currentInspectDistance
                         + cam.up * (openUp * o + panOffset.y)
                         + cam.right * panOffset.x;
        Vector3 wantCenter = bookState != 0 ? CurrentLocalCenter() : localCenter;
        viewCenter = bookState != 0
            ? wantCenter
            : Vector3.Lerp(viewCenter, wantCenter, Time.deltaTime * inspectMoveSpeed);
        Vector3 target = center - transform.TransformVector(viewCenter);
        transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * inspectMoveSpeed);

        // Esc = kembalikan ke lantai (hanya kalau buku sudah tertutup)
        if (escPressed)
        {
            activeBook = null;
            inspecting = false;
            returning = true;
            readShown = false;
            if (bookshelf != null) bookshelf.EndInspect();
            ResumePlayer();
        }
    }

    void UpdateReturn()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, originalScale, Time.deltaTime * inspectMoveSpeed);
        transform.position = Vector3.Lerp(transform.position, originalPos, Time.deltaTime * inspectMoveSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, originalRot, Time.deltaTime * inspectMoveSpeed);

        if (Vector3.Distance(transform.position, originalPos) < 0.002f &&
            Quaternion.Angle(transform.rotation, originalRot) < 0.5f)
        {
            transform.position = originalPos;
            transform.rotation = originalRot;
            transform.localScale = originalScale;
            returning = false;
            SetAboveDim(false);
            if (bookRb != null) bookRb.isKinematic = bookRbWasKinematic;
        }
    }
}
