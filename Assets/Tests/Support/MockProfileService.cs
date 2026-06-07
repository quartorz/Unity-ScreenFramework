using Sample.Api;

namespace Tests.Support
{
	[MockGenerator.GenerateMockFor(typeof(IProfileService))]
	public partial class MockProfileService { }
}
