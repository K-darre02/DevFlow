namespace DevFlow.Api.Storage;

public class LocalFileStorageOptions
{
    // Relative to IWebHostEnvironment.ContentRootPath — deliberately *not*
    // wwwroot (this Api project doesn't even serve static files — no
    // UseStaticFiles() in Program.cs — but App_Data is the conventional
    // "never served" ASP.NET Core directory name regardless, so this stays
    // safe even if that changes). The only way to read a file here is
    // through the signed-URL passthrough endpoint (BlobDownloadsController).
    public string RootPath { get; set; } = "App_Data/attachments";

    public string SigningKey { get; set; } = string.Empty;

    public int DownloadUrlExpiryMinutes { get; set; } = 5;
}
