using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using Sample;
using Sample.Api;

namespace Tests
{
	/// <summary>
	/// <see cref="GachaTopModel"/> の純粋 unit テスト。View / Navigator / Framework なし。
	/// <see cref="UserDataHolder"/> は実装、<see cref="IGachaService"/> / <see cref="IUserService"/> は Mock。
	/// </summary>
	public sealed class GachaTopModelTests
	{
		MockGachaService _gacha;
		MockUserService _user;
		UserDataHolder _holder;
		GachaTopModel _model;

		[SetUp]
		public void SetUp()
		{
			_gacha = new MockGachaService();
			_user = new MockUserService();
			_holder = new UserDataHolder();
			_holder.SetInfo(new UserInfo { UserId = "u1", Name = "x", Level = 1, Money = 1000 });
			_gacha.ListFunc = opt => UniTask.FromResult(MakeGachaList());
			_model = new GachaTopModel(_holder, _gacha, _user);
		}

		[TearDown]
		public void TearDown()
		{
			_model?.Dispose();
			_model = null;
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

		// ---------- Initialize ----------

		[Test]
		public async Task Initialize_LoadsGachasAndResetsIndex()
		{
			var calls = 0;
			_gacha.ListFunc = opt => { calls++; return UniTask.FromResult(MakeGachaList()); };

			await _model.Initialize(default);

			Assert.AreEqual(1, calls);
			Assert.AreEqual(3, _model.Gachas.Length);
			Assert.AreEqual(0, _model.CurrentIndex);
			Assert.AreEqual("a", _model.Current.id);
		}

		// ---------- MoveTo ----------

		[Test]
		public async Task MoveTo_Valid_ChangesIndexAndFiresObservable()
		{
			await _model.Initialize(default);

			var observed = new List<int>();
			using var _ = _model.CurrentIndexObservable.Subscribe(i => observed.Add(i));

			_model.MoveTo(1);

			Assert.AreEqual(1, _model.CurrentIndex);
			// Subscribe で初期値 0、その後 MoveTo(1) で 1
			Assert.AreEqual(new[] { 0, 1 }, observed.ToArray());
		}

		[Test]
		public async Task MoveTo_Negative_IsIgnored()
		{
			await _model.Initialize(default);

			var observed = new List<int>();
			using var _ = _model.CurrentIndexObservable.Subscribe(i => observed.Add(i));

			_model.MoveTo(-1);

			Assert.AreEqual(0, _model.CurrentIndex);
			// 初期値の 1 個だけ
			Assert.AreEqual(new[] { 0 }, observed.ToArray());
		}

		[Test]
		public async Task MoveTo_OutOfRange_IsIgnored()
		{
			await _model.Initialize(default);

			_model.MoveTo(99);

			Assert.AreEqual(0, _model.CurrentIndex);
		}

		// ---------- Charge ----------

		[Test]
		public async Task Charge_Success_UpdatesHolderAndTogglesBusy()
		{
			ChargeRequest captured = null;
			_user.ChargeFunc = (req, opt) =>
			{
				captured = req;
				return UniTask.FromResult(new ChargeResponse { money = 1500 });
			};

			var busyStates = new List<bool>();
			using var _ = _model.BusyObservable.Subscribe(b => busyStates.Add(b));

			await _model.Charge(500, default);

			Assert.IsNotNull(captured);
			Assert.AreEqual(500, captured.amount);
			Assert.AreEqual(1500, _holder.Money);
			Assert.AreEqual(1500, _model.Money);
			// 初期 false → 課金中 true → 完了 false
			Assert.AreEqual(new[] { false, true, false }, busyStates.ToArray());
		}

		[Test]
		public async Task Charge_WhileBusy_ReturnsImmediately()
		{
			var calls = 0;
			var tcs = new UniTaskCompletionSource<ChargeResponse>();
			_user.ChargeFunc = (req, opt) =>
			{
				calls++;
				return tcs.Task;
			};

			var first = _model.Charge(500, default); // hang
			Assert.AreEqual(1, calls);
			Assert.IsTrue(_model.Busy);

			await _model.Charge(500, default); // busy 中なので即 return
			Assert.AreEqual(1, calls, "busy 中の Charge は API を呼ばない");

			tcs.TrySetResult(new ChargeResponse { money = 1500 });
			await first;
			Assert.IsFalse(_model.Busy);
		}

		// ---------- Pull ----------

		[Test]
		public async Task Pull_Success_UpdatesHolderAndReturnsResp()
		{
			await _model.Initialize(default);
			_model.MoveTo(1); // ガチャB を選択（cost1=200）

			GachaPullRequest captured = null;
			_gacha.PullFunc = (req, opt) =>
			{
				captured = req;
				return UniTask.FromResult(new GachaPullResponse
				{
					items = new[] { new PulledItemResponse { code = "x", name = "X", rarity = 3 } },
					money = 800,
				});
			};

			var busyStates = new List<bool>();
			using var _ = _model.BusyObservable.Subscribe(b => busyStates.Add(b));

			var resp = await _model.Pull(1, default);

			Assert.IsNotNull(captured);
			Assert.AreEqual("b", captured.gachaId);
			Assert.AreEqual(1, captured.count);
			Assert.AreEqual(800, _holder.Money);
			Assert.AreEqual(800, _model.Money);
			Assert.AreEqual(1, resp.items.Length);
			Assert.AreEqual(new[] { false, true, false }, busyStates.ToArray());
		}

		[Test]
		public async Task Pull_WhileBusy_Throws()
		{
			await _model.Initialize(default);
			var tcs = new UniTaskCompletionSource<GachaPullResponse>();
			_gacha.PullFunc = (req, opt) => tcs.Task;

			var first = _model.Pull(1, default); // hang
			Assert.IsTrue(_model.Busy);

			Assert.ThrowsAsync<InvalidOperationException>(async () => await _model.Pull(1, default));

			tcs.TrySetResult(new GachaPullResponse { items = Array.Empty<PulledItemResponse>(), money = 900 });
			await first;
		}

		[Test]
		public void Pull_NoCurrent_Throws()
		{
			// Initialize しないので Gachas は空、Current は null
			Assert.ThrowsAsync<InvalidOperationException>(async () => await _model.Pull(1, default));
			Assert.IsFalse(_model.Busy, "例外時に Busy が立ったままにならない");
		}

		[Test]
		public async Task Pull_ApiFailure_ResetsBusy()
		{
			await _model.Initialize(default);
			_gacha.PullFunc = (req, opt) => UniTask.FromException<GachaPullResponse>(new Exception("oops"));

			try
			{
				await _model.Pull(1, default);
				Assert.Fail("例外が出るはず");
			}
			catch (Exception e) when (e.Message == "oops")
			{
				// 期待通り
			}

			Assert.IsFalse(_model.Busy, "API 失敗後でも Busy は false に戻る");
		}

		// ---------- Holder 連携 / Dispose ----------

		[Test]
		public void HolderMoneyChange_FiresMoneyObservable()
		{
			var observed = new List<int>();
			using var _ = _model.MoneyObservable.Subscribe(m => observed.Add(m));

			_holder.SetMoney(500);
			_holder.SetMoney(700);

			// Subscribe で初期値 1000、その後 500 / 700
			Assert.AreEqual(new[] { 1000, 500, 700 }, observed.ToArray());
		}

		[Test]
		public void Dispose_UnsubscribesFromHolder()
		{
			var observed = new List<int>();
			_model.MoneyObservable.Subscribe(m => observed.Add(m));

			_holder.SetMoney(2000);
			var countBeforeDispose = observed.Count;

			_model.Dispose();
			_model = null; // TearDown で二重 Dispose しないように

			_holder.SetMoney(3000);

			Assert.AreEqual(countBeforeDispose, observed.Count,
				"Dispose 後の Holder.SetMoney は Model 経由で伝播しない");
		}
	}
}
