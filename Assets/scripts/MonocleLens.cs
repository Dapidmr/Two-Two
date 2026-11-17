using UnityEngine;
using UnityEngine.InputSystem;

public class MonocleLens : MonoBehaviour
{
    public RectTransform lensUI;          // lingkaran di Canvas
    public float radiusUV = 0.15f;
    public float grabRadius = 90f;

    private PageLensPage[] pages;
    private ZoomView zoomView;
    private bool dragging = false;
    private bool wasActive = false;

    void Start()
    {
        pages = GetComponentsInChildren<PageLensPage>(true);
        if (lensUI != null) lensUI.gameObject.SetActive(false);
    }

    void Update()
    {
        if (zoomView == null) zoomView = GetComponent<ZoomView>();
        bool active = lensUI != null && zoomView != null && zoomView.IsZoomed;

        Debug.Log($"[MonocleLens] monocle={GameState.hasMonocle} zoom={(zoomView != null && zoomView.IsZoomed)} ui={(lensUI != null)}");
        if (!active)
        {
            if (wasActive) Hide();
            wasActive = false;
            return;
        }

        if (!wasActive)
        {
            lensUI.gameObject.SetActive(true);
            lensUI.position = new Vector2(Screen.width * 0.9f, Screen.height * 0.18f);
            foreach (PageLensPage p in pages) p.SetCollider(true);
            wasActive = true;
        }

        var mouse = Mouse.current;
        Vector2 m = mouse.position.ReadValue();
        if (mouse.leftButton.wasPressedThisFrame && Vector2.Distance(m, lensUI.position) < grabRadius) dragging = true;
        if (mouse.leftButton.wasReleasedThisFrame) dragging = false;
        if (dragging) lensUI.position = m;

        PageLensPage hitPage = null;
        Vector2 uv = Vector2.zero;
        Ray ray = Camera.main.ScreenPointToRay(lensUI.position);
        if (Physics.Raycast(ray, out RaycastHit hit, 50f))
        {
            hitPage = hit.collider.GetComponent<PageLensPage>();
            uv = hit.textureCoord;
        }
        foreach (PageLensPage p in pages)
            p.SetLens(uv, radiusUV, p == hitPage);
    }

    void Hide()
    {
        dragging = false;
        if (lensUI != null) lensUI.gameObject.SetActive(false);
        foreach (PageLensPage p in pages) { p.SetLens(Vector2.zero, radiusUV, false); p.SetCollider(false); }
    }
}