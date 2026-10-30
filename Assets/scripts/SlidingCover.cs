using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

// Tutupan yang bergeser terbuka saat diklik (misal tutup baterai radio).
// Pasang di objek tutupan. Objek harus punya Collider (Is Trigger tidak dicentang),
// dan collider itu dimasukkan ke Control Colliders milik ZoomView supaya hanya bisa diklik saat zoom.
[RequireComponent(typeof(Collider))]
public class SlidingCover : MonoBehaviour
{
    [Header("Arah geser")]
    public Vector3 slideAxis = Vector3.right;   // arah geser, dihitung dari sumbu lokal objek induknya
    public float slideFraction = 1f;            // 1 = geser sejauh lebar tutupan itu sendiri

    [Header("Perilaku")]
    public float openSpeed = 6f;                // makin besar, makin cepat bergeser
    public bool canClose = true;                // true = klik lagi untuk menutup
    public bool startOpen = false;

    [Header("Event")]
    public UnityEvent onOpened;
    public UnityEvent onClosed;

    // Hanya untuk dibaca script lain
    public bool IsOpen { get { return isOpen; } }

    private bool isOpen = false;
    private Vector3 closedLocalPos;
    private Vector3 openLocalPos;
    private Collider col;

    void Start()
    {
        col = GetComponent<Collider>();

        // Posisi tertutup = posisi awal. Posisi terbuka dihitung sekali di sini.
        closedLocalPos = transform.localPosition;

        Vector3 dirWorld = SlideDirWorld();
        Vector3 openWorld = transform.position + dirWorld * SlideDistance(dirWorld);
        openLocalPos = transform.parent != null
            ? transform.parent.InverseTransformPoint(openWorld)
            : openWorld;

        if (startOpen)
        {
            isOpen = true;
            transform.localPosition = openLocalPos;
        }
    }

    void Update()
    {
        // Klik pada tutupan (hanya saat collider menyala, yaitu saat zoom)
        if (col.enabled && Mouse.current != null && Camera.main != null
            && Mouse.current.leftButton.wasPressedThisFrame && IsMouseOverThis())
        {
            if (!isOpen) Open();
            else if (canClose) Close();
        }

        // Geser dengan halus ke posisi tujuan
        Vector3 target = isOpen ? openLocalPos : closedLocalPos;
        transform.localPosition = Vector3.Lerp(transform.localPosition, target, Time.deltaTime * openSpeed);

        if ((transform.localPosition - target).sqrMagnitude < 0.0000001f)
            transform.localPosition = target;
    }

    // Bisa dipanggil dari script lain atau dari event (misal On Exit milik ZoomView)
    public void Open()
    {
        if (isOpen) return;
        isOpen = true;
        onOpened.Invoke();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        onClosed.Invoke();
    }

    Vector3 SlideDirWorld()
    {
        Vector3 d = slideAxis.sqrMagnitude > 0f ? slideAxis.normalized : Vector3.right;
        return transform.parent != null ? transform.parent.TransformDirection(d) : d;
    }

    // Jarak geser = lebar tutupan menurut arah geser, dikali Slide Fraction
    float SlideDistance(Vector3 dirWorld)
    {
        Renderer r = GetComponentInChildren<Renderer>();
        if (r == null) return slideFraction;

        Vector3 a = new Vector3(Mathf.Abs(dirWorld.x), Mathf.Abs(dirWorld.y), Mathf.Abs(dirWorld.z));
        return Vector3.Dot(r.bounds.size, a) * slideFraction;
    }

    bool IsMouseOverThis()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

        SlidingCover closest = null;
        float closestDist = float.MaxValue;
        foreach (var h in hits)
        {
            var c = h.collider.GetComponent<SlidingCover>();
            if (c != null && h.distance < closestDist)
            {
                closest = c;
                closestDist = h.distance;
            }
        }
        return closest == this;
    }

    // Garis di Scene view yang menunjukkan ke mana tutupan akan bergeser (muncul saat objek dipilih)
    void OnDrawGizmosSelected()
    {
        Vector3 dirWorld = SlideDirWorld();
        Vector3 from = transform.position;
        Vector3 to = from + dirWorld * SlideDistance(dirWorld);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(from, to);
        Gizmos.DrawWireSphere(to, 0.02f);
    }
}