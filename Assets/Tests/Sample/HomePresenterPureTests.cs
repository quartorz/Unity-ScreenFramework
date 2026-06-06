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
		SampleRegistry _registry;

		MockView.Sample.MockHomeView _view;

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

			_registry = new SampleRegistry(
				useMockViews: true,
				gacha: new MockGachaService(),
				user: new MockUserService(),
				profile: new MockProfileService(),
				master: new MockMasterService());
			_registry.UserData.SetInfo(new UserInfo
			{
				UserId = "user-001",
				Name = "Alice",
				Level = 1,
			});

			_view = new MockView.Sample.MockHomeView();
		}

		HomePresenter NewPresenter() => new HomePresenter().WithServices(_registry);

		[Test]
		public async Task OnAfterLoad_SetsTitle_OnView()
		{
			string captured = null;
			_view.SetTitleFunc = title => captured = title;

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			Assert.AreEqual("Home Screen", captured);
		}

		[Test]
		public async Task GoProfileClick_PushesProfileScreen_WithUserIdFromServices()
		{
			_view.SetTitleFunc = _ => { };

			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);

			_view.goProfile.RaiseOnClicked();

			Assert.AreEqual(1, _pushedIds.Count);
			var profile = _pushedIds[0] as ProfileScreenId;
			Assert.IsNotNull(profile);
			Assert.AreEqual("user-001", profile.UserId);
		}

		[Test]
		public async Task OnAfterUnload_UnsubscribesHandler_NoFurtherPushOnRaise()
		{
			var presenter = (IScreenPresenter)NewPresenter();
			await ScreenTesting.PushAsync(presenter, _view);
			await ScreenTesting.PopAsync(presenter);

			_view.goGacha.RaiseOnClicked();

			Assert.AreEqual(0, _pushedIds.Count, "Unload 後にイベントが leak しないこと");
		}
	}
}
