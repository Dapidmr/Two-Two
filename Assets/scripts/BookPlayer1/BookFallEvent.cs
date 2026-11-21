using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class BookFallEvent : MonoBehaviour
{
    [Header("Buku yang akan jatuh")]
    public Transform bookToFall;

    [Header("Pemicu")]
    public bool listenToRadio = true;

    [Header("Mode")]
    public bool useRealPhysics = true;
    public Transform floorPosition;

    [Header("Fisika")]
    public float delay = 0f;
    public float mass = 1f;
    public Vector3 pushImpulse = new Vector3(0.05f, 0f, 0.02f);
    public float spin = 0.05f;
    public float linearDamping = 1.5f;     // peredam gerak
    public float angularDamping = 4f;      // peredam putaran
    public bool ignoreShelfCollision = true;
    public float shelfIgnoreTime = 0.4f;
    public bool settleFlat = false;

    [Header("Suara")]
    public AudioSource audioSource;
    public AudioClip thudSound;
    public float soundDelay = 0.3f;

    [Header("Setelah mendarat: bisa diinteract (tekan E)")]
    public bool interactAfterLanding = true;
    public bool requireFacing = false;           // false = tidak perlu menatap buku tepat
    public bool debugLog = true;                 // log diagnosis di Console
    public TextMeshProUGUI promptText;
    public MonoBehaviour playerController;
    public Renderer[] playerRenderers;
    public GameObject[] hideDuringZoom;
    public GameObject popupImage;
    public float interactRadius = 2f;
    public float zoomDistance = 0.8f;
    public float zoomPitch = 15f;
    public float zoomFieldOfView = 0f;
    public UnityEvent onInspectEnter;
    public UnityEvent onInspectExit;

    private bool hasFallen = false;
    private bool fallStarted = false;
    private bool rescued = false;
    private bool interactReady = false;
    private float sinceFall = 0f;
    private float stillTimer = 0f;
    private float nextDebug = 0f;

    private Rigidbody rb;
    private RadioTuner radio;
    private ZoomView zoomView;
    private Transform zoomPointTf;

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

        // ZoomView dan MonocleLens tidak boleh ikut dimatikan
        foreach (MonoBehaviour mb in book.GetComponents<MonoBehaviour>())
            if (mb != null && mb != this && !(mb is ZoomView) && !(mb is MonocleLens))
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
            rb.linearDamping = linearDamping;
            rb.angularDamping = angularDamping;
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

        if (interactReady && zoomView != null && !zoomView.IsZoomed)
            UpdateZoomPoint();

        if (debugLog && interactReady && Time.time > nextDebug)
        {
            nextDebug = Time.time + 2f;
            Transform p = playerController != null ? playerController.transform : null;
            string dist = p != null ? Vector3.Distance(p.position, BookCenter()).ToString("F2") : "player tidak ditemukan";
            Debug.Log("[BookFallEvent] siap | jarak pemain ke buku: " + dist + " | radius interaksi: " + interactRadius +
                      " | ZoomView aktif: " + (zoomView != null && zoomView.enabled));
        }
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
            interactReady = true;
            StartCoroutine(SetupInteraction());
            return;
        }
        if (rb == null) return;

        bool still = rb.linearVelocity.sqrMagnitude < 0.0025f && rb.angularVelocity.sqrMagnitude < 0.01f;
        stillTimer = still ? stillTimer + Time.deltaTime : 0f;

        if ((sinceFall > 0.5f && stillTimer > 0.6f) || sinceFall > 4f || rescued)
        {
            interactReady = true;
            StartCoroutine(SetupInteraction());
        }
    }

    IEnumerator SetupInteraction()
    {
        if (bookToFall == null) yield break;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        if (settleFlat && floorPosition != null)
            yield return SettleTo(floorPosition.position, floorPosition.rotation);

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
        if (zoomView == null)
        {
            Debug.LogWarning("[BookFallEvent] ZoomView tidak ada di " + bookToFall.name +
                             ". Dibuat otomatis, tapi sebaiknya pasang ZoomView di objek yang SAMA dengan Book To Fall.");
            zoomView = bookToFall.gameObject.AddComponent<ZoomView>();
        }

        zoomView.enabled = true;
        zoomView.displayMode = ZoomView.DisplayMode.PickUp;
        zoomView.zoomPoint = zoomPointTf;
        zoomView.heightOffset = 0.5f;
        zoomView.requireFacing = requireFacing;
        zoomView.controlColliders = new Collider[0];
        zoomView.allowExit = true;
        zoomView.resetOnExit = false;
        if (promptText != null) zoomView.promptText = promptText;
        if (playerController != null) zoomView.playerController = playerController;
        if (playerRenderers != null && playerRenderers.Length > 0) zoomView.playerRenderers = playerRenderers;
        if (hideDuringZoom != null && hideDuringZoom.Length > 0) zoomView.hideDuringZoom = hideDuringZoom;
        if (zoomFieldOfView > 0f) zoomView.zoomFieldOfView = zoomFieldOfView;

        // Cari pemain kalau belum diisi (untuk log diagnosis dan ZoomPoint)
        if (playerController == null) playerController = FindFirstObjectByType<FirstPersonController>();

        if (zoomView.onEnter == null) zoomView.onEnter = new UnityEvent();
        if (zoomView.onExit == null) zoomView.onExit = new UnityEvent();
        zoomView.onEnter.AddListener(() =>
        {
            if (popupImage != null) popupImage.SetActive(true);
            if (onInspectEnter != null) onInspectEnter.Invoke();
        });
        zoomView.onExit.AddListener(() =>
        {
            if (popupImage != null) popupImage.SetActive(false);
            if (onInspectExit != null) onInspectExit.Invoke();
        });

        UpdateZoomPoint();
        Debug.Log("[BookFallEvent] Buku sudah mendarat dan bisa diambil (tekan E di dekatnya).");
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