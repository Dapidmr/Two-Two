using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

// Section balok (4 balok bersimbol).
// 1. Saat dibuka, balok di meja terbang ke depan kamera seperti kartu.
// 2. Pemain KLIK balok (terpilih), lalu KLIK lubang di box -> balok itu terpasang di lubang tersebut.
//    Klik balok yang sudah terpasang -> balok itu dilepas lagi jadi kartu.
// 3. Setelah SEMUA lubang terisi baru dicek: lingkaran indikator MERAH = salah (semua balok keluar lagi),
//    HIJAU = benar (kembali ke zoom utama, section terkunci).
// Aturan benar: tiap balok harus berada di lubang yang jadi Slot-nya.
public class PandoraBlockPanel : PandoraPanel
{
    [System.Serializable]
    public class Block
    {
        public Transform block;     // objek balok
        public string id = "";      // nama simbol (hanya label)
        public Transform slot;      // lubang YANG BENAR untuk balok ini (objek kosong di lubang)
    }

    [Header("Balok")]
    public Block[] blocks;

    [Header("Kartu di depan pemain")]
    public float cardDistance = 0.35f;              // jarak dari kamera
    public Vector2 cardOffset = new Vector2(0f, -0.04f);   // geser kartu (X kanan, Y atas)
    [Tooltip("true = ukuran kartu otomatis sama untuk semua balok, apa pun skala modelnya.")]
    public bool autoSize = true;
    [Tooltip("Lebar kartu (meter, dilihat dari kamera) kalau Auto Size aktif.")]
    public float cardWidth = 0.12f;
    [Tooltip("Celah antar kartu, sebagai bagian dari lebar kartu (Auto Size).")]
    public float spacingGap = 0.25f;
    [Tooltip("Hanya dipakai kalau Auto Size mati.")]
    public float cardScale = 1f;
    public float cardSpacing = 0.14f;               // hanya kalau Auto Size mati
    [Tooltip("Sumbu LOKAL balok yang menghadap pemain (sisi bersimbol = sisi atas balok).")]
    public Vector3 faceLocalAxis = new Vector3(0f, 0f, 1f);
    [Tooltip("Sumbu LOKAL balok yang mengarah ke ATAS layar. Sumbu terpanjang balok akan jadi mendatar.")]
    public Vector3 faceLocalUp = new Vector3(1f, 0f, 0f);

    [Header("Gerak")]
    public float flySpeed = 7f;
    public float cameraDelay = 0.35f;               // tunggu kamera zoom dulu sebelum balok terbang
    public float stagger = 0.07f;                   // selisih waktu antar balok
    public float hoverLift = 0.015f;                // balok maju sedikit saat disorot
    public float hoverScale = 1.08f;
    public float selectLift = 0.03f;                // balok terpilih naik dan membesar sedikit
    public float selectScale = 1.15f;
    public float clickMaxPixels = 8f;

    [Header("Hasil")]
    public float wrongHold = 1f;                    // lama merah sebelum balok keluar lagi
    public float solveHold = 0.8f;                  // lama hijau sebelum kembali ke zoom utama

    [Header("Indikator lingkaran")]
    [Tooltip("Posisi di layar (0-1). Default: tengah atas.")]
    public Vector2 indicatorPosition = new Vector2(0.5f, 0.9f);
    public float indicatorSize = 90f;
    public Color waitColor = new Color(1f, 1f, 1f, 0.35f);
    public Color correctColor = new Color(0.2f, 1f, 0.3f, 1f);
    public Color wrongColor = new Color(1f, 0.15f, 0.15f, 1f);
    public Color rimColor = new Color(0f, 0f, 0f, 0.6f);

    [Header("UI (opsional)")]
    public TextMeshProUGUI display;                 // menampilkan "2 / 4" (jumlah balok terpasang)

    private enum Pose { Home, Card, Slot }

    private class State
    {
        public Transform t;
        public Transform slot;
        public Collider col;
        public MeshRenderer[] meshes;
        public Transform origParent;
        public Vector3 origLocalPos;
        public Quaternion origLocalRot;
        public Vector3 origLocalScale;
        public float cardFactor = 1f;
        public Pose pose = Pose.Home;
        public int holeIndex = -1;
        public float moveAfter;
    }

    private readonly List<State> states = new List<State>();
    private Collider[] holeCols;
    private int[] holeOccupant;                     // indeks balok yang ada di lubang ini, -1 = kosong
    private MaterialPropertyBlock mpb;                 // dibuat di Awake / saat dipakai (tidak boleh di field initializer)

