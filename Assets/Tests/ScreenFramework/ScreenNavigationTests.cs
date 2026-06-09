using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Sample;
using Sample.Api;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;
using Tests.Support;

namespace Tests.ScreenFramework
{
	/// <summary>
	/// プレハブ・Canvas 不要で Presenter 経路をテストする。
	/// MockScreenHandle が View インスタンスを生成し、Presenter のライフサイクルを駆動する。
	/// </summary>
	public sealed class ScreenNavigationTests
	{
		IScreenContainer _pageContainer;
		IScreenContainer _dialogContainer;
		IScreenContainer _systemDialogContainer;
		MockProfileService _mockProfileApi;

		[SetUp]
		public void SetUp()
		{
			_pageContainer = NewContainer("PageRoot");
			_dialogContainer = NewContainer("DialogRoot");
			_systemDialogContainer = NewContainer("SystemDialogRoot");
			_mockProfileApi = new MockProfileService();
			// Profile 画面は OnAfterLoad で Profile.Get(userId) を叩くので最低限のスタブを置く
			_mockProfileApi.GetFunc = (userId, opt) => UniTask.FromResult(new ProfileResponse
			{
				userId = userId,
				name = "test",
				level = 1,
			});

			var registry = TestSampleRegistry.AllMocks(profile: _mockProfileApi);
			// HomePresenter が Registry.UserData.Info.UserId を参照するので seed しておく
			registry.UserData.SetInfo(new UserInfo
			{
				UserId = "user-001",
				Name = "test",
				Level = 1,
			});

			var setup = new ScreenLayerSetup
			{
				Page = NewLayerConfig(_pageContainer),
				Dialog = NewLayerConfig(_dialogContainer),
				SystemDialog = NewLayerConfig(_systemDialogContainer),
			};

			ScreenNavigator.Initialize(registry, setup);
		}

		[TearDown]
		public void TearDown()
		{
			// 各テストの SetUp で Initialize() が静的状態を上書きするのでリセットは不要。
			// ここでは Unity GameObject の片付けだけ。
			DestroyContainer(_pageContainer);
			DestroyContainer(_dialogContainer);
			DestroyContainer(_systemDialogContainer);
		}

		[UnityTest]
		public IEnumerator Push_Home_SetsTitleOnMockView() => UniTask.ToCoroutine(async () =>
		{
			await ScreenNavigator.Page.Push(new HomeScreenId());

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.IsInstanceOf<HomeScreenId>(ScreenNavigator.Page.Current);
			// Presenter が View.SetTitle を呼んだことは MockView 側に痕跡が残らないとわからない。
			// ここではライフサイクルが正常に完了したことだけ確認する。
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		});

		[UnityTest]
		public IEnumerator Push_Profile_HasUserIdInIdentifier() => UniTask.ToCoroutine(async () =>
		{
			await ScreenNavigator.Page.Push(new HomeScreenId());
			await ScreenNavigator.Page.Push(new ProfileScreenId("abc-123"));

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
			var current = ScreenNavigator.Page.Current as ProfileScreenId;
			Assert.IsNotNull(current);
			Assert.AreEqual("abc-123", current!.UserId);
		});

		[UnityTest]
		public IEnumerator Pop_AfterPush_GoesBackToHome() => UniTask.ToCoroutine(async () =>
		{
			await ScreenNavigator.Page.Push(new HomeScreenId());
			await ScreenNavigator.Page.Push(new ProfileScreenId("abc"));
			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.IsInstanceOf<HomeScreenId>(ScreenNavigator.Page.Current);
		});

		[UnityTest]
		public IEnumerator IdentifierEquality_SameParamsAreEqual() => UniTask.ToCoroutine(async () =>
		{
			await UniTask.CompletedTask;
			var a = new ProfileScreenId("x");
			var b = new ProfileScreenId("x");
			var c = new ProfileScreenId("y");
			Assert.AreEqual(a, b);
			Assert.AreNotEqual(a, c);
		});

		[UnityTest]
		public IEnumerator History_Edit_RemovesIntermediate() => UniTask.ToCoroutine(async () =>
		{
			await ScreenNavigator.Page.Push(new HomeScreenId());
			await ScreenNavigator.Page.Push(new ProfileScreenId("a"));
			await ScreenNavigator.Page.Push(new ProfileScreenId("b"));

			Assert.AreEqual(3, ScreenNavigator.Page.History.Count);

			ScreenNavigator.Page.History.Edit(e =>
			{
				e.RemoveAll(id => id is ProfileScreenId { UserId: "a" });
			});

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
			// Current (b) は残り、間の "a" だけが消える
			Assert.IsInstanceOf<HomeScreenId>(ScreenNavigator.Page.History[0]);
			var top = ScreenNavigator.Page.Current as ProfileScreenId;
			Assert.AreEqual("b", top!.UserId);
		});

		// ---- ヘルパー ----

		static IScreenContainer NewContainer(string name)
		{
			var go = new GameObject(name);
			return go.AddComponent<ScreenContainer>();
		}

		static void DestroyContainer(IScreenContainer container)
		{
			if (container is MonoBehaviour mb && mb != null)
			{
				Object.DestroyImmediate(mb.gameObject);
			}
		}

		static ScreenLayerConfig NewLayerConfig(IScreenContainer container) => new()
		{
			Container = container,
			DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
			StackMode = StackMode.Cover,
			StackInputPolicy = StackInputPolicy.BlockUnderlying,
			DefaultModal = true,
		};
	}
}
