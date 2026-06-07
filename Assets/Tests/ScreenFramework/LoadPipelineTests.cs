using System;
using System.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// Load パイプライン(OnBeforeLoad / handle.Load / OnAfterLoad)の失敗時の挙動を検証する。
	/// 修正方針: CreateAndPreloadAsync は OCE/非OCE どちらでも handle.Unload + presenter.OnAfterUnload を
	/// 呼んでから元の例外で抜ける。
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
		public void TearDown() => DestroyContainer(_pageContainer);

		[Test]
		public async Task NonOceFailure_DuringLoad_StillUnloadsHandle()
		{
			// presenter.OnBeforeLoad が非 OCE を投げる。
			// 旧実装は catch(OCE) のみで handle.Unload を呼ばずに漏れ、利用側に OCE 詰め替えを強いていた。
			// 修正後: 非 OCE でも handle.Unload が呼ばれ、元の例外が伝播する。
			var handle = new InstantHandle();
			var id = new ControllableScreenId(handle, () => new ThrowingOnBeforeLoadPresenter());

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsNotNull(caught, "non-OCE exception should propagate");
			Assert.IsNotInstanceOf<OperationCanceledException>(caught,
				"元の例外型のまま (OCE 詰め替えされない)");
			Assert.IsTrue(handle.UnloadCalled, "handle.Unload must be called on non-OCE failure too");
		}

		[Test]
		public async Task LoadFailure_CallsOnAfterUnload()
		{
			// 正常退場と symmetry を取って、Load 失敗時も OnAfterUnload を呼ぶ契約。
			// OnAfterLoad の途中で張った購読の補償フックを画面側に与える。
			var presenter = new TrackingPresenter(throwOnAfterLoad: true);
			var id = new ControllableScreenId(new InstantHandle(), () => presenter);

			try { await ScreenNavigator.Page.Push(id); }
			catch { /* propagate ok */ }

			Assert.IsTrue(presenter.OnAfterUnloadCalled, "Load 失敗時も OnAfterUnload を呼ぶ契約");
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
