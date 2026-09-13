using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void StartGame()
    {
        if (Mirror.NetworkManager.singleton is MonsterSupergroup.NetworkCombat.BootGameplayNetworkManager manager)
            manager.CreatePreparationRoom();
    }

    public void ExitGame()
    {
        if (Mirror.NetworkManager.singleton is MonsterSupergroup.NetworkCombat.BootGameplayNetworkManager manager)
        { manager.QuitFromMenu(); return; }
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
