using System.Collections.Generic;

namespace DriveFlip.Services;

/// <summary>
/// Thin interface over OS-level data acquisition.
/// Returns dictionaries (replacing ManagementObject) and raw byte arrays (replacing P/Invoke results).
/// Implementations: WmiDriveDataProvider (real), JsonDriveDataProvider (dump replay).
/// </summary>
public interface IDriveDataProvider
{
    /// <summary>
    /// Returns all Win32_DiskDrive instances as property bags.
    /// </summary>
    List<Dictionary<string, object?>> GetWin32DiskDrives();

    /// <summary>
    /// Returns all MSFT_PhysicalDisk properties for a given device number.
    /// </summary>
    Dictionary<string, object?>? GetPhysicalDisk(int deviceNumber);

    /// <summary>
    /// Returns all MSFT_StorageReliabilityCounter properties for a given device number.
    /// </summary>
    Dictionary<string, object?>? GetReliabilityCounters(int deviceNumber);

    /// <summary>
    /// Returns drive partitions and drive letters via Win32 association chain.
    /// </summary>
    (List<string> Partitions, List<string> DriveLetters) GetDriveLettersWin32(string devicePath);

    /// <summary>
    /// Returns drive partitions and drive letters via MSFT_Disk → MSFT_Partition.
    /// </summary>
    (List<string> Partitions, List<string> DriveLetters) GetDriveLettersMsft(int deviceNumber);

    /// <summary>
    /// Returns partition style (0=RAW, 1=MBR, 2=GPT) for a given device number.
    /// </summary>
    int? GetPartitionStyle(int deviceNumber);

    /// <summary>
    /// Returns the raw 512-byte ATA IDENTIFY DEVICE buffer via SAT (for USB drives).
    /// </summary>
    byte[]? GetAtaIdentifyViaSat(int deviceNumber);

    /// <summary>
    /// Returns the raw 512-byte ATA IDENTIFY DEVICE buffer via SMART IOCTL (for SATA drives).
    /// </summary>
    byte[]? GetAtaIdentifyViaSmart(int deviceNumber);

    /// <summary>
    /// Returns USB bridge VID/PID from Win32_PnPEntity.
    /// Returns null if not a USB device or bridge not found.
    /// </summary>
    (string Vid, string Pid)? GetUsbBridgeVidPid(string pnpDeviceId);

    /// <summary>
    /// Returns SMART attribute and threshold buffers (full IOCTL output including headers).
    /// Parser reads from Smart.TableOffset.
    /// </summary>
    (byte[]? Attributes, byte[]? Thresholds) GetSmartData(int deviceNumber);

    /// <summary>
    /// Returns the full NVMe health log IOCTL output buffer.
    /// Parser reads from offset 8 + NVMe.ProtocolDataStructSize.
    /// </summary>
    byte[]? GetNvmeHealthLog(int deviceNumber);

    /// <summary>
    /// Returns a comprehensive dump of all available data for a drive,
    /// including WMI properties, raw byte buffers (base64), and metadata.
    /// </summary>
    Dictionary<string, object?> GetFullDump(int deviceNumber);
}