    private bool active = false;
    private bool settled = true;
    private bool focused = false;
    private bool busy = false;
    private bool pressing = false;
    private Vector2 pressPos;
    private int selected = -1;

    // indikator
    private GameObject indRoot;
    private Image indFill;
    private RectTransform indRim;
    private Color indCurrent;
    private Color indTarget;
    private float pulseT = 99f;
    private static Sprite circleSprite;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();

        foreach (Block b in blocks)
        {
            if (b == null || b.block == null) continue;

            State s = new State();
            s.t = b.block;
            s.slot = b.slot;
            s.meshes = b.block.GetComponentsInChildren<MeshRenderer>();
            s.origParent = b.block.parent;
            s.origLocalPos = b.block.localPosition;
            s.origLocalRot = b.block.localRotation;
            s.origLocalScale = b.block.localScale;

            Collider c = b.block.GetComponent<Collider>();
            if (c == null) c = AddBoxCollider(b.block);
            c.enabled = false;                      // baru aktif saat section ini dibuka
            s.col = c;

            s.cardFactor = cardScale;
            if (autoSize)
            {
                float w = MeasureWidth(s);
                if (w > 0.00001f) s.cardFactor = cardWidth / w;
            }

            if (b.slot == null)
                Debug.LogWarning("[Pandora] Balok '" + b.block.name + "' belum punya Slot, tidak bisa dipasang.");

            states.Add(s);
        }

        // Collider tiap lubang (dipakai untuk klik). Dibuat otomatis kalau Slot belum punya collider.
        holeCols = new Collider[states.Count];
        holeOccupant = new int[states.Count];
        for (int j = 0; j < states.Count; j++)
        {
            holeOccupant[j] = -1;
            Transform slot = states[j].slot;
            if (slot == null) continue;

            Collider c = slot.GetComponent<Collider>();
            if (c == null) c = AddHoleCollider(slot, states[j].t);
            c.enabled = false;
            holeCols[j] = c;
        }

