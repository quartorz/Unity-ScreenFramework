using NUnit.Framework;
using ScreenFramework;
using UnityEngine;

namespace Tests.ScreenFramework
{
	/// <summary>
	/// <see cref="EffectRegistry"/> の Resolve ロジック。null=wildcard、most-specific 勝ち、同点 first-wins、
	/// 0 件マッチで <c>HasMatch=false</c> が返ることを確認する。
	/// </summary>
	public sealed class EffectRegistryTests
	{
		sealed record IdA : ScreenIdentifier
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => null;
			public override IScreenPresenter CreatePresenter(ScreenServices s) => null;
		}
		sealed record IdB : ScreenIdentifier
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => null;
			public override IScreenPresenter CreatePresenter(ScreenServices s) => null;
		}

		/// <summary>
		/// Unity の SO はジェネリック型を <c>CreateInstance&lt;T&gt;()</c> で生成すると
		/// シリアライズ不能で null になることがあるため、非ジェネリックで Predicate を差し替える方式にする。
		/// </summary>
		sealed class FakeMatcher : ScreenMatcher
		{
			System.Func<IScreenIdentifier, bool> _predicate;
			public static FakeMatcher Create(System.Func<IScreenIdentifier, bool> predicate)
			{
				var m = ScriptableObject.CreateInstance<FakeMatcher>();
				m._predicate = predicate;
				return m;
			}
			public override bool Match(IScreenIdentifier id, ITransitionContext ctx) => _predicate(id);
		}

		static FakeMatcher NewTypeMatcher<T>() where T : IScreenIdentifier
			=> FakeMatcher.Create(id => id is T);

		static EffectRegistry NewRegistry(params EffectRegistry.Row[] rows)
		{
			var reg = ScriptableObject.CreateInstance<EffectRegistry>();
			var list = new System.Collections.Generic.List<EffectRegistry.Row>(rows);
			// _rows は private SerializeField。Reflection でセット。
			typeof(EffectRegistry)
				.GetField("_rows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
				.SetValue(reg, list);
			return reg;
		}

		static ITransitionContext NewCtx(IScreenIdentifier from, IScreenIdentifier to)
		{
			var store = new NavigationDataStoreForTest();
			return new TestContext(OperationKind.Push, from, to, store, store);
		}

		[Test]
		public void Resolve_NoRegistryRows_HasNoMatch()
		{
			var reg = NewRegistry();
			var result = reg.Resolve(new IdA(), new IdB(), NewCtx(new IdA(), new IdB()));
			Assert.IsFalse(result.HasMatch);
		}

		[Test]
		public void Resolve_NullMatcher_BehavesAsWildcard()
		{
			// (null, null) 行は何でもマッチして specificity 0
			var prefab = new UnityEngine.AddressableAssets.AssetReferenceGameObject(System.Guid.NewGuid().ToString());
			var reg = NewRegistry(new EffectRegistry.Row { From = null, To = null, EffectPrefab = prefab });
			var result = reg.Resolve(new IdA(), new IdB(), NewCtx(new IdA(), new IdB()));
			Assert.IsTrue(result.HasMatch);
			Assert.AreSame(prefab, result.EffectPrefab);
		}

		[Test]
		public void Resolve_MostSpecificWins_OverWildcard()
		{
			var wildcard = new UnityEngine.AddressableAssets.AssetReferenceGameObject(System.Guid.NewGuid().ToString());
			var specific = new UnityEngine.AddressableAssets.AssetReferenceGameObject(System.Guid.NewGuid().ToString());
			var fromMatcher = NewTypeMatcher<IdA>();
			var toMatcher = NewTypeMatcher<IdB>();
			// wildcard 先に置いても specific が勝つこと
			var reg = NewRegistry(
				new EffectRegistry.Row { From = null, To = null, EffectPrefab = wildcard },
				new EffectRegistry.Row { From = fromMatcher, To = toMatcher, EffectPrefab = specific });
			var result = reg.Resolve(new IdA(), new IdB(), NewCtx(new IdA(), new IdB()));
			Assert.IsTrue(result.HasMatch);
			Assert.AreSame(specific, result.EffectPrefab);
		}

		[Test]
		public void Resolve_PartialMatch_OneSideOnly()
		{
			var pref = new UnityEngine.AddressableAssets.AssetReferenceGameObject(System.Guid.NewGuid().ToString());
			var fromMatcher = NewTypeMatcher<IdA>();
			var reg = NewRegistry(new EffectRegistry.Row { From = fromMatcher, To = null, EffectPrefab = pref });
			var result = reg.Resolve(new IdA(), new IdB(), NewCtx(new IdA(), new IdB()));
			Assert.IsTrue(result.HasMatch);
		}

		[Test]
		public void Resolve_NoMatch_OnConcreteMatcherMismatch()
		{
			var pref = new UnityEngine.AddressableAssets.AssetReferenceGameObject(System.Guid.NewGuid().ToString());
			var fromMatcher = NewTypeMatcher<IdA>();
			var reg = NewRegistry(new EffectRegistry.Row { From = fromMatcher, To = null, EffectPrefab = pref });
			// from は IdB なので fromMatcher<IdA> はマッチしない
			var result = reg.Resolve(new IdB(), new IdA(), NewCtx(new IdB(), new IdA()));
			Assert.IsFalse(result.HasMatch);
		}

		[Test]
		public void Resolve_FromNull_DoesNotMatchNonNullFromMatcher()
		{
			// from=null のとき、from 側に Matcher 指定があっても通さない（誤発火防止）
			var pref = new UnityEngine.AddressableAssets.AssetReferenceGameObject(System.Guid.NewGuid().ToString());
			var fromMatcher = NewTypeMatcher<IdA>();
			var reg = NewRegistry(new EffectRegistry.Row { From = fromMatcher, To = null, EffectPrefab = pref });
			var result = reg.Resolve(from: null, to: new IdA(), NewCtx(null, new IdA()));
			Assert.IsFalse(result.HasMatch);
		}

		// TransitionContext を直接 new できるよう、internal を見るための薄いラッパ
		sealed class TestContext : ITransitionContext
		{
			readonly TransitionContextLike _inner;
			public TestContext(OperationKind kind, IScreenIdentifier from, IScreenIdentifier to, INavigationDataReader r, INavigationDataWriter w)
			{ _inner = new TransitionContextLike(kind, from, to, r, w); }
			public OperationKind Kind => _inner.Kind;
			public IScreenIdentifier From => _inner.From;
			public IScreenIdentifier To => _inner.To;
			public INavigationDataReader Reader => _inner.Reader;
			public INavigationDataWriter Writer => _inner.Writer;
			public void PublishStage<TStage>() where TStage : IStageKey => _inner.PublishStage<TStage>();
			public Cysharp.Threading.Tasks.UniTask WaitForStage<TStage>(System.Threading.CancellationToken ct = default, System.TimeSpan? timeout = null) where TStage : IStageKey
				=> _inner.WaitForStage<TStage>(ct, timeout);
		}

		sealed class TransitionContextLike : ITransitionContext
		{
			public OperationKind Kind { get; }
			public IScreenIdentifier From { get; }
			public IScreenIdentifier To { get; }
			public INavigationDataReader Reader { get; }
			public INavigationDataWriter Writer { get; }
			public TransitionContextLike(OperationKind k, IScreenIdentifier f, IScreenIdentifier t, INavigationDataReader r, INavigationDataWriter w)
			{ Kind = k; From = f; To = t; Reader = r; Writer = w; }
			public void PublishStage<TStage>() where TStage : IStageKey { }
			public Cysharp.Threading.Tasks.UniTask WaitForStage<TStage>(System.Threading.CancellationToken ct = default, System.TimeSpan? timeout = null) where TStage : IStageKey
				=> Cysharp.Threading.Tasks.UniTask.CompletedTask;
		}

		// internal NavigationDataStore に依存しないテスト用の no-op store
		sealed class NavigationDataStoreForTest : INavigationDataReader, INavigationDataWriter
		{
			public bool TryRead<T>(out T data) where T : INavigationData { data = default; return false; }
			public void Write<T>(T data) where T : INavigationData { }
		}
	}
}
