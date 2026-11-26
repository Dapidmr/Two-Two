using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(1000)]
public class LaciNakas : MonoBehaviour
{
    [Header("Laci")]
    // GANTI sesuai nama laci di Hierarchy (2 laci)
    public string[] drawerNames = { "Cube.002", "Cube.004" };
    public Vector3 openAxis = new Vector3(0, 0, 1); // sumbu lokal laci
    public float openDistance = 0.3f;               // negatif = arah sebaliknya
    public float speed = 2f;
    public float maxDistance = 3f;                  // jarak maksimal interaksi

    [Header("Zoom (urutan SAMA dengan Drawer Names)")]
    public Transform[] zoomPoints;                  // 1 empty per laci. Kosong = laci buka tanpa zoom
    public float cameraSpeed = 5f;
    public bool detachZoomPoints = true;            // lepas zoom point dari parent supaya tidak kena scale/rotasi
    public float returnTimeout = 2f;                // batas waktu kamera kembali, supaya player tidak macet

    [Header("Tulisan petunjuk")]
    public string openText = "Press E to Open";
    public string closeText = "Press E to Close";
    public string backText = "Press Esc to back";

    class Drawer
    {
        public Transform t;
        public Vector3 closed, open, current;
        public bool isOpen;
        public Transform zoomPoint;
    }

    readonly Dictionary<Transform, Drawer> drawers = new Dictionary<Transform, Drawer>();
    Drawer hovered;

    // ===== zoom =====
    Drawer zoomedDrawer;
    bool returning;
    float returnTimer;
    bool prevInspecting;
    Transform cam;
    Vector3 camOriginalPos;
    Quaternion camOriginalRot;
    MonoBehaviour playerController;
    Rigidbody playerRb;

    public bool IsZoomed => zoomedDrawer != null;

    public bool IsZoomedOn(Transform t)
    {
        return zoomedDrawer != null && t != null && t.IsChildOf(zoomedDrawer.t);
    }

    void Start()
    {
        for (int i = 0; i < drawerNames.Length; i++)
        {
            var t = FindDeep(transform, drawerNames[i]);
            if (t == null) { Debug.LogWarning("Laci tidak ditemukan: " + drawerNames[i]); continue; }

            // BoxCollider dari ukuran mesh (tidak peduli arah normal, beda dengan MeshCollider)
            foreach (var old in t.GetComponents<Collider>()) Destroy(old);
            var mf = t.GetComponent<MeshFilter>();
            var bc = t.gameObject.AddComponent<BoxCollider>();
            if (mf != null && mf.sharedMesh != null)
            {
                bc.center = mf.sharedMesh.bounds.center;
                bc.size = mf.sharedMesh.bounds.size;
            }
            else
            {
                Debug.LogWarning("Laci " + t.name + " tidak punya mesh");
            }

            var zp = (zoomPoints != null && i < zoomPoints.Length) ? zoomPoints[i] : null;

            drawers[t] = new Drawer
            {
                t = t,
                closed = t.localPosition,
                open = t.localPosition + openAxis.normalized * openDistance,
                current = t.localPosition,
                zoomPoint = zp
            };

            if (detachZoomPoints && zp != null)
            {
                Vector3 pos = zp.position;
                Quaternion rot = zp.rotation;
                zp.SetParent(null, true);
                zp.localScale = Vector3.one;
                zp.SetPositionAndRotation(pos, rot);
            }
        }

        var pl = GameObject.FindGameObjectWithTag("Player");
        if (pl != null)
        {
            playerController = pl.GetComponent<FirstPersonController>();
            playerRb = pl.GetComponent<Rigidbody>();
        }
    }

