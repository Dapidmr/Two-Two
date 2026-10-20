using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class Interactable : MonoBehaviour
{
    public TextMeshProUGUI promptText;  // tulisan "Tekan E"
    public GameObject popupImage;       // gambar yang muncul saat tekan E
    public float heightOffset = 1.5f;

    private bool playerInRange = false;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            if (promptText != null)
                promptText.gameObject.SetActive(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            if (promptText != null)
                promptText.gameObject.SetActive(false);
            if (popupImage != null)
                popupImage.SetActive(false); // gambar ikut hilang saat menjauh
        }
    }

    void Update()
    {
        if (!playerInRange) return;

        if (promptText != null && Camera.main != null)
        {
            Vector3 worldPos = transform.position + Vector3.up * heightOffset;
            promptText.transform.position = Camera.main.WorldToScreenPoint(worldPos);
        }

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            Interact();
        }
    }

    void Interact()
    {
        if (popupImage != null)
            popupImage.SetActive(!popupImage.activeSelf); // E: tampil/sembunyi
    }
}