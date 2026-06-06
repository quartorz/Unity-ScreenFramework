using Cysharp.Threading.Tasks;
using LocalServer;
using Sample.Api;
using Sample.Api.Net;
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
		[SerializeField] bool _startWithProfile;

		async void Start()
		{
			EnsureEventSystem();

			HttpClient.BaseUrl = ServerBoot.Instance.BaseUrl;
			var registry = new SampleRegistry(
				useMockViews: false,
				gacha: new GachaService(),
				user: new UserService(),
				profile: new ProfileService(),
				master: new MasterService());

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

			ScreenNavigator.Initialize(registry, setup);

			if (_startWithProfile)
				// debug 用ショートカット。Title をスキップしているので UserData は空のまま。
				// サーバ実装と一致する固定 ID をそのまま渡す。
				await ScreenNavigator.Page.Push(new ProfileScreenId("user-001"));
			else
				await ScreenNavigator.Page.Push(new TitleScreenId());
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
