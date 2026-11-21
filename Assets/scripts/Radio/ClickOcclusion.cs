using UnityEngine;

// Helper bersama: memastikan collider ini benar-benar objek paling depan
// yang kena raycast, dibanding SEMUA collider yang kena (bukan cuma sejenis).
// Dipakai supaya klik tidak "tembus" objek lain yang menghalangi (misal cover, body radio).
//
// Collider milik objek INDUK (misal collider badan radio yang membungkus semuanya)
// diabaikan, karena collider seperti itu selalu kena lebih dulu dan akan membuat
// semua tombol di dalamnya tidak pernah dianggap "paling depan".
public static class ClickOcclusion
{
    // Centang lewat kode kalau perlu melihat siapa yang menghalangi (Console)
    public static bool debug = false;

    public static bool IsFrontmost(Collider myCollider, RaycastHit[] hits)
    {
        if (myCollider == null) return false;

        Transform me = myCollider.transform;

        float myDistance = float.MaxValue;
        bool hitMine = false;
        float closest = float.MaxValue;
        string blocker = "-";

        foreach (var hit in hits)
        {
            if (hit.collider == myCollider)
            {
                myDistance = hit.distance;
                hitMine = true;
                if (hit.distance < closest) closest = hit.distance;
                continue;
            }

            // Lewati collider induk (badan radio dan sejenisnya)
            Transform ht = hit.collider.transform;
            if (me.IsChildOf(ht)) continue;

            if (hit.distance < closest)
            {
                closest = hit.distance;
                blocker = hit.collider.name;
            }
        }

        bool result = hitMine && myDistance <= closest + 0.0001f;

        if (debug && !result)
            Debug.Log("[ClickOcclusion] " + myCollider.name + " ditolak. Kena sendiri: " + hitMine +
                      " | penghalang terdekat: " + blocker);

        return result;
    }
}