using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// Load パイプライン(OnBeforeLoad / handle.Load / OnAfterLoad)の失敗時の挙動のうち、
	/// まだハーネスが無いもののプレースホルダ。失敗時の補償(handle.Unload + OnAfterUnload)と
	/// 例外伝播は <see cref="FaultInjectionPushTests"/> が網羅している。
	/// </summary>
	public sealed class LoadPipelineTests
	{
		IScreenContainer _pageContainer;

		[SetUp]
		public void SetUp()
		{
			_pageContainer = NewContainer("PageRoot");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = NewLayer(_pageContainer),
				Dialog = NewLayer(NewContainer("DialogRoot")),
				SystemDialog = NewLayer(NewContainer("SysRoot")),
			});
		}

		[TearDown]
		public void TearDown()
		{
			// 再 Initialize 例外ガード（既初期化なら throw）があるので、各テスト後に静的参照を畳む。
			ScreenNavigator.Shutdown().Forget();
			DestroyContainer(_pageContainer);
		}

		[Test]
		[Ignore("Addressables 実環境 or seam がないため別途ハーネスが必要。修正時に追加。")]
		public void AddressableScreenHandle_LoadCancellation_DoesNotLeak()
		{
			// AddressableScreenHandle.Load のポーリングループ中に ct がキャンセルされると
			// AsyncOperationHandle がローカルのまま脱出して Release されないリーク。
			// 修正は handle を フィールドで保持して Unload で未完了でも Release すること。
		}
	}
}
