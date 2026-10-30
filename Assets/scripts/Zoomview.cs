using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using TMPro;

// Sistem umum: tekan E dekat objek -> kamera zoom ke ZoomPoint -> benda bisa diputar 360 derajat
// dengan digeser (seperti bom di Keep Talking and Nobody Explodes).
// Dipakai untuk radio, Pandora, dan objek lain. Tidak bergantung pada script Bookshelf.
// Taruh script ini di objek yang punya Box Collider (Is Trigger) sebagai area deteksi.
public class ZoomView : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI promptText;      // tulisan "Tekan E"
    public float heightOffset = 1.5f;

    [Header("Kamera")]
    public Transform zoomPoint;             // titik tujuan kamera (di depan benda). Kamera diam di sini.
    public float cameraSpeed = 5f;

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
    public Collider[] controlColliders;     // knob, tombol, dll. Hanya aktif saat zoom.

    [Header("Player")]
    public MonoBehaviour playerController;  // script gerak player, dimatikan saat zoom
    public Renderer[] playerRenderers;      // mesh player yang disembunyikan saat zoom

    [Header("Sembunyikan saat zoom")]
    public GameObject[] hideDuringZoom;     // objek utuh yang disembunyikan saat zoom (misal Table)

    [Header("Keluar dari zoom")]
    public bool allowExit = true;           // false = pemain tidak bisa keluar (diatur script lain)

    [Header("Event")]
    public UnityEvent onEnter;
    public UnityEvent onExit;

    // Hanya untuk dibaca script lain
    public bool IsZoomed { get { return isZoomed; } }

    private bool playerInRange = false;
    private bool isZoomed = false;
    private bool returning = false;         // kamera sedang kembali ke posisi awal
    private bool resetting = false;         // benda sedang kembali ke rotasi awal

    private Transform cam;
    private Vector3 camOriginalPos;
    private Quaternion camOriginalRot;

    // Pose kamera saat zoom (dicatat sekali waktu masuk, jadi tidak ikut berputar bersama benda)
    private Vector3 viewPos;
    private Quaternion viewRot;

    // Pose awal benda
    private Vector3 origPos;
    private Quaternion origRot;
    private Vector3 pivotPos;

    private bool dragging = false;
    private Vector2 velocity = Vector2.zero;   // derajat per detik
    private int dragAxis = 0;               // 0 = belum ditentukan, 1 = kiri-kanan, 2 = atas-bawah
    private Vector2 dragAccumPx = Vector2.zero;
    private int scrollAxis = 0;
    private float lastScrollTime = -10f;
    private readonly System.Collections.Generic.List<Renderer> hiddenRenderers = new System.Collections.Generic.List<Renderer>();
    private float yaw = 0f;     // putaran kiri-kanan (derajat)
    private float pitch = 0f;   // putaran atas-bawah (derajat)

    void Start()
    {
        cam = Camera.main.transform;
        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;
        if (rotateTarget == null) rotateTarget = transform;

        SetControlsInteractable(false);
        if (promptText != null) promptText.gameObject.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
        if (promptText != null && !isZoomed) promptText.gameObject.SetActive(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (isZoomed) return;   // saat benda berputar, area deteksi ikut berputar. Abaikan.
        playerInRange = false;
        if (promptText != null) promptText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (cam == null || Keyboard.current == null || Mouse.current == null) return;

        // Prompt "Tekan E" mengikuti posisi objek
        if (playerInRange && !isZoomed && promptText != null)
        {
            Vector3 worldPos = transform.position + Vector3.up * heightOffset;
            promptText.transform.position = Camera.main.WorldToScreenPoint(worldPos);
        }

        bool ePressed = Keyboard.current.eKey.wasPressedThisFrame;
        bool escPressed = Keyboard.current.escapeKey.wasPressedThisFrame;

        if (!isZoomed && !resetting && playerInRange && ePressed)
            EnterZoom();
        else if (isZoomed && allowExit && (ePressed || escPressed))
            ExitZoom();

        if (isZoomed)
        {
            UpdateRotate();

            cam.position = Vector3.Lerp(cam.position, viewPos, Time.deltaTime * cameraSpeed);
            cam.rotation = Quaternion.Slerp(cam.rotation, viewRot, Time.deltaTime * cameraSpeed);
        }
        else
        {
            if (returning)
            {
                cam.position = Vector3.Lerp(cam.position, camOriginalPos, Time.deltaTime * cameraSpeed);
                cam.rotation = Quaternion.Slerp(cam.rotation, camOriginalRot, Time.deltaTime * cameraSpeed);

                if (Vector3.Distance(cam.position, camOriginalPos) < 0.01f)
                {
                    cam.position = camOriginalPos;
                    cam.rotation = camOriginalRot;
                    returning = false;
                }
            }

            if (resetting) UpdateReset();
        }
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
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
            if (System.Array.IndexOf(controlColliders, h.collider) >= 0)
                return true;
        return false;
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

        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;

        if (promptText != null) promptText.gameObject.SetActive(false);
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
        if (playerController != null) playerController.enabled = true;
        SetPlayerVisible(true);
        ShowObjects();
        if (playerInRange && promptText != null) promptText.gameObject.SetActive(true);

        // Kalau game pakai cursor terkunci (first person), aktifkan ini:
        // Cursor.lockState = CursorLockMode.Locked;
        // Cursor.visible = false;

        onExit.Invoke();
    }

    void SetControlsInteractable(bool value)
    {
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
        foreach (var r in playerRenderers)
            if (r != null) r.enabled = value;
    }
}