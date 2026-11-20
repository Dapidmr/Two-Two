using UnityEngine;
using UnityEngine.InputSystem;

// Section kompas: jarum berputar DATAR di permukaan kompas.
// Mode melingkar (default): tahan klik lalu gerakkan mouse mengelilingi pusat kompas,
// jarum mengikuti sudut mouse. Mode lurus: geser kanan/kiri.
// Sumbu putar otomatis = arah pandang Focus Point (tegak lurus permukaan kompas).
// Benar/salahnya HANYA dicek kalau section lain sudah selesai. Dicek saat tombol mouse dilepas.
public class PandoraCompassPanel : PandoraPanel
{
    [Header("Jarum")]
    public Transform needle;                        // objek "Jarum"
    [Tooltip("true = jarum berputar tegak lurus permukaan kompas (sumbu otomatis dari Focus Point).")]
    public bool useViewAxis = true;
    [Tooltip("Dipakai hanya kalau Use View Axis dimatikan. Sumbu LOKAL jarum.")]
    public Vector3 rotateAxis = Vector3.up;
    [Tooltip("Titik pusat putar dan pusat lingkaran mouse (misal objek Poros). Kosong = posisi objek jarum.")]
    public Transform pivot;

    [Header("Kontrol")]
    [Tooltip("true = jarum mengikuti mouse yang melingkar di sekitar pusat kompas.")]
    public bool circularDrag = true;
    [Tooltip("Mouse terlalu dekat ke pusat (dalam piksel) diabaikan supaya jarum tidak bergetar.")]
    public float centerDeadZonePixels = 20f;
    public float dragSensitivity = 0.5f;            // hanya untuk mode lurus
    public bool invert = false;                     // centang kalau arah putarnya terbalik

    [Header("Jawaban")]
    public float targetAngle = 90f;                 // sudut benar (derajat dari posisi awal)
    public float tolerance = 8f;

    [Header("Indikator (opsional)")]
    public Renderer indicator;
    public Color solvedColor = Color.green;

    private Quaternion startLocalRot;
    private Vector3 startLocalPos;
    private Vector3 axisParent;                     // sumbu putar dalam ruang induk jarum
    private Vector3 pivotLocal;
    private float angle = 0f;
    private bool focused = false;
    private bool dragging = false;
    private float lastMouseAngle = 0f;

    // Sumbu putar di dunia (benda belum diputar)
    Vector3 WorldAxis()
    {
        if (useViewAxis && focusPoint != null) return -focusPoint.forward;   // mengarah ke kamera
        return needle.TransformDirection(rotateAxis.normalized);
    }

    void Awake()
    {
        if (needle == null) return;

        startLocalRot = needle.localRotation;
        startLocalPos = needle.localPosition;

        Vector3 worldAxis = WorldAxis();
        Transform p = needle.parent;
        axisParent = (p != null ? p.InverseTransformDirection(worldAxis) : worldAxis).normalized;

        if (pivot != null)
            pivotLocal = p != null ? p.InverseTransformPoint(pivot.position) : pivot.position;
    }

    public override void OnFocus()
    {
        focused = true;
        dragging = false;
    }

    public override void OnUnfocus()
    {
        focused = false;
        dragging = false;
    }

    // Sudut mouse terhadap pusat kompas di layar (derajat, berlawanan jarum jam = positif).
    // Mengembalikan false kalau mouse terlalu dekat ke pusat.
    bool TryGetMouseAngle(out float deg)
    {
        deg = 0f;
        Camera cam = Camera.main;
        if (cam == null) return false;

        Vector3 centerWorld = pivot != null ? pivot.position : needle.position;
        Vector3 sp = cam.WorldToScreenPoint(centerWorld);
        if (sp.z <= 0f) return false;

        Vector2 d = Mouse.current.position.ReadValue() - new Vector2(sp.x, sp.y);
        if (d.magnitude < centerDeadZonePixels) return false;

        deg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        return true;
    }

    void Update()
    {
        if (!focused || solved || needle == null || Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            dragging = true;
            if (circularDrag)
            {
                float a;
                if (TryGetMouseAngle(out a)) lastMouseAngle = a;
                else dragging = false;          // klik terlalu dekat pusat: abaikan
            }
        }

        if (dragging)
        {
            if (circularDrag)
            {
                float a;
                if (TryGetMouseAngle(out a))
                {
                    // Sumbu mengarah ke kamera: sudut positif = berlawanan jarum jam di layar, sama dengan sudut mouse
                    float delta = Mathf.DeltaAngle(lastMouseAngle, a);
                    angle += (invert ? -1f : 1f) * delta;
                    lastMouseAngle = a;
                    ApplyAngle();
                }
            }
            else
            {
                float dx = Mouse.current.delta.ReadValue().x;
                angle += (invert ? 1f : -1f) * dx * dragSensitivity;
                ApplyAngle();
            }
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame && dragging)
        {
            dragging = false;
            Evaluate();
        }
    }

    void ApplyAngle()
    {
        Quaternion q = Quaternion.AngleAxis(angle, axisParent);
        needle.localRotation = q * startLocalRot;
        if (pivot != null)
            needle.localPosition = pivotLocal + q * (startLocalPos - pivotLocal);
    }

    void Evaluate()
    {
        // Section lain belum selesai: pemain tidak boleh tahu benar atau salah
        if (owner != null && !owner.OthersSolved(this)) return;

        if (Mathf.Abs(Mathf.DeltaAngle(angle, targetAngle)) <= tolerance)
        {
            angle = targetAngle;
            ApplyAngle();
            if (indicator != null) indicator.material.color = solvedColor;
            SetSolved();
        }
    }

    // Playmode: putar jarum ke posisi yang diinginkan, lalu klik kanan judul komponen > ini
    [ContextMenu("Pakai Sudut Sekarang Sebagai Target")]
    void UseCurrentAsTarget()
    {
        targetAngle = angle;
        Debug.Log("[Pandora] Target angle = " + angle + "  (salin nilai ini ke Target Angle setelah Stop)");
    }

    // Scene view: garis magenta = sumbu putar jarum (harus tegak lurus permukaan kompas)
    void OnDrawGizmosSelected()
    {
        if (needle == null) return;
        Gizmos.color = Color.magenta;
        Vector3 start = pivot != null ? pivot.position : needle.position;
        Gizmos.DrawLine(start, start + WorldAxis().normalized * 0.3f);
    }
}