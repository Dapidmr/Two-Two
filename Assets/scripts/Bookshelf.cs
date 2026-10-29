using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class Bookshelf : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI promptText;      // tulisan "Tekan E"
    public float heightOffset = 1.5f;

    [Header("Kamera")]
    public Transform zoomPoint;             // empty object di depan rak, tempat kamera berhenti
    public float cameraSpeed = 5f;

    [Header("Buku")]
    public Collider[] bookColliders;        // semua collider buku di rak ini

    [Header("Player")]
    public MonoBehaviour playerController;  // script gerak player, dimatikan saat zoom
    public Renderer[] playerRenderers;      // mesh player yang disembunyikan saat zoom

    [Header("Keluar dari zoom")]
    public bool allowExit = true;           // false = pemain tidak bisa keluar (diatur script lain, misal RadioTuner)

    private bool playerInRange = false;
    private bool isZoomed = false;
    private bool returning = false;

    private Transform cam;
    private Vector3 camOriginalPos;
    private Quaternion camOriginalRot;

    void Start()
    {
        cam = Camera.main.transform;

        // Simpan posisi awal kamera sejak Start
        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;

        SetBooksInteractable(false);
        if (promptText != null) promptText.gameObject.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
        if (promptText != null && !isZoomed) promptText.gameObject.SetActive(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = false;
        if (promptText != null) promptText.gameObject.SetActive(false);
    }

    void Update()
    {
        // Prompt "Tekan E" mengikuti posisi rak
        if (playerInRange && !isZoomed && promptText != null)
        {
            Vector3 worldPos = transform.position + Vector3.up * heightOffset;
            promptText.transform.position = Camera.main.WorldToScreenPoint(worldPos);
        }

        bool ePressed = Keyboard.current.eKey.wasPressedThisFrame;
        bool escPressed = Keyboard.current.escapeKey.wasPressedThisFrame;

        if (!isZoomed && playerInRange && ePressed)
            EnterZoom();
        else if (isZoomed && allowExit && (ePressed || escPressed))
            ExitZoom();

        // Kamera hanya digerakkan saat zoom masuk atau keluar
        if (isZoomed)
        {
            cam.position = Vector3.Lerp(cam.position, zoomPoint.position, Time.deltaTime * cameraSpeed);
            cam.rotation = Quaternion.Slerp(cam.rotation, zoomPoint.rotation, Time.deltaTime * cameraSpeed);
        }
        else if (returning)
        {
            cam.position = Vector3.Lerp(cam.position, camOriginalPos, Time.deltaTime * cameraSpeed);
            cam.rotation = Quaternion.Slerp(cam.rotation, camOriginalRot, Time.deltaTime * cameraSpeed);

            // Berhenti kalau sudah dekat
            if (Vector3.Distance(cam.position, camOriginalPos) < 0.01f)
            {
                cam.position = camOriginalPos;
                cam.rotation = camOriginalRot;
                returning = false;
            }
        }
    }

    void EnterZoom()
    {
        isZoomed = true;
        returning = false;

        // Simpan posisi kamera saat ini, supaya kembali ke posisi terakhir
        camOriginalPos = cam.position;
        camOriginalRot = cam.rotation;

        if (promptText != null) promptText.gameObject.SetActive(false);
        if (playerController != null) playerController.enabled = false;
        SetPlayerVisible(false);

        SetBooksInteractable(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ExitZoom()
    {
        returning = true;
        isZoomed = false;

        SetBooksInteractable(false);
        if (playerController != null) playerController.enabled = true;
        SetPlayerVisible(true);
        if (playerInRange && promptText != null) promptText.gameObject.SetActive(true);

        // Kalau game kamu pakai cursor terkunci (first person), aktifkan ini:
        // Cursor.lockState = CursorLockMode.Locked;
        // Cursor.visible = false;
    }

    void SetBooksInteractable(bool value)
    {
        foreach (var c in bookColliders)
            if (c != null) c.enabled = value;
    }

    void SetPlayerVisible(bool value)
    {
        foreach (var r in playerRenderers)
            if (r != null) r.enabled = value;
    }
}