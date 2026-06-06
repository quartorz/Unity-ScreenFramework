using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Sample.Api;
using Sample.Dialogs;
using ScreenFramework;
using UnityEngine;

namespace Sample
{
	/// <summary>
	/// ガチャトップ画面の「ガチャ選択 + Pull」を担当する Feature。
	/// 状態と通信は <see cref="GachaTopModel"/> に集約済みなので、ここでは
	/// UI orchestration（ダイアログ確認 / 結果画面遷移）と購読しかしない。
	/// 通信エラーダイアログは <see cref="ApiErrorHandler"/> が SystemDialog 側で出す。
	/// </summary>
	sealed class GachaPickerFeature
	{
		readonly MockView.Sample.IGachaTopViewInput _in;
		readonly MockView.Sample.IGachaTopViewOutput _out;
		readonly GachaTopModel _model;
		CompositeDisposable _bag;

		public GachaPickerFeature(
			MockView.Sample.IGachaTopViewInput input,
			MockView.Sample.IGachaTopViewOutput output,
			GachaTopModel model)
		{
			_in = input;
			_out = output;
			_model = model;
		}

		public void Attach()
		{
			_bag = new CompositeDisposable();
			_in.prevButton.OnClicked += OnPrev;
			_in.nextButton.OnClicked += OnNext;
			_in.pull1Button.OnClicked += OnPull1;
			_in.pull10Button.OnClicked += OnPull10;

			// CurrentIndex 変化（= ガチャ切替）でガチャ名/コスト等を一気に塗り直す
			_model.CurrentIndexObservable.Subscribe(_ => RefreshCurrent()).AddTo(_bag);
			// Money / Busy 変化で Pull ボタン interactable だけ再計算
			_model.MoneyObservable.Subscribe(_ => RefreshPullButtons()).AddTo(_bag);
			_model.BusyObservable.Subscribe(_ => RefreshPullButtons()).AddTo(_bag);
		}

		public void Detach()
		{
			if (_in != null)
			{
				_in.prevButton.OnClicked -= OnPrev;
				_in.nextButton.OnClicked -= OnNext;
				_in.pull1Button.OnClicked -= OnPull1;
				_in.pull10Button.OnClicked -= OnPull10;
			}
			_bag?.Dispose();
			_bag = null;
		}

		void RefreshCurrent()
		{
			var current = _model.Current;
			if (current == null) return;
			_out.SetGachaName(current.name);
			_out.SetIndex(_model.CurrentIndex, _model.Gachas.Length);
			_out.pull1Button.Text = $"1連\n{current.cost1:N0} G";
			_out.pull10Button.Text = $"10連\n{current.cost10:N0} G";
			_out.prevButton.Interactable = _model.CurrentIndex > 0;
			_out.nextButton.Interactable = _model.CurrentIndex < _model.Gachas.Length - 1;
			RefreshPullButtons();
		}

		void RefreshPullButtons()
		{
			var current = _model.Current;
			if (current == null) return;
			_out.pull1Button.Interactable = !_model.Busy && _model.Money >= current.cost1;
			_out.pull10Button.Interactable = !_model.Busy && _model.Money >= current.cost10;
		}

		void OnPrev() => _model.MoveTo(_model.CurrentIndex - 1);
		void OnNext() => _model.MoveTo(_model.CurrentIndex + 1);
		void OnPull1() => PullAsync(1).Forget();
		void OnPull10() => PullAsync(10).Forget();

		async UniTaskVoid PullAsync(int count)
		{
			var current = _model.Current;
			if (current == null) return;
			var cost = count == 10 ? current.cost10 : current.cost1;
			if (_model.Money < cost || _model.Busy) return;

			var ok = await MessageDialogs.ConfirmAsync(
				title: current.name,
				message: $"{count} 連を {cost:N0} G で引きますか？",
				okText: "引く");
			if (!ok) return;

			try
			{
				var resp = await _model.Pull(count, CancellationToken.None);
				await ScreenNavigator.Page.Push(new GachaResultScreenId(resp));
			}
			catch (OperationCanceledException) { }
			catch (ApiException) { /* SystemDialog 表示済み */ }
			catch (ApiTransportException) { /* SystemDialog 表示済み */ }
			catch (Exception e)
			{
				Debug.LogError($"[GachaPickerFeature] pull failed: {e}");
			}
		}
	}
}
