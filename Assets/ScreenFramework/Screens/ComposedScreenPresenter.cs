using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <summary>
	/// 複数の <see cref="IScreenPart"/>（Feature / Binder）に分割して構成する Presenter 基底（中〜大画面用）。
	/// 本体は Input / Output を直接保持・操作できず、<see cref="Compose"/> 内でのみ View I/O スライスを Part へ配線する。
	/// これにより「View へは Part 経由でしか到達できない」ことが型で保証され、Presenter 本体の肥大化を防ぐ。
	///
	/// Part は <see cref="Compose"/>（= OnAfterLoad 時点）で生成される。Part 生成に必要な Model 等は
	/// <see cref="ScreenPresenterBase{TInput,TOutput}.OnInitialize"/> /
	/// <see cref="ScreenPresenterBase{TInput,TOutput}.OnBeforeLoad"/> で先に用意しておくこと。
	///
	/// fan-out 順は入場系（Load/Show/Resume）が宣言順、退場系（Hide/Suspend/Unload）が逆順。
	/// </summary>
	public abstract class ComposedScreenPresenter<TInput, TOutput> : ScreenPresenterBase<TInput, TOutput>, IScreenPresenter
		where TInput : class
		where TOutput : class
	{
		readonly List<IScreenPart> _parts = new List<IScreenPart>();

		/// <summary>
		/// View I/O スライスを受け取り、画面を構成する Part を宣言順に返す。
		/// 生成した Part を Presenter のフィールドへ保持しておくと、後から名前で参照できる。
		/// </summary>
		/// <example>
		/// <code>
		/// HeaderPart _header;
		/// BodyPart _body;
		/// FooterPart _footer;
		///	protected override IEnumerable&lt;IScreenPart&gt; Compose(IHomeViewInput input, IHomeViewOutput output)
		/// {
		///		_header = new HeaderPart(input.header, output.header);
		///		_footer = new FooterPart(input.footer, output.footer);
		///		_body = new BodyPart(input.body, output.body, _header, _footer); // Partの間に依存がある場合はコンストラクタなどで渡す
		///
		///		// ここでyield returnした順序でOnAfterLoadなどのメソッドが呼ばれる
		///		yield return _header;
		///		yield return _body;
		///		yield return _footer;
		/// }
		/// </code>
		/// </example>
		protected abstract IEnumerable<IScreenPart> Compose(TInput input, TOutput output);

		// View 確定時に Compose で Part を組み立て、OnAfterLoad を fan-out する。
		// Part の初期化（購読など）が必ず走るよう、OnAfterLoad の fan-out はここで直接行う（override では潰せない）。
		UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance view, INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
			=> OnAfterLoadInternal(view, reader, ctx, ct);

		async UniTask OnAfterLoadInternal(IScreenViewInstance view, INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			_parts.Clear();
			foreach (var p in Compose(view.As<TInput>(), view.As<TOutput>()))
				_parts.Add(p);

			await OnAfterLoad(reader, ctx, ct);                 // 派生 Presenter 自身の hook（任意）
			foreach (var p in _parts)
				await p.OnAfterLoad(reader, ctx, ct);
		}

		// 以降の hook は Part へ fan-out する。派生で override する場合は base を呼んで fan-out を維持すること。
		// TODO: debug ビルドで「override したが base を呼んでいない＝Part に届いていない」を検出する手段を検討。

		protected override async UniTask OnBeforeShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			foreach (var p in _parts) await p.OnBeforeShow(reader, ctx, ct);
		}

		protected override async UniTask OnAfterShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			foreach (var p in _parts) await p.OnAfterShow(reader, ctx, ct);
		}

		protected override async UniTask OnResume(CancellationToken ct)
		{
			foreach (var p in _parts) await p.OnResume(ct);
		}

		protected override async UniTask OnBeforeHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct)
		{
			for (var i = _parts.Count - 1; i >= 0; i--) await _parts[i].OnBeforeHide(writer, ctx, ct);
		}

		protected override async UniTask OnAfterHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct)
		{
			for (var i = _parts.Count - 1; i >= 0; i--) await _parts[i].OnAfterHide(writer, ctx, ct);
		}

		protected override async UniTask OnSuspend(CancellationToken ct)
		{
			for (var i = _parts.Count - 1; i >= 0; i--) await _parts[i].OnSuspend(ct);
		}

		protected override async UniTask OnAfterUnload(INavigationDataWriter writer, CancellationToken ct)
		{
			for (var i = _parts.Count - 1; i >= 0; i--) await _parts[i].OnAfterUnload(writer, ct);
		}
	}
}
