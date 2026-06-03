using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Sample.Dialogs;
using ScreenFramework;
using UnityEngine;

namespace Sample
{
	/// <summary>
	/// ガチャトップ画面の「ヘッダー（所持金表示 + 課金）」を担当する Feature。
	/// 状態と通信は <see cref="GachaTopModel"/> に集約済みなので、ここでは
	/// UI orchestration（ダイアログ確認 / エラー表示）と購読しかしない。
	/// </summary>
	sealed class MoneyHeaderFeature
	{
		const int ChargeAmount = 1000;

		readonly MockView.Sample.IGachaTopViewInput _in;
		readonly MockView.Sample.IGachaTopHeaderViewOutput _out;
		readonly GachaTopModel _model;
		CompositeDisposable _bag;

		public MoneyHeaderFeature(
			MockView.Sample.IGachaTopViewInput input,
			MockView.Sample.IGachaTopHeaderViewOutput output,
			GachaTopModel model)
		{
			_in = input;
			_out = output;
			_model = model;
		}

		public void Attach()
		{
			_bag = new CompositeDisposable();
			_in.header.OnChargeClicked += OnCharge;

			// ReactiveProperty は Subscribe 時に現在値を流す → 初期反映を手動でやらなくていい
			_model.MoneyObservable.Subscribe(m => _out.SetMoney(m)).AddTo(_bag);
			_model.BusyObservable.Subscribe(b => _out.chargeButton.Interactable = !b).AddTo(_bag);
		}

		public void Detach()
		{
			if (_in != null) _in.header.OnChargeClicked -= OnCharge;
			_bag?.Dispose();
			_bag = null;
		}

		void OnCharge()
		{
			if (_model.Busy) return;
			ChargeAsync().Forget();
		}

		async UniTaskVoid ChargeAsync()
		{
			var ok = await MessageDialogs.ConfirmAsync(
				title: "課金確認",
				message: $"{ChargeAmount:N0} G 分課金しますか？",
				okText: "課金する");
			if (!ok) return;

			try
			{
				await _model.Charge(ChargeAmount, CancellationToken.None);
			}
			catch (OperationCanceledException) { }
			catch (Exception e)
			{
				Debug.LogError($"[MoneyHeaderFeature] charge failed: {e.Message}");
			}
		}
	}
}
