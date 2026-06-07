using Cysharp.Threading.Tasks;
using Sample.Api.Net;

namespace Sample.Api
{
	[MockGenerator.GenerateInterface]
	public sealed partial class MasterService : ApiBase
	{
		public MasterService(UserDataHolder userData) : base(userData) { }

		public UniTask<BootstrapMasterResponse> Bootstrap(Options opt = default)
			=> Send<BootstrapMasterResponse>(new HttpRequest { Path = "/master/bootstrap" }, opt);
	}
}
