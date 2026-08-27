using UnityEngine;
using UnityEngine.SceneManagement;

public class SystemController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private async void Start()
    {
        await InitializeAsync();
        await SceneManager.LoadSceneAsync("MainMenu");
    }
    
    private async Awaitable InitializeAsync()
    {
        // 初始化存档
        // 初始化配置
        // 初始化音频
        // 初始化资源系统
        // ...
        await Awaitable.WaitForSecondsAsync(0.1f);
        await Awaitable.NextFrameAsync();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
