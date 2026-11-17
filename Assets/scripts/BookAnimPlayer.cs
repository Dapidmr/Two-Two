using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BookAnimPlayer : MonoBehaviour
{
    [Header("Cover depan (yang ikut terbuka)")]
    public Transform frontCover;                  // drag Cover L atau Cover R

    [Header("Lembar (kode)")]
    public Transform hingePoint;                  // drag Empty.001
    public Vector3 flipAxis = Vector3.forward;
    public float flipAngle = -180f;
    public float flipSpeed = 300f;
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

    void Start()
    {
        foreach (Transform c in transform)
            if (c.name.StartsWith("Plane")) pages.Add(c);

        pages.Sort((a, b) =>
        {
            float ya = transform.InverseTransformPoint(Center(a)).y;
            float yb = transform.InverseTransformPoint(Center(b)).y;
            return yb.CompareTo(ya);
        });
        if (reverseOrder) pages.Reverse();

        if (frontCover != null) pages.Insert(0, frontCover);

        int n = pages.Count;
        hinges = new Transform[n];
        current = new float[n];
        target = new float[n];

        Vector3 pivot = hingePoint != null ? hingePoint.position : transform.position;
        for (int i = 0; i < n; i++)
        {
            GameObject h = new GameObject("PageHinge_" + i);
            h.transform.SetParent(transform, false);
            h.transform.position = pivot;
            h.transform.localRotation = Quaternion.identity;
            h.transform.localScale = Vector3.one;
            hinges[i] = h.transform;
            pages[i].SetParent(hinges[i], true);
        }
        ready = true;
        Debug.Log("[BookAnimPlayer] Jumlah halaman (termasuk cover): " + n);
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
                target[step] = flipAngle;
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
            hinges[i].localRotation = Quaternion.AngleAxis(current[i], flipAxis.normalized);
        }
    }
}