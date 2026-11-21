using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;

// Versi BookItem untuk buku yang berada di dalam laci (parent bisa bergerak).
// Perbedaan utama: posisi/rotasi "asli" disimpan relatif terhadap parent,
// jadi buku selalu kembali ke tempat yang benar di laci, bukan ke koordinat world lama.
[RequireComponent(typeof(Collider))]
public class BookCabin : MonoBehaviour
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
    public MejaLaci meja;

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

    [Header("Zoom Inspect")]
    public bool canZoom = false;           // centang agar scroll bisa zoom saat inspect
    public float zoomStep = 0.05f;         // perubahan jarak per notch scroll
    public float minInspectDistance = 0.25f;
    public float maxInspectDistance = 0.9f;
    public float zoomSmooth = 10f;

    public Vector3 faceLocalAxis = Vector3.up;   // sumbu lokal buku yang menghadap kamera (rebah: 0,1,0)
    public Vector3 faceLocalUp = Vector3.right;   // sumbu lokal buku (arah spine) yang diarahkan ke atas layar
    public bool faceCameraOnInspect = false;   // centang di buku diary & Hertz Code

    [Header("Buka Buku (E to read)")]
    public bool canOpen = false;
    public AnimationClip[] openClips;        // clip berawalan "Plane|" (10 clip)
    public float openDuration = 1.2f;
    public float openUp = 0.08f;             // geser ke atas, menyisakan ruang untuk teks bawah
    public float openScaleMul = 1.3f;        // diperbesar saat terbuka
    public float openRoll = 0f;              // putar searah jarum jam bila halaman miring (derajat)
    public bool notBook = false;         // true = setelah ditutup kembali ke rotasi saat "E to open", tidak berbalik ke belakang
    public float panSensitivity = 1f;    // drag saat terbuka: 1 = buku mengikuti kursor persis
    public Vector2 panLimitAbs = new Vector2(0.3f, 0.2f);   // batas geser absolut (world unit): x = kiri/kanan, y = atas/bawah

    [Header("Saat buku diambil")]
    public UnityEvent onCollected;       // opsional: isi nanti (suara, pesan, dll)

    // ====== PERUBAHAN: posisi/rotasi asli disimpan relatif terhadap parent (laci) ======
    private Vector3 originalLocalPos;
    private Quaternion originalLocalRot;
    private Vector3 startWorldPos;       // posisi world saat Start (untuk menggeser bounds)

    private Vector3 originalPos =>
        transform.parent != null ? transform.parent.TransformPoint(originalLocalPos) : originalLocalPos;
    private Quaternion originalRot =>
        transform.parent != null ? transform.parent.rotation * originalLocalRot : originalLocalRot;
    // ===================================================================================

    private bool isHovered = false;
    private bool locked = false;         // true = buku ini sudah tidak bisa diinteract
    private Vector2 pressPos;
    private bool pressedOnThis = false;
    private bool collecting = false;

    private Vector3 localCenter;           // pusat visual buku, supaya berputar di tengah
    private bool inspecting = false;
    private bool returning = false;
    private static BookCabin activeBook;   // buku yang sedang di-inspect (hanya 1 sekaligus)
    public static bool AnyInspecting => activeBook != null;
    private static BookCabin hoverOwner;   // buku yang sedang "memegang" hover
    private float bookRadius = 0f;
    private Vector3 originalScale;
    private Vector3 targetScale;
    private Bounds originalBounds;
    private bool hasBounds = false;
    private float hoverT = 0f;
    private float openT = 0f;
    private float openTarget = 0f;
    private bool readShown = false;
    private bool openedHintShown = false;
    private Quaternion openRot;
    private Renderer[] allRends;
    private Vector3 viewCenter;          // pusat visual saat ini (berubah saat buku terbuka)
    public float openBlendSpeed = 3f;    // kecepatan geser/scale kembali setelah buku menutup
    public float closeSpeedMul = 1.5f;   // animasi tutup lebih cepat dari buka
    [Range(0.1f, 0.9f)] public float openPeak = 0.48f;   // posisi clip saat buku terbuka penuh (0..1)
    public float closeSkip = 0.07f;      // lompati bagian diam di awal animasi tutup (0..1 dari clip)
    private float phase = 0f;       // posisi di clip (0..1), 0.5 = terbuka penuh
    private int bookState = 0;      // 0 tertutup, 1 membuka, 2 terbuka, 3 menutup
    private Quaternion inspectTargetRot;
    private bool settlingRot = false;
    private Quaternion preOpenRot;       // rotasi buku saat E ditekan
    private Vector2 panOffset;           // geseran x/y (ruang kamera) saat terbuka
    private float targetInspectDistance;
    private float currentInspectDistance;
    private readonly System.Collections.Generic.List<Material> queueMats = new System.Collections.Generic.List<Material>();
    private readonly System.Collections.Generic.List<int> queueOrig = new System.Collections.Generic.List<int>();

    void Start()
    {
        originalScale = transform.localScale;
        activeBook = null;
        hoverOwner = null;

        if (bookshelf == null) bookshelf = GetComponentInParent<Bookshelf>();
        if (meja == null) meja = GetComponentInParent<MejaLaci>();
        if (bookshelf == null) bookshelf = FindFirstObjectByType<Bookshelf>();
        if (bookshelf == null) Debug.LogWarning("BookCabin " + name + ": Bookshelf tidak ditemukan, dim/pan/teks inspect tidak akan jalan");

        // PERUBAHAN: simpan relatif terhadap parent
        startWorldPos = transform.position;
        if (transform.parent != null)
        {
            originalLocalPos = transform.parent.InverseTransformPoint(transform.position);
            originalLocalRot = Quaternion.Inverse(transform.parent.rotation) * transform.rotation;
        }
        else
        {
            originalLocalPos = transform.position;
            originalLocalRot = transform.rotation;
        }

        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            localCenter = transform.InverseTransformPoint(b.center);
            bookRadius = b.extents.magnitude;
            originalBounds = b;
            hasBounds = true;
            allRends = rends;
        }
    }

    // PERUBAHAN: bounds asli ikut bergeser bersama laci
    Bounds ShiftedBounds()
    {
        Bounds b = originalBounds;
        b.center += originalPos - startWorldPos;
        return b;
    }

    void Update()
    {
        if (Camera.main == null || Mouse.current == null) return;
        if (collecting) return;

        if (inspecting) { UpdateInspect(); return; }
        if (returning) { UpdateReturn(); return; }

        bool otherInspecting = activeBook != null || (bookshelf != null && bookshelf.IsInspecting);
        bool shelfActive = meja != null ? meja.IsZoomedOn(transform) : (bookshelf == null || bookshelf.IsZoomed);

        if (locked || otherInspecting || !shelfActive)
        {
            isHovered = false;
            pressedOnThis = false;
            if (hoverOwner == this) hoverOwner = null;
        }
        else
        {
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

            BookCabin closest = null;
            float closestDist = float.MaxValue;
            foreach (var h in hits)
            {
                var book = h.collider.GetComponent<BookCabin>();
                if (book != null && h.distance < closestDist)
                {
                    closest = book;
                    closestDist = h.distance;
                }
            }

            // Hysteresis: pemilik hover bertahan selama ray masih di bounds aslinya
            // dan ray tidak kena buku lain secara langsung.
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
        Vector3 basePos = originalPos;      // dihitung sekali per frame (ikut laci)
        Quaternion baseRot = originalRot;

        if (tiltOnHover && hasBounds)
        {
            hoverT = Mathf.Lerp(hoverT, isHovered ? 1f : 0f, Time.deltaTime * hoverSpeed);

            // Engsel = tepi kiri/kanan buku, diukur sejajar sumbu "right" rak
            Transform zp = (bookshelf != null) ? bookshelf.zoomPoint : null;
            Vector3 right = zp != null ? zp.right : transform.right;
            right.y = 0f; right.Normalize();

            Bounds sb = ShiftedBounds();
            float halfWidth = Mathf.Abs(Vector3.Dot(sb.extents, right));
            Vector3 pivot = sb.center + right * (hingeOnRight ? halfWidth : -halfWidth);

            float sign = hingeOnRight ? 1f : -1f;
            Quaternion q = Quaternion.AngleAxis(sign * tiltAngle * hoverT, Vector3.up);
            transform.rotation = q * baseRot;
            transform.position = pivot + q * (basePos - pivot);
        }
        else
        {
            Vector3 target = basePos + (isHovered ? Vector3.up * hoverLift : Vector3.zero);
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * hoverSpeed);
            // PERUBAHAN: rotasi ikut parent juga (kalau laci berputar)
            transform.rotation = Quaternion.Slerp(transform.rotation, baseRot, Time.deltaTime * hoverSpeed);
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
        Bounds b = ShiftedBounds();       // PERUBAHAN: ikut laci
        b.Expand(-holdShrink * 2f);       // Expand mengubah size, jadi dikali 2 untuk tiap sisi
        return b.IntersectRay(ray);
    }

    void TryCollect()
    {
        if (activeBook != null) return;   // sudah ada buku yang sedang diambil

        if (canBeCollected) Collect();    // buku target: dorong + jatuh
        else StartInspect();              // buku lain: diambil untuk dibaca
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
        // Frame buku: z = sumbu yang menghadap kamera, y = sumbu yang naik di layar
        Quaternion local = Quaternion.LookRotation(faceLocalAxis.normalized, faceLocalUp.normalized);
        // Frame layar: z = menjauhi kamera, y = atas kamera
        Quaternion view = Quaternion.LookRotation(-cam.forward, cam.up);
        return view * Quaternion.Inverse(local);
    }

    void ApplyOpenPose()
    {
        if (openClips == null) return;
        foreach (var c in openClips)
            if (c != null) c.SampleAnimation(gameObject, phase * c.length);
    }

    // Buku digambar SETELAH layar redup (queue 3100 > 3000), jadi tidak ikut gelap
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
                    if (m.renderQueue < 2450) m.renderQueue = 2450;   // tetap opaque (masuk depth prepass), tapi setelah layar redup
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
    void StartInspect()
    {
        activeBook = this;
        inspecting = true;
        float k = bookRadius > 0.0001f ? inspectRadius / bookRadius : 1f;
        targetScale = originalScale * k;
        targetInspectDistance = inspectDistance;
        currentInspectDistance = inspectDistance;
        panOffset = Vector2.zero;
        isHovered = false;
        hoverT = 0f;
        if (hoverOwner == this) hoverOwner = null;

        // Reset state buka buku
        openT = 0f;
        openTarget = 0f;
        phase = 0f;
        bookState = 0;
        viewCenter = localCenter;
        ApplyOpenPose();   // paksa pose tertutup (phase 0)
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
                    bookState = 3;                          // tutup: 50% -> 100%
                    phase = Mathf.Min(1f, phase + closeSkip);
                    if (notBook) openRot = preOpenRot;
                }
                if (bookState != 0) escPressed = false;     // selama animasi, jangan keluar inspect
            }
            else if (ePressed && bookState == 0)
            {
                preOpenRot = transform.rotation;            // simpan rotasi sebelum buka
                bookState = 1;                              // buka: 0% -> 50%
                openRot = Quaternion.AngleAxis(openRoll, -cam.forward) * FaceRot(cam);
            }

            float step = Time.deltaTime / Mathf.Max(0.01f, openDuration) * 0.5f;   // kecepatan sama seperti sebelumnya
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
                    // Pose 100% = cover belakang menghadap kamera
                    phase = 1f;
                    ApplyOpenPose();
                    Vector3 pivot = transform.TransformPoint(CurrentLocalCenter());
                    if (!notBook) transform.RotateAround(pivot, cam.up, 180f);

                    // Reset ke pose 0 (cover depan), lalu jaga pusat visual tetap di pivot
                    phase = 0f;
                    bookState = 0;
                    settlingRot = notBook;                 // notBook: putar halus kembali ke rotasi awal
                    if (notBook) inspectTargetRot = preOpenRot;
                    ApplyOpenPose();

                    Vector3 newCenterLocal = CurrentLocalCenter();
                    transform.position += pivot - transform.TransformPoint(newCenterLocal);
                    viewCenter = newCenterLocal;   // Lerp ke localCenter mulai dari sini, bukan dari nilai lama

                    prevPhase = phase;             // pose sudah diterapkan, tidak perlu lagi di bawah
                }
            }

            if (phase != prevPhase) ApplyOpenPose();

            // 0 = tertutup, 1 = terbuka penuh, dipakai untuk kunci rotasi, geser, dan scale
            openTarget = bookState == 0 ? 0f : 1f;
            openT = Mathf.MoveTowards(openT, openTarget, Time.deltaTime * openBlendSpeed);

            bool showRead = bookState == 0;
            if (showRead != readShown)
            {
                readShown = showRead;
                if (bookshelf != null) bookshelf.SetReadHint(showRead);
            }

            bool showOpened = bookState != 0;   // terbuka atau sedang membuka/menutup
            if (showOpened != openedHintShown)
            {
                openedHintShown = showOpened;
                if (bookshelf != null) bookshelf.SetOpenedHint(showOpened);
            }
        }

        if (bookState != 0)
        {
            // Terbuka: dikunci menghadap kamera
            transform.rotation = Quaternion.Slerp(transform.rotation, openRot, Time.deltaTime * inspectMoveSpeed);

            // Drag = geser x/y di bidang layar, rotasi tetap
            if (Mouse.current.leftButton.isPressed)
            {
                Camera c = Camera.main;
                Vector2 d = Mouse.current.delta.ReadValue();
                float halfH = currentInspectDistance * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float wpp = (2f * halfH / Screen.height) * panSensitivity;   // world unit per pixel
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
            targetInspectDistance = inspectDistance;   // tertutup: kembali ke jarak awal
        }
        currentInspectDistance = Mathf.Lerp(currentInspectDistance, targetInspectDistance, Time.deltaTime * zoomSmooth);

        if (bookState == 0)
            panOffset = Vector2.Lerp(panOffset, Vector2.zero, Time.deltaTime * inspectMoveSpeed);   // balik ke tengah saat tertutup

        Vector3 center = cam.position + cam.forward * currentInspectDistance
                         + cam.up * (openUp * o + panOffset.y)
                         + cam.right * panOffset.x;
        Vector3 wantCenter = bookState != 0 ? CurrentLocalCenter() : localCenter;
        viewCenter = bookState != 0
            ? wantCenter
            : Vector3.Lerp(viewCenter, wantCenter, Time.deltaTime * inspectMoveSpeed);
        Vector3 target = center - transform.TransformVector(viewCenter);
        transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * inspectMoveSpeed);

        // Esc = kembalikan ke rak (hanya kalau buku sudah tertutup)
        if (escPressed)
        {
            activeBook = null;
            inspecting = false;
            returning = true;
            readShown = false;
            if (bookshelf != null) bookshelf.EndInspect();
        }
    }

    void UpdateReturn()
    {
        // originalPos / originalRot dihitung ulang tiap frame, jadi tetap benar kalau laci bergerak
        Vector3 op = originalPos;
        Quaternion oq = originalRot;

        transform.localScale = Vector3.Lerp(transform.localScale, originalScale, Time.deltaTime * inspectMoveSpeed);
        transform.position = Vector3.Lerp(transform.position, op, Time.deltaTime * inspectMoveSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, oq, Time.deltaTime * inspectMoveSpeed);

        if (Vector3.Distance(transform.position, op) < 0.002f &&
            Quaternion.Angle(transform.rotation, oq) < 0.5f)
        {
            transform.position = op;
            transform.rotation = oq;
            transform.localScale = originalScale;
            returning = false;
            SetAboveDim(false);
        }
    }

    void Collect()
    {
        if (collecting) return;
        collecting = true;

        Debug.Log("Buku diambil: " + gameObject.name);
        onCollected.Invoke();

        // Kunci semua buku lain di parent yang sama
        if (lockOthersOnCollect && transform.parent != null)
        {
            foreach (var b in transform.parent.GetComponentsInChildren<BookCabin>())
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