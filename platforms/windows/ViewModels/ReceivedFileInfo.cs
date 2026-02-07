using System;

namespace VoidWarp.Windows.ViewModels
{
    public enum TransferStatus
    {
        InProgress,
        Success,
        Failed,
        Deleted
    }

    public class ReceivedFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty; // Formatted size
        public string SenderName { get; set; } = string.Empty;
        public DateTime ReceivedTime { get; set; } = DateTime.Now;
        public TransferStatus Status { get; set; } = TransferStatus.InProgress;
        public bool IsFolder { get; set; }

        public bool FileExists => IsFolder 
            ? System.IO.Directory.Exists(FilePath) 
            : System.IO.File.Exists(FilePath);
    }
}
