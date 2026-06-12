using PulseRelay.App.Localization;

namespace PulseRelay.Desktop.ViewModels;

public sealed class ThirdPartyNoticesViewModel
{
    public const string FileName = "THIRD-PARTY-NOTICES.txt";

    public ThirdPartyNoticesViewModel()
        : this(AppContext.BaseDirectory)
    {
    }

    public ThirdPartyNoticesViewModel(string baseDirectory)
    {
        NoticesPath = Path.Combine(baseDirectory, FileName);
        NoticesText = LoadNotices(NoticesPath);
    }

    public string NoticesPath { get; }

    public string NoticesText { get; }

    private static string LoadNotices(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        catch (IOException ex)
        {
            return LocalizationManager.Format("ThirdParty_ReadError", FileName, path, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return LocalizationManager.Format("ThirdParty_ReadError", FileName, path, ex.Message);
        }

        return LocalizationManager.Format("ThirdParty_Missing", FileName, path);
    }
}
