using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using TMPro;

// Sistem umum: tekan E dekat objek -> kamera zoom ke ZoomPoint.
// Mode Camera To Point: benda bisa diputar 360 derajat dengan digeser (radio, rak, Pandora).
// Mode Pick Up: benda maju ke depan kamera, latar menggelap, bisa diputar dan (opsional)
//               dibuka dengan clip animasi (buku, benda yang bisa diangkat). Gaya BookItem.
// Taruh script ini di objek yang punya Collider (Is Trigger) sebagai area deteksi.
//
// FocusOn() / Unfocus() untuk zoom level 2 (dipakai PandoraController), hanya di mode Camera To Point.
public class ZoomView : MonoBehaviour
{
    public enum DisplayMode { CameraToPoint, PickUp }

    [Header("Mode tampilan")]
    public DisplayMode displayMode = DisplayMode.CameraToPoint;

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
    public bool debugDrag = false;   // centang sementara: Console menampilkan apa yang ada di bawah kursor saat klik
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

    // ===== Pick Up: tampilan =====
    [Header("Pick Up: tampilan")]
    public float inspectDistance = 0.5f;     // jarak benda dari kamera
    public float inspectRadius = 0.2f;       // ukuran benda saat diangkat (radius, world unit)
    public float inspectMoveSpeed = 8f;
    public float rotateSpeed = 0.4f;         // derajat per pixel drag
    public bool faceCameraOnInspect = true;
    public Vector3 faceLocalAxis = Vector3.up;     // sumbu lokal benda yang menghadap kamera
    public Vector3 faceLocalUp = Vector3.right;    // sumbu lokal benda yang naik di layar

    [Header("Pick Up: latar gelap")]
    public float dimMax = 0.75f;
    public float dimFadeSpeed = 4f;
    public float dimGap = 0.05f;

    [Header("Pick Up: teks petunjuk (boleh pakai teks yang sama dengan rak)")]
    public TextMeshProUGUI inspectHintText;  // "Press Esc to back"
    public TextMeshProUGUI readHintText;     // "E to read"
    public string hintDefault = "Press Esc to back";
    public string hintOpened = "Press Esc to close book";

    [Header("Pick Up: buka dengan animasi (kosongkan kalau benda tidak bisa dibuka)")]
    public AnimationClip[] openClips;        // clip "Plane|..."
    public float openDuration = 1.2f;
    public float openUp = 0.08f;
    public float openScaleMul = 1.3f;
    public float openRoll = 0f;
    public float openBlendSpeed = 3f;
    public float closeSpeedMul = 1.5f;
    public float closeSkip = 0.07f;

    public bool IsZoomed { get { return isZoomed; } }
    public bool IsPickUp { get { return displayMode == DisplayMode.PickUp; } }

    // true kalau isi benda terlihat: mode biasa = selama zoom. Pick Up dengan clip = hanya saat terbuka.
    public bool ContentOpen
    {
        get
        {
            if (!isZoomed) return false;
            if (!IsPickUp) return true;
            bool hasClips = openClips != null && openClips.Length > 0;
            return !hasClips || bookState != 0;
        }
    }

    // true kalau kamera sedang fokus ke bagian tertentu (zoom level 2)
    public bool IsFocused { get { return focused; } }
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

    private Transform camParent;
    private bool camDetached = false;
    private Rigidbody playerRb;
    private bool playerRbWasKinematic;

    private Vector3 viewPos;
    private Quaternion viewRot;

    private Vector3 origPos;
    private Quaternion origRot;
    private Vector3 pivotPos;

    private bool focused = false;
    private float focusFov = 0f;
    private bool fovTouched = false;

    private bool dragging = false;
    private int dragAxis = 0;
    private Vector2 dragAccumPx = Vector2.zero;
    private int scrollAxis = 0;
    private float lastScrollTime = -10f;
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private Vector2 velocity = Vector2.zero;
    private float yaw = 0f;
    private float pitch = 0f;

