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
		[SerializeField] EffectHost _effectHost;
		[SerializeField] EffectRegistry _pageEffectRegistry;
		[SerializeField] bool _startWithProfile;

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

			var setup = SampleScreenLayers.Create(
				_pageContainer, _dialogContainer, _sysDialogContainer,
				_effectHost, _pageEffectRegistry);

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
