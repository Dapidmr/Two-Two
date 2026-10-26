using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class BookItem : MonoBehaviour
{
    [Header("Hover")]
    public float hoverLift = 0.05f;      // buku naik sedikit saat kursor di atasnya
    public float hoverSpeed = 10f;

    [Header("Aturan")]
    public bool lockOthersOnCollect = true;  // true = hanya 1 buku yang boleh diambil

    [Header("Saat buku diambil")]
    public UnityEvent onCollected;       // opsional: isi nanti (suara, pesan, dll)

    private Vector3 originalPos;
    private bool isHovered = false;
    private bool locked = false;         // true = buku ini sudah tidak bisa diinteract

    void Start()
    {
        originalPos = transform.position;
    }

    void Update()
    {
        if (Camera.main == null || Mouse.current == null) return;

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
        if (isHovered && Mouse.current.leftButton.wasPressedThisFrame)
            Collect();
    }

    void Collect()
    {
        Debug.Log("Buku diambil: " + gameObject.name);
        onCollected.Invoke();

        // Kunci semua buku lain di rak yang sama (satu parent)
        if (lockOthersOnCollect && transform.parent != null)
        {
            foreach (var b in transform.parent.GetComponentsInChildren<BookItem>())
                if (b != this) b.locked = true;
        }

        // Buku ini hilang
        gameObject.SetActive(false);
    }
}