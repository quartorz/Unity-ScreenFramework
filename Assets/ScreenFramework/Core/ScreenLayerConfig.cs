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
		/// 指定した場合、Navigator が <see cref="Container"/> 配下にこのレイヤー専用の Canvas
		/// （<see cref="UnityEngine.RenderMode.ScreenSpaceCamera"/>・このカメラ・<see cref="SortingOrder"/>）を
		/// 動的生成し、画面ビュー／ModalBlocker をその下に置く。レイヤー間の重なり順は <see cref="SortingOrder"/>
		/// で決まり、上位レイヤー（Dialog 等）の遮蔽板が下位レイヤー（Page）を Canvas 優先度で遮断できる。
		/// <para>null の場合は従来どおり <see cref="Container"/> の Transform 直下に置く（Canvas はシーン側任せ）。
		/// useMockViews のテストなど Canvas 不要な構成はここを null にする。</para>
		/// <para><b>重要</b>: <see cref="Container"/> は他の Canvas の配下に置かないこと。uGUI ではネストした
		/// 子 Canvas は親 Canvas の renderMode を継承するため、生成したレイヤー Canvas が既存 Canvas の下に
		/// ネストすると ScreenSpaceCamera 指定が無視される。Container は（Canvas を持たない）素の Transform を
		/// シーン直下に置き、このレイヤー Canvas を実質のルート Canvas にすること。</para>
		/// <para><b>画面ごとの Canvas</b>: 指定時はこのレイヤー内の画面の重なりを sibling 順ではなく
		/// sortingOrder で制御する（nested canvas は sibling 順を無視するため）。各画面プレハブのルートに
		/// Canvas を付けておけば、Navigator が Push/復元のたびに overrideSorting=true と
		/// 「<see cref="SortingOrder"/> + スタック位置」を自動で振る。ModalBlocker も同様に所属画面の直下へ。
		/// このレイヤーで複数画面を同時表示する（Stack 等）なら全画面にルート Canvas を付けること。</para>
		/// </summary>
		public Camera Camera { get; init; }

		/// <summary>
		/// <see cref="Camera"/> 指定時に動的生成するレイヤー Canvas の <see cref="Canvas.sortingOrder"/>。
		/// Page &lt; Dialog &lt; SystemDialog の順に大きくする。プロジェクト側の入力遮蔽板（Shield）は
		/// SystemDialog より小さい値に置くこと（最前面のエラーダイアログを操作できなくなり進行不能になるため）。
		/// </summary>
		public int SortingOrder { get; init; }

		/// <summary>
		/// 遷移演出（Effect）の選択表。null の場合 Effect は一切走らない。
		/// v1 では Page Navigator のみに渡し、Dialog/SystemDialog は null 推奨。
		/// 共通フェードは Registry の <c>(null, null)</c> 行で表現する。
		/// </summary>
		public EffectRegistry Registry { get; init; }

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
