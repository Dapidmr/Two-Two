using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(1000)]
public class MejaLaci : MonoBehaviour
{
    [Header("Laci")]
    public string[] drawerNames = { "Cube.002", "Cube.003", "Cube.004", "Cube.005", "Cube.007", "Cube.008" };
    public Vector3 openAxis = new Vector3(0, 0, 1); // sumbu lokal laci
    public float openDistance = 0.4f;               // negatif = arah sebaliknya
    public float speed = 2f;
    public float maxDistance = 3f;                  // jarak maksimal interaksi

    [Header("Zoom (urutan SAMA dengan Drawer Names)")]
    public Transform[] zoomPoints;                  // 1 empty per laci, di atas laci. Kosong = laci buka tanpa zoom
    public float cameraSpeed = 5f;

    class Drawer
    {
        public Transform t;
        public Vector3 closed, open, current;
        public bool isOpen;
        public Transform zoomPoint;
    }

    readonly Dictionary<Transform, Drawer> drawers = new Dictionary<Transform, Drawer>();
    Drawer hovered;
    GUIStyle style, shadow;

    // ===== zoom =====
    Drawer zoomedDrawer;
    bool returning;
    bool prevInspecting;
    Transform cam;
    Vector3 camOriginalPos;
    Quaternion camOriginalRot;
    MonoBehaviour playerController;
    Rigidbody playerRb;

    public bool IsZoomed => zoomedDrawer != null;

    // true kalau kamera sedang zoom ke laci yang menjadi parent dari t (dipakai BookCabin)
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

            if (t.GetComponent<Collider>() == null) t.gameObject.AddComponent<BoxCollider>();

            drawers[t] = new Drawer
            {
                t = t,
                closed = t.localPosition,
                open = t.localPosition + openAxis.normalized * openDistance,
                current = t.localPosition,
                zoomPoint = (zoomPoints != null && i < zoomPoints.Length) ? zoomPoints[i] : null
            };
        }

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            playerController = p.GetComponent<FirstPersonController>();
            playerRb = p.GetComponent<Rigidbody>();
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (Camera.main != null) cam = Camera.main.transform;

        // blokir E/Esc saat buku sedang di-inspect (dan 1 frame sesudahnya, supaya Esc yang sama tidak ikut menutup laci)
        bool anyInsp = BookCabin.AnyInspecting;
        bool block = anyInsp || prevInspecting;
        prevInspecting = anyInsp;

        if (zoomedDrawer != null)
        {
            hovered = null;

            if (!block && kb != null && (kb.eKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
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
            if (cam != null)
            {
                cam.position = Vector3.Lerp(cam.position, camOriginalPos, Time.deltaTime * cameraSpeed);
                cam.rotation = Quaternion.Slerp(cam.rotation, camOriginalRot, Time.deltaTime * cameraSpeed);

                if (Vector3.Distance(cam.position, camOriginalPos) < 0.01f &&
                    Quaternion.Angle(cam.rotation, camOriginalRot) < 0.5f)
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
            // kursor terkunci -> tengah layar, kursor bebas -> posisi mouse
            Vector2 screenPos = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : Mouse.current.position.ReadValue();

            Ray ray = c.ScreenPointToRay(screenPos);
            var hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (IsPlayer(hit.transform)) continue; // lewati badan player

                // naik ke parent sampai ketemu laci (supaya BookMonocle dkk tetap terhitung)
                for (var t = hit.transform; t != null; t = t.parent)
                    if (drawers.TryGetValue(t, out var d)) { hovered = d; break; }

                break; // benda pertama selain player yang kena
            }
        }

        if (hovered != null && kb != null && kb.eKey.wasPressedThisFrame)
        {
            if (!hovered.isOpen && hovered.zoomPoint != null)
            {
                hovered.isOpen = true;
                EnterZoom(hovered);
            }
            else
            {
                hovered.isOpen = !hovered.isOpen;   // laci tanpa zoom point: perilaku lama
            }
        }
    }

    void EnterZoom(Drawer d)
    {
        if (cam == null) return;

        zoomedDrawer = d;
        returning = false;
        hovered = null;

        // simpan posisi kamera sekarang, supaya kembali ke sini
        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;

        if (playerController != null) playerController.enabled = false;   // OnDisable melepas kursor
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
        // player diaktifkan lagi di FinishExit(), setelah kamera sampai
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
        if (playerController != null) playerController.enabled = true;     // OnEnable mengunci kursor lagi
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

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
            shadow = new GUIStyle(style);
            shadow.normal.textColor = Color.black;
        }

        // Saat zoom: teks tetap di bawah layar, hilang saat buku sedang di-inspect
        if (zoomedDrawer != null)
        {
            if (BookCabin.AnyInspecting) return;
            DrawLabel("E to close", new Rect(Screen.width * 0.5f - 100, Screen.height - 80, 200, 40));
            return;
        }

        if (hovered == null || Mouse.current == null) return;

        string text = hovered.isOpen ? "E to close" : "E to open";

        Vector2 p = Cursor.lockState == CursorLockMode.Locked
            ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            : new Vector2(Mouse.current.position.ReadValue().x, Screen.height - Mouse.current.position.ReadValue().y);

        DrawLabel(text, new Rect(p.x - 100, p.y + 25, 200, 40));
    }

    void DrawLabel(string text, Rect rect)
    {
        GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, shadow);
        GUI.Label(rect, text, style);
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