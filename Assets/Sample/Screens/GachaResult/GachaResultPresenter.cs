using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sample.Api;
using ScreenFramework;

namespace Sample
{
	public sealed class GachaResultPresenter
		: SamplePresenter<
			MockView.Sample.IGachaResultViewInput,
			MockView.Sample.IGachaResultViewOutput>
	{
		readonly GachaPullResponse _result;

		public GachaResultPresenter(GachaPullResponse result) { _result = result; }

		protected override UniTask OnAfterLoad(IScreenDataReader reader, CancellationToken ct)
		{
			In.OnBackClicked += OnBack;
			Out.SetTitle($"{_result.items.Length}連 結果");
			Out.SetItems(
				_result.items.Select(i => i.name).ToArray(),
				_result.items.Select(i => i.rarity).ToArray());
			return UniTask.CompletedTask;
		}

		protected override UniTask OnAfterUnload(IScreenDataWriter writer, CancellationToken ct)
		{
			if (In != null) In.OnBackClicked -= OnBack;
			return UniTask.CompletedTask;
		}

		void OnBack() => ScreenNavigator.Page.Pop().Forget();
	}
}
