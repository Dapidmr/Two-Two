using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BookAnimPlayer : MonoBehaviour
{
    [Header("Cover depan (diisi otomatis kalau Auto Pick Cover aktif)")]
    public Transform frontCover;

    [Header("Lembar")]
    public Transform hingePoint;                  // drag empty01 (posisi engsel / spine)
    public float flipAngle = 180f;                // besar sudut saja, arah dihitung otomatis
    public float flipSpeed = 300f;

    [Header("Sisi pemain (diisi otomatis)")]
    public Transform viewPoint;                   // kosong = cari objek "ZoomPoint", lalu Main Camera
    public bool autoPickCover = true;             // cover yang menghadap pemain = cover depan
    public bool invertFront = false;              // centang kalau yang terbuka malah sisi belakang
    public bool invertDirection = false;          // centang kalau halaman berayun menembus tumpukan

    [Header("Otomatis")]
    public bool autoDetect = true;
    public bool logInfo = true;

    [Header("Manual (hanya dipakai kalau Auto Detect mati)")]
    public Vector3 flipAxis = Vector3.forward;
    public Vector3 stackAxis = Vector3.up;
    public bool reverseOrder = false;

    [Header("Kontrol")]
    public bool requireZoom = true;               // WAJIB dicentang

    private ZoomView zoomView;
    private readonly List<Transform> pages = new List<Transform>();
    private Transform[] hinges;
    private float[] current;
    private float[] target;
    private int step = 0;
    private bool ready = false;

    private Vector3 useAxis;
    private float useAngle;

    void Start()
    {
        foreach (Transform c in transform)
            if (c.name.StartsWith("Plane")) pages.Add(c);

        Vector3 pivotWorld = hingePoint != null ? hingePoint.position : transform.position;
        Vector3 pivotLocal = transform.InverseTransformPoint(pivotWorld);

        Vector3 front = stackAxis.sqrMagnitude > 0.0001f ? stackAxis.normalized : Vector3.up;
        useAxis = flipAxis.sqrMagnitude > 0.0001f ? flipAxis.normalized : Vector3.forward;
        float sign = flipAngle < 0f ? -1f : 1f;
        float mag = Mathf.Abs(flipAngle);

        List<Vector3> centers = new List<Vector3>();
        foreach (Transform p in pages) centers.Add(transform.InverseTransformPoint(Center(p)));

        if (autoDetect && pages.Count > 1)
        {
            Vector3 centroid = Vector3.zero;
            foreach (Vector3 c in centers) centroid += c;
            centroid /= centers.Count;

            // 1) Arah tumpukan = arah antara dua halaman yang paling berjauhan
            float best = 0f;
            Vector3 dir = Vector3.zero;
            for (int i = 0; i < centers.Count; i++)
                for (int j = i + 1; j < centers.Count; j++)
                {
                    Vector3 d = centers[j] - centers[i];
                    if (d.magnitude > best) { best = d.magnitude; dir = d; }
                }

            if (best > 1e-5f)
            {
                front = dir.normalized;

                // "Depan" = sisi yang menghadap pemain
                Vector3 viewLocal = transform.InverseTransformPoint(GetViewerPosition());
                if (Vector3.Dot(viewLocal - centroid, front) < 0f) front = -front;
            }
            if (invertFront) front = -front;

            // 2) Pilih cover yang menghadap pemain
            if (autoPickCover)
            {
                Transform bestCover = null;
                float bestDot = float.MinValue;
                foreach (string nm in new[] { "Cover L", "Cover R" })
                {
                    Transform c = FindDeep(nm);
                    if (c == null) continue;
                    float d = Vector3.Dot(transform.InverseTransformPoint(Center(c)) - centroid, front);
                    if (d > bestDot) { bestDot = d; bestCover = c; }
                }
                if (bestCover != null) frontCover = bestCover;
            }

            // 3) Sumbu engsel = sisi terpanjang dari mesh Spine
            Transform spine = FindDeep("Spine");
            MeshFilter mf = spine != null ? spine.GetComponent<MeshFilter>() : null;
            if (mf != null && mf.sharedMesh != null)
            {
                Vector3 size = Vector3.Scale(mf.sharedMesh.bounds.size, spine.lossyScale);
                Vector3 localAxis = Vector3.right;
                float m = Mathf.Abs(size.x);
                if (Mathf.Abs(size.y) > m) { m = Mathf.Abs(size.y); localAxis = Vector3.up; }
                if (Mathf.Abs(size.z) > m) { localAxis = Vector3.forward; }

                Vector3 a = transform.InverseTransformDirection(spine.TransformDirection(localAxis)).normalized;
                a -= Vector3.Project(a, front);
                if (a.sqrMagnitude > 0.25f) useAxis = a.normalized;
                else Debug.LogWarning("[BookAnimPlayer] Sumbu Spine tidak cocok, pakai Flip Axis manual.");
            }

            // 4) Arah putar: tepi bebas halaman bergerak ke sisi depan (menuju pemain)
            Vector3 f = centroid - pivotLocal;
            f -= Vector3.Project(f, useAxis);
            if (f.sqrMagnitude > 1e-8f)
                sign = Vector3.Dot(Vector3.Cross(useAxis, f), front) < 0f ? -1f : 1f;
            else
                Debug.LogWarning("[BookAnimPlayer] Hinge Point terlalu dekat ke halaman, arah mungkin salah.");
        }

        if (invertDirection) sign = -sign;
        useAngle = sign * mag;

        pages.Sort((a, b) =>
        {
            float sa = Vector3.Dot(transform.InverseTransformPoint(Center(a)), front);
            float sb = Vector3.Dot(transform.InverseTransformPoint(Center(b)), front);
            return sb.CompareTo(sa);
        });
        if (!autoDetect && reverseOrder) pages.Reverse();

        if (frontCover != null) pages.Insert(0, frontCover);

        if (logInfo)
        {
            Debug.Log("[BookAnimPlayer] cover depan=" + (frontCover != null ? frontCover.name : "-") +
                      " depan=" + front + " engsel=" + useAxis + " sudut=" + useAngle);
            for (int i = 0; i < pages.Count; i++)
                Debug.Log("[BookAnimPlayer] urutan " + i + " = " + pages[i].name);
        }

        int n = pages.Count;
        hinges = new Transform[n];
        current = new float[n];
        target = new float[n];

        for (int i = 0; i < n; i++)
        {
            GameObject h = new GameObject("PageHinge_" + i);
            h.transform.SetParent(transform, false);
            h.transform.position = pivotWorld;
            h.transform.localRotation = Quaternion.identity;
            h.transform.localScale = Vector3.one;
            hinges[i] = h.transform;
            pages[i].SetParent(hinges[i], true);
        }
        ready = true;
    }

    Vector3 GetViewerPosition()
    {
        if (viewPoint != null) return viewPoint.position;
        GameObject z = GameObject.Find("ZoomPoint");
        if (z != null) return z.transform.position;
        if (Camera.main != null) return Camera.main.transform.position;
        return transform.position;
    }

    Transform FindDeep(string n)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == n) return t;
        return null;
    }

    Vector3 Center(Transform t)
    {
        Renderer r = t.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.center : t.position;
    }

    bool CanControl()
    {
        if (!requireZoom) return true;
        if (zoomView == null) zoomView = GetComponent<ZoomView>();
        return zoomView != null && zoomView.IsZoomed;
    }

    void Update()
    {
        if (!ready) return;

        if (CanControl() && Keyboard.current != null)
        {
            var k = Keyboard.current;
            if (k.rightArrowKey.wasPressedThisFrame && step < pages.Count)
            {
                target[step] = useAngle;
                step++;
            }
            if (k.leftArrowKey.wasPressedThisFrame && step > 0)
            {
                step--;
                target[step] = 0f;
            }
        }

        for (int i = 0; i < pages.Count; i++)
        {
            current[i] = Mathf.MoveTowards(current[i], target[i], flipSpeed * Time.deltaTime);
            hinges[i].localRotation = Quaternion.AngleAxis(current[i], useAxis);
        }
    }
}