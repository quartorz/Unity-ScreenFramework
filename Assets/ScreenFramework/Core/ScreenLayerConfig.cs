using UnityEngine;

namespace ScreenFramework
{
	public sealed class ScreenLayerConfig
	{
		public IScreenContainer Container { get; init; }
		public ScreenCacheMode DefaultCacheMode { get; init; } = ScreenCacheMode.DestroyOnCover;
		public StackMode StackMode { get; init; } = StackMode.Cover;
		public StackInputPolicy StackInputPolicy { get; init; } = StackInputPolicy.BlockUnderlying;
		public bool DefaultModal { get; init; } = true;

		/// <summary>
		/// 遷移演出（Effect）の選択表。null の場合 Effect は一切走らない。
		/// v1 では Page Navigator のみに渡し、Dialog/SystemDialog は null 推奨。
		/// 共通フェードは Registry の <c>(null, null)</c> 行で表現する。
		/// </summary>
		public IEffectRegistry Registry { get; init; }

		/// <summary>
		/// 遷移演出が乗る共有オーバーレイ。Registry を渡す場合は必須。Effect prefab の親・描画カメラ・
		/// Sorting Layer を提供し、order を採番する。同じ高さのレイヤー（例: Page/Dialog）には<b>同一インスタンス</b>を
		/// 渡してよく、その場合は host が order を一元採番して Effect 同士の衝突を防ぐ。
		/// </summary>
		public IEffectHost EffectHost { get; init; }
	}

	public sealed class ScreenLayerSetup
	{
		public ScreenLayerConfig Page { get; init; }
		public ScreenLayerConfig Dialog { get; init; }
		public ScreenLayerConfig SystemDialog { get; init; }
	}
}
