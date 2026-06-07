using System;
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
	/// <see cref="IProfileService"/> は MockGenerator 生成の <see cref="MockProfileService"/>、
	/// Dialog Navigator は <see cref="MockScreenNavigatorExtensions.SetupPushAndAwait{TId,TResult}"/>
	/// で InputDialogId に対するレスポンスを直接登録する。
	/// </summary>
	public sealed class ProfilePresenterPureTests
	{
		MockProfileService _profileApi;
		MockScreenNavigator _pageNav;
		MockScreenNavigator _dialogNav;
		MockView.Sample.MockProfileView _view;

		[SetUp]
		public void SetUp()
		{
			_profileApi = new MockProfileService();
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
			.WithServices(new SampleRegistry(
				useMockViews: true,
				gacha: new MockGachaService(),
				user: new MockUserService(),
				profile: _profileApi,
				master: new MockMasterService()));

		[Test]
		public async Task OnAfterLoad_FetchesProfile_AndAppliesToView()
		{
			string capturedName = null;
			int capturedLevel = 0;
			string capturedUserId = null;
			_view.SetNameFunc = v => capturedName = v;
			_view.SetLevelFunc = v => capturedLevel = v;
			_view.SetUserIdFunc = v => capturedUserId = v;

			_profileApi.GetFunc = (userId, opt) => UniTask.FromResult(new ProfileResponse
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
			_profileApi.GetFunc = (userId, opt) => UniTask.FromResult(new ProfileResponse
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
			_profileApi.PostFunc = (req, opt) =>
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
			Assert.IsNotNull(posted, "Post が呼ばれていない");
			Assert.AreEqual("user-001", posted.userId);
			Assert.AreEqual("New", posted.name);
			Assert.AreEqual(3, posted.level);
			Assert.AreEqual("New", lastName);
		}

		[Test]
		public async Task EditName_Cancelled_DoesNotPost()
		{
			_profileApi.GetFunc = (userId, opt) => UniTask.FromResult(new ProfileResponse
			{
				userId = "user-001",
				name   = "Keep",
				level  = 1,
			});

			// 結果 null = キャンセル相当。AwaitedIds で「開かれた」事実は検証したい
			_dialogNav.TrackPushAndAwait<InputDialogId, InputDialogResult>();
			int postCount = 0;
			_profileApi.PostFunc = (dto, opt) =>
			{
				postCount++;
				return UniTask.FromResult(new ProfileResponse { userId = dto.userId, name = dto.name, level = dto.level });
			};

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.RaiseOnEditNameClicked();

			Assert.AreEqual(0, postCount, "キャンセル時は Post しない");
			Assert.AreEqual(1, _dialogNav.AwaitedIds().Count, "Dialog は開かれている");
		}

		[Test]
		public async Task SetSaving_IsToggledAroundInitialFetch()
		{
			var states = new System.Collections.Generic.List<bool>();
			_view.SetSavingFunc = v => states.Add(v);

			_profileApi.GetFunc = (userId, opt) => UniTask.FromResult(new ProfileResponse
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
			_profileApi.GetFunc = (userId, opt) => UniTask.FromResult(new ProfileResponse
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

		// ========================================================================
		// 通信エラー系
		//
		// Service mock から例外が来る = HttpClient 内のリトライダイアログでユーザーが
		// 「閉じる/Cancel」を選んで諦めた状態と等価（Retry が選ばれていればここに例外は来ない）。
		// HttpClient のリトライループ自体は HttpClient 層の責務なので別途統合テストで担保。
		// ========================================================================

		// ---------- OnAfterLoad ----------

		[Test]
		public void OnAfterLoad_GetThrowsApiException_RolledBackAsOCE_AndSavingReset()
		{
			var savingStates = new System.Collections.Generic.List<bool>();
			_view.SetSavingFunc = v => savingStates.Add(v);

			_profileApi.GetFunc = (userId, opt) => UniTask.FromException<ProfileResponse>(
				new ApiException(500, null, null, "server boom"));

			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; // Debug.LogError を fail させない

			var presenter = (IScreenPresenter)NewPresenter();

			Assert.CatchAsync<ApiException>(
				async () => await ScreenTesting.PushAsync(presenter, _view));

			Assert.AreEqual(new[] { true, false }, savingStates.ToArray(),
				"失敗時も SetSaving(false) が呼ばれて UI がスピナーで固まらない");
		}

		[Test]
		public void OnAfterLoad_GetThrowsTransportException_RolledBackAsOCE_AndSavingReset()
		{
			var savingStates = new System.Collections.Generic.List<bool>();
			_view.SetSavingFunc = v => savingStates.Add(v);

			// HttpClient 内のリトライダイアログでユーザーが「閉じる」を選んだ状態に相当
			_profileApi.GetFunc = (userId, opt) => UniTask.FromException<ProfileResponse>(
				new ApiTransportException(TransportFailure.Network, "no net"));

			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			var presenter = (IScreenPresenter)NewPresenter();

			Assert.CatchAsync<ApiTransportException>(
				async () => await ScreenTesting.PushAsync(presenter, _view));

			Assert.AreEqual(new[] { true, false }, savingStates.ToArray());
		}

		[Test]
		public void OnAfterLoad_GetThrowsOCE_RethrownAsIs()
		{
			// 本物のキャンセルはそのまま OCE 透過（詰め替えではなく re-throw）
			var savingStates = new System.Collections.Generic.List<bool>();
			_view.SetSavingFunc = v => savingStates.Add(v);

			_profileApi.GetFunc = (userId, opt) => UniTask.FromCanceled<ProfileResponse>(
				new System.Threading.CancellationToken(canceled: true));

			var presenter = (IScreenPresenter)NewPresenter();

			// async Task のルール：throw した OCE がキャンセル済みトークンを持つと
			// Task が Canceled 状態に遷移し、await で TaskCanceledException（OCE のサブクラス）に化ける。
			// 1/2 番のテストは Presenter が new OperationCanceledException()（トークンなし）を投げるので
			// 素の OCE のまま出るが、3 番は GetFunc が canceled token 付きで投げるので TCE になる。
			// 全テストで同じ assertion を使うため、サブクラス許容の CatchAsync で統一。
			Assert.CatchAsync<OperationCanceledException>(
				async () => await ScreenTesting.PushAsync(presenter, _view));

			Assert.AreEqual(new[] { true, false }, savingStates.ToArray());
		}

		// ---------- EditName (Post) ----------

		[Test]
		public async Task EditName_PostThrowsApiException_SilentlySwallowed_AndBusyReset()
		{
			_profileApi.GetFunc = (userId, opt) => UniTask.FromResult(new ProfileResponse
			{
				userId = "user-001", name = "Old", level = 1,
			});
			_dialogNav.SetupPushAndAwait<InputDialogId, InputDialogResult>(_ => new InputDialogResult("New"));

			var postCalls = 0;
			_profileApi.PostFunc = (req, opt) =>
			{
				postCalls++;
				return UniTask.FromException<ProfileResponse>(new ApiException(409, null, null, "conflict"));
			};

			var savingStates = new System.Collections.Generic.List<bool>();
			_view.SetSavingFunc = v => savingStates.Add(v);

			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.RaiseOnEditNameClicked();

			Assert.AreEqual(1, postCalls);
			// 初期 true→false（fetch）、edit 開始 true→失敗で false
			Assert.AreEqual(new[] { true, false, true, false }, savingStates.ToArray(),
				"Post 失敗でも finally で SetSaving(false) が走る");

			// busy フラグが解除されたことを「もう一度クリックで Post が呼ばれる」で確認
			_view.RaiseOnEditNameClicked();
			Assert.AreEqual(2, postCalls, "busy が解除されているので 2 回目の編集が走る");
		}

		[Test]
		public async Task EditName_PostThrowsTransportException_SilentlySwallowed_AndBusyReset()
		{
			_profileApi.GetFunc = (userId, opt) => UniTask.FromResult(new ProfileResponse
			{
				userId = "user-001", name = "Old", level = 1,
			});
			_dialogNav.SetupPushAndAwait<InputDialogId, InputDialogResult>(_ => new InputDialogResult("New"));

			var postCalls = 0;
			_profileApi.PostFunc = (req, opt) =>
			{
				postCalls++;
				return UniTask.FromException<ProfileResponse>(
					new ApiTransportException(TransportFailure.Timeout, "timeout"));
			};

			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.RaiseOnEditNameClicked();

			Assert.AreEqual(1, postCalls);

			_view.RaiseOnEditNameClicked();
			Assert.AreEqual(2, postCalls, "Transport 失敗後も busy 解除されて再試行できる");
		}

		[Test]
		public async Task EditName_PostThrowsOCE_SilentlySwallowed_AndBusyReset()
		{
			_profileApi.GetFunc = (userId, opt) => UniTask.FromResult(new ProfileResponse
			{
				userId = "user-001", name = "Old", level = 1,
			});
			_dialogNav.SetupPushAndAwait<InputDialogId, InputDialogResult>(_ => new InputDialogResult("New"));

			var postCalls = 0;
			_profileApi.PostFunc = (req, opt) =>
			{
				postCalls++;
				return UniTask.FromCanceled<ProfileResponse>(
					new System.Threading.CancellationToken(canceled: true));
			};

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.RaiseOnEditNameClicked();

			Assert.AreEqual(1, postCalls);

			_view.RaiseOnEditNameClicked();
			Assert.AreEqual(2, postCalls, "OCE 後も busy 解除されて再試行できる");
		}
	}
}
