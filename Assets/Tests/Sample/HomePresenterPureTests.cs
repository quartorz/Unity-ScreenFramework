using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Sample;
using Sample.Api;
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
		SampleServices _services;

		[SetUp]
		public void SetUp()
		{
			_pushedIds = new List<IScreenIdentifier>();

			_nav = new MockScreenNavigator
			{
				PushFunc = (id, opt, ct) =>
				{
					_pushedIds.Add(id);
					return UniTask.FromResult<IScreenEntry>(null);
				},
			};

			// Dialog / SystemDialog は本テストでは使わないが Override は値を null 以外で渡す必要がある
			ScreenNavigator.Override(
				page: _nav,
				dialog: new MockScreenNavigator(),
				systemDialog: new MockScreenNavigator());

			_services = new SampleServices(useMockViews: true, api: new MockApiClient());
			_services.UserData.SetInfo(new UserInfo
			{
				UserId = "user-001",
				Name = "Alice",
				Level = 1,
			});
		}

		HomePresenter NewPresenter() => new HomePresenter().WithServices(_services);

		[Test]
		public async Task OnAfterLoad_SetsTitle_OnView()
		{
			var mockView = new MockView.Sample.MockHomeView();
			string captured = null;
			mockView.SetTitleFunc = title => captured = title;

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, mockView);

			Assert.AreEqual("Home Screen", captured);
		}

		[Test]
		public async Task GoProfileClick_PushesProfileScreen_WithUserIdFromServices()
		{
			var mockView = new MockView.Sample.MockHomeView();
			mockView.SetTitleFunc = _ => { };

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, mockView);

			mockView.goProfile.RaiseOnClicked();

			Assert.AreEqual(1, _pushedIds.Count);
			var profile = _pushedIds[0] as ProfileScreenId;
			Assert.IsNotNull(profile);
			Assert.AreEqual("user-001", profile.UserId);
		}

		[Test]
		public async Task OnAfterUnload_UnsubscribesHandler_NoFurtherPushOnRaise()
		{
			var mockView = new MockView.Sample.MockHomeView();

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, mockView);
			await ScreenTesting.PopAsync(presenter);

			mockView.goGacha.RaiseOnClicked();

			Assert.AreEqual(0, _pushedIds.Count, "Unload 後にイベントが leak しないこと");
		}
	}
}
