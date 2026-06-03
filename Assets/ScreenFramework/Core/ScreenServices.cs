namespace ScreenFramework
{
	/// <summary>
	/// プロジェクトごとに継承して共通依存を増やすための基底クラス。
	/// UseMockViews フラグだけは framework が用意する。
	/// </summary>
	public abstract class ScreenServices
	{
		public bool UseMockViews { get; }

		protected ScreenServices(bool useMockViews)
		{
			UseMockViews = useMockViews;
		}
	}
}
