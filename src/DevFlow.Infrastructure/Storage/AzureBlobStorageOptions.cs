namespace DevFlow.Infrastructure.Storage;

public class AzureBlobStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string ContainerName { get; set; } = "task-attachments";

    public int DownloadUrlExpiryMinutes { get; set; } = 5;
}
