using UnityEngine;

// Tempel di tombol Character A / Character B.
// Durasi fade bisa diubah sesuai selera (fadeOut, fadeIn dalam detik).
public class SceneLoader : MonoBehaviour
{
    public void LoadCharacterA()
    {
        SceneFader.LoadWithFade("Assets/Scenes/CharacterA/Room-004.unity", 1.5f, 1.2f);
    }

    public void LoadCharacterB()
    {
        SceneFader.LoadWithFade("Assets/Scenes/CharacterB/Room-999.unity", 1.5f, 1.2f);
    }
}