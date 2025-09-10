using Xunit;
namespace BlazorWP.E2E;
[CollectionDefinition("e2e")]
public sealed class E2ECollection : ICollectionFixture<BrowserFixture>, ICollectionFixture<WordPressFixture> { }
