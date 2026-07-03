namespace METERP.Application.Options;

public class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    /// <summary>Root folder for uploaded files (relative to content root or absolute).</summary>
    public string RootPath { get; set; } = "uploads";
}