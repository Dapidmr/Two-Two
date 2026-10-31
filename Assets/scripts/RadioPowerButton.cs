using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class RadioPowerButton : MonoBehaviour
{
    public RadioTuner tuner;             // kosongkan kalau RadioTuner ada di induk objek ini

    private Collider col;

    void Start()
    {
        col = GetComponent<Collider>();
        if (tuner == null) tuner = GetComponentInParent<RadioTuner>();
    }

    void Update()
    {
        if (Camera.main == null || Mouse.current == null) return;

        // Collider mati = pemain belum zoom, jadi tombol tidak bisa ditekan
        if (col.enabled && Mouse.current.leftButton.wasPressedThisFrame && IsMouseOverThisButton())
        {
            if (tuner != null) tuner.PressPower();
        }
    }

    bool IsMouseOverThisButton()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

        RadioPowerButton closest = null;
        float closestDist = float.MaxValue;
        foreach (var h in hits)
        {
            var b = h.collider.GetComponent<RadioPowerButton>();
            if (b != null && h.distance < closestDist)
            {
                closest = b;
                closestDist = h.distance;
            }
        }
        return closest == this;
    }
}