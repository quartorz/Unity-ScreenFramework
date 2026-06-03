using MockGenerator;
using UnityEngine;
using UnityEngine.UI;

namespace Sample
{
	/// <summary>
	/// ガチャトップ画面。ヘッダー（所持金 + 課金ボタン）と本体（ガチャ切替 + 1連/10連）を含む。
	/// 各 UI 要素は Input/Output 付きのサブコンポーネント
	/// （<see cref="GachaTopHeaderView"/> / <see cref="SampleButton"/>）として持ち、
	/// フィールドに [Input]/[Output] を付けて mock 側に getter/setter を生成させる。
	/// Presenter 側は <see cref="GachaTopPresenter"/> が root としてサブ Presenter (Feature) に View を分配する。
	/// </summary>
	[RequireComponent(typeof(RectTransform))]
	[GenerateMockView, GenerateViewInterfaces]
	public sealed partial class GachaTopView : MonoBehaviour
	{
		[Input, Output, SerializeField] GachaTopHeaderView _header;

		[SerializeField] Text _gachaNameLabel;
		[SerializeField] Text _indexLabel; // "1 / 3" など

		[Input, Output, SerializeField] SampleButton _prevButton;
		[Input, Output, SerializeField] SampleButton _nextButton;
		// 1連 / 10連 ボタンはラベルにコストを焼くので Text も Output 経由で書き換える
		[Input, Output, SerializeField] SampleButton _pull1Button;
		[Input, Output, SerializeField] SampleButton _pull10Button;
		[Input, SerializeField] SampleButton _backButton;

		[Output] public void SetGachaName(string name)
		{
			if (_gachaNameLabel != null) _gachaNameLabel.text = name;
		}

		[Output] public void SetIndex(int current, int total)
		{
			if (_indexLabel != null) _indexLabel.text = $"{current + 1} / {total}";
		}
	}
}
