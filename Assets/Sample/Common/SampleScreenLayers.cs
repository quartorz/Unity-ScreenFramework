using ScreenFramework;
using UnityEngine;

namespace Sample
{
	/// <summary>
	/// Sample / SampleDebug 共通のレイヤー構成（<see cref="ScreenLayerSetup"/>）を組み立てる単一の生成元。
	/// 以前は SampleBootstrap と DebugBootstrap が同じ設定を丸コピーしていて drift の温床だったため、
	/// ここに集約する。レイヤーのキャッシュ方針・StackMode などのポリシーを変えるときはここだけ直す。
	/// </summary>
	public static class SampleScreenLayers
	{
		public static ScreenLayerSetup Create(
			ScreenContainer pageContainer,
			ScreenContainer dialogContainer,
			ScreenContainer sysDialogContainer,
			Transform effectRoot,
			EffectRegistry pageEffectRegistry)
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
				},
				SystemDialog = new ScreenLayerConfig
				{
					Container = sysDialogContainer,
					DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
					StackMode = StackMode.Stack,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
				},
			};
		}
	}
}
