using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// hook（OnAfterEnter）内からの <see cref="ScreenNavigatorRedirectExtensions.Redirect(UniTask)"/> による
	/// リダイレクトが、デッドロックせず現在の遷移の後に実行されることを検証する。
	/// （同じことを await で書くと恒久デッドロックする＝#4 の注意書きの裏付け。）
	/// </summary>
	public sealed class RedirectTests
	{
		IScreenContainer _page, _dialog, _sys;

		[SetUp]
		public void SetUp()
		{
			_page = NewContainer("PageRoot");
			_dialog = NewContainer("DialogRoot");
			_sys = NewContainer("SysRoot");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = NewLayer(_page),
				Dialog = NewLayer(_dialog),
				SystemDialog = NewLayer(_sys),
			});
		}

		[TearDown]
		public void TearDown()
		{
			ScreenNavigator.Shutdown().Forget();
			DestroyContainer(_page);
			DestroyContainer(_dialog);
			DestroyContainer(_sys);
		}

		[Test]
		public async Task Redirect_FromOnAfterEnter_RunsAfterCurrent_NoDeadlock()
		{
			var idB = new MarkerScreenId("B");

			// A の OnAfterEnter で B へリダイレクト（.Redirect() = fire-and-forget）。
			// await Push(A) はデッドロックせず完了する。
			await ScreenNavigator.Page.Push(new RedirectingScreenId(idB));

			// 現在の遷移（A）完了後にキューされた B が走る。数フレーム待って反映を確認。
			for (var i = 0; i < 10 && !IsCurrent("B"); i++)
				await UniTask.Yield();

			Assert.IsTrue(IsCurrent("B"), "リダイレクト先 B が現在画面になる");
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
		}

		static bool IsCurrent(string label)
			=> ScreenNavigator.Page.Current is MarkerScreenId m && m.Label == label;

		sealed record RedirectingScreenId(IScreenIdentifier Next) : ScreenIdentifier
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new RedirectingPresenter(Next);
		}

		sealed class RedirectingPresenter : IScreenPresenter
		{
			readonly IScreenIdentifier _next;
			public RedirectingPresenter(IScreenIdentifier next) => _next = next;

			UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext ctx, CancellationToken c)
			{
				// await すると恒久デッドロック。Redirect（=Forget）で発行し、現遷移完了後に走らせる。
				ScreenNavigator.Page
					.Push(_next, new PushOptions { InterruptPriority = InterruptPriority.Queue })
					.Redirect();
				return UniTask.CompletedTask;
			}
		}
	}
}
