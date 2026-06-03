using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Sample.Api;

namespace Sample
{
	/// <summary>
	/// ガチャトップ画面ローカルの Model。画面の論理状態（ガチャ一覧 / 現在 index / busy）と、
	/// 画面横断状態の橋渡し（<see cref="UserDataHolder.Money"/>）を Observable で公開する。
	/// <para>
	/// 状態 mutate の正解パスを 1 つに絞るため：
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// 公開メソッドはユースケース粒度（<see cref="Initialize"/> / <see cref="MoveTo"/> / <see cref="Charge"/> / <see cref="Pull"/>）
	/// </description></item>
	/// <item><description>
	/// 内部 mutation は property の private setter のみ（バリデーションは setter に集約）
	/// </description></item>
	/// <item><description>
	/// <c>Set*</c> 系メソッドは廃止、<c>_rp.Value =</c> の直接代入も書き手の規律で禁止
	/// </description></item>
	/// </list>
	/// <para>
	/// API 呼び出し + 状態書き戻し + busy 制御も Model 内で完結する（Feature は UI orchestration だけ）。
	/// </para>
	/// </summary>
	public sealed class GachaTopModel : IDisposable
	{
		readonly UserDataHolder _user;
		readonly IApiClient _api;
		readonly ReactiveProperty<int> _currentIndex = new(0);
		readonly ReactiveProperty<bool> _busy = new(false);
		readonly ReactiveProperty<int> _money;
		readonly Action _onUserMoneyChanged;

		public GachaInfoResponse[] Gachas { get; private set; } = Array.Empty<GachaInfoResponse>();
		public GachaInfoResponse Current => Gachas.Length > 0 ? Gachas[CurrentIndex] : null;

		public int CurrentIndex
		{
			get => _currentIndex.Value;
			private set
			{
				if (value < 0 || value >= Gachas.Length) return;
				_currentIndex.Value = value;
			}
		}

		public bool Busy
		{
			get => _busy.Value;
			private set => _busy.Value = value;
		}

		public int Money => _money.Value;

		public Observable<int>  CurrentIndexObservable => _currentIndex;
		public Observable<bool> BusyObservable         => _busy;
		public Observable<int>  MoneyObservable        => _money;

		public GachaTopModel(UserDataHolder user, IApiClient api)
		{
			_user = user;
			_api = api;
			_money = new ReactiveProperty<int>(_user.Money);
			// Holder は plain Action のままにしてあり、Model 側で RP に橋渡しする
			_onUserMoneyChanged = () => _money.Value = _user.Money;
			_user.OnMoneyChanged += _onUserMoneyChanged;
		}

		public void Dispose()
		{
			_user.OnMoneyChanged -= _onUserMoneyChanged;
			_currentIndex.Dispose();
			_busy.Dispose();
			_money.Dispose();
		}

		/// <summary>ガチャ一覧を取得して初期化する。</summary>
		public async UniTask Initialize(CancellationToken ct)
		{
			var list = await _api.GetGachaList(ct);
			Gachas = list.gachas ?? Array.Empty<GachaInfoResponse>();
			CurrentIndex = 0;
		}

		/// <summary>現在ガチャの index を変更（範囲外は無視）。</summary>
		public void MoveTo(int index) => CurrentIndex = index;

		/// <summary>所持金加算（課金を模す）。</summary>
		public async UniTask Charge(int amount, CancellationToken ct)
		{
			if (Busy) return;
			Busy = true;
			try
			{
				var resp = await _api.ChargeMoney(new ChargeRequest { amount = amount }, ct);
				_user.SetMoney(resp.money); // → Holder.OnMoneyChanged → Model._money RP に伝播
			}
			finally
			{
				Busy = false;
			}
		}

		/// <summary>現在ガチャを <paramref name="count"/> 回引く。所持金は Holder 経由で更新される。</summary>
		public async UniTask<GachaPullResponse> Pull(int count, CancellationToken ct)
		{
			if (Busy) throw new InvalidOperationException("already busy");
			var current = Current ?? throw new InvalidOperationException("no current gacha");
			Busy = true;
			try
			{
				var resp = await _api.PullGacha(
					new GachaPullRequest { gachaId = current.id, count = count }, ct);
				_user.SetMoney(resp.money);
				return resp;
			}
			finally
			{
				Busy = false;
			}
		}
	}
}
