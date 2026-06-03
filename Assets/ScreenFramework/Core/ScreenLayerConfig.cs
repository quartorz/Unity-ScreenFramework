namespace ScreenFramework
{
	public sealed class ScreenLayerConfig
	{
		public IScreenContainer Container { get; init; }
		public ScreenCacheMode DefaultCacheMode { get; init; } = ScreenCacheMode.DestroyOnCover;
		public StackMode StackMode { get; init; } = StackMode.Cover;
		public StackInputPolicy StackInputPolicy { get; init; } = StackInputPolicy.BlockUnderlying;
		public bool DefaultModal { get; init; } = true;
		public IScreenTransitionDirector DefaultTransition { get; init; } = ImmediateTransition.Instance;
	}

	public sealed class ScreenLayerSetup
	{
		public ScreenLayerConfig Page { get; init; }
		public ScreenLayerConfig Dialog { get; init; }
		public ScreenLayerConfig SystemDialog { get; init; }
	}
}
