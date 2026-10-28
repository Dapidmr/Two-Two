using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class RadioKnob : MonoBehaviour
{
    [Range(0f, 1f)]
    public float value = 0f;             // posisi knob, 0 sampai 1

    [Header("Kontrol")]
    public float sensitivity = 0.004f;   // seberapa cepat knob berputar per pixel mouse
    public float maxAngle = 270f;        // putaran total dari 0 ke 1

    [Header("Rotasi")]
    public Vector3 rotationAxis = Vector3.forward;  // sumbu putar (lokal). Ganti kalau arah putarnya salah.

    private Quaternion startRot;
    private bool dragging = false;
    private Collider col;

    void Start()
    {
        col = GetComponent<Collider>();
        startRot = transform.localRotation;
        ApplyRotation();
    }

    void Update()
    {
        if (Camera.main == null || Mouse.current == null) return;

        // Mulai drag kalau klik tepat di knob ini
        if (Mouse.current.leftButton.wasPressedThisFrame && IsMouseOverThisKnob())
            dragging = true;

        if (Mouse.current.leftButton.wasReleasedThisFrame)
            dragging = false;

        if (dragging)
        {
            float dx = Mouse.current.delta.ReadValue().x;
            value = Mathf.Clamp01(value + dx * sensitivity);
            ApplyRotation();
        }
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