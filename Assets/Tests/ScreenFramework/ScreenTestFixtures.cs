using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;

namespace Tests.ScreenFramework
{
	/// <summary>
	/// ScreenFramework のテストで共有する handle / presenter / identifier / view の dummy 実装と
	/// container / layer / services の組み立てヘルパー。
	/// 既存テストの nested helper は残してあるが、新規テストはこちらを使う。
	/// </summary>
	internal static class ScreenTestFixtures
	{
		public static IScreenContainer NewContainer(string name)
			=> new GameObject(name).AddComponent<ScreenContainer>();

		public static void DestroyContainer(IScreenContainer container)
		{
			if (container is MonoBehaviour mb && mb != null)
				UnityEngine.Object.DestroyImmediate(mb.gameObject);
		}

		public static ScreenLayerConfig NewLayer(
			IScreenContainer container,
			StackMode stack = StackMode.Cover,
			ScreenCacheMode cache = ScreenCacheMode.DestroyOnCover,
			StackInputPolicy inputPolicy = StackInputPolicy.BlockUnderlying,
			bool modal = true)
			=> new()
			{
				Container = container,
				DefaultCacheMode = cache,
				StackMode = stack,
				StackInputPolicy = inputPolicy,
				DefaultModal = modal,
			};
	}

	internal sealed class TestServices : ScreenServices
	{
		public TestServices() : base(useMockViews: true) { }
	}

	/// <summary>何もしない IScreenViewInstance。</summary>
	internal sealed class NopView : IScreenViewInstance
	{
		public void SetActive(bool active) { }
		public void SetParent(Transform parent) { }
		public T As<T>() where T : class => null;
	}

	/// <summary>OnLoad で即座に NopView を返す handle。Unload 呼出を記録する。</summary>
	internal sealed class InstantHandle : IScreenHandle
	{
		public bool UnloadCalled { get; private set; }
		public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
			=> UniTask.FromResult<IScreenViewInstance>(new NopView());
		public UniTask Unload(CancellationToken c) { UnloadCalled = true; return UniTask.CompletedTask; }
	}

	/// <summary>
	/// 外部 <see cref="UniTaskCompletionSource{IScreenViewInstance}"/> で Load 完了を制御できる handle。
	/// ct で <see cref="UniTaskCompletionSource{IScreenViewInstance}.TrySetCanceled"/> を発火するよう
	/// <see cref="CancellationToken.Register(Action)"/> を仕込む。
	/// </summary>
	internal sealed class ControllableHandle : IScreenHandle
	{
		readonly UniTaskCompletionSource<IScreenViewInstance> _source;
		public bool UnloadCalled { get; private set; }
		public ControllableHandle(UniTaskCompletionSource<IScreenViewInstance> source) => _source = source;
		public async UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken ct)
		{
			using (ct.Register(() => _source.TrySetCanceled(ct)))
				return await _source.Task;
		}
		public UniTask Unload(CancellationToken ct) { UnloadCalled = true; return UniTask.CompletedTask; }
	}

	/// <summary>全 callback を default 実装(no-op)に委ねる presenter。</summary>
	internal sealed class NullPresenter : IScreenPresenter { }

	/// <summary>
	/// OnBeforeEnter で <see cref="Started"/> を立ててから <see cref="Release"/> が呼ばれるまで待機する。
	/// push を「完走必須ゾーン」(safeCt=None) で固定して、外部からの Cancel が効かない状況を作るために使う。
	/// 複合操作の race を意図的に出すテスト用。
	/// </summary>
	internal sealed class GatedPresenter : IScreenPresenter
	{
		readonly UniTaskCompletionSource _started = new();
		readonly UniTaskCompletionSource _release = new();
		public UniTask Started => _started.Task;
		public void Release() => _release.TrySetResult();

		UniTask IScreenPresenter.OnBeforeEnter(INavigationDataReader r, CancellationToken c)
		{
			_started.TrySetResult();
			return _release.Task;
		}
	}

	/// <summary>OnBeforeLoad で非 OCE 例外を投げる presenter。</summary>
	internal sealed class ThrowingOnBeforeLoadPresenter : IScreenPresenter
	{
		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, CancellationToken c)
			=> throw new InvalidOperationException("test failure during OnBeforeLoad");
	}

	/// <summary>
	/// <see cref="OnAfterUnloadCalled"/> を介して OnAfterUnload の呼出を観測する。
	/// オプションで OnAfterLoad を throw させられる(Load 失敗時の補償フックを確認する用)。
	/// </summary>
	internal sealed class TrackingPresenter : IScreenPresenter
	{
		readonly bool _throwOnAfterLoad;
		public bool OnAfterUnloadCalled { get; private set; }
		public TrackingPresenter(bool throwOnAfterLoad = false) { _throwOnAfterLoad = throwOnAfterLoad; }

		UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance v, INavigationDataReader r, CancellationToken c)
		{
			if (_throwOnAfterLoad) throw new InvalidOperationException("OnAfterLoad threw");
			return UniTask.CompletedTask;
		}

		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
		{
			OnAfterUnloadCalled = true;
			return UniTask.CompletedTask;
		}
	}

	/// <summary>任意の handle と presenter factory を差し込める汎用 identifier。</summary>
	internal sealed record ControllableScreenId(IScreenHandle Handle, Func<IScreenPresenter> PresenterFactory = null) : ScreenIdentifier
	{
		public override IScreenHandle CreateHandle(ScreenServices s) => Handle;
		public override IScreenPresenter CreatePresenter(ScreenServices s)
			=> (PresenterFactory ?? (() => new NullPresenter())).Invoke();
	}

	/// <summary>ラベル付きの単純な identifier。等価判定はラベル + 型で行われる(record の自動実装)。</summary>
	internal sealed record MarkerScreenId(string Label) : ScreenIdentifier
	{
		public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
		public override IScreenPresenter CreatePresenter(ScreenServices s) => new NullPresenter();
	}

	/// <summary>結果を返すダイアログのテスト用 result。</summary>
	internal sealed class EchoResult : INavigationData
	{
		public string Text;
	}

	/// <summary>text != null なら OnAfterLoad で SetResult する dialog identifier。</summary>
	internal sealed record EchoDialogId(string text) : ScreenIdentifier<EchoResult>
	{
		public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
		public override IScreenPresenter CreatePresenter(ScreenServices s) => new EchoDialogPresenter(text);
	}

	internal sealed class EchoDialogPresenter : DialogPresenter<object, object, EchoResult>
	{
		readonly string _text;
		public EchoDialogPresenter(string text) { _text = text; }
		protected override UniTask OnAfterLoad(INavigationDataReader reader, CancellationToken ct)
		{
			if (_text != null) SetResult(new EchoResult { Text = _text });
			return UniTask.CompletedTask;
		}
	}
}
