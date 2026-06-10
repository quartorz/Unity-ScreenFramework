using System;
using ScreenFramework;

namespace Sample.Debug
{
	/// <summary>
	/// 画面ピッカーの 1 エントリ。Route は Page レイヤーに下から順に積む ScreenId 列を返す
	/// （先頭が Reset、以降が Push される）。ScreenId にペイロードが要る画面は
	/// ここのファクトリが <see cref="DummyResponses"/> から生成する。
	/// </summary>
	public sealed record DebugScreenEntry(string Label, Func<IScreenIdentifier[]> Route);
}
