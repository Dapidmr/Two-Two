using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class RadioKnob : MonoBehaviour
{
    [Range(0f, 1f)]
    public float value = 0f;             // posisi knob, 0 sampai 1

    [Header("Kontrol")]
    public float stepSize = 0.005f;      // sekali tekan panah = geser segini (0.005 = 0.1 MHz di rentang 88-108)
    public float holdDelay = 0.3f;       // tahan panah lebih dari ini (detik) = gerak cepat
    public float keySpeed = 0.35f;       // kecepatan gerak cepat (nilai per detik)
    public float maxAngle = 270f;        // putaran total dari 0 ke 1

    [Header("Rotasi")]
    public Vector3 rotationAxis = Vector3.forward;  // sumbu putar (lokal)

    [Header("Penanda knob terpilih")]
    public float selectedScale = 1.1f;   // knob terpilih membesar sedikit

    // Knob yang sedang dipilih (dipakai bersama semua knob)
    private static RadioKnob selected;

    private Quaternion startRot;
    private Vector3 startScale;
    private Collider col;
    private float holdTimer = 0f;
    private bool started = false;

    void OnEnable()
    {
        if (selected == null) selected = this;
    }

    void OnDisable()
    {
        if (selected == this) selected = null;
    }

    void Start()
    {
        col = GetComponent<Collider>();
        startRot = transform.localRotation;
        startScale = transform.localScale;
        ApplyRotation();
        started = true;
    }

    // Dipakai script lain untuk menetapkan posisi knob
    public void SetValue(float v)
    {
        value = Mathf.Clamp01(v);
        if (started) ApplyRotation();
    }

    void Update()
    {
        if (Camera.main == null || Mouse.current == null || Keyboard.current == null) return;

        // Collider mati = pemain belum zoom ke radio, jadi knob tidak boleh diinteract
        if (col.enabled)
        {
            // Klik knob = pilih knob itu
            if (Mouse.current.leftButton.wasPressedThisFrame && IsMouseOverThisKnob())
                selected = this;

            if (selected == this)
            {
                var kb = Keyboard.current;
                bool rightDown = kb.rightArrowKey.wasPressedThisFrame;
                bool leftDown = kb.leftArrowKey.wasPressedThisFrame;
                bool rightHeld = kb.rightArrowKey.isPressed;
                bool leftHeld = kb.leftArrowKey.isPressed;

                if (rightDown || leftDown)
                {
                    // Sekali tekan: geser tepat satu langkah (dirapikan ke kelipatan langkah)
                    int dir = rightDown ? 1 : -1;
                    value = Mathf.Clamp01(Snap(value) + dir * stepSize);
                    holdTimer = 0f;
                    ApplyRotation();
                }
                else if (rightHeld != leftHeld)
                {
                    // Ditahan: setelah jeda, gerak cepat
                    holdTimer += Time.deltaTime;
                    if (holdTimer > holdDelay)
                    {
                        float dir = rightHeld ? 1f : -1f;
                        value = Mathf.Clamp01(value + dir * keySpeed * Time.deltaTime);
                        ApplyRotation();
                    }
                }
                else
                {
                    holdTimer = 0f;
                }
            }
        }

        // Knob terpilih membesar sedikit
        float s = (selected == this && col.enabled) ? selectedScale : 1f;
        transform.localScale = Vector3.Lerp(transform.localScale, startScale * s, Time.deltaTime * 12f);
    }

    float Snap(float v)
    {
        if (stepSize <= 0f) return v;
        return Mathf.Round(v / stepSize) * stepSize;
    }

    bool IsMouseOverThisKnob()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

        RadioKnob closest = null;
        float closestDist = float.MaxValue;
        foreach (var h in hits)
        {
            var k = h.collider.GetComponent<RadioKnob>();
            if (k != null && h.distance < closestDist)
            {
                closest = k;
                closestDist = h.distance;
            }
        }
        return closest == this;
    }

    void ApplyRotation()
    {
        transform.localRotation = startRot * Quaternion.AngleAxis(value * maxAngle, rotationAxis);
    }
}   