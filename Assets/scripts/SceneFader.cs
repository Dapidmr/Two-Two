using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Pindah scene dengan transisi halus:
// layar memudar ke hitam (suara ikut mengecil) -> scene baru dimuat di belakang layar hitam
// -> layar hitam memudar hilang (suara naik lagi) di scene tujuan.
// Tidak perlu dipasang di objek mana pun. Cukup panggil: SceneFader.LoadWithFade("Home", 1.5f, 1.2f);
public class SceneFader : MonoBehaviour
{
    private CanvasGroup group;

    public static void LoadWithFade(string sceneName, float fadeOut, float fadeIn)
    {
        GameObject go = new GameObject("SceneFader");
        DontDestroyOnLoad(go);                      // tetap hidup saat scene berganti
        go.AddComponent<SceneFader>().Begin(sceneName, fadeOut, fadeIn);
    }

    void Begin(string sceneName, float fadeOut, float fadeIn)
    {
        // Canvas hitam di atas segalanya
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        gameObject.AddComponent<GraphicRaycaster>();

        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = true;

        GameObject black = new GameObject("Black", typeof(RectTransform));
        black.transform.SetParent(transform, false);
        Image img = black.AddComponent<Image>();
        img.color = Color.black;
        RectTransform rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        StartCoroutine(Run(sceneName, fadeOut, fadeIn));
    }

    IEnumerator Run(string sceneName, float fadeOut, float fadeIn)
    {
        float startVolume = AudioListener.volume;

        // 1. Memudar ke hitam (easing halus), suara ikut mengecil
        float t = 0f;
        while (t < fadeOut)
        {
            t += Time.deltaTime;
            float e = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(fadeOut, 0.01f));
            group.alpha = e;
            AudioListener.volume = startVolume * (1f - e);
            yield return null;
        }
        group.alpha = 1f;
        AudioListener.volume = 0f;

        // 2. Muat scene baru di belakang layar hitam
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        if (op == null)
        {
            Debug.LogError("[SceneFader] Scene '" + sceneName + "' tidak bisa dimuat. Pastikan sudah ada di Build Settings.");
            AudioListener.volume = startVolume;
            Destroy(gameObject);
            yield break;
        }
        while (!op.isDone) yield return null;
        yield return null;                          // tunggu satu frame supaya scene baru sudah tergambar

        // 3. Layar hitam memudar hilang, suara naik lagi
        t = 0f;
        while (t < fadeIn)
        {
            t += Time.deltaTime;
            float e = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(fadeIn, 0.01f));
            group.alpha = 1f - e;
            AudioListener.volume = startVolume * e;
            yield return null;
        }

        AudioListener.volume = startVolume;
        Destroy(gameObject);
    }
}