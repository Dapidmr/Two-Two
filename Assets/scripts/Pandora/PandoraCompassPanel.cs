using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

// Section kompas.
// - Selalu bisa dimainkan (tidak terkunci).
// - Tahan klik lalu gerakkan mouse melingkar mengelilingi pusat kompas, jarum mengikuti mouse.
// - Saat jarum digerakkan, tulisan derajat muncul di sebelah kompas.
//   Derajat = arah kompas: 0 = atas, 90 = kanan, 180 = bawah, 270 = kiri.
// - Jarum diam selama Confirm Delay (default 1 detik) -> merah = salah, hijau = benar.
//   Kalau "Require Others Solved" dicentang, hijau/merah baru muncul setelah section lain selesai.
// Sumbu putar otomatis = arah pandang Focus Point (tegak lurus permukaan kompas).
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
    [Tooltip("Mouse terlalu dekat ke pusat (dalam piksel) diabaikan supaya jarum tidak bergetar.")]
    public float centerDeadZonePixels = 20f;
    public bool invert = false;                     // centang kalau jarum berputar berlawanan dengan mouse

    [Header("Jawaban")]
    [Tooltip("Arah jarum di posisi awal (0 = atas, 90 = kanan, searah jarum jam).")]
    public float startBearing = 337f;
    [Tooltip("Arah kompas yang benar (0 = atas, 90 = kanan, 180 = bawah, 270 = kiri).")]
    public float targetDegrees = 90f;
    [Tooltip("Selisih yang masih dianggap benar. 1 = harus spesifik.")]
    public float tolerance = 1f;
    [Tooltip("Jarum harus diam selama ini (detik) baru dicek benar atau salah.")]
    public float confirmDelay = 1f;
    [Tooltip("true = hijau/merah baru muncul setelah section lain selesai (perilaku lama). false = hijau/merah langsung muncul.")]
    public bool requireOthersSolved = false;

    [Header("Tulisan derajat")]
    public TextMeshProUGUI degreeText;              // TMP di Canvas, misal "135°"
    [Tooltip("Opsional: objek kosong di sebelah kompas. Tulisan derajat akan menempel di titik ini. Kosong = tulisan tetap di posisinya di Canvas.")]
    public Transform textAnchor;
    public Vector2 textOffsetPixels = Vector2.zero;
    public Color normalColor = Color.white;
    public Color correctColor = Color.green;
    public Color wrongColor = Color.red;

    [Header("Indikator (opsional)")]
    public Renderer indicator;
    public Color solvedColor = Color.green;

    private Quaternion startLocalRot;
    private Vector3 startLocalPos;
    private Vector3 axisParent;                     // sumbu putar dalam ruang induk jarum
    private Vector3 pivotLocal;

    private float turn = 0f;                        // total putaran jarum yang terlihat (berlawanan jarum jam = positif)
    private float lastTurn = 0f;
    private float lastChangeTime = 0f;
    private bool hasMoved = false;
    private bool evaluated = false;
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
        if (degreeText != null) degreeText.gameObject.SetActive(false);
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
        evaluated = false;
        lastTurn = turn;
        lastChangeTime = Time.time;

        // Kalau jarum sudah pernah digerakkan, tulisan derajat langsung tampil
        hasMoved = hasMoved || Mathf.Abs(turn) > 0.05f;
        if (degreeText != null) degreeText.gameObject.SetActive(hasMoved || solved);
        RefreshText(solved ? correctColor : normalColor);
    }

    public override void OnUnfocus()
    {
        focused = false;
        dragging = false;
        if (degreeText != null) degreeText.gameObject.SetActive(false);
    }

    // Arah kompas jarum: 0 = atas, 90 = kanan, 180 = bawah, 270 = kiri
    float DisplayDegrees()
    {
        return Mathf.Repeat(startBearing - turn, 360f);
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

        // --- Drag melingkar ---
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            float a;
            dragging = TryGetMouseAngle(out a);
            if (dragging) lastMouseAngle = a;
        }

        if (dragging)
        {
            float a;
            if (TryGetMouseAngle(out a))
            {
                turn += Mathf.DeltaAngle(lastMouseAngle, a);
                lastMouseAngle = a;
                ApplyTurn();
            }
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame) dragging = false;

        // --- Jarum bergerak: tulisan derajat muncul, status cek direset ---
        if (Mathf.Abs(turn - lastTurn) > 0.05f)
        {
            lastTurn = turn;
            lastChangeTime = Time.time;
            hasMoved = true;
            evaluated = false;
            if (degreeText != null) degreeText.gameObject.SetActive(true);
            RefreshText(normalColor);
        }

        // --- Jarum diam cukup lama: cek benar atau salah ---
        if (hasMoved && !evaluated && Time.time - lastChangeTime >= confirmDelay)
        {
            evaluated = true;
            Evaluate();
        }
    }

    void LateUpdate()
    {
        // Tulisan derajat menempel di sebelah kompas
        if (!focused || textAnchor == null || degreeText == null || !degreeText.gameObject.activeSelf) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 sp = cam.WorldToScreenPoint(textAnchor.position);
        if (sp.z > 0f)
            degreeText.transform.position = new Vector3(sp.x + textOffsetPixels.x, sp.y + textOffsetPixels.y, 0f);
    }

    void ApplyTurn()
    {
        float needleAngle = invert ? -turn : turn;
        Quaternion q = Quaternion.AngleAxis(needleAngle, axisParent);
        needle.localRotation = q * startLocalRot;
        if (pivot != null)
            needle.localPosition = pivotLocal + q * (startLocalPos - pivotLocal);

        RefreshText(normalColor);
    }

    void Evaluate()
    {
        // Perilaku lama (opsional): hijau/merah ditahan sampai section lain selesai
        if (requireOthersSolved && owner != null && !owner.OthersSolved(this)) return;

        float diff = Mathf.Abs(Mathf.DeltaAngle(DisplayDegrees(), targetDegrees));

        if (diff <= tolerance)
        {
            turn += Mathf.DeltaAngle(targetDegrees, DisplayDegrees());   // pas di sudut target
            lastTurn = turn;
            ApplyTurn();
            RefreshText(correctColor);
            if (indicator != null) indicator.material.color = solvedColor;
            SetSolved();
        }
        else
        {
            RefreshText(wrongColor);
        }
    }

    void RefreshText(Color color)
    {
        if (degreeText == null) return;
        degreeText.text = Mathf.RoundToInt(DisplayDegrees()) % 360 + "°";
        degreeText.color = color;
    }

    // Playmode: putar jarum ke posisi yang diinginkan, lalu klik kanan judul komponen > ini
    [ContextMenu("Pakai Derajat Sekarang Sebagai Target")]
    void UseCurrentAsTarget()
    {
        targetDegrees = Mathf.Round(DisplayDegrees()) % 360f;
        Debug.Log("[Pandora] Target Degrees = " + targetDegrees + "  (salin nilai ini ke Target Degrees setelah Stop)");
    }

    // Scene view: garis magenta = sumbu putar jarum (harus tegak lurus permukaan kompas)
    void OnDrawGizmosSelected()
    {
        if (needle == null) return;
        Gizmos.color = Color.magenta;
        Vector3 start = pivot != null ? pivot.position : needle.position;
        Gizmos.DrawLine(start, start + WorldAxis().normalized * 0.3f);

        if (textAnchor != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(textAnchor.position, 0.02f);
        }
    }
}