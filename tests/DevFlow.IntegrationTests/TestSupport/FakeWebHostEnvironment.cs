using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace DevFlow.IntegrationTests.TestSupport;

// Only ContentRootPath is ever read by LocalFileBlobStorageService — the
// rest exist purely to satisfy IWebHostEnvironment's shape.
public sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string ContentRootPath { get; set; } = string.Empty;
    public string WebRootPath { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "DevFlow.IntegrationTests";
    public string EnvironmentName { get; set; } = "Test";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
