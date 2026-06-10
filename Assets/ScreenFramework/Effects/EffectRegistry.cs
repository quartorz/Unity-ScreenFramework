using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ScreenFramework
{
	/// <summary>
	/// 遷移演出（Effect）の選択表。各行は <c>(from Matcher, to Matcher, Effect prefab AssetReference)</c>。
	/// from / to を null にすると「その側は何でも match」の意味になり、<c>(null, null)</c> 行が共通／デフォルト枠。
	/// マッチは「単一・最 specific 勝ち」（非 null Matcher の数が多い行を優先、同点は先勝ち）。
	/// 0 件マッチは素通し（Effect なしで遷移続行）。
	/// </summary>
	[CreateAssetMenu(fileName = "EffectRegistry", menuName = "ScreenFramework/Effect Registry")]
	public sealed class EffectRegistry : ScriptableObject
	{
		[Serializable]
		public struct Row
		{
			[Tooltip("from 側の Matcher。null なら from を問わない")]
			public ScreenMatcher From;

			[Tooltip("to 側の Matcher。null なら to を問わない")]
			public ScreenMatcher To;

			[Tooltip("一致時に Instantiate される Effect prefab。ScreenEffect コンポーネントが付いている前提")]
			public AssetReferenceGameObject EffectPrefab;
		}

		[SerializeField] List<Row> _rows = new();

		public IReadOnlyList<Row> Rows => _rows;

		// 参照切れ行は黙って捨てると specific 行が無音でデフォルトに化けるので警告するが、
		// Resolve は毎遷移呼ばれるため行ごとに 1 回だけログる。
		[NonSerialized] HashSet<int> _warnedBrokenRows;

		/// <summary>
		/// from / to / ctx に合致する行のうち最 specific を返す。一致無しなら <c>HasMatch = false</c>。
		/// </summary>
		public ResolveResult Resolve(IScreenIdentifier from, IScreenIdentifier to, ITransitionContext ctx)
		{
			int bestScore = -1;
			Row bestRow = default;
			bool hasMatch = false;

			for (int i = 0; i < _rows.Count; i++)
			{
				var row = _rows[i];

				// null Matcher は wildcard。非 null の場合のみ判定。
				if (row.From != null)
				{
					if (from == null) continue;
					try { if (!row.From.Match(from, ctx)) continue; }
					catch (Exception e) { UnityEngine.Debug.LogException(e); continue; }
				}
				if (row.To != null)
				{
					if (to == null) continue;
					try { if (!row.To.Match(to, ctx)) continue; }
					catch (Exception e) { UnityEngine.Debug.LogException(e); continue; }
				}

				// マッチした行の prefab が参照切れなら、無音で捨てず警告して skip
				// （この行が出すはずだった演出が、より緩い行のデフォルトに化けるのを気付けるように）。
				if (row.EffectPrefab == null || !row.EffectPrefab.RuntimeKeyIsValid())
				{
					WarnBrokenRowOnce(i, from, to);
					continue;
				}

				int score = (row.From != null ? 1 : 0) + (row.To != null ? 1 : 0);
				if (score > bestScore)
				{
					bestScore = score;
					bestRow = row;
					hasMatch = true;
				}
			}

			return new ResolveResult(hasMatch, bestRow.EffectPrefab);
		}

		void WarnBrokenRowOnce(int rowIndex, IScreenIdentifier from, IScreenIdentifier to)
		{
			_warnedBrokenRows ??= new HashSet<int>();
			if (!_warnedBrokenRows.Add(rowIndex)) return;
			Debug.LogWarning(
				$"[ScreenFramework] EffectRegistry '{name}' row {rowIndex} matched (from={from}, to={to}) " +
				"but its EffectPrefab reference is missing/invalid. Skipping it; a less-specific row or no effect will be used.");
		}

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
}
