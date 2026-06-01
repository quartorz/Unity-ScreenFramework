using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sample
{
	/// <summary>
	/// シーンに置いた Canvas 配下の Page / Dialog / SystemDialog / FadeOverlay を
	/// Inspector で受け取り、ScreenFramework を初期化して Home 画面を Push する。
	/// </summary>
	public sealed class SampleBootstrap : MonoBehaviour
	{
		[SerializeField] ScreenContainer _pageContainer;
		[SerializeField] ScreenContainer _dialogContainer;
		[SerializeField] ScreenContainer _sysDialogContainer;
		[SerializeField] RectTransform _fadeOverlayParent;

		async void Start()
		{
			EnsureEventSystem();

			var services = new SampleServices(useMockViews: false);

			var setup = new ScreenLayerSetup
			{
				Page = new ScreenLayerConfig
				{
					Container = _pageContainer,
					DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
					StackMode = StackMode.Cover,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
					DefaultTransition = new FadeTransition(_fadeOverlayParent, duration: 0.25f),
				},
				Dialog = new ScreenLayerConfig
				{
					Container = _dialogContainer,
					DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
					StackMode = StackMode.Cover,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
					DefaultTransition = ImmediateTransition.Instance,
				},
				SystemDialog = new ScreenLayerConfig
				{
					Container = _sysDialogContainer,
					DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
					StackMode = StackMode.Stack,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
					DefaultTransition = ImmediateTransition.Instance,
				},
			};

			ScreenNavigator.Initialize(services, setup);

			await ScreenNavigator.Page.Push(new HomeScreenId());
		}

		static void EnsureEventSystem()
		{
			if (FindFirstObjectByType<EventSystem>() == null)
			{
				var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
				DontDestroyOnLoad(go);
			}
		}
	}
}
