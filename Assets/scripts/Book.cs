using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class BookItem : MonoBehaviour
{
    [Header("Hover")]
    public float hoverLift = 0.1f;      // buku naik sedikit saat kursor di atasnya
    public float hoverSpeed = 10f;

    [Header("Referensi")]
    public Bookshelf bookshelf;          // drag rak induknya ke sini di Inspector

    [Header("Aturan")]
    public bool lockOthersOnCollect = true;  // true = hanya 1 buku yang boleh diambil

    [Header("Target")]
    public bool canBeCollected = false;      // centang HANYA di 1 buku yang boleh diambil
    public UnityEvent onWrongBook;           // opsional: dipanggil saat buku lain diklik

    [Header("Animasi Collect")]
    public float pushDistance = 0.15f;       // seberapa jauh buku terdorong ke dalam
    public float pushDuration = 0.25f;
    public float fallDuration = 1.0f;        // lama jatuh sebelum buku hilang
    public float fallDrift = 0.3f;           // laju geser ke dalam saat jatuh
    public float tiltSpeed = 120f;           // derajat/detik, buku miring saat jatuh

    [Header("Saat buku diambil")]
    public UnityEvent onCollected;       // opsional: isi nanti (suara, pesan, dll)

    private Vector3 originalPos;
    private bool isHovered = false;
    private bool locked = false;         // true = buku ini sudah tidak bisa diinteract
    private Vector2 pressPos;
    private bool pressedOnThis = false;
    private bool collecting = false;

    void Start()
    {
        originalPos = transform.position;
    }

    void Update()
    {
        if (Camera.main == null || Mouse.current == null) return;
        if (collecting) return;   // animasi collect sedang jalan, jangan ditimpa hover

        if (locked)
        {
            isHovered = false;
        }
        else
        {
            // Cari buku terdekat yang tepat di bawah kursor
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

            isHovered = (closest == this);
        }

        // Animasi hover halus
        Vector3 target = originalPos + (isHovered ? Vector3.up * hoverLift : Vector3.zero);
        transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * hoverSpeed);

        // Klik kiri
        // Klik kiri: diproses saat dilepas, dan diabaikan kalau tadi drag
        // Catat: tombol ditekan saat kursor di atas buku ini
        if (isHovered && Mouse.current.leftButton.wasPressedThisFrame)
        {
            pressedOnThis = true;
            pressPos = Mouse.current.position.ReadValue();
        }

        // Saat dilepas: collect hanya kalau ditekan di buku ini DAN mouse hampir tidak bergerak
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

    void TryCollect()
    {
        if (!canBeCollected)
        {
            onWrongBook.Invoke();
            return;
        }
        Collect();
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
        dir = -dir;
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