    // ----- Pick Up -----
    private bool pickReturning = false;
    private Vector3 origScale;
    private Vector3 localCenter, viewCenter, targetScale;
    private float bookRadius = 0f;
    private Renderer[] allRends;
    private float phase = 0f;               // 0 = tertutup, 0.5 = terbuka penuh
    private int bookState = 0;              // 0 tertutup, 1 membuka, 2 terbuka, 3 menutup
    private float openT = 0f;
    private Quaternion openRot, inspectTargetRot;
    private bool settlingRot = false;
    private bool readShown = false;
    private Transform dimQuad;
    private Material dimMat;
    private float dimAlpha = 0f, dimTarget = 0f;

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

    void OnDestroy()
    {
        if (dimQuad != null) Destroy(dimQuad.gameObject);
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
        if (inspectHintText != null && displayMode == DisplayMode.PickUp) inspectHintText.gameObject.SetActive(false);
        if (readHintText != null && displayMode == DisplayMode.PickUp) readHintText.gameObject.SetActive(false);
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
            if (z.isZoomed || z.returning || z.resetting || z.pickReturning) return null;

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
        else if (isZoomed && IsPickUp)
        {
            if (UpdatePickUp() && allowExit) ExitZoom();
        }
        else if (isZoomed && focused && (ePressed || escPressed))
            Unfocus();
        else if (isZoomed && allowExit && (ePressed || escPressed))
            ExitZoom();

        if (isZoomed)
        {
            if (IsPickUp)
            {
                // sudah ditangani UpdatePickUp
            }
            else if (focused)
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

                if (fovTouched && !camComp.orthographic)
                    camComp.fieldOfView = Mathf.Lerp(camComp.fieldOfView, origFov, Time.deltaTime * cameraSpeed);

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
            if (pickReturning) UpdatePickReturn();
        }
    }

    void LateUpdate()
    {
        if (dimQuad == null) return;

        dimAlpha = Mathf.MoveTowards(dimAlpha, dimTarget, Time.deltaTime * dimFadeSpeed);
        dimMat.color = new Color(0f, 0f, 0f, dimAlpha);

        bool show = dimAlpha > 0.001f;
        dimQuad.gameObject.SetActive(show);
        if (show && camComp != null)
        {
            float dist = inspectDistance + inspectRadius + dimGap;
            dimQuad.localPosition = new Vector3(0f, 0f, dist);
            float h = 2f * dist * Mathf.Tan(camComp.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * camComp.aspect;
            dimQuad.localScale = new Vector3(w * 1.2f, h * 1.2f, 1f);
        }
    }

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

    // ==================== Zoom level 2 (Camera To Point) ====================

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

    // ==================== Pick Up ====================

    void BeginPickUp()
    {
        origScale = rotateTarget.localScale;
        pickReturning = false;

        allRends = rotateTarget.GetComponentsInChildren<Renderer>();
        bookRadius = 0f;
        if (allRends.Length > 0)
        {
            Bounds b = allRends[0].bounds;
            for (int i = 1; i < allRends.Length; i++) b.Encapsulate(allRends[i].bounds);
            localCenter = rotateTarget.InverseTransformPoint(b.center);
            bookRadius = b.extents.magnitude;
        }
        viewCenter = localCenter;
        float k = bookRadius > 0.0001f ? inspectRadius / bookRadius : 1f;
        targetScale = origScale * k;

        phase = 0f; bookState = 0; openT = 0f;
        ApplyOpenPose();
        readShown = false;
        settlingRot = faceCameraOnInspect;

        SetOpenedHint(false);
        if (inspectHintText != null) inspectHintText.gameObject.SetActive(true);

        if (dimQuad == null)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "ZoomViewDim";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(cam, false);
            q.transform.localRotation = Quaternion.identity;

            var r = q.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            dimMat = new Material(Shader.Find("Sprites/Default"));
            dimMat.color = new Color(0f, 0f, 0f, 0f);
            r.material = dimMat;

            dimQuad = q.transform;
            q.SetActive(false);
        }
        dimTarget = dimMax;
    }

