using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Sample;
using Sample.Api;
using ScreenFramework;

namespace Tests
{
	/// <summary>
	/// TitlePresenter を GameObject 一切無しで単体テスト。
	/// User / Master Service は Mock、Page Navigator は <see cref="MockScreenNavigator"/> で
	/// Change 呼び出しを観測する。Items Store は SampleRegistry が実体を持つので
	/// SetData の効果は store.UniqueIndex / CodeIndex 経由で検証できる。
	/// </summary>
	public sealed class TitlePresenterPureTests
	{
		MockUserService _userApi;
		MockMasterService _masterApi;
		MockScreenNavigator _pageNav;
		MockView.Sample.MockTitleView _view;
		SampleRegistry _registry;

		List<IScreenIdentifier> _changedIds;

		[SetUp]
		public void SetUp()
		{
			_userApi = new MockUserService();
			_masterApi = new MockMasterService();
			_pageNav = new MockScreenNavigator();
			_view = new MockView.Sample.MockTitleView();
			_registry = new SampleRegistry(
				useMockViews: true,
				gacha: new MockGachaService(),
				user: _userApi,
				profile: new MockProfileService(),
				master: _masterApi);

			_view.SetTitleFunc = _ => { };
			_view.SetStatusFunc = _ => { };
			_view.SetStartButtonInteractableFunc = _ => { };

			// 既定のスタブ。各テストは必要に応じて上書きする。
			_userApi.InfoFunc = opt => UniTask.FromResult(new UserInfoResponse
			{
				userId = "user-001",
				name = "テストユーザー",
				level = 1,
			});

			_changedIds = new List<IScreenIdentifier>();
			_pageNav.ChangeFunc = (id, opt, ct) =>
			{
				_changedIds.Add(id);
				return AsyncTestHelper.Done();
			};

			ScreenNavigator.Override(
				page: _pageNav,
				dialog: new MockScreenNavigator(),
				systemDialog: new MockScreenNavigator());
		}

		TitlePresenter NewPresenter() => new TitlePresenter().WithServices(_registry);

		static BootstrapMasterResponse SampleMaster() => new BootstrapMasterResponse
		{
			items = new[]
			{
				new ItemMasterResponse { id = 1, code = "sword_wood",  name = "木の剣",           rarity = 1 },
				new ItemMasterResponse { id = 5, code = "sword_excal", name = "エクスカリバー",   rarity = 5 },
				new ItemMasterResponse { id = 9, code = "potion_heal", name = "ポーション",       rarity = 1 },
			},
		};

		[Test]
		public async Task OnAfterLoad_FetchesBootstrap_PopulatesItemsStore()
		{
			_masterApi.BootstrapFunc = opt => UniTask.FromResult(SampleMaster());

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			Assert.IsTrue(_registry.Items.UniqueIndex.ContainsKey(1));
			Assert.IsTrue(_registry.Items.UniqueIndex.ContainsKey(5));
			Assert.AreEqual("エクスカリバー", _registry.Items.UniqueIndex[5].Name);
			Assert.AreEqual(1, _registry.Items.CodeIndex["sword_wood"].Id);
		}

		[Test]
		public async Task OnAfterLoad_FetchesUserInfo_PopulatesUserDataHolder()
		{
			_masterApi.BootstrapFunc = opt => UniTask.FromResult(SampleMaster());
			_userApi.InfoFunc = opt => UniTask.FromResult(new UserInfoResponse
			{
				userId = "user-042",
				name = "Bob",
				level = 9,
			});

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			Assert.IsNotNull(_registry.UserData.Info);
			Assert.AreEqual("user-042", _registry.UserData.Info.UserId);
			Assert.AreEqual("Bob", _registry.UserData.Info.Name);
			Assert.AreEqual(9, _registry.UserData.Info.Level);
		}

		[Test]
		public async Task OnAfterLoad_TogglesStartButton_DisabledThenEnabled()
		{
			_masterApi.BootstrapFunc = opt => UniTask.FromResult(SampleMaster());
			var states = new List<bool>();
			_view.SetStartButtonInteractableFunc = v => states.Add(v);

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			Assert.AreEqual(new[] { false, true }, states.ToArray());
		}

		[Test]
		public async Task OnAfterLoad_StatusReflectsLoadingThenCount()
		{
			_masterApi.BootstrapFunc = opt => UniTask.FromResult(SampleMaster());
			var statuses = new List<string>();
			_view.SetStatusFunc = s => statuses.Add(s);

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			Assert.AreEqual(2, statuses.Count);
			StringAssert.Contains("取得中", statuses[0]);
			StringAssert.Contains("3", statuses[1]); // 件数
		}

		[Test]
		public async Task OnAfterLoad_FetchFails_StatusShowsError_ButtonStaysDisabled()
		{
			_masterApi.BootstrapFunc = opt => UniTask.FromException<BootstrapMasterResponse>(
				new System.Exception("boom"));
			var states = new List<bool>();
			var statuses = new List<string>();
			_view.SetStartButtonInteractableFunc = v => states.Add(v);
			_view.SetStatusFunc = s => statuses.Add(s);

			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; // Debug.LogError で fail させない

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			Assert.AreEqual(new[] { false }, states.ToArray(), "失敗時はボタンを enable しない");
			StringAssert.Contains("取得失敗", statuses[statuses.Count - 1]);
		}

		[Test]
		public async Task StartClick_AfterReady_ChangesToHome()
		{
			_masterApi.BootstrapFunc = opt => UniTask.FromResult(SampleMaster());

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.RaiseOnStartClicked();
			await UniTask.WaitUntil(() => _changedIds.Count >= 1);

			Assert.AreEqual(1, _changedIds.Count);
			Assert.IsInstanceOf<HomeScreenId>(_changedIds[0]);
		}

		[Test]
		public async Task StartClick_BeforeReady_DoesNothing()
		{
			// 完了させないために TCS で OnAfterEnter の await を手前で止める
			var tcs = new UniTaskCompletionSource<BootstrapMasterResponse>();
			_masterApi.BootstrapFunc = opt => tcs.Task;

			var presenter = (IScreenPresenter)NewPresenter();
			ScreenTesting.PushAsync(presenter, _view).Forget(); // 起動だけ

			_view.RaiseOnStartClicked();
			// Forget チェーンが万一走るなら drain で観測する
			for (var i = 0; i < 5; i++) await UniTask.Yield();

			Assert.AreEqual(0, _changedIds.Count, "ready 前の click では遷移しない");

			// 後始末（リーク防止）
			tcs.TrySetCanceled();
		}

		[Test]
		public async Task OnAfterUnload_UnsubscribesStartHandler()
		{
			_masterApi.BootstrapFunc = opt => UniTask.FromResult(SampleMaster());

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);
			await ScreenTesting.PopAsync(presenter);

			_view.RaiseOnStartClicked();
			for (var i = 0; i < 5; i++) await UniTask.Yield();

			Assert.AreEqual(0, _changedIds.Count, "Unload 後の click が leak しないこと");
		}
	}
}
