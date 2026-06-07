using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;

namespace Sample
{
	/// <summary>
	/// ガチャトップの root Presenter。
	/// 画面ローカル Model (<see cref="GachaTopModel"/>) を生成して 2 つの Feature に配り、Attach させる：
	/// <list type="bullet">
	/// <item><description>
	/// <see cref="MoneyHeaderFeature"/>: 所持金表示 + 課金フロー
	/// </description></item>
	/// <item><description>
	/// <see cref="GachaPickerFeature"/>: ガチャ切替 + 1連 / 10連 Pull
	/// </description></item>
	/// </list>
	/// 通信・状態更新・busy 制御は Model に閉じ、Feature は UI orchestration のみ。
	/// </summary>
	public sealed class GachaTopPresenter
		: SamplePresenter<
			MockView.Sample.IGachaTopViewInput,
			MockView.Sample.IGachaTopViewOutput>
	{
		GachaTopModel _model;
		MoneyHeaderFeature _money;
		GachaPickerFeature _picker;

		protected override async UniTask OnAfterLoad(IScreenDataReader reader, CancellationToken ct)
		{
			In.backButton.OnClicked += OnBack;

			_model = new GachaTopModel(Registry.UserData, Registry.Gacha, Registry.User);
			try
			{
				await _model.Initialize(ct);
			}
			catch
			{
				_model.Dispose();
				_model = null;
				throw;
			}

			_money = new MoneyHeaderFeature(In, Out.header, _model);
			_picker = new GachaPickerFeature(In, Out, _model);
			_money.Attach();
			_picker.Attach();
		}

		protected override UniTask OnAfterUnload(IScreenDataWriter writer, CancellationToken ct)
		{
			if (In != null) In.backButton.OnClicked -= OnBack;
			_money?.Detach();
			_picker?.Detach();
			_model?.Dispose();
			return UniTask.CompletedTask;
		}

		void OnBack() => ScreenNavigator.Page.Pop().Forget();
	}
}
