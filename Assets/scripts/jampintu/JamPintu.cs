using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(1000)]
public class JamPintu : MonoBehaviour
{
    [Header("Pintu")]
    // GANTI sesuai nama objek pintu di Hierarchy (boleh lebih dari 1, misal pintu kiri & kanan)
    public string[] doorNames = { "Pintu" };
    public Vector3 rotateAxis = new Vector3(0, 1, 0); // sumbu lokal engsel
    public float openAngle = 90f;                     // negatif = arah sebaliknya
    public float speed = 120f;                        // derajat per detik
    public float maxDistance = 3f;                    // jarak maksimal interaksi

    [Header("Tulisan petunjuk")]
    public string openText = "Press E to Open";
    public string closeText = "Press E to Close";

    class Door
    {
        public Transform t;
        public Quaternion closed, open;
        public bool isOpen;
    }

    readonly Dictionary<Transform, Door> doors = new Dictionary<Transform, Door>();
    Door hovered;

    void Start()
    {
        foreach (var n in doorNames)
        {
            var t = FindDeep(transform, n);
            if (t == null) { Debug.LogWarning("Pintu tidak ditemukan: " + n); continue; }

            // BoxCollider dari ukuran mesh (tidak peduli arah normal)
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
                Debug.LogWarning("Pintu " + t.name + " tidak punya mesh");
            }

            doors[t] = new Door
            {
                t = t,
                closed = t.localRotation,
                open = t.localRotation * Quaternion.AngleAxis(openAngle, rotateAxis.normalized)
            };
        }
    }

    void Update()
    {
        hovered = null;
        var c = Camera.main;
        if (c == null || Mouse.current == null) return;

        // kursor terkunci -> tengah layar, kursor bebas -> posisi mouse
        Vector2 screenPos = Cursor.lockState == CursorLockMode.Locked
            ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            : Mouse.current.position.ReadValue();

        Ray ray = c.ScreenPointToRay(screenPos);
        var hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (IsPlayer(hit.transform)) continue;

            // naik ke parent sampai ketemu pintu
            for (var t = hit.transform; t != null; t = t.parent)
                if (doors.TryGetValue(t, out var d)) { hovered = d; break; }

            break; // benda pertama selain player yang kena
        }

        if (hovered != null)
            HintUI.ShowAim(hovered.isOpen ? closeText : openText);

        var kb = Keyboard.current;
        if (hovered != null && kb != null && kb.eKey.wasPressedThisFrame)
            hovered.isOpen = !hovered.isOpen;
    }

    // gerak pintu di LateUpdate supaya tidak ditimpa Animator/script lain
    void LateUpdate()
    {
        foreach (var d in doors.Values)
        {
            var target = d.isOpen ? d.open : d.closed;
            d.t.localRotation = Quaternion.RotateTowards(d.t.localRotation, target, speed * Time.deltaTime);
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