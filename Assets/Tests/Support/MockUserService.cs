using Sample.Api;

namespace Tests.Support
{
	[MockGenerator.GenerateMockFor(typeof(IUserService))]
	public partial class MockUserService { }
}
