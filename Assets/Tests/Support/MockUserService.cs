using Sample.Api;

namespace Tests
{
	[MockGenerator.GenerateMockFor(typeof(IUserService))]
	public partial class MockUserService { }
}
