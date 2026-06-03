using MockGenerator;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Sample
{
	/// <summary>
	/// ガチャトップ画面。ヘッダー（所持金 + 課金ボタン）と本体（ガチャ切替 + 1連/10連）を含む 1 つの View。
	/// Presenter 側は <see cref="GachaTopPresenter"/> が root としてサブ Presenter (Feature) に View を分配する。
	/// </summary>
	[RequireComponent(typeof(RectTransform))]
	[GenerateMockView, GenerateViewInterfaces]
	public sealed partial class GachaTopHeaderView : MonoBehaviour
	{
		[SerializeField] Text _moneyLabel;
		[Output, SerializeField] SampleButton _chargeButton;

		[Input] public event Action OnChargeClicked;

		void Awake()
		{
			if (_chargeButton != null) _chargeButton.OnClicked += () => OnChargeClicked?.Invoke();
		}

		[Output] public void SetMoney(int money)
		{
			if (_moneyLabel != null) _moneyLabel.text = $"{money:N0} G";
		}
	}
}