        if (display != null) display.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (indRoot != null) Destroy(indRoot);
    }

    public override void OnFocus()
    {
        focused = true;
        busy = false;
        pressing = false;
        selected = -1;

        for (int j = 0; j < holeOccupant.Length; j++)
        {
            holeOccupant[j] = -1;
            if (holeCols[j] != null) holeCols[j].enabled = true;
        }

        for (int i = 0; i < states.Count; i++)
        {
            State s = states[i];
            s.pose = Pose.Card;
            s.holeIndex = -1;
            s.col.enabled = true;
            s.moveAfter = Time.time + cameraDelay + i * stagger;
        }

        active = true;
        if (display != null) display.gameObject.SetActive(true);
        RefreshDisplay();

        BuildIndicator();
        indRoot.SetActive(true);
        indCurrent = indTarget = waitColor;
    }

    public override void OnUnfocus()
    {
        focused = false;
        busy = false;
        pressing = false;
        selected = -1;
        StopAllCoroutines();
        ClearFlash();

        foreach (State s in states) s.col.enabled = false;
        foreach (Collider c in holeCols) if (c != null) c.enabled = false;
        if (display != null) display.gameObject.SetActive(false);
        if (indRoot != null) indRoot.SetActive(false);

        // Setelah selesai balok tetap di lubang; kalau belum selesai semuanya kembali ke meja
        for (int i = 0; i < states.Count; i++)
        {
            State s = states[i];
            bool stay = solved && s.pose == Pose.Slot;
            if (stay) continue;

            s.pose = Pose.Home;
            s.holeIndex = -1;
            s.moveAfter = Time.time + i * 0.05f;
        }
        for (int j = 0; j < holeOccupant.Length; j++) holeOccupant[j] = -1;

        active = true;
    }

    void Update()
    {
        if (focused) UpdateIndicator();
        if (!active || states.Count == 0) return;

        float k = 1f - Mathf.Exp(-flySpeed * Time.deltaTime);

        int hoverBlock = -1;
        if (focused && !busy && Mouse.current != null)
        {
            int hoverHole;
            FindHover(out hoverBlock, out hoverHole);
            HandleClick(hoverBlock, hoverHole);
        }

        bool allArrived = true;
        for (int i = 0; i < states.Count; i++)
        {
            State s = states[i];

            Vector3 tp; Quaternion tr; Vector3 ts;
            bool hovered = hoverBlock == i && s.pose == Pose.Card;
            Target(i, hovered, out tp, out tr, out ts);

            if (Time.time < s.moveAfter) { allArrived = false; continue; }

            s.t.position = Vector3.Lerp(s.t.position, tp, k);
            s.t.rotation = Quaternion.Slerp(s.t.rotation, tr, k);
            s.t.localScale = Vector3.Lerp(s.t.localScale, ts, k);

            if ((s.t.position - tp).sqrMagnitude > 0.000004f || Quaternion.Angle(s.t.rotation, tr) > 0.5f)
                allArrived = false;
        }
        settled = allArrived;

        // Semua sudah sampai dan tidak sedang di dalam section: rapikan posisi di meja lalu berhenti
        if (!focused && allArrived)
        {
            foreach (State s in states)
            {
                if (s.pose != Pose.Home) continue;
                s.t.localPosition = s.origLocalPos;
                s.t.localRotation = s.origLocalRot;
                s.t.localScale = s.origLocalScale;
            }
            active = false;
        }
    }

    // Posisi, putaran, dan ukuran tujuan tiap balok
    void Target(int i, bool hovered, out Vector3 pos, out Quaternion rot, out Vector3 scale)
    {
        State s = states[i];

        if (s.pose == Pose.Slot && s.holeIndex >= 0 && states[s.holeIndex].slot != null)
        {
            Transform hole = states[s.holeIndex].slot;
            pos = hole.position;
            rot = hole.rotation;
            scale = s.origLocalScale;
        }
        else if (s.pose == Pose.Card)
        {
            float spacing = autoSize ? cardWidth * (1f + spacingGap) : cardSpacing;
            Vector3 center = focusPoint.position
                             + focusPoint.forward * cardDistance
                             + focusPoint.right * cardOffset.x
                             + focusPoint.up * cardOffset.y;
            float x = (i - (states.Count - 1) * 0.5f) * spacing;

            pos = center + focusPoint.right * x;
            float mul = 1f;

            if (selected == i)
            {
                pos += focusPoint.up * selectLift - focusPoint.forward * hoverLift;
                mul = selectScale;
            }
            else if (hovered)
            {
                pos -= focusPoint.forward * hoverLift;
                mul = hoverScale;
            }

            rot = FaceRot();
            scale = s.origLocalScale * s.cardFactor * mul;
        }
        else
        {
            pos = s.origParent != null ? s.origParent.TransformPoint(s.origLocalPos) : s.origLocalPos;
            rot = s.origParent != null ? s.origParent.rotation * s.origLocalRot : s.origLocalRot;
            scale = s.origLocalScale;
        }
    }

    void GetFaceAxes(out Vector3 a, out Vector3 u)
    {
        a = faceLocalAxis.sqrMagnitude > 0.0001f ? faceLocalAxis.normalized : Vector3.forward;
        u = faceLocalUp.sqrMagnitude > 0.0001f ? faceLocalUp.normalized : Vector3.right;
        if (Mathf.Abs(Vector3.Dot(a, u)) > 0.99f) u = Mathf.Abs(a.x) < 0.9f ? Vector3.right : Vector3.up;
    }

    // Putaran balok supaya sisi bersimbol menghadap kamera (titik fokus)
    Quaternion FaceRot()
    {
        Vector3 a, u;
        GetFaceAxes(out a, out u);

        Quaternion local = Quaternion.LookRotation(a, u);
        Quaternion view = Quaternion.LookRotation(-focusPoint.forward, focusPoint.up);
        return view * Quaternion.Inverse(local);
    }

    // Lebar balok (arah mendatar di layar) dalam satuan dunia, dari ukuran mesh dan skala
    float MeasureWidth(State s)
    {
        MeshFilter mf = s.t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return 0f;

        Vector3 a, u;
        GetFaceAxes(out a, out u);
        Vector3 r = Vector3.Cross(a, u);
        Vector3 ar = new Vector3(Mathf.Abs(r.x), Mathf.Abs(r.y), Mathf.Abs(r.z));

        Vector3 sz = mf.sharedMesh.bounds.size;
        Vector3 ls = s.t.lossyScale;
        Vector3 size = new Vector3(sz.x * Mathf.Abs(ls.x), sz.y * Mathf.Abs(ls.y), sz.z * Mathf.Abs(ls.z));
        return Vector3.Dot(size, ar);
    }

    // ---------- Input ----------

    // Mencari balok atau lubang kosong terdekat di bawah kursor
    void FindHover(out int block, out int hole)
    {
        block = -1;
        hole = -1;

        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            if (h.distance >= best) continue;

            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].col != h.collider) continue;
                best = h.distance;
                block = i;
                hole = -1;
            }

            // Lubang hanya bisa diklik kalau ada balok terpilih dan lubangnya kosong
            if (selected < 0) continue;
            for (int j = 0; j < holeCols.Length; j++)
            {
                if (holeCols[j] == null || holeCols[j] != h.collider || holeOccupant[j] >= 0) continue;
                best = h.distance;
                hole = j;
                block = -1;
            }
        }
    }

    void HandleClick(int hoverBlock, int hoverHole)
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            pressing = true;
            pressPos = Mouse.current.position.ReadValue();
        }

        if (!Mouse.current.leftButton.wasReleasedThisFrame || !pressing) return;
        pressing = false;

        float moved = Vector2.Distance(pressPos, Mouse.current.position.ReadValue());
        if (moved > clickMaxPixels) return;

        if (hoverBlock >= 0)
        {
            State s = states[hoverBlock];
            if (s.pose == Pose.Card)
                selected = selected == hoverBlock ? -1 : hoverBlock;     // pilih / batal pilih
            else if (s.pose == Pose.Slot)
                PopOut(hoverBlock);                                       // lepas dari lubang
        }
        else if (hoverHole >= 0 && selected >= 0)
        {
            Place(selected, hoverHole);
        }
    }

    void Place(int block, int hole)
    {
        State s = states[block];
        if (states[hole].slot == null) return;

        holeOccupant[hole] = block;
        if (holeCols[hole] != null) holeCols[hole].enabled = false;

        s.holeIndex = hole;
        s.pose = Pose.Slot;
        s.moveAfter = Time.time;
        selected = -1;
        RefreshDisplay();

        if (PlacedCount() >= HoleCount()) StartCoroutine(CheckRoutine());
    }

    void PopOut(int block)
    {
        State s = states[block];
        if (s.holeIndex >= 0)
        {
            holeOccupant[s.holeIndex] = -1;
            if (holeCols[s.holeIndex] != null) holeCols[s.holeIndex].enabled = true;
        }

        s.holeIndex = -1;
        s.pose = Pose.Card;
        s.moveAfter = Time.time;
        RefreshDisplay();
    }

    int PlacedCount()
    {
        int n = 0;
        foreach (int o in holeOccupant) if (o >= 0) n++;
        return n;
    }

    int HoleCount()
    {
        int n = 0;
        foreach (State s in states) if (s.slot != null) n++;
        return n;
    }

    // ---------- Hasil ----------

    IEnumerator CheckRoutine()
    {
        busy = true;
        selected = -1;

        // Tunggu balok terakhir benar-benar sampai di lubang
        yield return null;
        float timeout = Time.time + 2f;
        while (!settled && Time.time < timeout) yield return null;

        bool ok = true;
        for (int j = 0; j < holeOccupant.Length; j++)
            if (states[j].slot != null && holeOccupant[j] != j) ok = false;

        if (!ok)
        {
            // Salah: merah, lalu semua balok keluar lagi
            SetIndicator(wrongColor);
            FlashAll(wrongColor);
            yield return new WaitForSeconds(wrongHold);
            ClearFlash();

            for (int i = 0; i < states.Count; i++)
            {
                State s = states[i];
                if (s.pose != Pose.Slot) continue;
                s.pose = Pose.Card;
                s.holeIndex = -1;
                s.moveAfter = Time.time + i * 0.04f;
            }
            for (int j = 0; j < holeOccupant.Length; j++)
            {
                holeOccupant[j] = -1;
                if (holeCols[j] != null) holeCols[j].enabled = true;
            }

            SetIndicator(waitColor, false);
            RefreshDisplay();
            busy = false;
            yield break;
        }

        // Benar: hijau, balok menempel ke box, kembali ke zoom utama
        SetIndicator(correctColor);
        FlashAll(correctColor);
        yield return new WaitForSeconds(solveHold);
        ClearFlash();

        foreach (State s in states) s.col.enabled = false;
        foreach (Collider c in holeCols) if (c != null) c.enabled = false;

        foreach (State s in states)
        {
            if (s.pose != Pose.Slot || s.holeIndex < 0) continue;
            Transform hole = states[s.holeIndex].slot;
            s.t.SetParent(owner != null ? owner.transform : hole.parent, true);
            s.t.position = hole.position;
            s.t.rotation = hole.rotation;
        }

        SetSolved();
        yield return new WaitForSeconds(0.5f);
        if (owner != null) owner.LeaveFocus();
    }

    // ---------- Indikator lingkaran ----------

    void BuildIndicator()
    {
        if (indRoot != null) return;

        indRoot = new GameObject("BlockIndicatorCanvas");
        Canvas canvas = indRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;

        CanvasScaler scaler = indRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Bingkai gelap
        GameObject rim = new GameObject("Rim", typeof(RectTransform));
        rim.transform.SetParent(indRoot.transform, false);
        Image rimImg = rim.AddComponent<Image>();
        rimImg.sprite = GetCircle();
        rimImg.color = rimColor;
        rimImg.raycastTarget = false;

        indRim = rimImg.rectTransform;
        indRim.anchorMin = indicatorPosition;
        indRim.anchorMax = indicatorPosition;
        indRim.pivot = new Vector2(0.5f, 0.5f);
        indRim.sizeDelta = new Vector2(indicatorSize, indicatorSize);
        indRim.anchoredPosition = Vector2.zero;

        // Isi berwarna
        GameObject fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(rim.transform, false);
        indFill = fill.AddComponent<Image>();
        indFill.sprite = GetCircle();
        indFill.color = waitColor;
        indFill.raycastTarget = false;

        RectTransform frt = indFill.rectTransform;
        frt.anchorMin = new Vector2(0.5f, 0.5f);
        frt.anchorMax = new Vector2(0.5f, 0.5f);
        frt.pivot = new Vector2(0.5f, 0.5f);
        frt.sizeDelta = new Vector2(indicatorSize * 0.78f, indicatorSize * 0.78f);
        frt.anchoredPosition = Vector2.zero;

        indCurrent = indTarget = waitColor;
    }

    void SetIndicator(Color c, bool pulse = true)
    {
        indTarget = c;
        if (pulse) pulseT = 0f;
    }

    void UpdateIndicator()
    {
        if (indFill == null) return;

        indCurrent = Color.Lerp(indCurrent, indTarget, 1f - Mathf.Exp(-12f * Time.deltaTime));
        indFill.color = indCurrent;

        if (pulseT < 0.5f)
        {
            pulseT += Time.deltaTime;
            float s = 1f + 0.25f * Mathf.Sin(Mathf.Clamp01(pulseT / 0.5f) * Mathf.PI);
            indRim.localScale = Vector3.one * s;
        }
        else
        {
            indRim.localScale = Vector3.one;
        }
    }

    // Lingkaran putih dibuat lewat kode (tidak perlu gambar)
    static Sprite GetCircle()
    {
        if (circleSprite != null) return circleSprite;

        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float r = size * 0.5f;
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float a = Mathf.Clamp01(r - d);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();

        circleSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return circleSprite;
    }

    // ---------- Util ----------

    void RefreshDisplay()
    {
        if (display == null) return;
        display.text = PlacedCount() + " / " + HoleCount();
        display.color = Color.white;
    }

    // Kedip warna sementara pada badan balok (simbol Sprite tidak ikut berubah)
    void FlashAll(Color c)
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();

        foreach (State s in states)
        {
            foreach (MeshRenderer r in s.meshes)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c);
                mpb.SetColor("_Color", c);
                r.SetPropertyBlock(mpb);
            }
        }
    }

    void ClearFlash()
    {
        foreach (State s in states)
            foreach (MeshRenderer r in s.meshes)
                if (r != null) r.SetPropertyBlock(null);
    }

    BoxCollider AddBoxCollider(Transform t)
    {
        BoxCollider box = t.gameObject.AddComponent<BoxCollider>();
        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            box.center = mf.sharedMesh.bounds.center;
            box.size = mf.sharedMesh.bounds.size;
        }
        return box;
    }

    // Collider untuk lubang yang belum punya: ukurannya sama dengan balok (slot dibuat dari duplikat balok)
    BoxCollider AddHoleCollider(Transform slot, Transform block)
    {
        BoxCollider box = slot.gameObject.AddComponent<BoxCollider>();
        MeshFilter mf = block.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            box.center = mf.sharedMesh.bounds.center;
            box.size = mf.sharedMesh.bounds.size;
        }
        return box;
    }

    // Scene view: kotak cyan = posisi kartu di depan kamera fokus
    void OnDrawGizmosSelected()
    {
        if (focusPoint == null) return;

        int n = blocks != null ? blocks.Length : 4;
        float spacing = autoSize ? cardWidth * (1f + spacingGap) : cardSpacing;
        float w = autoSize ? cardWidth : 0.06f;

        Gizmos.color = Color.cyan;
        Vector3 center = focusPoint.position
                         + focusPoint.forward * cardDistance
                         + focusPoint.right * cardOffset.x
                         + focusPoint.up * cardOffset.y;

        for (int i = 0; i < n; i++)
        {
            float x = (i - (n - 1) * 0.5f) * spacing;
            Gizmos.DrawWireCube(center + focusPoint.right * x, new Vector3(w, w * 0.5f, 0.01f));
        }
    }
}