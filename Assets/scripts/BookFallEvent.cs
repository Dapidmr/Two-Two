using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class BookFallEvent : MonoBehaviour
{
    [Header("Buku yang akan jatuh")]
    public Transform bookToFall;          // drag salah satu buku, mis. Book (5)

    [Header("Pemicu")]
    public bool listenToRadio = true;     // true = otomatis jatuh saat radio dimatikan (tidak perlu menyambung event manual)

    [Header("Mode")]
    public bool useRealPhysics = true;    // true = jatuh pakai Rigidbody, false = langsung pindah ke lantai
    public Transform floorPosition;       // objek kosong penanda posisi akhir di lantai. Dipakai kalau useRealPhysics = false,
                                          // dan sebagai pengaman kalau buku sampai tembus lantai.

    [Header("Fisika")]
    public float delay = 0f;              // jeda sebelum buku mulai jatuh (detik)
    public float mass = 0.5f;
    public Vector3 pushImpulse = new Vector3(0.5f, 0f, 0.3f);  // dorongan awal (arah dunia), supaya buku jatuh keluar dari rak
    public float spin = 0.5f;
    public bool ignoreShelfCollision = true;   // true = buku tidak menabrak badan rak (mencegah buku terjebak di dalam collider rak)

    [Header("Suara")]
    public AudioSource audioSource;       // AudioSource untuk bunyi "bruk"
    public AudioClip thudSound;
    public float soundDelay = 0.3f;       // jeda sebelum suara bruk (kira-kira waktu buku jatuh)

    [Header("Setelah mendarat: bisa diinteract (tekan E untuk zoom dan putar)")]
    public bool interactAfterLanding = true;
    public TextMeshProUGUI promptText;      // tulisan "Tekan E" (PromptText di Canvas)
    public MonoBehaviour playerController;  // script gerak player, dimatikan saat zoom
    public Renderer[] playerRenderers;      // mesh player yang disembunyikan saat zoom
    public GameObject[] hideDuringZoom;     // objek lain yang disembunyikan saat zoom (opsional, mis. Table)
    public GameObject popupImage;           // opsional: gambar yang muncul selama memeriksa buku
    public float interactRadius = 1.2f;     // jarak pemain ke buku supaya "Tekan E" muncul (meter)
    public float zoomDistance = 0.8f;       // jarak kamera dari buku saat zoom
    public float zoomPitch = 55f;           // sudut kamera melihat ke bawah (derajat)
    public float zoomFieldOfView = 0f;      // 0 = tidak diubah
    public UnityEvent onInspectEnter;
    public UnityEvent onInspectExit;

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

    // Dipanggil otomatis saat radio dimatikan (atau dari event kalau kamu menyambungnya sendiri)
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

        // 1) Pastikan buku aktif (kalau sebelumnya sempat diambil dan disembunyikan)
        book.SetActive(true);

        // 2) Matikan script lain di buku. BookItem menarik buku kembali ke rak setiap frame,
        //    itu yang bikin buku tidak mau jatuh.
        foreach (MonoBehaviour mb in book.GetComponents<MonoBehaviour>())
            if (mb != null && mb != this) mb.enabled = false;

        // 3) Pastikan collider ada dan menyala. Script Bookshelf mematikannya, makanya buku tembus lantai.
        Collider bookCol = book.GetComponent<Collider>();
        if (bookCol == null) bookCol = AddBoxCollider(bookToFall);
        bookCol.enabled = true;
        bookCol.isTrigger = false;

        // 4) Lepaskan dari daftar Bookshelf supaya collider-nya tidak dimatikan lagi nanti
        UnregisterFromBookshelf(bookCol);

        // 5) Cek apakah ada lantai (collider) di bawah buku
        Transform shelfRoot = bookToFall.parent;
        float floorY = FindFloorY(bookCol, shelfRoot);
        if (float.IsNaN(floorY))
            Debug.LogWarning("[BookFallEvent] Tidak ada collider lantai di bawah buku, buku akan tembus. Tambahkan Box Collider di lantai.");

        // 6) Abaikan tabrakan dengan badan rak, lalu lepas buku dari rak (jadi objek mandiri)
        if (ignoreShelfCollision && shelfRoot != null)
        {
            foreach (Collider c in shelfRoot.GetComponentsInChildren<Collider>())
            {
                if (c == bookCol || c.isTrigger) continue;
                if (!c.enabled || !c.gameObject.activeInHierarchy) continue;   // IgnoreCollision hanya untuk collider yang aktif
                Physics.IgnoreCollision(bookCol, c, true);
            }
        }
        bookToFall.SetParent(null, true);

        // 7) Jatuh
        if (useRealPhysics)
        {
            rb = book.GetComponent<Rigidbody>();
            if (rb == null) rb = book.AddComponent<Rigidbody>();

            rb.isKinematic = false;   // matikan kinematic dulu, baru atur mode tabrakan
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

    void Update()
    {
        RescueIfBelowFloor();

        // Setelah buku berhenti, pasang interaksi
        if (interactAfterLanding && fallStarted && !interactReady)
            CheckLanded();

        // Titik kamera selalu menghadap sisi tempat pemain berada (sebelum zoom dimulai)
        if (interactReady && zoomView != null && !zoomView.IsZoomed)
            UpdateZoomPoint();
    }

    // Pengaman: kalau buku sampai jatuh jauh di bawah lantai, taruh di Floor Position
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

    // Buku dianggap sudah mendarat kalau diam sekitar 0,6 detik
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

        // Bekukan buku di tempatnya supaya bisa diputar lewat ZoomView tanpa dilawan fisika
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        // Area deteksi pemain (bola trigger di sekitar buku)
        SphereCollider area = bookToFall.gameObject.AddComponent<SphereCollider>();
        area.isTrigger = true;
        Vector3 ls = bookToFall.lossyScale;
        float s = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        if (s < 0.0001f) s = 1f;
        area.radius = interactRadius / s;
        area.center = bookToFall.InverseTransformPoint(BookCenter());

        // Titik tujuan kamera (objek terpisah, posisinya diperbarui terus sebelum zoom)
        GameObject zp = new GameObject("BookZoomPoint");
        zoomPointTf = zp.transform;

        // ZoomView yang sama seperti di radio
        zoomView = bookToFall.gameObject.AddComponent<ZoomView>();
        zoomView.promptText = promptText;
        zoomView.heightOffset = 0.5f;
        zoomView.zoomPoint = zoomPointTf;
        zoomView.zoomFieldOfView = zoomFieldOfView;
        zoomView.controlColliders = new Collider[0];
        zoomView.playerController = playerController;
        zoomView.playerRenderers = playerRenderers != null ? playerRenderers : new Renderer[0];
        zoomView.hideDuringZoom = hideDuringZoom;
        zoomView.allowExit = true;

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
        Debug.Log("[BookFallEvent] Buku sudah mendarat dan bisa diinteract (tekan E di dekatnya).");
    }

    Vector3 BookCenter()
    {
        Renderer r = bookToFall.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.center : bookToFall.position;
    }

    // Kamera diletakkan di sisi pemain, miring ke bawah menghadap buku
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

    // Mencari tinggi lantai tepat di bawah buku. NaN = tidak ada collider di bawahnya.
    float FindFloorY(Collider bookCol, Transform shelfRoot)
    {
        Vector3 origin = bookCol.bounds.center;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        float y = float.NaN;
        foreach (RaycastHit h in hits)
        {
            if (h.collider == bookCol) continue;
            if (shelfRoot != null && h.collider.transform.IsChildOf(shelfRoot)) continue;   // badan rak diabaikan

            if (h.distance < best)
            {
                best = h.distance;
                y = h.point.y;
            }
        }
        return y;
    }

    // Keluarkan collider buku dari daftar rak (Bookshelf lama maupun ZoomView) supaya tidak dimatikan lagi
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
        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            box.center = mf.sharedMesh.bounds.center;
            box.size = mf.sharedMesh.bounds.size;
        }
        return box;
    }

    void PlayThud()
    {
        if (audioSource != null && thudSound != null)
            audioSource.PlayOneShot(thudSound);
    }
}   