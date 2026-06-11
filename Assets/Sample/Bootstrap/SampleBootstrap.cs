using Cysharp.Threading.Tasks;
using LocalServer;
using Sample.Api;
using Sample.Api.Net;
using Sample.Effects;
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
		[SerializeField] Transform _effectRoot;
		[SerializeField] EffectRegistry _pageEffectRegistry;
		[SerializeField] bool _startWithProfile;

		// 指定するとレイヤーごとに ScreenSpaceCamera の Canvas を動的生成し、Page < Dialog < SystemDialog の
		// sortingOrder で重ねる（Dialog レイヤーの Shield が Page を Canvas 優先度で遮断できる）。
		// 未指定なら従来どおりシーンの単一 Canvas をそのまま使う。
		[SerializeField] Camera _uiCamera;

		// デフォルト Effect の bundle を起動時に常駐させておく簡易ウォーマ。握りっぱなしにするため field で保持。
		readonly EffectWarmer _effectWarmer = new();

		async void Start()
		{
			EnsureEventSystem();

			HttpClient.BaseUrl = ServerBoot.Instance.BaseUrl;
			var userData = new UserDataHolder();
			var api = new SampleApiServices(
				gacha: new GachaService(userData),
				user: new UserService(userData),
				profile: new ProfileService(userData),
				master: new MasterService(userData));
			var registry = new SampleRegistry(useMockViews: false, api, userData);

			var setup = new ScreenLayerSetup
			{
				Page = new ScreenLayerConfig
				{
					Container = _pageContainer,
					DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
					StackMode = StackMode.Cover,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
					Registry = _pageEffectRegistry,
					EffectRoot = _effectRoot,
					Camera = _uiCamera,
					SortingOrder = 0,
				},
				Dialog = new ScreenLayerConfig
				{
					Container = _dialogContainer,
					// Cover + DestroyOnCover だと PushAndAwait 中のダイアログから別ダイアログを開いた瞬間、
					// 下のダイアログの awaiter が TrySetCanceled → OCE で死ぬ(framework 仕様)。
					// 「ダイアログからダイアログ」は普通の要求なので KeepOnCover で寝かせて、
					// 上が閉じたら自分の Pop で正常 resolve させる。
					DefaultCacheMode = ScreenCacheMode.KeepOnCover,
					StackMode = StackMode.Cover,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
					Camera = _uiCamera,
					SortingOrder = 100,
				},
				SystemDialog = new ScreenLayerConfig
				{
					Container = _sysDialogContainer,
					DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
					StackMode = StackMode.Stack,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
					Camera = _uiCamera,
					SortingOrder = 200,
				},
			};

			ScreenNavigator.Initialize(registry, setup);

			// デフォルト Effect だけ先に常駐させておく（全行 warm はしない）。最初の Push を待たせない fire-and-forget。
			_effectWarmer.WarmDefaults(_pageEffectRegistry).Forget();

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
