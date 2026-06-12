using System;

namespace ScreenFramework
{
	public readonly struct PushOptions
	{
		/// <summary>
		/// 遷移データ bag への書き込みを行うコールバック。宛先 Presenter / Effect が
		/// 同じ bag を共有して読む（<see cref="INavigationDataReader"/> 経由）。
		/// 複数の型を続けて Write してよい。
		/// <para>
		/// <b>注意</b>: ここで渡すのは「この 1 回の遷移限りの一時的な受け渡し」。Pop で下画面が
		/// <c>DestroyOnCover</c>（既定）で破棄された後に再表示される場合、復元ロードは空の bag で行われ
		/// この Configure の内容は再現されない。画面の再生成に耐えるべきパラメータは
		/// <see cref="IScreenIdentifier"/>（record）のフィールドに持たせること。
		/// </para>
		/// </summary>
		public Action<INavigationDataWriter> Configure { get; init; }

		/// <summary>
		/// <b>この遷移で入る新画面自身</b>のキャッシュ方針を上書きする（null なら Identifier の
		/// <see cref="IScreenIdentifier.CachePolicy"/>、それも null ならレイヤー既定）。
		/// 後でこの画面が別画面に覆われたとき、破棄する（<see cref="ScreenCacheMode.DestroyOnCover"/>）か
		/// 非表示で保持する（<see cref="ScreenCacheMode.KeepOnCover"/>）かを決める。
		/// 覆う側の遷移ではなく、覆われる画面自身を Push したときの指定が効く。
		/// <para>
		/// <b>注意</b>: この上書きが効くのは「その Push で生成されたインスタンス」のみ。
		/// 破棄された後に Pop で復元される場合、復元インスタンスの方針は Identifier の
		/// CachePolicy（無ければレイヤー既定）に戻る（<see cref="Configure"/> と同じく
		/// 復元には引き継がれない）。復元後も効かせたい方針は Identifier 側に持たせること。
		/// </para>
		/// </summary>
		public ScreenCacheMode? CachePolicyOverride { get; init; }
		public bool? ModalOverride { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct PopOptions
	{
		/// <summary>
		/// 遷移データ bag への書き込みコールバック。Pop では revealed 画面は既存のため、
		/// 主に Pop 遷移の Effect 用パラメータを seed する用途（戻り値の return store とは別チャネル）。
		/// </summary>
		public Action<INavigationDataWriter> Configure { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct ReplaceOptions
	{
		public Action<INavigationDataWriter> Configure { get; init; }
		public ScreenCacheMode? CachePolicyOverride { get; init; }
		public bool? ModalOverride { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct ChangeOptions
	{
		public Action<INavigationDataWriter> Configure { get; init; }
		public ScreenCacheMode? CachePolicyOverride { get; init; }
		public bool? ModalOverride { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct ResetOptions
	{
		public Action<INavigationDataWriter> Configure { get; init; }
		public ScreenCacheMode? CachePolicyOverride { get; init; }
		public bool? ModalOverride { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct PopToOptions
	{
		/// <summary>
		/// 遷移データ bag への書き込みコールバック（<see cref="PopOptions.Configure"/> と同じチャネル）。
		/// 中間画面の破棄を始める前に評価され、例外はスタック無傷のまま伝播する。
		/// </summary>
		public Action<INavigationDataWriter> Configure { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct ScreenTransitionEvent
	{
		public IScreenIdentifier From { get; }
		public IScreenIdentifier To { get; }
		public ScreenTransitionKind Kind { get; }

		/// <summary>
		/// この遷移が成功したか。<see cref="IScreenNavigator.OnTransitionStart"/> では常に true。
		/// <see cref="IScreenNavigator.OnTransitionEnd"/> では、ロールバック可能ゾーンでの失敗
		/// （ロード例外）や preempt によるキャンセルで遷移が完走しなかった場合に false になる。
		/// 完走必須ゾーンの hook 例外は遷移本筋を止めない（吸収される）ので Succeeded には影響しない。
		/// </summary>
		public bool Succeeded { get; }

		public ScreenTransitionEvent(IScreenIdentifier from, IScreenIdentifier to, ScreenTransitionKind kind, bool succeeded = true)
		{
			From = from;
			To = to;
			Kind = kind;
			Succeeded = succeeded;
		}
	}

	public enum ScreenTransitionKind
	{
		Push,
		Pop,
		Replace,
		Change,
		Reset,
		PopTo,
		/// <summary>参照指定で特定エントリを閉じる（<see cref="IScreenNavigator.Close"/>）。最上段でも中間でも Close。</summary>
		Close,
		/// <summary>全画面を畳む（<see cref="IScreenNavigator.DismissAll"/>）。</summary>
		DismissAll,
	}
}
