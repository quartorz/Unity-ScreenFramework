using ScreenFramework;

namespace Sample.Debug
{
	/// <summary>
	/// デバッグ起動で開ける画面の定義一覧。
	/// 既定は「目的画面の下に Home を 1 枚敷く」構成で、Pop・退場/下画面復帰エフェクトまで確認できる。
	/// 文脈依存の強い画面（経路を積まないと意味がない画面）はここに個別の経路付きエントリを足す。
	/// </summary>
	public static class DebugScreenIds
	{
		public static readonly DebugScreenEntry[] Entries =
		{
			new("Title", () => new IScreenIdentifier[]
			{
				new TitleScreenId(),
			}),
			new("Home", () => new IScreenIdentifier[]
			{
				new HomeScreenId(),
			}),
			new("GachaTop", () => new IScreenIdentifier[]
			{
				new HomeScreenId(),
				new GachaTopScreenId(),
			}),
			new("GachaResult (1連)", () => new IScreenIdentifier[]
			{
				new HomeScreenId(),
				new GachaTopScreenId(),
				new GachaResultScreenId(DummyResponses.GachaPull(1)),
			}),
			new("GachaResult (10連)", () => new IScreenIdentifier[]
			{
				new HomeScreenId(),
				new GachaTopScreenId(),
				new GachaResultScreenId(DummyResponses.GachaPull(10)),
			}),
			new("Profile", () => new IScreenIdentifier[]
			{
				new HomeScreenId(),
				new ProfileScreenId(DummyResponses.UserId),
			}),
		};

		/// <summary>起動直後に開く既定画面。</summary>
		public static IScreenIdentifier[] DefaultRoute() => new IScreenIdentifier[] { new HomeScreenId() };
	}
}
