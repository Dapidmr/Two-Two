using System.Collections.Generic;
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

    [Header("Bantuan mencari masalah")]
    public bool showStatusOnScreen = true;   // tampilkan status di pojok kiri atas layar Game

    private bool hasFallen = false;
    private bool rescued = false;
    private Rigidbody rb;
    private RadioTuner radio;
    private string status = "";

    void Start()
    {
        if (listenToRadio)
        {
            radio = FindFirstObjectByType<RadioTuner>();
            if (radio != null)
            {
                if (radio.onPoweredOff == null) radio.onPoweredOff = new UnityEvent();
                radio.onPoweredOff.AddListener(TriggerFall);
                status = "siap, menunggu radio dimatikan";
            }
            else
            {
                status = "RadioTuner tidak ditemukan di scene";
            }
        }
        else
        {
            status = "menunggu dipanggil lewat event";
        }

        if (bookToFall == null) status = "Book To Fall masih KOSONG";
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
            status = "Book To Fall masih KOSONG, drag bukunya ke slot itu";
            Debug.LogWarning("[BookFallEvent] Book To Fall masih kosong. Drag bukunya ke slot itu.");
            return;
        }
        if (hasFallen)
        {
            Debug.Log("[BookFallEvent] Buku sudah pernah dijatuhkan, panggilan ini diabaikan.");
            return;
        }

        hasFallen = true;
        status = "dipanggil untuk: " + bookToFall.name;
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

        status = "buku dijatuhkan: " + book.name + " | collider " + (bookCol.enabled ? "aktif" : "MATI")
               + " | " + (float.IsNaN(floorY) ? "TIDAK ADA lantai (collider) di bawah buku, buku akan tembus!" : "lantai di y=" + floorY.ToString("0.00"));
        Debug.Log("[BookFallEvent] " + status);

        Invoke(nameof(PlayThud), soundDelay);
    }

    // Pengaman: kalau buku sampai jatuh jauh di bawah lantai, taruh di Floor Position
    void Update()
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
            status = "buku tembus lantai, dipindah ke Floor Position";
        }
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

    void UnregisterFromBookshelf(Collider col)
    {
        foreach (Bookshelf shelf in FindObjectsByType<Bookshelf>(FindObjectsSortMode.None))
        {
            if (shelf.bookColliders == null) continue;

            List<Collider> list = new List<Collider>(shelf.bookColliders);
            if (list.Remove(col)) shelf.bookColliders = list.ToArray();
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

    void OnGUI()
    {
        if (!showStatusOnScreen) return;

        GUIStyle st = new GUIStyle(GUI.skin.label);
        st.fontSize = 22;
        st.fontStyle = FontStyle.Bold;
        st.normal.textColor = Color.yellow;

        string text = "[BookFall] " + status;
        if (hasFallen && bookToFall != null)
            text += " | tinggi buku sekarang: " + bookToFall.position.y.ToString("0.00");

        GUI.Label(new Rect(20, 20, Screen.width - 40, 40), text, st);
    }
}