using ScreenFramework;
using UnityEngine;

namespace Sample
{
	/// <summary>
	/// Sample / SampleDebug 共通のレイヤー構成（<see cref="ScreenLayerSetup"/>）を組み立てる単一の生成元。
	/// 以前は SampleBootstrap と DebugBootstrap が同じ設定を丸コピーしていて drift の温床だったため、
	/// ここに集約する。レイヤーのキャッシュ方針・StackMode・sortingOrder などのポリシーを変えるときはここだけ直す。
	/// </summary>
	public static class SampleScreenLayers
	{
		// レイヤー Canvas の sortingOrder。Page < Dialog < SystemDialog。
		// プロジェクトの入力遮蔽板（InputShield）は SystemDialog より小さい値に置くこと。
		// 1 レイヤーあたりの最大積み枚数は (次レイヤーとの差) ÷ ScreenSortingStep(=2) で、ここでは 50 枚。
		public const int PageSortingOrder = 0;
		public const int DialogSortingOrder = 100;
		public const int SystemDialogSortingOrder = 200;

		/// <param name="uiCamera">
		/// 指定するとレイヤーごとに ScreenSpaceCamera の Canvas を動的生成して上記 sortingOrder で重ねる
		/// （Dialog レイヤーの Shield が Page を Canvas 優先度で遮断できる）。null なら従来どおりシーンの単一 Canvas を使う。
		/// </param>
		public static ScreenLayerSetup Create(
			ScreenContainer pageContainer,
			ScreenContainer dialogContainer,
			ScreenContainer sysDialogContainer,
			Transform effectRoot,
			EffectRegistry pageEffectRegistry,
			Camera uiCamera)
		{
			return new ScreenLayerSetup
			{
				Page = new ScreenLayerConfig
				{
					Container = pageContainer,
					DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
					StackMode = StackMode.Cover,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
					Registry = pageEffectRegistry,
					EffectRoot = effectRoot,
					Camera = uiCamera,
					SortingOrder = PageSortingOrder,
				},
				Dialog = new ScreenLayerConfig
				{
					Container = dialogContainer,
					// Cover + DestroyOnCover だと PushAndAwait 中のダイアログから別ダイアログを開いた瞬間、
					// 下のダイアログの awaiter が TrySetCanceled → OCE で死ぬ(framework 仕様)。
					// 「ダイアログからダイアログ」は普通の要求なので KeepOnCover で寝かせて、
					// 上が閉じたら自分の Pop で正常 resolve させる。
					DefaultCacheMode = ScreenCacheMode.KeepOnCover,
					StackMode = StackMode.Cover,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
					Camera = uiCamera,
					SortingOrder = DialogSortingOrder,
				},
				SystemDialog = new ScreenLayerConfig
				{
					Container = sysDialogContainer,
					DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
					StackMode = StackMode.Stack,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
					Camera = uiCamera,
					SortingOrder = SystemDialogSortingOrder,
				},
			};
		}
	}
}
