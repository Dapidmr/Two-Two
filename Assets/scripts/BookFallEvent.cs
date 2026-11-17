using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class BookFallEvent : MonoBehaviour
{
    [Header("Buku yang akan jatuh")]
    public Transform bookToFall;          // drag Diary Book - Good

    [Header("Pemicu")]
    public bool listenToRadio = true;

    [Header("Mode")]
    public bool useRealPhysics = true;
    public Transform floorPosition;       // posisi + rotasi akhir buku berbaring di lantai

    [Header("Fisika")]
    public float delay = 0f;
    public float mass = 0.5f;
    public Vector3 pushImpulse = new Vector3(0.5f, 0f, 0.3f);
    public float spin = 0.5f;
    public bool ignoreShelfCollision = true;
    public float shelfIgnoreTime = 0.4f;
    public bool settleFlat = true;

    [Header("Suara")]
    public AudioSource audioSource;
    public AudioClip thudSound;
    public float soundDelay = 0.3f;

    [Header("Setelah mendarat: bisa diinteract (tekan E untuk zoom)")]
    public bool interactAfterLanding = true;
    public TextMeshProUGUI promptText;
    public MonoBehaviour playerController;
    public Renderer[] playerRenderers;
    public GameObject[] hideDuringZoom;
    public GameObject popupImage;
    public float interactRadius = 1.2f;
    public float zoomDistance = 0.8f;
    public float zoomPitch = 55f;
    public float zoomFieldOfView = 0f;
    public UnityEvent onInspectEnter;
    public UnityEvent onInspectExit;

    [Header("Tampilan zoom (buku menghadap kamera, memenuhi layar)")]
    public bool faceCameraOnZoom = true;
    public bool flipFace = false;
    public float displayRoll = 0f;
    public float fillMargin = 1.15f;
    public float openWidthFactor = 1f;
    public float moveDuration = 0.4f;
    public bool hideEverythingElse = true;

    private bool hasFallen = false;
    private bool fallStarted = false;
    private bool rescued = false;
    private bool interactReady = false;
    private float sinceFall = 0f;
    private float stillTimer = 0f;

    private Rigidbody rb;
    private RadioTuner radio;
    private ZoomView zoomView;
    private Transform zoomPointTf;

    private Vector3 savedPos;
    private Quaternion savedRot;
    private bool hasSaved = false;
    private Coroutine moveRoutine;
    private readonly List<Renderer> hiddenOthers = new List<Renderer>();

    void Start()
    {
        if (listenToRadio)
        {
            radio = FindFirstObjectByType<RadioTuner>();
            if (radio != null)
            {
                if (radio.onPoweredOff == null) radio.onPoweredOff = new UnityEvent();
                radio.onPoweredOff.AddListener(TriggerFall);
            }
            else
            {
                Debug.LogWarning("[BookFallEvent] RadioTuner tidak ditemukan di scene.");
            }
        }

        if (bookToFall == null)
            Debug.LogWarning("[BookFallEvent] Book To Fall masih kosong. Drag bukunya ke slot itu.");
    }

    void OnDestroy()
    {
        if (radio != null && radio.onPoweredOff != null)
            radio.onPoweredOff.RemoveListener(TriggerFall);
    }

    public void TriggerFall()
    {
        if (bookToFall == null)
        {
            Debug.LogWarning("[BookFallEvent] Book To Fall masih kosong. Drag bukunya ke slot itu.");
            return;
        }
        if (hasFallen) return;

        hasFallen = true;
        Debug.Log("[BookFallEvent] TriggerFall dipanggil untuk: " + bookToFall.name);

        if (delay > 0f) Invoke(nameof(DoFall), delay);
        else DoFall();
    }

    void DoFall()
    {
        GameObject book = bookToFall.gameObject;
        book.SetActive(true);

        foreach (MonoBehaviour mb in book.GetComponents<MonoBehaviour>())
            if (mb != null && mb != this && !(mb is BookAnimPlayer) && !(mb is ZoomView))
                mb.enabled = false;

        Collider bookCol = book.GetComponent<Collider>();
        if (bookCol == null) bookCol = AddBoxCollider(bookToFall);
        bookCol.enabled = true;
        bookCol.isTrigger = false;

        UnregisterFromBookshelf(bookCol);

        Transform shelfRoot = bookToFall.parent;
        float floorY = FindFloorY(bookCol, shelfRoot);
        if (float.IsNaN(floorY))
            Debug.LogWarning("[BookFallEvent] Tidak ada collider lantai di bawah buku, buku akan tembus. Tambahkan Box Collider di lantai.");

        if (ignoreShelfCollision && shelfRoot != null)
        {
            List<Collider> ignored = new List<Collider>();
            foreach (Collider c in shelfRoot.GetComponentsInChildren<Collider>())
            {
                if (c == bookCol || c.isTrigger) continue;
                if (!c.enabled || !c.gameObject.activeInHierarchy) continue;
                Physics.IgnoreCollision(bookCol, c, true);
                ignored.Add(c);
            }
            StartCoroutine(RestoreShelfCollision(bookCol, ignored, shelfIgnoreTime));
        }
        bookToFall.SetParent(null, true);

        if (useRealPhysics)
        {
            rb = book.GetComponent<Rigidbody>();
            if (rb == null) rb = book.AddComponent<Rigidbody>();

            rb.isKinematic = false;
            rb.useGravity = true;
            rb.mass = mass;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            rb.AddForce(pushImpulse, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * spin, ForceMode.Impulse);
        }
        else if (floorPosition != null)
        {
            bookToFall.position = floorPosition.position;
            bookToFall.rotation = floorPosition.rotation;
        }

        Debug.Log("[BookFallEvent] Buku dijatuhkan: " + book.name);
        Invoke(nameof(PlayThud), soundDelay);

        fallStarted = true;
        sinceFall = 0f;
    }

    IEnumerator RestoreShelfCollision(Collider bookCol, List<Collider> list, float wait)
    {
        yield return new WaitForSeconds(wait);
        foreach (Collider c in list)
            if (c != null && bookCol != null) Physics.IgnoreCollision(bookCol, c, false);
    }

    void Update()
    {
        RescueIfBelowFloor();

        if (interactAfterLanding && fallStarted && !interactReady)
            CheckLanded();

        // Titik kamera hanya diperbarui saat tidak zoom dan buku tidak sedang bergerak
        if (interactReady && zoomView != null && !zoomView.IsZoomed && moveRoutine == null)
            UpdateZoomPoint();
    }

    void RescueIfBelowFloor()
    {
        if (!hasFallen || rescued || !useRealPhysics || floorPosition == null || bookToFall == null) return;

        if (bookToFall.position.y < floorPosition.position.y - 0.3f)
        {
            rescued = true;

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            bookToFall.position = floorPosition.position;
            bookToFall.rotation = floorPosition.rotation;
        }
    }

    void CheckLanded()
    {
        sinceFall += Time.deltaTime;

        if (!useRealPhysics)
        {
            SetupInteraction();
            return;
        }
        if (rb == null) return;

        bool still = rb.linearVelocity.sqrMagnitude < 0.0025f && rb.angularVelocity.sqrMagnitude < 0.01f;
        stillTimer = still ? stillTimer + Time.deltaTime : 0f;

        if ((sinceFall > 0.5f && stillTimer > 0.6f) || sinceFall > 6f || rescued)
            SetupInteraction();
    }

    void SetupInteraction()
    {
        if (interactReady || bookToFall == null) return;
        interactReady = true;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        if (settleFlat && floorPosition != null)
            StartCoroutine(SettleTo(floorPosition.position, floorPosition.rotation));

        SphereCollider area = bookToFall.gameObject.AddComponent<SphereCollider>();
        area.isTrigger = true;
        Vector3 ls = bookToFall.lossyScale;
        float s = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        if (s < 0.0001f) s = 1f;
        area.radius = interactRadius / s;
        area.center = bookToFall.InverseTransformPoint(BookCenter());

        GameObject zp = new GameObject("BookZoomPoint");
        zoomPointTf = zp.transform;

        zoomView = bookToFall.GetComponent<ZoomView>();
        if (zoomView == null) zoomView = bookToFall.gameObject.AddComponent<ZoomView>();

        zoomView.promptText = promptText;
        zoomView.heightOffset = 0.5f;
        zoomView.zoomPoint = zoomPointTf;
        zoomView.zoomFieldOfView = zoomFieldOfView;
        zoomView.controlColliders = new Collider[0];
        zoomView.playerController = playerController;
        zoomView.playerRenderers = playerRenderers != null ? playerRenderers : new Renderer[0];
        zoomView.hideDuringZoom = hideDuringZoom;
        zoomView.allowExit = true;
        zoomView.resetOnExit = false;   // kembalinya buku diurus script ini
        zoomView.allowRotate = false;   // buku tidak diputar mouse, halaman dibalik pakai panah

        if (zoomView.onEnter == null) zoomView.onEnter = new UnityEvent();
        if (zoomView.onExit == null) zoomView.onExit = new UnityEvent();
        zoomView.onEnter.AddListener(() =>
        {
            OnBookZoomEnter();
            if (popupImage != null) popupImage.SetActive(true);
            if (onInspectEnter != null) onInspectEnter.Invoke();
        });
        zoomView.onExit.AddListener(() =>
        {
            OnBookZoomExit();
            if (popupImage != null) popupImage.SetActive(false);
            if (onInspectExit != null) onInspectExit.Invoke();
        });

        UpdateZoomPoint();
        Debug.Log("[BookFallEvent] Buku sudah mendarat dan bisa diinteract (tekan E di dekatnya).");
    }

    IEnumerator SettleTo(Vector3 pos, Quaternion rot)
    {
        Vector3 p0 = bookToFall.position;
        Quaternion r0 = bookToFall.rotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.4f;
            float k = Mathf.SmoothStep(0f, 1f, t);
            bookToFall.position = Vector3.Lerp(p0, pos, k);
            bookToFall.rotation = Quaternion.Slerp(r0, rot, k);
            yield return null;
        }
    }

    Vector3 BookCenter()
    {
        Renderer r = bookToFall.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.center : bookToFall.position;
    }

    void UpdateZoomPoint()
    {
        if (zoomPointTf == null || bookToFall == null) return;

        Vector3 center = BookCenter();

        Transform playerTf = playerController != null ? playerController.transform
                           : (Camera.main != null ? Camera.main.transform : null);

        Vector3 toPlayer = playerTf != null ? playerTf.position - center : Vector3.back;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) toPlayer = Vector3.back;
        toPlayer.Normalize();

        float pitch = zoomPitch * Mathf.Deg2Rad;
        Vector3 dir = (toPlayer * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)).normalized;

        zoomPointTf.position = center + dir * zoomDistance;
        zoomPointTf.rotation = Quaternion.LookRotation(center - zoomPointTf.position, Vector3.up);
    }

    // ---------- Tampilan zoom: buku menghadap kamera ----------

    void OnBookZoomEnter()
    {
        if (hideEverythingElse) HideOthers();
        if (!faceCameraOnZoom || zoomPointTf == null) return;

        if (!hasSaved)
        {
            savedPos = bookToFall.position;
            savedRot = bookToFall.rotation;
            hasSaved = true;
        }

        Vector3 camFwd = zoomPointTf.forward;
        Vector3 camUp = zoomPointTf.up;

        Quaternion rot = Quaternion.LookRotation(camUp, -camFwd);
        if (flipFace) rot = rot * Quaternion.AngleAxis(180f, Vector3.forward);
        rot = rot * Quaternion.AngleAxis(displayRoll, Vector3.up);

        Bounds lb = LocalBookBounds();
        Vector3 ls = bookToFall.lossyScale;
        float h = lb.size.z * Mathf.Abs(ls.z);
        float w = lb.size.x * Mathf.Abs(ls.x) * openWidthFactor;
        if (Mathf.Abs(Mathf.Sin(displayRoll * Mathf.Deg2Rad)) > 0.5f) { float t = h; h = w; w = t; }

        Camera c = Camera.main;
        float tanV = Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float dist = Mathf.Max((h * 0.5f) / tanV, (w * 0.5f) / (tanV * c.aspect)) * fillMargin;

        Vector3 target = zoomPointTf.position + camFwd * dist;
        Vector3 pos = target - rot * Vector3.Scale(lb.center, ls);

        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveBook(pos, rot, true));
    }

    void OnBookZoomExit()
    {
        ShowOthers();
        if (!faceCameraOnZoom || !hasSaved) return;

        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveBook(savedPos, savedRot, false));
    }

    IEnumerator MoveBook(Vector3 pos, Quaternion rot, bool rebase)
    {
        Vector3 p0 = bookToFall.position;
        Quaternion r0 = bookToFall.rotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, moveDuration);
            float k = Mathf.SmoothStep(0f, 1f, t);
            bookToFall.position = Vector3.Lerp(p0, pos, k);
            bookToFall.rotation = Quaternion.Slerp(r0, rot, k);
            yield return null;
        }
        bookToFall.position = pos;
        bookToFall.rotation = rot;

        if (rebase && zoomView != null) zoomView.RebaseOrigin();
        if (!rebase) hasSaved = false;
        moveRoutine = null;
    }

    Bounds LocalBookBounds()
    {
        bool first = true;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Renderer r in bookToFall.GetComponentsInChildren<Renderer>())
        {
            Bounds b = r.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = bookToFall.InverseTransformPoint(r.transform.TransformPoint(corner));
                if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                else result.Encapsulate(p);
            }
        }
        return result;
    }

    void HideOthers()
    {
        hiddenOthers.Clear();
        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled) continue;
            if (r.transform.IsChildOf(bookToFall)) continue;
            r.enabled = false;
            hiddenOthers.Add(r);
        }
    }

    void ShowOthers()
    {
        foreach (Renderer r in hiddenOthers)
            if (r != null) r.enabled = true;
        hiddenOthers.Clear();
    }

    // ---------- Pendukung ----------

    float FindFloorY(Collider bookCol, Transform shelfRoot)
    {
        Vector3 origin = bookCol.bounds.center;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        float y = float.NaN;
        foreach (RaycastHit h in hits)
        {
            if (h.collider == bookCol) continue;
            if (shelfRoot != null && h.collider.transform.IsChildOf(shelfRoot)) continue;

            if (h.distance < best)
            {
                best = h.distance;
                y = h.point.y;
            }
        }
        return y;
    }

    void UnregisterFromBookshelf(Collider col)
    {
        foreach (Bookshelf shelf in FindObjectsByType<Bookshelf>(FindObjectsSortMode.None))
        {
            if (shelf.bookColliders == null) continue;

            List<Collider> list = new List<Collider>(shelf.bookColliders);
            if (list.Remove(col)) shelf.bookColliders = list.ToArray();
        }

        foreach (ZoomView zv in FindObjectsByType<ZoomView>(FindObjectsSortMode.None))
        {
            if (zv.controlColliders == null) continue;

            List<Collider> list = new List<Collider>(zv.controlColliders);
            if (list.Remove(col)) zv.controlColliders = list.ToArray();
        }
    }

    BoxCollider AddBoxCollider(Transform t)
    {
        BoxCollider box = t.gameObject.AddComponent<BoxCollider>();
        Bounds b = LocalBookBounds();
        box.center = b.center;
        box.size = b.size;
        return box;
    }

    void PlayThud()
    {
        if (audioSource != null && thudSound != null)
            audioSource.PlayOneShot(thudSound);
    }
}