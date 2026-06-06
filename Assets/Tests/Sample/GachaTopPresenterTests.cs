using System.Collections.Generic;
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
	/// <see cref="GachaTopPresenter"/> + 配下の Feature + 実 <see cref="GachaTopModel"/> を
	/// MockView / Mock Service / MockScreenNavigator で end-to-end に駆動するテスト。
	/// Model は real。サブコンポーネントは MockView を個別 new して手繋ぎする。
	/// </summary>
	public sealed class GachaTopPresenterTests
	{
		MockGachaService _gachaApi;
		MockUserService _userApi;
		MockScreenNavigator _pageNav;
		MockScreenNavigator _dialogNav;
		SampleRegistry _registry;
		List<IScreenIdentifier> _pushed;

		MockView.Sample.MockGachaTopView _view;

		[SetUp]
		public void SetUp()
		{
			_gachaApi = new MockGachaService();
			_userApi = new MockUserService();
			_gachaApi.ListFunc = opt => UniTask.FromResult(MakeGachaList());

			_pushed = new List<IScreenIdentifier>();
			_pageNav = new MockScreenNavigator
			{
				PushFunc = (id, opt, ct) =>
				{
					_pushed.Add(id);
					return UniTask.FromResult<IScreenEntry>(null);
				},
			};
			_dialogNav = new MockScreenNavigator();

			ScreenNavigator.Override(
				page: _pageNav,
				dialog: _dialogNav,
				systemDialog: new MockScreenNavigator());

			_registry = new SampleRegistry(
				useMockViews: true,
				gacha: _gachaApi,
				user: _userApi,
				profile: new MockProfileService(),
				master: new MockMasterService());
			_registry.UserData.SetInfo(new UserInfo
			{
				UserId = "u1",
				Name = "x",
				Level = 1,
				Money = 1000,
			});

			_view = new MockView.Sample.MockGachaTopView
			{
				header = new MockView.Sample.MockGachaTopHeaderView
				{
					chargeButton = new MockView.Sample.MockSampleButton(),
				},
				prevButton = new MockView.Sample.MockSampleButton(),
				nextButton = new MockView.Sample.MockSampleButton(),
				pull1Button = new MockView.Sample.MockSampleButton(),
				pull10Button = new MockView.Sample.MockSampleButton(),
				backButton = new MockView.Sample.MockSampleButton(),
			};

			// 触る Func の no-op 既定（NRE 回避）
			_view.SetGachaNameFunc = _ => { };
			_view.SetIndexFunc = (_, _) => { };
			_view.header.SetMoneyFunc = _ => { };
		}

		static GachaListResponse MakeGachaList() => new GachaListResponse
		{
			gachas = new[]
			{
				new GachaInfoResponse { id = "a", name = "ガチャA", cost1 = 100, cost10 = 900 },
				new GachaInfoResponse { id = "b", name = "ガチャB", cost1 = 200, cost10 = 1800 },
				new GachaInfoResponse { id = "c", name = "ガチャC", cost1 = 300, cost10 = 2700 },
			},
		};

		GachaTopPresenter NewPresenter() => new GachaTopPresenter().WithServices(_registry);

		// ----- Initial render -----

		[Test]
		public async Task OnAfterLoad_RendersInitialState()
		{
			int? money = null;
			string gachaName = null;
			(int cur, int tot)? indexPair = null;

			_view.header.SetMoneyFunc = m => money = m;
			_view.SetGachaNameFunc = n => gachaName = n;
			_view.SetIndexFunc = (c, t) => indexPair = (c, t);

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			Assert.AreEqual(1000, money);
			Assert.AreEqual("ガチャA", gachaName);
			Assert.AreEqual((0, 3), indexPair);
			Assert.AreEqual("1連\n100 G", _view.pull1Button.Text);
			Assert.AreEqual("10連\n900 G", _view.pull10Button.Text);
			Assert.AreEqual(false, _view.prevButton.Interactable, "index 0 では prev は disabled");
			Assert.AreEqual(true, _view.nextButton.Interactable, "index 0 では next は enabled");
			Assert.AreEqual(true, _view.pull1Button.Interactable, "money(1000) >= cost1(100)");
			Assert.AreEqual(true, _view.pull10Button.Interactable, "money(1000) >= cost10(900)");
			Assert.AreEqual(true, _view.header.chargeButton.Interactable, "busy=false で charge は enabled");
		}

		// ----- Money sync -----

		[Test]
		public async Task HolderMoneyChange_FlowsToHeader()
		{
			var moneyValues = new List<int>();
			_view.header.SetMoneyFunc = m => moneyValues.Add(m);

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_registry.UserData.SetMoney(2500);

			Assert.Contains(2500, moneyValues);
		}

		// ----- Charge -----

		[Test]
		public async Task ChargeButton_Confirmed_CallsApi_AndUpdatesMoney()
		{
			ChargeRequest captured = null;
			_userApi.ChargeFunc = (req, opt) =>
			{
				captured = req;
				return UniTask.FromResult(new ChargeResponse { money = 2000 });
			};
			_dialogNav.SetupPushAndAwait<MessageDialogId, MessageDialogResult>(_ => new MessageDialogResult(1));

			var moneyValues = new List<int>();
			_view.header.SetMoneyFunc = m => moneyValues.Add(m);

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.header.RaiseOnChargeClicked();

			Assert.IsNotNull(captured, "Charge が呼ばれる");
			Assert.AreEqual(1000, captured.amount, "ChargeAmount=1000 が渡る");
			Assert.AreEqual(2000, _registry.UserData.Money, "Holder.Money が API レスポンスで更新");
			Assert.Contains(2000, moneyValues, "header.SetMoney(2000) が呼ばれる");
		}

		[Test]
		public async Task ChargeFlow_TransitionsChargeButtonInteractable()
		{
			// 課金中は Model.Busy=true → chargeButton.Interactable=false に倒れ、
			// 完了で true に戻る一連のシーケンスを観測する。
			// 最終状態だけ見たければ Mock の auto-property（_view.header.chargeButton.Interactable）で十分だが、
			// 「途中で false に落ちたこと」を検証したい場面では setter 観測用の OnInteractableSet を使う。
			_userApi.ChargeFunc = (req, opt) => UniTask.FromResult(new ChargeResponse { money = 2000 });
			_dialogNav.SetupPushAndAwait<MessageDialogId, MessageDialogResult>(_ => new MessageDialogResult(1));

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			// 既に初期反映で 1 回 set されているはず。ここからシーケンス記録開始
			var transitions = new List<bool>();
			_view.header.chargeButton.OnInteractableSet = b => transitions.Add(b);

			_view.header.RaiseOnChargeClicked();

			// Charge 開始で false → 完了で true（OK の試行を含めて Busy が true→false 遷移）
			Assert.AreEqual(new[] { false, true }, transitions.ToArray(),
				"課金中 Interactable=false、完了で true に戻る");
			Assert.AreEqual(true, _view.header.chargeButton.Interactable, "最終状態は enabled");
		}

		[Test]
		public async Task ChargeButton_Cancelled_DoesNotCallApi()
		{
			var calls = 0;
			_userApi.ChargeFunc = (req, opt) =>
			{
				calls++;
				return UniTask.FromResult(new ChargeResponse { money = 9999 });
			};
			// Cancel 相当（SetResult されない）
			_dialogNav.TrackPushAndAwait<MessageDialogId, MessageDialogResult>();

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.header.RaiseOnChargeClicked();

			Assert.AreEqual(0, calls, "キャンセル時は Charge 呼ばれない");
			Assert.AreEqual(1, _dialogNav.AwaitedIds().Count, "Dialog は開かれている");
			Assert.AreEqual(1000, _registry.UserData.Money, "所持金は変わらない");
		}

		// ----- Picker -----

		[Test]
		public async Task NextButton_UpdatesCurrentGacha()
		{
			var names = new List<string>();
			var indices = new List<(int, int)>();
			_view.SetGachaNameFunc = n => names.Add(n);
			_view.SetIndexFunc = (c, t) => indices.Add((c, t));

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.nextButton.RaiseOnClicked();

			Assert.Contains("ガチャB", names, "次ガチャへ切り替わる");
			Assert.Contains((1, 3), indices);
		}

		[Test]
		public async Task Pull1Button_Confirmed_CallsApi_AndPushesResultScreen()
		{
			var pullResp = new GachaPullResponse
			{
				items = new[] { new PulledItemResponse { code = "x", name = "X", rarity = 3 } },
				money = 900,
			};
			GachaPullRequest pullReq = null;
			_gachaApi.PullFunc = (req, opt) =>
			{
				pullReq = req;
				return UniTask.FromResult(pullResp);
			};
			_dialogNav.SetupPushAndAwait<MessageDialogId, MessageDialogResult>(_ => new MessageDialogResult(1));

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.pull1Button.RaiseOnClicked();

			Assert.IsNotNull(pullReq);
			Assert.AreEqual("a", pullReq.gachaId, "現在ガチャ id");
			Assert.AreEqual(1, pullReq.count);
			Assert.AreEqual(900, _registry.UserData.Money);

			// GachaResultScreenId が Push されている
			Assert.AreEqual(1, _pushed.Count);
			var resultId = _pushed[0] as GachaResultScreenId;
			Assert.IsNotNull(resultId, "GachaResultScreenId が Push される");
			Assert.AreSame(pullResp, resultId.Result, "Pull レスポンスがそのまま渡る");
		}

		[Test]
		public async Task Pull1Button_Cancelled_DoesNotCallApi_AndDoesNotPush()
		{
			var calls = 0;
			_gachaApi.PullFunc = (req, opt) =>
			{
				calls++;
				return UniTask.FromResult(new GachaPullResponse { items = System.Array.Empty<PulledItemResponse>(), money = 0 });
			};
			_dialogNav.TrackPushAndAwait<MessageDialogId, MessageDialogResult>();

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.pull1Button.RaiseOnClicked();

			Assert.AreEqual(0, calls);
			Assert.AreEqual(0, _pushed.Count, "結果画面 Push されない");
		}

		// ----- Unload leak -----

		[Test]
		public async Task OnAfterUnload_HolderChangeDoesNotReachView()
		{
			var moneyValues = new List<int>();
			_view.header.SetMoneyFunc = m => moneyValues.Add(m);

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);
			await ScreenTesting.PopAsync(presenter);

			var countBefore = moneyValues.Count;
			_registry.UserData.SetMoney(9999);

			Assert.AreEqual(countBefore, moneyValues.Count,
				"Unload 後の Holder.SetMoney は Model.Dispose 経由で unsubscribe されているはず");
		}
	}
}
