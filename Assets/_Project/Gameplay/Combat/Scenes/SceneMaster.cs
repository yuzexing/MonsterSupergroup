using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralShift.Control.Controllers;
using AstralShift.FadeEffect;
using AstralShift.Helpers;
using AstralShift.Helpers.Initialization;
using AstralShift.Managers;
using AstralShift.SceneLoading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AstralShift.HellMaiden.Scenes
{
	public class SceneMaster : MonoBehaviour
	{
		public static SceneMaster Instance;

		[SerializeField]
		private LoadingScreen loadingScreen;

		private LoadingScreen _currentLoadingScreen;

		[SerializeField]
		public List<Sprite> possibleLoadingImages;

		private Scene _currentScene;

		private bool _isLoadingPending;

		private bool _firstLoad = true;

		private Coroutine _mainOperationCoroutine;

		private CancellationTokenSource _loadingScreenRequestCTS;

		private CancellationTokenSource _loadingScreenDisposeCTS;

		private Coroutine _loadUnloadOperationCoroutine;

		private AsyncOperation _unloadSceneAsyncOperation;

		private AsyncOperation _loadSceneAsyncOperation;

		private Coroutine _loadSceneDelegatorOperationCoroutine;

		private Task _loadingTimerTask;

		private CancellationTokenSource _loadingTimerTaskCTS;

		private float _elapsedLoadTime;

		public Scene CurrentScene => _currentScene;

		public SceneEnum FirstScene => SceneEnum.TitleScreen;

		public SceneEnum CurrentSceneEnum => SceneManager.GetActiveScene().name.ConvertToSceneEnum();

		public string PreviousScene { get; set; }

		public SceneEnum PreviousSceneEnum => PreviousScene.ConvertToSceneEnum();

		public string NextScene { get; set; }

		public SceneEnum NextSceneEnum => NextScene.ConvertToSceneEnum();

		public bool overrideFadeIn { get; set; }

		private bool IsFirstLoad
		{
			get
			{
				if (_firstLoad)
				{
					return NextScene == FirstScene.ToString();
				}
				return false;
			}
		}

		public float ElapsedLoadTime => _elapsedLoadTime;

		public event Action OnSceneHideStart;

		public event Action OnSceneHideFinish;

		public event Action OnSceneUnload;

		public event Action OnSceneLoad;

		public event Action OnSceneInit;

		public event Action OnSceneShowStart;

		public event Action OnSceneShowFinish;

		public event Action OnSceneHideStartPersist;

		public event Action OnSceneHideFinishPersist;

		public event Action OnSceneUnloadPersist;

		public event Action OnSceneLoadPersist;

		public event Action OnSceneInitPersist;

		public event Action OnSceneShowStartPersist;

		public event Action OnSceneShowFinishPersist;

		public void Init()
		{
			Instance = this;
			SceneManager.sceneLoaded += OnSceneLoadedCallback;
			SceneManager.sceneUnloaded += OnSceneUnloadedCallback;
			_firstLoad = true;
		}

		private void OnDestroy()
		{
			if (_loadingScreenRequestCTS != null)
			{
				_loadingScreenRequestCTS.Cancel();
				_loadingScreenRequestCTS.Dispose();
				_loadingScreenRequestCTS = null;
			}
			if (_loadingScreenDisposeCTS != null)
			{
				_loadingScreenDisposeCTS.Cancel();
				_loadingScreenDisposeCTS.Dispose();
				_loadingScreenDisposeCTS = null;
			}
			StopLoadingTimer();
		}

		public void LoadFirstScene()
		{
			LoadScene(FirstScene, unloadPreviousScene: false);
		}

		public void LoadScene(SceneEnum scene, bool unloadPreviousScene = true, bool pauseDuringLoading = true)
		{
			if (scene != SceneEnum.Systems && !_isLoadingPending && _mainOperationCoroutine == null)
			{
				NextScene = scene.ToString();
				_mainOperationCoroutine = StartCoroutine(MainOperationCoroutine(scene, unloadPreviousScene, pauseDuringLoading));
			}
		}

		public void LoadScene(SceneEnum scene, FadeEffectEnum fadeOut, FadeEffectEnum fadeIn, bool unloadPreviousScene = true)
		{
			ScreenFader.Instance.SetFadeOut(fadeOut);
			ScreenFader.Instance.SetFadeIn(fadeIn);
			NextScene = scene.ToString();
			LoadScene(scene, unloadPreviousScene);
		}

		private IEnumerator MainOperationCoroutine(SceneEnum nextScene, bool unloadPreviousScene, bool pauseDuringLoadingScreen)
		{
			ControllerManager.Instance.RenewControllerStack(runDeactivate: true);
			LoadingGameController loadingGameController = ControllerManager.Instance.OverrideGameController<LoadingGameController>();
			if (pauseDuringLoadingScreen)
			{
				loadingGameController.PauseDuringLoading();
			}
			_isLoadingPending = true;
			StartLoadingTimer();
			this.OnSceneHideStartPersist?.Invoke();
			this.OnSceneHideStart?.Invoke();
			this.OnSceneHideStart = null;
			if (_loadingScreenRequestCTS != null)
			{
				_loadingScreenRequestCTS.Cancel();
				_loadingScreenRequestCTS = null;
			}
			if (_loadingScreenRequestCTS == null)
			{
				_loadingScreenRequestCTS = new CancellationTokenSource();
			}
			Task fadeOutToLoadingTask = FadeOutToLoading(_loadingScreenRequestCTS.Token);
			yield return new WaitUntil(() => fadeOutToLoadingTask.IsCompleted);
			this.OnSceneHideFinishPersist?.Invoke();
			this.OnSceneHideFinish?.Invoke();
			this.OnSceneHideFinish = null;
			if (!IsFirstLoad)
			{
				Task loadingScreenRequestTask = RequestLoadingScreen(_loadingScreenRequestCTS.Token);
				yield return new WaitUntil(() => loadingScreenRequestTask.IsCompleted);
			}
			_loadUnloadOperationCoroutine = StartCoroutine(LoadUnloadOperationCoroutine(nextScene, unloadPreviousScene));
			yield return _loadUnloadOperationCoroutine;
			_loadSceneDelegatorOperationCoroutine = StartCoroutine(LoadSceneDelegatorOperationCoroutine());
			yield return _loadSceneDelegatorOperationCoroutine;
			ActivateScene();
			StopLoadingTimer();
			if (_loadingScreenDisposeCTS != null)
			{
				_loadingScreenDisposeCTS.Cancel();
				_loadingScreenDisposeCTS = null;
			}
			if (_loadingScreenDisposeCTS == null)
			{
				_loadingScreenDisposeCTS = new CancellationTokenSource();
			}
			Task loadingScreenDisposeTask = DisposeLoadingScreen(_loadingScreenDisposeCTS.Token);
			yield return new WaitUntil(() => loadingScreenDisposeTask.IsCompleted);
			this.OnSceneShowStartPersist?.Invoke();
			this.OnSceneShowStart?.Invoke();
			this.OnSceneShowStart = null;
			Task fadeInToScene = FadeInToScene(_loadingScreenDisposeCTS.Token);
			yield return new WaitUntil(() => fadeInToScene.IsCompleted);
			this.OnSceneShowFinishPersist?.Invoke();
			this.OnSceneShowFinish?.Invoke();
			this.OnSceneShowFinish = null;
			_isLoadingPending = false;
			_mainOperationCoroutine = null;
			_firstLoad = false;
		}

		private IEnumerator LoadUnloadOperationCoroutine(SceneEnum nextScene, bool unloadPreviousScene)
		{
			if (unloadPreviousScene)
			{
				Scene activeScene = SceneManager.GetActiveScene();
				SceneManager.SetActiveScene(SceneManager.GetSceneByName(SceneEnum.Systems.ToString()));
				_unloadSceneAsyncOperation = SceneManager.UnloadSceneAsync(activeScene);
				_unloadSceneAsyncOperation.allowSceneActivation = true;
				while (!_unloadSceneAsyncOperation.isDone)
				{
					yield return null;
				}
				_loadSceneAsyncOperation = SceneManager.LoadSceneAsync(nextScene.ToString(), LoadSceneMode.Additive);
				_loadSceneAsyncOperation.allowSceneActivation = false;
				while (!_loadSceneAsyncOperation.isDone)
				{
					if (_loadSceneAsyncOperation.progress >= 0.9f)
					{
						_loadSceneAsyncOperation.allowSceneActivation = true;
					}
					yield return null;
				}
			}
			else
			{
				_loadSceneAsyncOperation = SceneManager.LoadSceneAsync(nextScene.ToString(), LoadSceneMode.Additive);
				_loadSceneAsyncOperation.allowSceneActivation = false;
				while (!_loadSceneAsyncOperation.isDone)
				{
					if (_loadSceneAsyncOperation.progress >= 0.9f)
					{
						_loadSceneAsyncOperation.allowSceneActivation = true;
					}
					yield return null;
				}
			}
			yield return _loadSceneAsyncOperation;
			if ((bool)(UnityEngine.Object)(object)AstarPath.active)
			{
				AstarPath.active.FlushWorkItems();
			}
			yield return Resources.UnloadUnusedAssets();
			SceneManager.SetActiveScene(_currentScene);
			yield return null;
		}

		private IEnumerator LoadSceneDelegatorOperationCoroutine()
		{
			GameObject[] rootGameObjects = _currentScene.GetRootGameObjects();
			if (rootGameObjects.Length == 0)
			{
				this.OnSceneInitPersist?.Invoke();
				this.OnSceneInit?.Invoke();
				this.OnSceneInit = null;
				yield break;
			}
			Task sceneDelegatorTask = null;
			GameObject[] array = rootGameObjects;
			for (int i = 0; i < array.Length; i++)
			{
				SceneLoadingDelegator[] componentsInChildren = array[i].GetComponentsInChildren<SceneLoadingDelegator>(includeInactive: true);
				if (componentsInChildren != null && componentsInChildren.Length != 0)
				{
					componentsInChildren[0].gameObject.SetActive(value: true);
					sceneDelegatorTask = componentsInChildren[0].LoadAsync().AsTask();
				}
			}
			if (sceneDelegatorTask != null)
			{
				yield return new WaitUntil(() => sceneDelegatorTask.IsCompleted);
			}
			this.OnSceneInitPersist?.Invoke();
			this.OnSceneInit?.Invoke();
			this.OnSceneInit = null;
		}

		private void ActivateScene()
		{
			RestoreSceneHierarchy();
		}

		private void LogHierarchy(string title, GameObject[] rootGameObjects)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(title);
			foreach (GameObject gameObject in rootGameObjects)
			{
				stringBuilder.Append("\n" + gameObject.name);
			}
			Debug.Log(stringBuilder.ToString());
		}

		private void RestoreSceneHierarchy()
		{
			GameObject[] rootGameObjects = _currentScene.GetRootGameObjects();
			for (int num = rootGameObjects.Length - 1; num >= 0; num--)
			{
				if (rootGameObjects[num].name == "Root")
				{
					for (int num2 = rootGameObjects[num].transform.childCount - 1; num2 >= 0; num2--)
					{
						rootGameObjects[num].transform.GetChild(num2).SetParent(null, worldPositionStays: true);
					}
					UnityEngine.Object.Destroy(rootGameObjects[num]);
					break;
				}
			}
		}

		public void ReloadScene()
		{
			LoadScene(CurrentSceneEnum);
		}

		private void OnSceneLoadedCallback(Scene scene, LoadSceneMode mode)
		{
			this.OnSceneLoadPersist?.Invoke();
			this.OnSceneLoad?.Invoke();
			this.OnSceneLoad = null;
			_currentScene = scene;
		}

		private void OnSceneUnloadedCallback(Scene scene)
		{
			this.OnSceneUnloadPersist?.Invoke();
			this.OnSceneUnload?.Invoke();
			this.OnSceneUnload = null;
		}

		private void StartLoadingTimer()
		{
			StopLoadingTimer();
			_elapsedLoadTime = 0f;
			_loadingTimerTaskCTS = new CancellationTokenSource();
			_loadingTimerTask = TrackLoadingTimeAsync(_loadingTimerTaskCTS.Token);
		}

		private void StopLoadingTimer()
		{
			if (_loadingTimerTaskCTS != null)
			{
				_loadingTimerTaskCTS.Cancel();
				_loadingTimerTask = null;
			}
		}

		private async Task TrackLoadingTimeAsync(CancellationToken cancellationToken)
		{
			float startTime = Time.realtimeSinceStartup;
			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					_elapsedLoadTime = Time.realtimeSinceStartup - startTime;
					await Task.Delay(10);
				}
			}
			catch (TaskCanceledException)
			{
			}
		}

		private async Task FadeOutToLoading(CancellationToken token)
		{
			await ScreenFader.Instance.FadeOutTask(token);
		}

		private async Task FadeInToScene(CancellationToken token)
		{
			if (!overrideFadeIn)
			{
				await ScreenFader.Instance.FadeInTask(token);
			}
			else
			{
				overrideFadeIn = false;
			}
		}

		private async Task RequestLoadingScreen(CancellationToken token)
		{
			_currentLoadingScreen = UnityEngine.Object.Instantiate(loadingScreen, base.transform);
			await _currentLoadingScreen.Run();
			await ScreenFader.Instance.FadeInTask(token);
		}

		private async Task DisposeLoadingScreen(CancellationToken token)
		{
			if (!(_currentLoadingScreen == null))
			{
				await ScreenFader.Instance.FadeOutTask(token);
				await _currentLoadingScreen.Stop();
				UnityEngine.Object.Destroy(_currentLoadingScreen);
			}
		}
	}
}
