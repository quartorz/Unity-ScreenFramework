using System;

namespace ScreenFramework
{
	public readonly struct PushOptions
	{
		/// <summary>
		/// 遷移データ bag への書き込みを行うコールバック。宛先 Presenter / Effect が
		/// 同じ bag を共有して読む（<see cref="INavigationDataReader"/> 経由）。
		/// 複数の型を続けて Write してよい。
		/// </summary>
		public Action<INavigationDataWriter> Configure { get; init; }
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
		public Action<INavigationDataWriter> Configure { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct ScreenTransitionEvent
	{
		public IScreenIdentifier From { get; }
		public IScreenIdentifier To { get; }
		public ScreenTransitionKind Kind { get; }

		public ScreenTransitionEvent(IScreenIdentifier from, IScreenIdentifier to, ScreenTransitionKind kind)
		{
			From = from;
			To = to;
			Kind = kind;
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
	}
}