    void EndPickUp()
    {
        dimTarget = 0f;
        readShown = false;
        if (readHintText != null) readHintText.gameObject.SetActive(false);
        if (inspectHintText != null) inspectHintText.gameObject.SetActive(false);
    }

    Quaternion FaceRot(Transform c)
    {
        Quaternion local = Quaternion.LookRotation(faceLocalAxis.normalized, faceLocalUp.normalized);
        Quaternion view = Quaternion.LookRotation(-c.forward, c.up);
        return view * Quaternion.Inverse(local);
    }

    void ApplyOpenPose()
    {
        if (openClips == null) return;
        foreach (var c in openClips)
            if (c != null) c.SampleAnimation(rotateTarget.gameObject, phase * c.length);
    }

    Vector3 CurrentLocalCenter()
    {
        if (allRends == null) return localCenter;
        Matrix4x4 w2l = rotateTarget.worldToLocalMatrix;
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
                    (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = m.MultiplyPoint3x4(c);
                if (first) { lb = new Bounds(p, Vector3.zero); first = false; }
                else lb.Encapsulate(p);
            }
        }
        return first ? localCenter : lb.center;
    }

    void SetReadHint(bool show)
    {
        if (show == readShown) return;
        readShown = show;
        if (readHintText != null) readHintText.gameObject.SetActive(show);
    }

    void SetOpenedHint(bool opened)
    {
        if (inspectHintText != null) inspectHintText.text = opened ? hintOpened : hintDefault;
    }

    // Mengembalikan true kalau pemain ingin keluar
    bool UpdatePickUp()
    {
        Transform t = rotateTarget;
        Transform c = cam;
        float dt = Time.deltaTime;
        bool e = Keyboard.current.eKey.wasPressedThisFrame;
        bool esc = Keyboard.current.escapeKey.wasPressedThisFrame;
        bool hasClips = openClips != null && openClips.Length > 0;
        bool wantExit;

        if (hasClips)
        {
            if (esc)
            {
                if (bookState == 2)
                {
                    bookState = 3;
                    phase = Mathf.Min(1f, phase + closeSkip);
                }
                if (bookState != 0) esc = false;      // selama buku terbuka/animasi, jangan keluar
            }
            else if (e && bookState == 0)
            {
                bookState = 1;
                openRot = Quaternion.AngleAxis(openRoll, -c.forward) * FaceRot(c);
            }
            wantExit = esc;

            float step = dt / Mathf.Max(0.01f, openDuration) * 0.5f;
            float prevPhase = phase;

            if (bookState == 1)
            {
                phase = Mathf.MoveTowards(phase, 0.5f, step);
                if (phase >= 0.5f) bookState = 2;
            }
            else if (bookState == 3)
            {
                phase = Mathf.MoveTowards(phase, 1f, step * closeSpeedMul);
                if (phase >= 1f)
                {
                    phase = 1f;
                    ApplyOpenPose();
                    Vector3 pv = t.TransformPoint(CurrentLocalCenter());
                    t.RotateAround(pv, c.up, 180f);

                    phase = 0f;
                    bookState = 0;
                    settlingRot = false;
                    ApplyOpenPose();

                    Vector3 newCenterLocal = CurrentLocalCenter();
                    t.position += pv - t.TransformPoint(newCenterLocal);
                    viewCenter = newCenterLocal;
                    prevPhase = phase;
                }
            }
            if (phase != prevPhase) ApplyOpenPose();

            openT = Mathf.MoveTowards(openT, bookState == 0 ? 0f : 1f, dt * openBlendSpeed);
            SetReadHint(bookState == 0);
            SetOpenedHint(bookState != 0);
        }
        else
        {
            wantExit = e || esc;
        }

        // Rotasi
        if (bookState != 0)
        {
            t.rotation = Quaternion.Slerp(t.rotation, openRot, dt * inspectMoveSpeed);
        }
        else if (allowRotate && Mouse.current.leftButton.isPressed)
        {
            settlingRot = false;
            Vector2 d = Mouse.current.delta.ReadValue();
            t.Rotate(c.up, -d.x * rotateSpeed, Space.World);
            t.Rotate(c.right, d.y * rotateSpeed, Space.World);
        }
        else if (settlingRot)
        {
            inspectTargetRot = FaceRot(c);   // ikut arah kamera yang sedang bergerak
            t.rotation = Quaternion.Slerp(t.rotation, inspectTargetRot, dt * inspectMoveSpeed);
        }

        // Skala dan posisi
        float o = Mathf.SmoothStep(0f, 1f, openT);
        Vector3 scaleT = targetScale * Mathf.Lerp(1f, openScaleMul, o);
        t.localScale = Vector3.Lerp(t.localScale, scaleT, dt * inspectMoveSpeed);

        Vector3 center = c.position + c.forward * inspectDistance + c.up * (openUp * o);
        Vector3 wantCenter = bookState != 0 ? CurrentLocalCenter() : localCenter;
        viewCenter = bookState != 0 ? wantCenter : Vector3.Lerp(viewCenter, wantCenter, dt * inspectMoveSpeed);
        Vector3 target = center - t.TransformVector(viewCenter);
        t.position = Vector3.Lerp(t.position, target, dt * inspectMoveSpeed);

        return wantExit;
    }

    void UpdatePickReturn()
    {
        Transform t = rotateTarget;
        float k = Time.deltaTime * inspectMoveSpeed;
        t.localScale = Vector3.Lerp(t.localScale, origScale, k);
        t.position = Vector3.Lerp(t.position, origPos, k);
        t.rotation = Quaternion.Slerp(t.rotation, origRot, k);

        if (Vector3.Distance(t.position, origPos) < 0.002f && Quaternion.Angle(t.rotation, origRot) < 0.5f)
        {
            t.position = origPos;
            t.rotation = origRot;
            t.localScale = origScale;
            pickReturning = false;
        }
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

        // 1. Kalau kursor tepat di atas SlidingCover, selalu boleh memutar radio.
        //    Dicek langsung ke collider cover (bukan lewat jarak dengan collider lain),
        //    jadi keycap, badan radio, atau IsControlVisible tidak bisa mengganggu.
        foreach (Collider c in controlColliders)
        {
            if (c == null || !c.enabled) continue;
            if (c.GetComponent<SlidingCover>() == null) continue;

            RaycastHit ch;
            if (c.Raycast(ray, out ch, 100f))
            {
                if (debugDrag) Debug.Log("[ZoomView] Kursor di atas cover '" + c.name + "' -> drag boleh memutar radio");
                return false;
            }
        }

        // 2. Selain cover: kontrol terdekat yang terlihat (knob, tombol power, keycap)
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);
        string found = "-";
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (System.Array.IndexOf(controlColliders, h.collider) < 0) continue;
            if (!IsControlVisible(h.collider)) continue;
            if (h.distance < best)
            {
                best = h.distance;
                found = h.collider.name;
            }
        }

        if (debugDrag)
            Debug.Log("[ZoomView] Kontrol di bawah kursor: " + found + (best == float.MaxValue ? " (tidak ada -> radio berputar)" : " (radio TIDAK berputar)"));

        return best != float.MaxValue;
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
        focused = false;
        focusFov = 0f;

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

        if (IsPickUp) BeginPickUp();

        onEnter.Invoke();
    }

    void ExitZoom()
    {
        if (focused)
        {
            focused = false;
            focusFov = 0f;
            if (Unfocused != null) Unfocused();
        }

        returning = true;
        isZoomed = false;
        dragging = false;

        if (IsPickUp)
        {
            resetting = false;
            pickReturning = true;
            EndPickUp();
        }
        else
        {
            resetting = resetOnExit;
            if (resetting)
            {
                yaw = Mathf.DeltaAngle(0f, yaw);
                pitch = Mathf.DeltaAngle(0f, pitch);
            }
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