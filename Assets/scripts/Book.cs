using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class BookItem : MonoBehaviour
{
    [Header("Hover")]
    public float hoverLift = 0.1f;      // buku naik sedikit saat kursor di atasnya
    public float hoverSpeed = 10f;
    public float holdShrink = 0.03f;   // bounds penahan hover dikecilkan sebesar ini tiap sisi

    [Header("Hover Swing (untuk buku tumpukan bawah)")]
    public bool tiltOnHover = false;      // centang di 3 buku bawah
    public float tiltAngle = 15f;         // derajat putar horizontal
    public bool hingeOnRight = false;     // false = engsel di kiri, sisi kanan menjulang keluar

    [Header("Referensi")]
    public Bookshelf bookshelf;          // drag rak induknya ke sini di Inspector

    [Header("Aturan")]
    public bool lockOthersOnCollect = false;  // true = hanya 1 buku yang boleh diambil

    [Header("Target")]
    public bool canBeCollected = false;      // centang HANYA di 1 buku yang boleh diambil
    public UnityEvent onWrongBook;           // opsional: dipanggil saat buku lain diklik

    [Header("Animasi Collect")]
    public float pushDistance = 0.15f;       // seberapa jauh buku terdorong ke dalam
    public float pushDuration = 0.25f;
    public float fallDuration = 1.0f;        // lama jatuh sebelum buku hilang
    public float fallDrift = 0.3f;           // laju geser ke dalam saat jatuh
    public float tiltSpeed = 120f;           // derajat/detik, buku miring saat jatuh

    [Header("Inspect (buku non-target)")]
    public float inspectDistance = 0.5f;   // jarak pusat buku dari kamera
    public float inspectMoveSpeed = 8f;
    public float rotateSpeed = 0.4f;       // derajat per pixel drag
    public float inspectRadius = 0.2f;     // ukuran buku saat inspect (radius ujung terjauh, world unit)

    [Header("Saat buku diambil")]
    public UnityEvent onCollected;       // opsional: isi nanti (suara, pesan, dll)

    private Vector3 originalPos;
    private bool isHovered = false;
    private bool locked = false;         // true = buku ini sudah tidak bisa diinteract
    private Vector2 pressPos;
    private bool pressedOnThis = false;
    private bool collecting = false;

    private Quaternion originalRot;
    private Vector3 localCenter;           // pusat visual buku, supaya berputar di tengah
    private bool inspecting = false;
    private bool returning = false;
    private static BookItem activeBook;   // buku yang sedang di-inspect (hanya 1 sekaligus)
    private static BookItem hoverOwner;   // buku yang sedang "memegang" hover
    private float bookRadius = 0f;
    private Vector3 originalScale;
    private Vector3 targetScale;
    private Bounds originalBounds;
    private bool hasBounds = false;
    private float hoverT = 0f;
    void Start()
    {

        originalScale = transform.localScale;
        activeBook = null;
        hoverOwner = null;

        if (bookshelf == null) bookshelf = GetComponentInParent<Bookshelf>();
        if (bookshelf == null) bookshelf = FindFirstObjectByType<Bookshelf>();
        if (bookshelf == null) Debug.LogWarning("BookItem " + name + ": Bookshelf tidak ditemukan, dim/pan/teks inspect tidak akan jalan");

        originalPos = transform.position;
        originalRot = transform.rotation;

        var rend = GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            localCenter = transform.InverseTransformPoint(rend.bounds.center);
            bookRadius = rend.bounds.extents.magnitude;   // jarak pusat ke ujung terjauh
            originalBounds = rend.bounds;   // <- tambah
            hasBounds = true;               // <- tambah
        }
    }

    void Update()
    {
        if (Camera.main == null || Mouse.current == null) return;
        if (collecting) return;

        if (inspecting) { UpdateInspect(); return; }
        if (returning) { UpdateReturn(); return; }

        bool otherInspecting = activeBook != null || (bookshelf != null && bookshelf.IsInspecting);

        if (locked || otherInspecting)
        {
            isHovered = false;
            if (hoverOwner == this) hoverOwner = null;
        }
        else
        {
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

            BookItem closest = null;
            float closestDist = float.MaxValue;
            foreach (var h in hits)
            {
                var book = h.collider.GetComponent<BookItem>();
                if (book != null && h.distance < closestDist)
                {
                    closest = book;
                    closestDist = h.distance;
                }
            }

            // Hysteresis: kalau sudah hover, tetap hover selama ray masih kena
            // posisi ASLI buku (mencegah loop naik-turun) dan tidak ada buku lain di depan
            // Pemilik hover bertahan selama ray masih di bounds aslinya.
            // Buku lain baru boleh ambil alih setelah kursor keluar dari bounds itu.
            // Pemilik hanya menahan hover kalau ray tidak kena buku lain secara langsung
            // (kasus celah antar buku). Kalau ray kena buku lain yang BUKAN pemilik, hover pindah.
            bool hitOtherDirectly = closest != null && closest != hoverOwner;

            bool ownerHolds = hoverOwner != null
                              && hoverOwner != this
                              && !hoverOwner.locked
                              && !hitOtherDirectly
                              && hoverOwner.RayInHoldBounds(ray);

            if (ownerHolds)
            {
                isHovered = false;
            }
            else
            {
                bool stillOnMe = hoverOwner == this
                                 && closest == null
                                 && RayInHoldBounds(ray);

                isHovered = (closest == this) || stillOnMe;

                if (isHovered) hoverOwner = this;
                else if (hoverOwner == this) hoverOwner = null;
            }
        }

        // Animasi hover halus
        // Animasi hover halus
        if (tiltOnHover && hasBounds)
        {
            hoverT = Mathf.Lerp(hoverT, isHovered ? 1f : 0f, Time.deltaTime * hoverSpeed);

            // Engsel = tepi kiri/kanan buku, diukur sejajar sumbu "right" rak
            Transform zp = (bookshelf != null) ? bookshelf.zoomPoint : null;
            Vector3 right = zp != null ? zp.right : transform.right;
            right.y = 0f; right.Normalize();

            float halfWidth = Mathf.Abs(Vector3.Dot(originalBounds.extents, right));
            Vector3 pivot = originalBounds.center + right * (hingeOnRight ? halfWidth : -halfWidth);

            float sign = hingeOnRight ? 1f : -1f;
            Quaternion q = Quaternion.AngleAxis(sign * tiltAngle * hoverT, Vector3.up);
            transform.rotation = q * originalRot;
            transform.position = pivot + q * (originalPos - pivot);
        }
        else
        {
            Vector3 target = originalPos + (isHovered ? Vector3.up * hoverLift : Vector3.zero);
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * hoverSpeed);
        }

        // Catat: tombol ditekan saat kursor di atas buku ini
        if (isHovered && Mouse.current.leftButton.wasPressedThisFrame)
        {
            pressedOnThis = true;
            pressPos = Mouse.current.position.ReadValue();
        }

        // Saat dilepas: klik valid kalau ditekan di buku ini DAN mouse hampir tidak bergerak
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            bool pressedHere = pressedOnThis;
            pressedOnThis = false;

            if (pressedHere && isHovered)
            {
                float moved = Vector2.Distance(Mouse.current.position.ReadValue(), pressPos);
                float threshold = bookshelf != null ? bookshelf.dragThreshold : 5f;
                if (moved <= threshold) TryCollect();
            }
        }
    }
    bool RayInHoldBounds(Ray ray)
    {
        if (!hasBounds) return false;
        Bounds b = originalBounds;
        b.Expand(-holdShrink * 2f);   // Expand mengubah size, jadi dikali 2 untuk tiap sisi
        return b.IntersectRay(ray);
    }
    void TryCollect()
    {
        if (activeBook != null) return;   // sudah ada buku yang sedang diambil

        if (canBeCollected) Collect();    // buku target: dorong + jatuh
        else StartInspect();              // buku lain: diambil untuk dibaca
    }

    void StartInspect()
    {
        activeBook = this;
        inspecting = true;
        float k = bookRadius > 0.0001f ? inspectRadius / bookRadius : 1f;
        targetScale = originalScale * k;
        isHovered = false;
        hoverT = 0f;
        if (hoverOwner == this) hoverOwner = null;
        if (bookshelf != null) bookshelf.BeginInspect(inspectDistance, inspectRadius);
    }

    void UpdateInspect()
    {
        Transform cam = Camera.main.transform;

        // Drag mouse = putar buku
        if (Mouse.current.leftButton.isPressed)
        {
            Vector2 d = Mouse.current.delta.ReadValue();
            transform.Rotate(cam.up, -d.x * rotateSpeed, Space.World);
            transform.Rotate(cam.right, d.y * rotateSpeed, Space.World);
        }

        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * inspectMoveSpeed);
        // Pusat buku ditahan di depan kamera
        Vector3 center = cam.position + cam.forward * inspectDistance;
        Vector3 target = center - transform.TransformVector(localCenter);
        transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * inspectMoveSpeed);

        // Esc = kembalikan ke rak
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            activeBook = null;
            inspecting = false;
            returning = true;
            if (bookshelf != null) bookshelf.EndInspect();
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
        }
    }


    void Collect()
    {
        if (collecting) return;
        collecting = true;

        Debug.Log("Buku diambil: " + gameObject.name);
        onCollected.Invoke();

        // Kunci semua buku lain di rak yang sama (satu parent)
        if (lockOthersOnCollect && transform.parent != null)
        {
            foreach (var b in transform.parent.GetComponentsInChildren<BookItem>())
                if (b != this) b.locked = true;
        }

        // Matikan collider supaya tidak menghalangi raycast buku lain
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        StartCoroutine(CollectRoutine());
    }
    

    System.Collections.IEnumerator CollectRoutine()
    {
        // Arah "ke dalam rak" = arah hadap zoomPoint (diratakan horizontal)
        Vector3 dir = (bookshelf != null && bookshelf.zoomPoint != null)
            ? bookshelf.zoomPoint.forward
            : transform.forward;
        dir.y = 0f;
        dir.Normalize();

        // Fase 1: terdorong ke dalam
        Vector3 start = transform.position;
        Vector3 pushed = start + dir * pushDistance;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / pushDuration;
            transform.position = Vector3.Lerp(start, pushed, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        // Fase 2: jatuh sambil miring
        Vector3 fallDir = dir;
        Vector3 tiltAxis = Vector3.Cross(Vector3.up, fallDir);
        float vy = 0f;
        float elapsed = 0f;
        while (elapsed < fallDuration)
        {
            float dt = Time.deltaTime;
            vy += Physics.gravity.y * dt;
            transform.position += (Vector3.up * vy + fallDir * fallDrift) * dt;
            transform.Rotate(tiltAxis, tiltSpeed * dt, Space.World);
            elapsed += dt;
            yield return null;
        }

        gameObject.SetActive(false);
    }
}