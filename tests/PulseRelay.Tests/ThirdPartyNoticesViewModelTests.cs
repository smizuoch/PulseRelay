using PulseRelay.Desktop.ViewModels;
using Xunit;

namespace PulseRelay.Tests;

public class ThirdPartyNoticesViewModelTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PulseRelayTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Loads_notices_from_base_directory()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, ThirdPartyNoticesViewModel.FileName);
        File.WriteAllText(path, "license text");

        var viewModel = new ThirdPartyNoticesViewModel(_directory);

        Assert.Equal(path, viewModel.NoticesPath);
        Assert.Equal("license text", viewModel.NoticesText);
    }

    [Fact]
    public void Default_constructor_uses_app_base_directory()
    {
        var viewModel = new ThirdPartyNoticesViewModel();

        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, ThirdPartyNoticesViewModel.FileName),
            viewModel.NoticesPath);
    }

    [Fact]
    public void Missing_notices_file_returns_localized_message_with_path()
    {
        Directory.CreateDirectory(_directory);
        string expectedPath = Path.Combine(_directory, ThirdPartyNoticesViewModel.FileName);

        var viewModel = new ThirdPartyNoticesViewModel(_directory);

        Assert.Equal(expectedPath, viewModel.NoticesPath);
        Assert.Contains(ThirdPartyNoticesViewModel.FileName, viewModel.NoticesText);
        Assert.Contains(expectedPath, viewModel.NoticesText);
    }

    [Fact]
    public void Unreadable_notices_file_returns_localized_error()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, ThirdPartyNoticesViewModel.FileName);
        File.WriteAllText(path, "license text");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            var viewModel = new ThirdPartyNoticesViewModel(_directory);

            Assert.Contains(ThirdPartyNoticesViewModel.FileName, viewModel.NoticesText);
            Assert.Contains(path, viewModel.NoticesText);
            Assert.NotEqual("license text", viewModel.NoticesText);
        }
        finally
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
