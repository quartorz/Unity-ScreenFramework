using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Sample;
using Sample.Api;
using Sample.Dialogs;
using ScreenFramework;

namespace Tests
{
	/// <summary>
	/// ProfilePresenter を GameObject 一切無しで単体テスト。
	/// IApiClient は MockGenerator 生成の <see cref="MockApiClient"/>、
	/// Dialog Navigator は <see cref="MockScreenNavigatorExtensions.SetupPushAndAwait{TId,TResult}"/>
	/// で InputDialogId に対するレスポンスを直接登録する。
	/// </summary>
	public sealed class ProfilePresenterPureTests
	{
		MockApiClient _api;
		MockScreenNavigator _pageNav;
		MockScreenNavigator _dialogNav;
		MockView.Sample.MockProfileView _view;

		[SetUp]
		public void SetUp()
		{
			_api = new MockApiClient();
			_pageNav = new MockScreenNavigator();
			_dialogNav = new MockScreenNavigator();
			_view = new MockView.Sample.MockProfileView();

			_view.SetUserIdFunc = _ => { };
			_view.SetLevelFunc  = _ => { };
			_view.SetNameFunc   = _ => { };
			_view.SetSavingFunc = _ => { };

			ScreenNavigator.Override(page: _pageNav, dialog: _dialogNav, systemDialog: new MockScreenNavigator());
		}

		const string TargetUserId = "user-001";

		ProfilePresenter NewPresenter() => new ProfilePresenter(TargetUserId)
			.WithServices(new SampleServices(useMockViews: true, api: _api));

		[Test]
		public async Task OnAfterLoad_FetchesProfile_AndAppliesToView()
		{
			string capturedName = null;
			int capturedLevel = 0;
			string capturedUserId = null;
			_view.SetNameFunc = v => capturedName = v;
			_view.SetLevelFunc = v => capturedLevel = v;
			_view.SetUserIdFunc = v => capturedUserId = v;

			_api.GetProfileFunc = (userId, ct) => UniTask.FromResult(new ProfileResponse
			{
				userId = "user-001",
				name   = "Alice",
				level  = 7,
			});

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			Assert.AreEqual("user-001", capturedUserId);
			Assert.AreEqual("Alice", capturedName);
			Assert.AreEqual(7, capturedLevel);
		}

		[Test]
		public async Task EditName_OpensDialog_WithCurrentNameAsInitial_AndPostsNewName()
		{
			_api.GetProfileFunc = (userId, ct) => UniTask.FromResult(new ProfileResponse
			{
				userId = "user-001",
				name   = "Old",
				level  = 3,
			});

			InputDialogId observedId = null;
			_dialogNav.SetupPushAndAwait<InputDialogId, InputDialogResult>(id =>
			{
				observedId = id;
				return new InputDialogResult("New");
			});

			ProfileRequest posted = null;
			_api.PostProfileFunc = (req, ct) =>
			{
				posted = req;
				return UniTask.FromResult(new ProfileResponse
				{
					userId = req.userId,
					name   = req.name,
					level  = req.level,
				});
			};

			string lastName = null;
			_view.SetNameFunc = v => lastName = v;

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.RaiseOnEditNameClicked();

			Assert.IsNotNull(observedId, "Dialog の PushAndAwait が呼ばれていない");
			Assert.AreEqual("Old", observedId.InitialText, "現在の名前が initial として渡される");
			Assert.IsNotNull(posted, "PostProfile が呼ばれていない");
			Assert.AreEqual("user-001", posted.userId);
			Assert.AreEqual("New", posted.name);
			Assert.AreEqual(3, posted.level);
			Assert.AreEqual("New", lastName);
		}

		[Test]
		public async Task EditName_Cancelled_DoesNotPost()
		{
			_api.GetProfileFunc = (userId, ct) => UniTask.FromResult(new ProfileResponse
			{
				userId = "user-001",
				name   = "Keep",
				level  = 1,
			});

			// 結果 null = キャンセル相当。AwaitedIds で「開かれた」事実は検証したい
			_dialogNav.TrackPushAndAwait<InputDialogId, InputDialogResult>();
			int postCount = 0;
			_api.PostProfileFunc = (dto, ct) =>
			{
				postCount++;
				return UniTask.FromResult(new ProfileResponse { userId = dto.userId, name = dto.name, level = dto.level });
			};

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.RaiseOnEditNameClicked();

			Assert.AreEqual(0, postCount, "キャンセル時は POST しない");
			Assert.AreEqual(1, _dialogNav.AwaitedIds().Count, "Dialog は開かれている");
		}

		[Test]
		public async Task SetSaving_IsToggledAroundInitialFetch()
		{
			var states = new System.Collections.Generic.List<bool>();
			_view.SetSavingFunc = v => states.Add(v);

			_api.GetProfileFunc = (userId, ct) => UniTask.FromResult(new ProfileResponse
			{
				userId = "user-001",
				name   = "Bob",
				level  = 1,
			});

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			Assert.AreEqual(new[] { true, false }, states.ToArray());
		}

		[Test]
		public async Task OnAfterUnload_UnsubscribesHandlers()
		{
			_api.GetProfileFunc = (userId, ct) => UniTask.FromResult(new ProfileResponse
			{
				userId = "user-001",
				name   = "C",
				level  = 1,
			});

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);
			await ScreenTesting.PopAsync(presenter);

			_view.RaiseOnEditNameClicked();

			Assert.AreEqual(0, _dialogNav.AwaitedIds().Count, "Unload 後に Edit クリックが leak しないこと");
		}
	}
}
