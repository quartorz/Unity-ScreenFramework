namespace ScreenFramework
{
	public readonly struct PushOptions
	{
		public INavigationData Data { get; init; }
		public IScreenTransitionDirector TransitionDirector { get; init; }
		public ScreenCacheMode? CachePolicyOverride { get; init; }
		public bool? ModalOverride { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct PopOptions
	{
		public IScreenTransitionDirector TransitionDirector { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct ReplaceOptions
	{
		public INavigationData Data { get; init; }
		public IScreenTransitionDirector TransitionDirector { get; init; }
		public ScreenCacheMode? CachePolicyOverride { get; init; }
		public bool? ModalOverride { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct ChangeOptions
	{
		public INavigationData Data { get; init; }
		public IScreenTransitionDirector TransitionDirector { get; init; }
		public ScreenCacheMode? CachePolicyOverride { get; init; }
		public bool? ModalOverride { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct ResetOptions
	{
		public INavigationData Data { get; init; }
		public IScreenTransitionDirector TransitionDirector { get; init; }
		public ScreenCacheMode? CachePolicyOverride { get; init; }
		public bool? ModalOverride { get; init; }
		public InterruptPriority InterruptPriority { get; init; }
	}

	public readonly struct PopToOptions
	{
		public IScreenTransitionDirector TransitionDirector { get; init; }
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
