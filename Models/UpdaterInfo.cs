using System;

namespace ArcademiaGameLauncher.Models
{
    public class UpdaterInfo
    {
        public string VersionNumber { get; set; } = null!;
        public string FileUrl { get; set; } = null!;
        public DateTime UploadedAt { get; set; }
    }
}
