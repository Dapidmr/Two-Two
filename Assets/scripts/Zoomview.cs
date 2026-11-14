using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using TMPro;

// Sistem umum: tekan E dekat objek -> kamera zoom ke ZoomPoint -> benda bisa diputar 360 derajat
// dengan digeser (seperti bom di Keep Talking and Nobody Explodes).
// Dipakai untuk radio, buku, rak, Pandora, dan objek lain. Tidak bergantung pada script Bookshelf.
// Taruh script ini di objek yang punya Box Collider (Is Trigger) sebagai area deteksi.
// Mendukung kamera tetap maupun first person.
public class ZoomView : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI promptText;      // tulisan "Tekan E"
    public float heightOffset = 1.5f;

    [Header("Kamera")]
    public Transform zoomPoint;             // titik tujuan kamera (di depan benda). Kamera diam di sini.
    public float cameraSpeed = 5f;
    public float zoomFieldOfView = 0f;      // 0 = tidak diubah. Isi 25-40 untuk mengurangi efek melar/gepeng saat benda berputar.

    [Header("First person")]
    public bool requireFacing = true;       // true = pemain harus menghadap objek untuk bisa menekan E
    public float facingAngle = 60f;         // seberapa miring dari tengah layar (derajat) masih dianggap menghadap

    [Header("Kontrol di sisi belakang benda")]
    public bool blockHiddenControls = true;    // true = tombol yang tertutup badan benda tidak bisa diklik (misal tombol di belakang radio saat dilihat dari depan)
    public bool autoBodyBlocker = true;        // true = bentuk badan benda dicari otomatis sebagai penghalang (hanya untuk benda yang bisa diputar)
    public Collider[] bodyBlockers;            // opsional: collider badan benda yang kamu pilih sendiri
    public float occlusionTolerance = 0.02f;   // kontrol yang menempel rata pada badan tetap bisa diklik

    [Header("Memutar benda (digeser)")]
    public bool allowRotate = true;
    public Transform rotateTarget;          // benda yang diputar. Kosong = objek ini sendiri.
    public Transform pivot;                 // pusat putaran. Kosong = pusat benda dihitung otomatis.
    public float dragSensitivity = 0.4f;    // klik-tahan-geser (derajat per pixel)
    public float swipeSensitivity = 0.15f;  // geser dua jari di trackpad
    [Range(0f, 1f)] public float momentum = 0.35f;  // 0 = berhenti langsung, makin besar makin meluncur
    public float spinDamping = 6f;          // makin besar, makin cepat berhenti meluncur
    public bool invertX = false;            // centang kalau arah kiri-kanan terbalik
    public bool invertY = false;            // centang kalau arah atas-bawah terbalik
    public bool resetOnExit = true;         // benda kembali ke posisi awal saat keluar
    public float resetSpeed = 6f;
    public bool limitPitch = false;         // true = putaran atas-bawah dibatasi
    public float maxPitch = 90f;            // batas atas-bawah (derajat), dipakai kalau Limit Pitch aktif
    public bool lockToSingleAxis = true;    // true = satu geseran hanya satu arah (kiri-kanan ATAU atas-bawah)
    public float axisDecidePixels = 6f;     // geseran awal sejauh ini menentukan arahnya

    [Header("Kontrol di dalam tampilan ini")]
    public Collider[] controlColliders;     // knob, tombol, buku, dll. Hanya aktif saat zoom.

    [Header("Player")]
    public MonoBehaviour playerController;  // script gerak player, dimatikan saat zoom dan dinyalakan lagi setelah kamera kembali
    public Renderer[] playerRenderers;      // mesh player yang disembunyikan saat zoom (kosongkan untuk first person)

    [Header("Sembunyikan saat zoom")]
    public GameObject[] hideDuringZoom;     // objek utuh yang disembunyikan saat zoom (misal Table)

    [Header("Keluar dari zoom")]
    public bool allowExit = true;           // false = pemain tidak bisa keluar (diatur script lain)

    [Header("Event")]
    public UnityEvent onEnter;
    public UnityEvent onExit;

    // Hanya untuk dibaca script lain
    public bool IsZoomed { get { return isZoomed; } }

    // ---- Daftar semua ZoomView, supaya hanya satu yang bisa ditekan E (yang paling dihadap) ----
    private static readonly List<ZoomView> instances = new List<ZoomView>();
    private static ZoomView bestTarget;
    private static int bestFrame = -1;

    private bool playerInRange = false;
    private bool isZoomed = false;
    private bool returning = false;         // kamera sedang kembali ke posisi awal
    private bool resetting = false;         // benda sedang kembali ke rotasi awal
    private bool promptOwned = false;       // true = tulisan "Tekan E" sedang ditampilkan oleh objek ini
    private readonly HashSet<Collider> bodyBlockerSet = new HashSet<Collider>();   // collider yang menghalangi klik

    private Transform cam;
    private Camera camComp;
    private float origFov;
    private Vector3 camOriginalPos;
    private Quaternion camOriginalRot;
    private Renderer focusRenderer;

    // Pose kamera saat zoom (dicatat sekali waktu masuk, jadi tidak ikut berputar bersama benda)
    private Vector3 viewPos;
    private Quaternion viewRot;

    // Pose awal benda
    private Vector3 origPos;
    private Quaternion origRot;
    private Vector3 pivotPos;

    private bool dragging = false;
    private int dragAxis = 0;               // 0 = belum ditentukan, 1 = kiri-kanan, 2 = atas-bawah
    private Vector2 dragAccumPx = Vector2.zero;
    private int scrollAxis = 0;
    private float lastScrollTime = -10f;
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private Vector2 velocity = Vector2.zero;   // derajat per detik
    private float yaw = 0f;                    // putaran kiri-kanan (derajat)
    private float pitch = 0f;                  // putaran atas-bawah (derajat)

    void OnEnable()
    {
        instances.Add(this);
    }

    void OnDisable()
    {
        instances.Remove(this);

        if (promptOwned && promptText != null) promptText.gameObject.SetActive(false);
        promptOwned = false;

        // Jangan sampai pemain terkunci kalau objek ini dimatikan saat zoom
        if ((isZoomed || returning) && playerController != null) playerController.enabled = true;
    }

    void Start()
    {
        camComp = Camera.main;
        cam = camComp.transform;
        origFov = camComp.fieldOfView;
        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;
        if (rotateTarget == null) rotateTarget = transform;

        // Kalau slot Player Controller kosong, cari pengendali first person secara otomatis
        if (playerController == null) playerController = FindFirstObjectByType<FirstPersonController>();

        focusRenderer = rotateTarget.GetComponentInChildren<Renderer>();
        SetupBlockers();

        SetControlsInteractable(false);
        if (promptText != null) promptText.gameObject.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (isZoomed) return;   // saat benda berputar, area deteksi ikut berputar. Abaikan.
        playerInRange = false;
    }

    // ---------- Memilih objek yang sedang dihadap ----------

    Vector3 FocusPoint()
    {
        return focusRenderer != null ? focusRenderer.bounds.center : transform.position;
    }

    float AngleToCamera()
    {
        Vector3 to = FocusPoint() - cam.position;
        if (to.sqrMagnitude < 0.000001f) return 0f;
        return Vector3.Angle(cam.forward, to);
    }

    // Dari semua ZoomView yang dalam jangkauan dan dihadap, pilih yang paling lurus di tengah layar
    static ZoomView ComputeBest()
    {
        foreach (ZoomView z in instances)
            if (z.isZoomed || z.returning || z.resetting) return null;   // sedang ada yang aktif

        ZoomView best = null;
        float bestAngle = float.MaxValue;
        foreach (ZoomView z in instances)
        {
            if (!z.playerInRange || z.cam == null) continue;

            float a = z.AngleToCamera();
            if (z.requireFacing && a > z.facingAngle) continue;

            if (a < bestAngle)
            {
                bestAngle = a;
                best = z;
            }
        }
        return best;
    }

    void UpdatePrompt(bool show)
    {
        if (promptText == null) return;

        if (show)
        {
            Vector3 sp = camComp.WorldToScreenPoint(transform.position + Vector3.up * heightOffset);
            if (sp.z > 0f)   // hanya kalau objek ada di depan kamera
            {
                promptText.gameObject.SetActive(true);
                promptText.transform.position = sp;
                promptOwned = true;
                return;
            }
        }

        if (promptOwned)
        {
            promptText.gameObject.SetActive(false);
            promptOwned = false;
        }
    }

    // ---------- Update ----------

    void Update()
    {
        if (cam == null || Keyboard.current == null || Mouse.current == null) return;

        if (Time.frameCount != bestFrame)
        {
            bestFrame = Time.frameCount;
            bestTarget = ComputeBest();
        }
        bool canInteract = bestTarget == this;

        UpdatePrompt(canInteract);

        bool ePressed = Keyboard.current.eKey.wasPressedThisFrame;
        bool escPressed = Keyboard.current.escapeKey.wasPressedThisFrame;

        if (canInteract && ePressed)
            EnterZoom();
        else if (isZoomed && allowExit && (ePressed || escPressed))
            ExitZoom();

        if (isZoomed)
        {
            UpdateRotate();

            cam.position = Vector3.Lerp(cam.position, viewPos, Time.deltaTime * cameraSpeed);
            cam.rotation = Quaternion.Slerp(cam.rotation, viewRot, Time.deltaTime * cameraSpeed);

            if (zoomFieldOfView > 0f && !camComp.orthographic)
                camComp.fieldOfView = Mathf.Lerp(camComp.fieldOfView, zoomFieldOfView, Time.deltaTime * cameraSpeed);
        }
        else
        {
            if (returning)
            {
                cam.position = Vector3.Lerp(cam.position, camOriginalPos, Time.deltaTime * cameraSpeed);
                cam.rotation = Quaternion.Slerp(cam.rotation, camOriginalRot, Time.deltaTime * cameraSpeed);

                if (zoomFieldOfView > 0f && !camComp.orthographic)
                    camComp.fieldOfView = Mathf.Lerp(camComp.fieldOfView, origFov, Time.deltaTime * cameraSpeed);

                if (Vector3.Distance(cam.position, camOriginalPos) < 0.01f)
                {
                    cam.position = camOriginalPos;
                    cam.rotation = camOriginalRot;
                    if (zoomFieldOfView > 0f && !camComp.orthographic) camComp.fieldOfView = origFov;
                    returning = false;
                    OnCameraReturned();
                }
            }

            if (resetting) UpdateReset();
        }
    }

    // Setelah kamera kembali: pemain boleh bergerak lagi (supaya tidak berebut kamera dengan script gerak)
    void OnCameraReturned()
    {
        if (playerController != null) playerController.enabled = true;
        SetPlayerVisible(true);
    }

    void UpdateRotate()
    {
        float dt = Time.deltaTime;
        Vector2 input = Vector2.zero;   // derajat pada frame ini

        if (allowRotate)
        {
            // Klik-tahan-geser (tidak dimulai kalau kursor di atas knob atau tombol)
            if (Mouse.current.leftButton.wasPressedThisFrame && !IsOverControl())
            {
                dragging = true;
                dragAxis = 0;
                dragAccumPx = Vector2.zero;
            }
            if (Mouse.current.leftButton.wasReleasedThisFrame)
                dragging = false;

            if (dragging)
            {
                Vector2 raw = Mouse.current.delta.ReadValue();

                if (lockToSingleAxis)
                {
                    if (dragAxis == 0)
                    {
                        // Tunggu gerakan awal untuk menentukan arah: kiri-kanan atau atas-bawah
                        dragAccumPx += raw;
                        if (dragAccumPx.magnitude >= axisDecidePixels)
                        {
                            dragAxis = Mathf.Abs(dragAccumPx.x) >= Mathf.Abs(dragAccumPx.y) ? 1 : 2;
                            raw = dragAxis == 1 ? new Vector2(dragAccumPx.x, 0f) : new Vector2(0f, dragAccumPx.y);
                        }
                        else
                        {
                            raw = Vector2.zero;
                        }
                    }
                    else
                    {
                        raw = dragAxis == 1 ? new Vector2(raw.x, 0f) : new Vector2(0f, raw.y);
                    }
                }

                input += raw * dragSensitivity;
            }

            // Geser dua jari di trackpad / scroll
            Vector2 sc = Mouse.current.scroll.ReadValue();
            if (sc.sqrMagnitude > 0f)
            {
                if (lockToSingleAxis)
                {
                    // Satu gesekan = satu arah. Gesekan baru dimulai setelah jeda sebentar.
                    if (Time.time - lastScrollTime > 0.2f) scrollAxis = 0;
                    if (scrollAxis == 0) scrollAxis = Mathf.Abs(sc.x) >= Mathf.Abs(sc.y) ? 1 : 2;
                    sc = scrollAxis == 1 ? new Vector2(sc.x, 0f) : new Vector2(0f, sc.y);
                }
                lastScrollTime = Time.time;
                input += sc * swipeSensitivity;
            }
        }

        Vector2 applied;
        if (input.sqrMagnitude > 0f)
        {
            applied = input;
            velocity = input / Mathf.Max(dt, 0.0001f) * momentum;
        }
        else
        {
            // Meluncur pelan lalu berhenti setelah dilepas
            velocity = Vector2.Lerp(velocity, Vector2.zero, dt * spinDamping);
            applied = velocity * dt;
        }

        if (applied.sqrMagnitude <= 0f || rotateTarget == null) return;

        // Hanya dua sumbu: kiri-kanan (yaw) dan atas-bawah (pitch). Tidak ada miring ke samping (roll).
        yaw += (invertX ? 1f : -1f) * applied.x;
        pitch += (invertY ? -1f : 1f) * applied.y;
        if (limitPitch) pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        ApplyPose();
    }

    // Hitung pose benda dari dua sudut (yaw dan pitch) terhadap pose awalnya
    void ApplyPose()
    {
        Vector3 camUp = viewRot * Vector3.up;
        Vector3 camRight = viewRot * Vector3.right;

        // Putar kiri-kanan dulu, lalu atas-bawah. Urutan ini yang mencegah benda miring ke samping.
        Quaternion delta = Quaternion.AngleAxis(pitch, camRight) * Quaternion.AngleAxis(yaw, camUp);

        rotateTarget.rotation = delta * origRot;
        rotateTarget.position = pivotPos + delta * (origPos - pivotPos);
    }

    void UpdateReset()
    {
        if (rotateTarget == null) { resetting = false; return; }

        yaw = Mathf.Lerp(yaw, 0f, Time.deltaTime * resetSpeed);
        pitch = Mathf.Lerp(pitch, 0f, Time.deltaTime * resetSpeed);
        ApplyPose();

        if (Mathf.Abs(yaw) < 0.1f && Mathf.Abs(pitch) < 0.1f)
        {
            yaw = 0f;
            pitch = 0f;
            ApplyPose();
            resetting = false;
        }
    }

    bool IsOverControl()
    {
        if (controlColliders == null) return false;

        Ray ray = camComp.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
            if (System.Array.IndexOf(controlColliders, h.collider) >= 0 && IsControlVisible(h.collider))
                return true;
        return false;
    }

    // ---------- Kontrol yang tertutup badan benda ----------

    bool IsControlObject(Transform t)
    {
        foreach (Collider c in controlColliders)
            if (c != null && (t == c.transform || t.IsChildOf(c.transform)))
                return true;
        return false;
    }

    // Menyiapkan penghalang klik: badan benda (bentuk terbesar selain kontrol) dijadikan collider
    void SetupBlockers()
    {
        bodyBlockerSet.Clear();
        if (!blockHiddenControls || controlColliders == null || controlColliders.Length == 0) return;

        if (bodyBlockers != null)
            foreach (Collider c in bodyBlockers)
                if (c != null) bodyBlockerSet.Add(c);

        // Otomatis hanya untuk benda yang bisa diputar (di situ tombol bisa berada di sisi belakang)
        if (!autoBodyBlocker || !allowRotate) return;

        Renderer best = null;
        float bestVolume = 0f;
        foreach (Renderer r in rotateTarget.GetComponentsInChildren<Renderer>())
        {
            if (IsControlObject(r.transform)) continue;

            Vector3 sz = r.bounds.size;
            float volume = sz.x * sz.y * sz.z;
            if (volume > bestVolume)
            {
                bestVolume = volume;
                best = r;
            }
        }
        if (best == null) return;

        // Pakai collider yang sudah ada kalau ada, kalau belum buat dari bentuk modelnya
        Collider body = best.GetComponent<Collider>();
        if (body == null)
        {
            MeshFilter mf = best.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            MeshCollider mc = best.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            body = mc;
        }

        bodyBlockerSet.Add(body);
        Debug.Log("[ZoomView] Penghalang klik (badan benda): " + best.name);
    }

    // true = kontrol ini terlihat dan boleh diklik. false = tertutup badan benda dari sudut kamera sekarang.
    public bool IsControlVisible(Collider control)
    {
        if (!blockHiddenControls || control == null || camComp == null || bodyBlockerSet.Count == 0) return true;
        if (Mouse.current == null) return true;

        Ray ray = camComp.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

        float controlDist = float.MaxValue;
        float blockerDist = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            if (h.collider == control) controlDist = Mathf.Min(controlDist, h.distance);
            else if (bodyBlockerSet.Contains(h.collider)) blockerDist = Mathf.Min(blockerDist, h.distance);
        }

        if (controlDist == float.MaxValue) return true;
        return blockerDist >= controlDist - occlusionTolerance;   // badan benda tidak ada di depan kontrol
    }

    Vector3 ComputeCenter()
    {
        if (pivot != null) return pivot.position;

        Renderer[] rs = rotateTarget.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return rotateTarget.position;

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b.center;
    }

    void EnterZoom()
    {
        isZoomed = true;
        returning = false;
        dragging = false;
        velocity = Vector2.zero;
        yaw = 0f;
        pitch = 0f;

        // Catat pose awal benda dan titik kamera (sekali, sebelum benda diputar)
        origPos = rotateTarget.position;
        origRot = rotateTarget.rotation;
        pivotPos = ComputeCenter();
        viewPos = zoomPoint.position;
        viewRot = zoomPoint.rotation;

        // Pose kamera sekarang = posisi kamera saat berdiri (first person) atau kamera tetap
        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;

        if (promptText != null && promptOwned)
        {
            promptText.gameObject.SetActive(false);
            promptOwned = false;
        }

        // Matikan gerak pemain selama zoom (OnDisable-nya membuat kursor bebas)
        if (playerController != null) playerController.enabled = false;
        SetPlayerVisible(false);
        HideObjects();

        SetControlsInteractable(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        onEnter.Invoke();
    }

    void ExitZoom()
    {
        returning = true;
        isZoomed = false;
        dragging = false;
        resetting = resetOnExit;
        if (resetting)
        {
            yaw = Mathf.DeltaAngle(0f, yaw);
            pitch = Mathf.DeltaAngle(0f, pitch);
        }

        SetControlsInteractable(false);
        ShowObjects();

        // Pemain baru boleh bergerak lagi setelah kamera kembali (lihat OnCameraReturned)
        onExit.Invoke();
    }

    void SetControlsInteractable(bool value)
    {
        if (controlColliders == null) return;
        foreach (var c in controlColliders)
            if (c != null) c.enabled = value;
    }

    void HideObjects()
    {
        hiddenRenderers.Clear();
        if (hideDuringZoom == null) return;
        foreach (var go in hideDuringZoom)
        {
            if (go == null) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.enabled)
                {
                    r.enabled = false;
                    hiddenRenderers.Add(r);
                }
            }
        }
    }

    void ShowObjects()
    {
        foreach (var r in hiddenRenderers)
            if (r != null) r.enabled = true;
        hiddenRenderers.Clear();
    }

    void SetPlayerVisible(bool value)
    {
        if (playerRenderers == null) return;
        foreach (var r in playerRenderers)
            if (r != null) r.enabled = value;
    }
}