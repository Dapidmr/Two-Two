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
//
// BARU: FocusOn() / Unfocus() untuk zoom level 2 (dipakai PandoraController).
// Kalau tidak dipanggil, perilaku sama persis seperti sebelumnya.
public class ZoomView : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI promptText;
    public float heightOffset = 1.5f;

    [Header("Kamera")]
    public Transform zoomPoint;
    public float cameraSpeed = 5f;
    public float zoomFieldOfView = 0f;

    [Header("First person")]
    public bool requireFacing = true;
    public float facingAngle = 60f;

    [Header("Kontrol di sisi belakang benda")]
    public bool blockHiddenControls = true;
    public bool autoBodyBlocker = true;
    public Collider[] bodyBlockers;
    public float occlusionTolerance = 0.02f;

    [Header("Memutar benda (digeser)")]
    public bool allowRotate = true;
    public Transform rotateTarget;
    public Transform pivot;
    public float dragSensitivity = 0.4f;
    public float swipeSensitivity = 0.15f;
    [Range(0f, 1f)] public float momentum = 0.35f;
    public float spinDamping = 6f;
    public bool invertX = false;
    public bool invertY = false;
    public bool resetOnExit = true;
    public float resetSpeed = 6f;
    public bool limitPitch = false;
    public float maxPitch = 90f;
    public bool lockToSingleAxis = true;
    public float axisDecidePixels = 6f;

    [Header("Kontrol di dalam tampilan ini")]
    public Collider[] controlColliders;

    [Header("Player")]
    public MonoBehaviour playerController;
    public Renderer[] playerRenderers;

    [Header("Sembunyikan saat zoom")]
    public GameObject[] hideDuringZoom;

    [Header("Keluar dari zoom")]
    public bool allowExit = true;

    [Header("Event")]
    public UnityEvent onEnter;
    public UnityEvent onExit;

    public bool IsZoomed { get { return isZoomed; } }

    // BARU: true kalau kamera sedang fokus ke bagian tertentu (zoom level 2)
    public bool IsFocused { get { return focused; } }
    // BARU: dipanggil saat fokus selesai (Esc/E atau keluar dari zoom)
    public event System.Action Unfocused;

    private static readonly List<ZoomView> instances = new List<ZoomView>();
    private static ZoomView bestTarget;
    private static int bestFrame = -1;

    private bool playerInRange = false;
    private bool isZoomed = false;
    private bool returning = false;
    private bool resetting = false;
    private bool promptOwned = false;
    private readonly HashSet<Collider> bodyBlockerSet = new HashSet<Collider>();

    private Transform cam;
    private Camera camComp;
    private float origFov;
    private Vector3 camOriginalPos;
    private Quaternion camOriginalRot;
    private Renderer focusRenderer;

    // Kamera dilepas dari Capsule saat zoom, Rigidbody dibekukan
    private Transform camParent;
    private bool camDetached = false;
    private Rigidbody playerRb;
    private bool playerRbWasKinematic;

    private Vector3 viewPos;
    private Quaternion viewRot;

    private Vector3 origPos;
    private Quaternion origRot;
    private Vector3 pivotPos;

    // BARU: status fokus section
    private bool focused = false;
    private float focusFov = 0f;
    private bool fovTouched = false;    // true kalau ZoomView ini yang mengubah FOV kamera

    private bool dragging = false;
    private int dragAxis = 0;
    private Vector2 dragAccumPx = Vector2.zero;
    private int scrollAxis = 0;
    private float lastScrollTime = -10f;
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private Vector2 velocity = Vector2.zero;
    private float yaw = 0f;
    private float pitch = 0f;

    void OnEnable()
    {
        instances.Add(this);
    }

    void OnDisable()
    {
        instances.Remove(this);

        if (promptOwned && promptText != null) promptText.gameObject.SetActive(false);
        promptOwned = false;

        if (isZoomed || returning)
        {
            RestorePlayerAndCamera();
            if (playerController != null) playerController.enabled = true;
        }
    }

    void Start()
    {
        camComp = Camera.main;
        cam = camComp.transform;
        origFov = camComp.fieldOfView;
        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;
        if (rotateTarget == null) rotateTarget = transform;

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
        if (isZoomed) return;
        playerInRange = false;
    }

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

    static ZoomView ComputeBest()
    {
        foreach (ZoomView z in instances)
            if (z.isZoomed || z.returning || z.resetting) return null;

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
            if (sp.z > 0f)
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
        else if (isZoomed && focused && (ePressed || escPressed))
            Unfocus();                                  // BARU: Esc/E dulu kembali ke tampilan box
        else if (isZoomed && allowExit && (ePressed || escPressed))
            ExitZoom();

        if (isZoomed)
        {
            // BARU: saat fokus, benda tidak bisa diputar (hanya dikembalikan ke posisi awal)
            if (focused)
            {
                if (resetting) UpdateReset();
            }
            else
            {
                UpdateRotate();
            }

            cam.position = Vector3.Lerp(cam.position, viewPos, Time.deltaTime * cameraSpeed);
            cam.rotation = Quaternion.Slerp(cam.rotation, viewRot, Time.deltaTime * cameraSpeed);

            float targetFov = (focused && focusFov > 0f) ? focusFov : zoomFieldOfView;
            if (targetFov > 0f && !camComp.orthographic)
            {
                fovTouched = true;
                camComp.fieldOfView = Mathf.Lerp(camComp.fieldOfView, targetFov, Time.deltaTime * cameraSpeed);
            }
        }
        else
        {
            if (returning)
            {
                cam.position = Vector3.Lerp(cam.position, camOriginalPos, Time.deltaTime * cameraSpeed);
                cam.rotation = Quaternion.Slerp(cam.rotation, camOriginalRot, Time.deltaTime * cameraSpeed);

                // FOV hanya dikembalikan kalau ZoomView ini yang mengubahnya
                if (fovTouched && !camComp.orthographic)
                    camComp.fieldOfView = Mathf.Lerp(camComp.fieldOfView, origFov, Time.deltaTime * cameraSpeed);

                // Posisi DAN rotasi harus sudah dekat, supaya tidak "snap" di akhir
                if (Vector3.Distance(cam.position, camOriginalPos) < 0.01f &&
                    Quaternion.Angle(cam.rotation, camOriginalRot) < 0.5f)
                {
                    cam.position = camOriginalPos;
                    cam.rotation = camOriginalRot;
                    if (fovTouched && !camComp.orthographic) camComp.fieldOfView = origFov;
                    fovTouched = false;
                    returning = false;
                    OnCameraReturned();
                }
            }

            if (resetting) UpdateReset();
        }
    }

    // Bekukan Rigidbody pemain dan lepas kamera dari Capsule
    void FreezePlayerAndDetachCamera()
    {
        if (playerRb == null && playerController != null)
            playerRb = playerController.GetComponent<Rigidbody>();

        if (playerRb != null)
        {
            playerRbWasKinematic = playerRb.isKinematic;
            if (!playerRb.isKinematic)
            {
                playerRb.linearVelocity = Vector3.zero;
                playerRb.angularVelocity = Vector3.zero;
            }
            playerRb.isKinematic = true;
        }

        if (cam.parent != null && !camDetached)
        {
            camParent = cam.parent;
            cam.SetParent(null, true);
            camDetached = true;
        }
    }

    void RestorePlayerAndCamera()
    {
        if (camDetached && cam != null)
        {
            cam.SetParent(camParent, true);
            camDetached = false;
            camParent = null;
        }
        if (playerRb != null) playerRb.isKinematic = playerRbWasKinematic;
    }

    void OnCameraReturned()
    {
        RestorePlayerAndCamera();
        if (playerController != null) playerController.enabled = true;
        SetPlayerVisible(true);
    }

    // ==================== BARU: zoom level 2 ====================

    // Pindahkan kamera ke titik lain (misal ke satu section benda). Box dikembalikan ke posisi awal.
    // Titik kamera harus ditentukan saat benda belum diputar, dan bukan anak dari benda yang diputar.
    public void FocusOn(Transform point, float fov)
    {
        if (!isZoomed || point == null) return;

        focused = true;
        focusFov = fov;
        dragging = false;
        velocity = Vector2.zero;
        yaw = Mathf.DeltaAngle(0f, yaw);
        pitch = Mathf.DeltaAngle(0f, pitch);
        resetting = true;
        viewPos = point.position;
        viewRot = point.rotation;
    }

    // Kembali ke tampilan zoom biasa (bisa diputar lagi)
    public void Unfocus()
    {
        if (!focused) return;

        focused = false;
        focusFov = 0f;
        resetting = false;
        viewPos = zoomPoint.position;
        viewRot = zoomPoint.rotation;

        if (Unfocused != null) Unfocused();
    }

    // ============================================================

    void UpdateRotate()
    {
        float dt = Time.deltaTime;
        Vector2 input = Vector2.zero;

        if (allowRotate)
        {
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

            Vector2 sc = Mouse.current.scroll.ReadValue();
            if (sc.sqrMagnitude > 0f)
            {
                if (lockToSingleAxis)
                {
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
            velocity = Vector2.Lerp(velocity, Vector2.zero, dt * spinDamping);
            applied = velocity * dt;
        }

        if (applied.sqrMagnitude <= 0f || rotateTarget == null) return;

        yaw += (invertX ? 1f : -1f) * applied.x;
        pitch += (invertY ? -1f : 1f) * applied.y;
        if (limitPitch) pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        ApplyPose();
    }

    void ApplyPose()
    {
        Vector3 camUp = viewRot * Vector3.up;
        Vector3 camRight = viewRot * Vector3.right;

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

    bool IsControlObject(Transform t)
    {
        foreach (Collider c in controlColliders)
            if (c != null && (t == c.transform || t.IsChildOf(c.transform)))
                return true;
        return false;
    }

    void SetupBlockers()
    {
        bodyBlockerSet.Clear();
        if (!blockHiddenControls || controlColliders == null || controlColliders.Length == 0) return;

        if (bodyBlockers != null)
            foreach (Collider c in bodyBlockers)
                if (c != null) bodyBlockerSet.Add(c);

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
        return blockerDist >= controlDist - occlusionTolerance;
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
        focused = false;                // BARU
        focusFov = 0f;                  // BARU

        origPos = rotateTarget.position;
        origRot = rotateTarget.rotation;
        pivotPos = ComputeCenter();
        viewPos = zoomPoint.position;
        viewRot = zoomPoint.rotation;

        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;

        if (promptText != null && promptOwned)
        {
            promptText.gameObject.SetActive(false);
            promptOwned = false;
        }

        FreezePlayerAndDetachCamera();

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
        // BARU: pastikan status fokus bersih (pemakai Unfocused ikut diberi tahu)
        if (focused)
        {
            focused = false;
            focusFov = 0f;
            if (Unfocused != null) Unfocused();
        }

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

    public void RebaseOrigin()
    {
        if (rotateTarget == null) return;
        origPos = rotateTarget.position;
        origRot = rotateTarget.rotation;
        pivotPos = ComputeCenter();
        yaw = 0f;
        pitch = 0f;
        velocity = Vector2.zero;
    }
}