    // kotak hijau = collider laci, merah = laci yang sedang di-hover (hanya terlihat dengan Gizmos menyala)
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        foreach (var d in drawers.Values)
        {
            var col = d.t.GetComponent<Collider>();
            if (col == null) continue;
            Gizmos.color = (d == hovered) ? Color.red : Color.green;
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (Camera.main != null) cam = Camera.main.transform;

        // blokir E/Esc saat buku sedang di-inspect (dan 1 frame sesudahnya)
        bool anyInsp = BookCabin.AnyInspecting;
        bool block = anyInsp || prevInspecting;
        prevInspecting = anyInsp;

        if (zoomedDrawer != null)
        {
            hovered = null;

            // tulisan "Press Esc to back" di tengah bawah layar (hilang saat buku sedang di-inspect)
            if (!anyInsp) HintUI.ShowBottom(backText);

            if (!block && kb != null && kb.escapeKey.wasPressedThisFrame)
                ExitZoom();

            if (zoomedDrawer != null && cam != null)
            {
                var zp = zoomedDrawer.zoomPoint;
                cam.position = Vector3.Lerp(cam.position, zp.position, Time.deltaTime * cameraSpeed);
                cam.rotation = Quaternion.Slerp(cam.rotation, zp.rotation, Time.deltaTime * cameraSpeed);
            }
            return;
        }

        if (returning)
        {
            hovered = null;
            returnTimer += Time.deltaTime;
            if (cam != null)
            {
                cam.position = Vector3.Lerp(cam.position, camOriginalPos, Time.deltaTime * cameraSpeed);
                cam.rotation = Quaternion.Slerp(cam.rotation, camOriginalRot, Time.deltaTime * cameraSpeed);

                bool arrived = Vector3.Distance(cam.position, camOriginalPos) < 0.01f &&
                               Quaternion.Angle(cam.rotation, camOriginalRot) < 0.5f;
                if (arrived || returnTimer >= returnTimeout)
                    FinishExit();
            }
            else FinishExit();
            return;
        }

        // ===== mode normal: hover + E =====
        hovered = null;
        var c = Camera.main;
        if (c != null && Mouse.current != null)
        {
            Vector2 screenPos = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : Mouse.current.position.ReadValue();

            Ray ray = c.ScreenPointToRay(screenPos);
            var hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (IsPlayer(hit.transform)) continue;
                if (hit.collider.GetComponentInParent<BookCabin>() != null) continue;

                // naik ke parent sampai ketemu laci
                for (var t = hit.transform; t != null; t = t.parent)
                    if (drawers.TryGetValue(t, out var d)) { hovered = d; break; }

                break; // benda pertama selain player dan buku yang kena
            }
        }

        if (hovered != null)
            HintUI.ShowAim(hovered.isOpen ? closeText : openText);

        if (hovered != null && kb != null && kb.eKey.wasPressedThisFrame)
        {
            if (!hovered.isOpen && hovered.zoomPoint != null)
            {
                hovered.isOpen = true;
                EnterZoom(hovered);
            }
            else
            {
                hovered.isOpen = !hovered.isOpen;   // laci tanpa zoom point: buka/tutup biasa
            }
        }
    }

    void EnterZoom(Drawer d)
    {
        if (cam == null) return;

        zoomedDrawer = d;
        returning = false;
        hovered = null;

        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;

        if (playerController != null) playerController.enabled = false;
        if (playerRb != null)
        {
            playerRb.linearVelocity = Vector3.zero;
            playerRb.isKinematic = true;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ExitZoom()
    {
        if (zoomedDrawer != null) zoomedDrawer.isOpen = false;
        zoomedDrawer = null;
        returning = true;
        returnTimer = 0f;
    }

    void FinishExit()
    {
        if (cam != null)
        {
            cam.position = camOriginalPos;
            cam.rotation = camOriginalRot;
        }
        returning = false;

        if (playerRb != null) playerRb.isKinematic = false;
        if (playerController != null) playerController.enabled = true;
    }

    // gerak laci di LateUpdate supaya tidak ditimpa Animator/script lain
    void LateUpdate()
    {
        foreach (var d in drawers.Values)
        {
            d.current = Vector3.MoveTowards(d.current, d.isOpen ? d.open : d.closed, speed * Time.deltaTime);
            d.t.localPosition = d.current;
        }
    }

    static bool IsPlayer(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.CompareTag("Player")) return true;
        return false;
    }

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform c in parent)
        {
            if (c.name == name) return c;
            var r = FindDeep(c, name);
            if (r) return r;
        }
        return null;
    }
}