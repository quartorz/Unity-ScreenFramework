using UnityEngine.AddressableAssets;

namespace ScreenFramework
{
	/// <summary>
	/// 遷移演出（Effect）の選択表。from / to / ctx から再生すべき Effect prefab を解決する。
	/// 0 件マッチは素通し（Effect なしで遷移続行）。
	/// </summary>
	public interface IEffectRegistry
	{
		/// <summary>
		/// from / to / ctx に合致する Effect prefab を解決する。一致無しなら <c>HasMatch = false</c>。
		/// </summary>
		ResolveResult Resolve(IScreenIdentifier from, IScreenIdentifier to, ITransitionContext ctx);
	}

	/// <summary>
	/// <see cref="IEffectRegistry.Resolve"/> の結果。<see cref="HasMatch"/> が false なら Effect は走らない。
	/// </summary>
	public readonly struct ResolveResult
	{
		public bool HasMatch { get; }
		public AssetReferenceGameObject EffectPrefab { get; }

		public ResolveResult(bool hasMatch, AssetReferenceGameObject prefab)
		{
			HasMatch = hasMatch;
			EffectPrefab = prefab;
		}
	}
}
