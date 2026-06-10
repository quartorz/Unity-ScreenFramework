using System.Linq;
using Cysharp.Threading.Tasks;
using Sample.Effects;
using ScreenFramework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sample.Debug
{
	/// <summary>
	/// デバッグ専用の Bootstrap。<see cref="SampleBootstrap"/> と同じレイヤー構成で初期化するが、
	/// 通信は <see cref="DebugApiBase"/> 派生のモック Service に差し替え、ローカルサーバーを起動しない。
	/// Title が充填する横断状態（マスタ・ユーザー情報）も起動時にここで充填するので、
	/// 任意の画面を直接開ける。操作は <see cref="DebugOverlay"/> から。
	/// 本番 Bootstrap はこのアセンブリを一切知らない。IS_DEBUG が無ければアセンブリごと消える。
	/// </summary>
	public sealed class DebugBootstrap : MonoBehaviour
	{
		[SerializeField] ScreenContainer _pageContainer;
		[SerializeField] ScreenContainer _dialogContainer;
		[SerializeField] ScreenContainer _sysDialogContainer;
		[SerializeField] Transform _effectRoot;
		[SerializeField] EffectRegistry _pageEffectRegistry;

		// SampleBootstrap と同じく bundle 常駐用に field で保持。
		readonly EffectWarmer _effectWarmer = new();

		async void Start()
		{
			EnsureEventSystem();

			var scenario = new DebugScenarioState();
			var userData = new UserDataHolder();
			var api = new SampleApiServices(
				gacha: new DebugGachaService(userData, scenario),
				user: new DebugUserService(userData, scenario),
				profile: new DebugProfileService(userData, scenario),
				master: new DebugMasterService(userData, scenario));
			var registry = new SampleRegistry(useMockViews: false, api, userData);

			FillCrossScreenState(registry);

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
				},
				Dialog = new ScreenLayerConfig
				{
					Container = _dialogContainer,
					// SampleBootstrap と同じ理由（ダイアログからダイアログを開いた時の awaiter 死亡回避）。
					DefaultCacheMode = ScreenCacheMode.KeepOnCover,
					StackMode = StackMode.Cover,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
				},
				SystemDialog = new ScreenLayerConfig
				{
					Container = _sysDialogContainer,
					DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
					StackMode = StackMode.Stack,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
				},
			};

			ScreenNavigator.Initialize(registry, setup);
			_effectWarmer.WarmDefaults(_pageEffectRegistry).Forget();

			gameObject.AddComponent<DebugOverlay>().Initialize(scenario);

			var route = DebugScreenIds.DefaultRoute();
			await ScreenNavigator.Page.Push(route[0]);
			for (var i = 1; i < route.Length; i++)
			{
				await ScreenNavigator.Page.Push(route[i]);
			}
		}

		/// <summary>
		/// 実 Title が <see cref="TitlePresenter"/> でやっている充填手順を Title UI を通さずコード化したもの。
		/// 充填元は Debug Service の戻り値と同じ <see cref="DummyResponses"/> なので、
		/// Title を経由して起動した場合とも矛盾しない。
		/// </summary>
		static void FillCrossScreenState(SampleRegistry registry)
		{
			var master = DummyResponses.MasterBootstrap();
			registry.Items.SetData(master.items.Select(r => new ItemMaster
			{
				Id = r.id,
				Code = r.code,
				Name = r.name,
				Rarity = r.rarity,
			}));

			var info = DummyResponses.UserInfo();
			registry.UserData.SetInfo(new UserInfo
			{
				UserId = info.userId,
				Name = info.name,
				Level = info.level,
				Money = info.money,
			});
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
