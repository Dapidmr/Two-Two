using UnityEngine;
using UnityEngine.InputSystem;

public class MonocleItem : MonoBehaviour
{
    public float pickupDistance = 1.5f;
    public Transform player;              // drag Capsule

    void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame) return;

        Transform p = player != null ? player : (Camera.main != null ? Camera.main.transform : null);
        if (p == null) return;

        if (Vector3.Distance(p.position, transform.position) <= pickupDistance)
        {
            GameState.SetMonocle(true);
            Debug.Log("[MonocleItem] Monocle diambil");
            gameObject.SetActive(false);
        }
    }
}