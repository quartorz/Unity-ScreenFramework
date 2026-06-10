using Cysharp.Threading.Tasks;
using Sample.Api;
using Sample.Api.Net;

namespace Sample.Debug
{
	/// <summary>
	/// <see cref="IMasterService"/> のデバッグ実装。固定のマスタ一式を返す。
	/// </summary>
	public sealed class DebugMasterService : DebugApiBase, IMasterService
	{
		public DebugMasterService(UserDataHolder userData, DebugScenarioState scenario)
			: base(userData, scenario) { }

		public UniTask<BootstrapMasterResponse> Bootstrap(Options opt = default)
			=> Send(DummyResponses.MasterBootstrap, opt);
	}
}
