using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// 画面が Enter フェーズ（OnBeforeShow / OnAfterShow）で観測する状態の検証。
	/// bookkeeping は Enter hook より前に済むので hook 内で自分が最上段として見え、
	/// push payload は OnBeforeShow / OnAfterShow の両方に渡る（後者だけ空、という非対称はない）。
	/// </summary>
	public sealed class EnterPhaseTests
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
			ScreenNavigator.Shutdown().Forget();
			DestroyContainer(_pageContainer);
		}

		[Test]
		public async Task OnAfterShow_SeesItselfAsCurrentAndFindable()
		{
			var presenter = new SelfObservingPresenter();
			var id = new ControllableScreenId(new InstantHandle(), () => presenter);

			await ScreenNavigator.Page.Push(id);

			Assert.AreSame(id, presenter.CurrentAtAfterEnter,
				"OnAfterShow 時点で Current が自分になっている");
			Assert.IsTrue(presenter.FoundSelfAtAfterEnter,
				"OnAfterShow 時点で FindEntry から自分が見える（孤児にならない）");
		}

		[Test]
		public async Task OnAfterShow_ReceivesPushPayload()
		{
			var presenter = new PayloadCapturePresenter();
			var id = new ControllableScreenId(new InstantHandle(), () => presenter);

			await ScreenNavigator.Page.Push(id, new PushOptions
			{
				Configure = w => w.Write(new PayloadData { V = "hello" }),
			});

			Assert.AreEqual("hello", presenter.AfterEnterValue,
				"OnAfterShow でも push payload が読める");
		}

		sealed class SelfObservingPresenter : IScreenPresenter
		{
			public IScreenIdentifier CurrentAtAfterEnter;
			public bool FoundSelfAtAfterEnter;

			UniTask IScreenPresenter.OnAfterShow(INavigationDataReader r, ITransitionContext ctx, CancellationToken c)
			{
				CurrentAtAfterEnter = ScreenNavigator.Page.Current;
				FoundSelfAtAfterEnter = ScreenNavigator.Page.FindEntry<SelfObservingPresenter>() != null;
				return UniTask.CompletedTask;
			}
		}

		sealed class PayloadData : INavigationData { public string V; }

		sealed class PayloadCapturePresenter : IScreenPresenter
		{
			public string AfterEnterValue = "<none>";

			UniTask IScreenPresenter.OnAfterShow(INavigationDataReader r, ITransitionContext ctx, CancellationToken c)
			{
				if (r != null && r.TryRead<PayloadData>(out var d)) AfterEnterValue = d.V;
				return UniTask.CompletedTask;
			}
		}
	}
}
