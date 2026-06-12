using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// フォールトインジェクションテスト群の共有基底。各カテゴリ別クラス
	/// (<see cref="FaultInjectionPushTests"/> など)が継承する。
	/// 全クラス共通の Navigator セットアップと、再 Initialize 例外ガードを避けるための
	/// 静的参照の畳み込み(<see cref="TearDown"/>)をまとめる。
	/// フォールト注入用のテストダブルは <see cref="FaultInjectionFixtures"/> に集約してある。
	/// </summary>
	public abstract class FaultInjectionTestBase
	{
		protected IScreenContainer _pageContainer;

		[TearDown]
		public void TearDown()
		{
			// 再 Initialize 例外ガード(既初期化なら throw)があるので、各テスト後に静的参照を畳む。
			ScreenNavigator.Shutdown().Forget();
			DestroyContainer(_pageContainer);
		}

		protected void SetupNavigator(ScreenCacheMode cache = ScreenCacheMode.DestroyOnCover)
		{
			_pageContainer = NewContainer("PageRoot");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = NewLayer(_pageContainer, cache: cache),
				Dialog = NewLayer(NewContainer("DialogRoot")),
				SystemDialog = NewLayer(NewContainer("SysRoot")),
			});
		}

		/// <summary>Page レイヤーに EffectRegistry を渡すセットアップ(effectRoot 省略時は意図的に未設定)。</summary>
		protected void SetupNavigatorWithPageRegistry(EffectRegistry registry, Transform effectRoot = null)
		{
			_pageContainer = NewContainer("PageRoot");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = new ScreenLayerConfig
				{
					Container = _pageContainer,
					DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
					StackMode = StackMode.Cover,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
					Registry = registry,
					EffectRoot = effectRoot,
				},
				Dialog = NewLayer(NewContainer("DialogRoot")),
				SystemDialog = NewLayer(NewContainer("SysRoot")),
			});
		}
	}
}
