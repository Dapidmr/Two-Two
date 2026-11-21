using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

// Tutupan yang bergeser terbuka saat diklik (misal tutup baterai radio).
// Pasang di objek tutupan. Objek harus punya Collider (Is Trigger tidak dicentang),
// dan collider itu dimasukkan ke Control Colliders milik ZoomView supaya hanya bisa diklik saat zoom.
//
// Klik = tekan lalu lepas tanpa banyak bergeser. Kalau mouse digeser (drag), cover TIDAK membuka
// dan radio diputar oleh ZoomView.
//
// PENTING: posisikan objek ini di Scene view PERSIS di posisi TERTUTUP (menutupi keypad).
// Itu akan dianggap sebagai posisi awal/tertutup oleh script ini.
[RequireComponent(typeof(Collider))]
public class SlidingCover : MonoBehaviour
{
    [Header("Arah geser")]
    public Vector3 slideAxis = Vector3.right;   // arah geser, dihitung dari sumbu lokal objek induknya
    public float slideFraction = 1f;            // 1 = geser sejauh lebar tutupan itu sendiri

    [Header("Perilaku")]
    public float openSpeed = 6f;                // makin besar, makin cepat bergeser
    public bool canClose = true;                // true = klik lagi untuk menutup
    public bool startOpen = false;              // pastikan ini UNCHECKED kalau mau mulai tertutup
    public float clickMoveTolerance = 8f;       // mouse bergeser lebih dari ini (pixel) = dianggap drag, bukan klik

    [Header("Debug")]
    public bool debugClicks = false;            // centang sementara: Console menampilkan kenapa klik ditolak

    [Header("Event")]
    public UnityEvent onOpened;
    public UnityEvent onClosed;

    // Hanya untuk dibaca script lain
    public bool IsOpen { get { return isOpen; } }

    private bool isOpen = false;
    private Vector3 closedLocalPos;
    private Vector3 openLocalPos;
    private Collider col;
    private ZoomView owner;      // ZoomView induk (untuk memeriksa apakah kontrol tertutup badan benda)

    private bool pressing = false;       // true = tombol ditekan di atas cover dan belum jadi drag
    private Vector2 pressStartPos;

    void Start()
    {
        col = GetComponent<Collider>();
        owner = GetComponentInParent<ZoomView>();

        // Posisi tertutup = posisi awal di Scene. Posisi terbuka dihitung sekali di sini.
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
        else
        {
            isOpen = false;
            transform.localPosition = closedLocalPos;
        }
    }

    void Update()
    {
        HandleClick();

        // Geser dengan halus ke posisi tujuan
        Vector3 target = isOpen ? openLocalPos : closedLocalPos;
        transform.localPosition = Vector3.Lerp(transform.localPosition, target, Time.deltaTime * openSpeed);

        if ((transform.localPosition - target).sqrMagnitude < 0.0000001f)
            transform.localPosition = target;
    }

    void HandleClick()
    {
        if (Mouse.current == null || Camera.main == null) return;

        // Collider mati = pemain belum zoom, jadi batalkan semua
        if (!col.enabled)
        {
            if (debugClicks && Mouse.current.leftButton.wasPressedThisFrame)
                Debug.Log("[SlidingCover] Klik ditolak: collider cover MATI. Masukkan collider ini ke Control Colliders milik ZoomView.", this);
            pressing = false;
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();

        // Tekan: catat posisi, belum membuka
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (IsMouseOverThis())
            {
                pressing = true;
                pressStartPos = mousePos;
            }
            else
            {
                pressing = false;
            }
        }

        if (!pressing) return;

        // Bergeser terlalu jauh = ini drag (memutar radio), bukan klik
        if ((mousePos - pressStartPos).magnitude > clickMoveTolerance)
        {
            pressing = false;
            return;
        }

        // Lepas tanpa bergeser = klik, baru buka/tutup
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            pressing = false;
            if (!isOpen) Open();
            else if (canClose) Close();
        }
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
        RaycastHit[] all = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

        // Buang collider milik objek INDUK (misal collider badan radio yang membungkus semuanya).
        List<RaycastHit> filtered = new List<RaycastHit>();
        foreach (var h in all)
        {
            Transform ht = h.collider.transform;
            bool isAncestor = ht != transform && transform.IsChildOf(ht);
            if (!isAncestor) filtered.Add(h);
        }
        RaycastHit[] hits = filtered.ToArray();

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

        bool isMe = closest == this;
        bool visible = owner == null || owner.IsControlVisible(col);
        bool frontmost = ClickOcclusion.IsFrontmost(col, hits);

        if (debugClicks)
        {
            string blocker = "-";
            float best = float.MaxValue;
            foreach (var h in hits)
                if (h.collider != col && h.distance < best) { best = h.distance; blocker = h.collider.name; }

            Debug.Log("[SlidingCover] kena cover: " + isMe + " | IsControlVisible: " + visible +
                      " | paling depan: " + frontmost + " | collider terdekat lain: " + blocker, this);
        }

        return isMe && visible && frontmost;
    }

    // Garis di Scene view yang menunjukkan ke mana tutupan akan bergeser (muncul saat objek dipilih)
    void OnDrawGizmosSelected()
    {
        Vector3 dirWorld = SlideDirWorld();
        Vector3 closedPos = Application.isPlaying && transform.parent != null
            ? transform.parent.TransformPoint(closedLocalPos)
            : transform.position;
        Vector3 openPos = closedPos + dirWorld * SlideDistance(dirWorld);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(closedPos, 0.02f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(closedPos, openPos);
        Gizmos.DrawWireSphere(openPos, 0.02f);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(closedPos, "CLOSED (taruh objek di sini)");
        UnityEditor.Handles.Label(openPos, "OPEN");
#endif
    }
}