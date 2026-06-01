using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Sample;
using ScreenFramework;

namespace Tests
{
	/// <summary>
	/// HomePresenter を GameObject・Container 一切無しで単体テストする例。
	/// Navigator は MockGenerator 生成の <see cref="MockScreenNavigator"/> に差し替えて
	/// クリック → Push の意図を assert する。
	/// </summary>
	public sealed class HomePresenterPureTests
	{
		MockScreenNavigator _nav;
		List<IScreenIdentifier> _pushedIds;

		[SetUp]
		public void SetUp()
		{
			_pushedIds = new List<IScreenIdentifier>();

			_nav = new MockScreenNavigator
			{
				PushFunc = (id, opt, ct) =>
				{
					_pushedIds.Add(id);
					return UniTask.CompletedTask;
				},
			};

			// Dialog / SystemDialog は本テストでは使わないが Override は値を null 以外で渡す必要がある
			ScreenNavigator.Override(
				page: _nav,
				dialog: new MockScreenNavigator(),
				systemDialog: new MockScreenNavigator());
		}

		[Test]
		public void OnAfterLoad_SetsTitle_OnView()
		{
			var mockView = new MockView.Sample.MockHomeView();
			string captured = null;
			mockView.SetTitleFunc = title => captured = title;

			var presenter = (IScreenPresenter)new HomePresenter();
			presenter.OnAfterLoad(ScreenTesting.ViewOf(mockView), ScreenTesting.EmptyReader, CancellationToken.None)
				.GetAwaiter().GetResult();

			Assert.AreEqual("Home Screen", captured);
		}

		[Test]
		public void GoDetailClick_PushesDetailScreen_WithUserId()
		{
			var mockView = new MockView.Sample.MockHomeView();
			mockView.SetTitleFunc = _ => { };

			var presenter = (IScreenPresenter)new HomePresenter();
			presenter.OnAfterLoad(ScreenTesting.ViewOf(mockView), ScreenTesting.EmptyReader, CancellationToken.None)
				.GetAwaiter().GetResult();

			mockView.RaiseOnGoDetailClicked();

			Assert.AreEqual(1, _pushedIds.Count);
			var detail = _pushedIds[0] as DetailScreenId;
			Assert.IsNotNull(detail);
			Assert.AreEqual("user-001", detail.UserId);
		}

		[Test]
		public void OnAfterUnload_UnsubscribesHandler_NoFurtherPushOnRaise()
		{
			var mockView = new MockView.Sample.MockHomeView();
			mockView.SetTitleFunc = _ => { };

			var presenter = (IScreenPresenter)new HomePresenter();
			presenter.OnAfterLoad(ScreenTesting.ViewOf(mockView), ScreenTesting.EmptyReader, CancellationToken.None)
				.GetAwaiter().GetResult();

			presenter.OnAfterUnload(ScreenTesting.NewWriter(out _), CancellationToken.None)
				.GetAwaiter().GetResult();

			mockView.RaiseOnGoDetailClicked();

			Assert.AreEqual(0, _pushedIds.Count, "Unload 後にイベントが leak しないこと");
		}
	}
}
