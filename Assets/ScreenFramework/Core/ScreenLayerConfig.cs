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
		/// Effect prefab を Instantiate する親 Transform。Registry を渡す場合は必須。
		/// シーン上の Canvas 配下に置いた空の Transform を渡す前提。
		/// </summary>
		public Transform EffectRoot { get; init; }
	}

	public sealed class ScreenLayerSetup
	{
		public ScreenLayerConfig Page { get; init; }
		public ScreenLayerConfig Dialog { get; init; }
		public ScreenLayerConfig SystemDialog { get; init; }
	}
}
