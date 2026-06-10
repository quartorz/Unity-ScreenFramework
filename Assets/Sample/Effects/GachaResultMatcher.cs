using ScreenFramework;
using UnityEngine;

namespace Sample.Effects
{
	/// <summary>
	/// 「to が <see cref="GachaResultScreenId"/>」の遷移にマッチする粗い分岐。
	/// 細かい分岐（rarity ごとの演出差）は <see cref="GachaRarityEffect"/> 内で行う。
	/// </summary>
	[CreateAssetMenu(fileName = "GachaResultMatcher", menuName = "Sample/Effect Matchers/Gacha Result")]
	public sealed class GachaResultMatcher : ScreenMatcher
	{
		public override bool Match(IScreenIdentifier id, ITransitionContext ctx)
			=> id is GachaResultScreenId;
	}
}
