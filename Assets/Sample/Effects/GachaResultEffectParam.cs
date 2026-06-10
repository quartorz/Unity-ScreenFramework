using ScreenFramework;

namespace Sample.Effects
{
	/// <summary>
	/// ガチャ結果遷移時に Push 側から Effect へ渡す runtime パラメータ。
	/// Identifier を直接覗くより、明示的に「演出が必要な値」だけを切り出すことで
	/// 「演出用に Identifier の中身を増やしていく」事故を避ける。
	/// </summary>
	public sealed class GachaResultEffectParam : INavigationData
	{
		public int MaxRarity { get; }
		public int ItemCount { get; }

		public GachaResultEffectParam(int maxRarity, int itemCount)
		{
			MaxRarity = maxRarity;
			ItemCount = itemCount;
		}
	}
}
