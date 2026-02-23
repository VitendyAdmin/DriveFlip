namespace DriveFlip.Constants;

// ── WMI Property Name Constants ──

public static class Wmi
{
    public static class Win32DiskDrive
    {
        public const string Index = "Index";
        public const string Model = "Model";
        public const string SerialNumber = "SerialNumber";
        public const string InterfaceType = "InterfaceType";
        public const string MediaType = "MediaType";
        public const string Size = "Size";
        public const string BytesPerSector = "BytesPerSector";
        public const string FirmwareRevision = "FirmwareRevision";
        public const string Status = "Status";
        public const string PNPDeviceID = "PNPDeviceID";
    }

    public static class PhysicalDisk
    {
        public const string BusType = "BusType";
        public const string Model = "Model";
        public const string FriendlyName = "FriendlyName";
        public const string PhysicalSectorSize = "PhysicalSectorSize";
        public const string LogicalSectorSize = "LogicalSectorSize";
        public const string PartNumber = "PartNumber";
        public const string HealthStatus = "HealthStatus";
        public const string MediaType = "MediaType";
        public const string SpindleSpeed = "SpindleSpeed";
        public const string IsWriteCacheEnabled = "IsWriteCacheEnabled";
        public const string IsPowerProtected = "IsPowerProtected";
    }

    public static class StorageReliability
    {
        public const string Temperature = "Temperature";
        public const string TemperatureMax = "TemperatureMax";
        public const string PowerOnHours = "PowerOnHours";
        public const string ReadErrorsTotal = "ReadErrorsTotal";
        public const string WriteErrorsTotal = "WriteErrorsTotal";
        public const string Wear = "Wear";
        public const string ReadErrorsCorrected = "ReadErrorsCorrected";
        public const string ReadErrorsUncorrected = "ReadErrorsUncorrected";
        public const string WriteErrorsCorrected = "WriteErrorsCorrected";
        public const string WriteErrorsUncorrected = "WriteErrorsUncorrected";
        public const string ReadLatencyMax = "ReadLatencyMax";
        public const string WriteLatencyMax = "WriteLatencyMax";
        public const string FlushLatencyMax = "FlushLatencyMax";
        public const string StartStopCycleCount = "StartStopCycleCount";
        public const string StartStopCycleCountMax = "StartStopCycleCountMax";
        public const string LoadUnloadCycleCount = "LoadUnloadCycleCount";
        public const string LoadUnloadCycleCountMax = "LoadUnloadCycleCountMax";
        public const string ManufactureDate = "ManufactureDate";
    }

    public static class MsftDisk
    {
        public const string PartitionStyle = "PartitionStyle";
        public const string Number = "Number";
    }

    public static class MsftPartition
    {
        public const string PartitionNumber = "PartitionNumber";
        public const string DriveLetter = "DriveLetter";
    }
}

// ── Type-Safe Storage Enums ──

public enum StorageBusType
{
    Unknown = 0,
    SCSI = 1,
    ATAPI = 2,
    ATA = 3,
    IEEE1394 = 5,
    SSA = 6,
    USB = 7,
    RAID = 8,
    iSCSI = 9,
    SAS = 10,
    SATA = 11,
    SD = 12,
    MMC = 13,
    FileBackedVirtual = 15,
    StorageSpaces = 16,
    NVMe = 17
}

public enum StorageMediaType
{
    Unspecified = 0,
    HDD = 3,
    SSD = 4,
    SCM = 5
}

public enum DiskPartitionStyle
{
    RAW = 0,
    MBR = 1,
    GPT = 2
}

public enum WmiHealthStatus
{
    Healthy = 0,
    Warning = 1,
    Unhealthy = 2
}

public static class StorageEnumExtensions
{
    public static string ToDisplayName(this StorageBusType t) => t switch
    {
        StorageBusType.Unknown => "Unknown",
        StorageBusType.SCSI => "SCSI",
        StorageBusType.ATAPI => "ATAPI",
        StorageBusType.ATA => "ATA",
        StorageBusType.IEEE1394 => "1394",
        StorageBusType.SSA => "SSA",
        StorageBusType.USB => "USB",
        StorageBusType.RAID => "RAID",
        StorageBusType.iSCSI => "iSCSI",
        StorageBusType.SAS => "SAS",
        StorageBusType.SATA => "SATA",
        StorageBusType.SD => "SD",
        StorageBusType.MMC => "MMC",
        StorageBusType.FileBackedVirtual => "File Backed Virtual",
        StorageBusType.StorageSpaces => "Storage Spaces",
        StorageBusType.NVMe => "NVMe",
        _ => $"Other ({(int)t})"
    };

    public static string ToDisplayName(this StorageMediaType t) => t switch
    {
        StorageMediaType.HDD => "HDD",
        StorageMediaType.SSD => "SSD",
        StorageMediaType.SCM => "SCM",
        _ => "Unspecified"
    };

    public static string ToDisplayName(this DiskPartitionStyle s) => s switch
    {
        DiskPartitionStyle.MBR => "MBR",
        DiskPartitionStyle.GPT => "GPT",
        _ => "RAW"
    };

    public static string ToDisplayName(this WmiHealthStatus s) => s switch
    {
        WmiHealthStatus.Healthy => "Healthy",
        WmiHealthStatus.Warning => "Warning",
        WmiHealthStatus.Unhealthy => "Unhealthy",
        _ => $"Unknown ({(int)s})"
    };

    public static bool IsRemovableBus(this StorageBusType t) =>
        t is StorageBusType.USB or StorageBusType.SD or StorageBusType.MMC;

    public static bool IsSataFamily(this StorageBusType t) =>
        t is StorageBusType.SATA or StorageBusType.ATA or StorageBusType.ATAPI;
}

// ── ATA / SMART Hardware Constants ──

public static class Ata
{
    public const byte CmdIdentifyDevice = 0xEC;
    public const byte CmdSmart = 0xB0;
    public const byte SmartReadData = 0xD0;
    public const byte SmartReadThresholds = 0xD1;
    public const byte CylLow = 0x4F;
    public const byte CylHigh = 0xC2;

    // ATA IDENTIFY word offsets
    public const int WordSerial = 10;
    public const int WordFirmware = 23;
    public const int WordModel = 27;
    public const int WordTrimSupport = 69;
    public const int WordQueueDepth = 75;
    public const int WordSataCapabilities = 76;
    public const int WordSataCurrent = 77;
    public const int WordFeatures82 = 82;
    public const int WordFeatures83 = 83;
    public const int WordFeaturesEnabled85 = 85;
    public const int WordFeaturesEnabled86 = 86;
    public const int WordUdma = 88;
}

public static class Smart
{
    public const int HeaderSize = 16;
    public const int VersionSize = 2;
    public const int TableOffset = HeaderSize + VersionSize; // 18
    public const int SlotCount = 30;
    public const int SlotSize = 12;
    public const int AttrBufferSize = 512;
}

public static class NVMe
{
    public const int PropertyIdProtocolSpecific = 50;
    public const int ProtocolTypeNvme = 3;
    public const int DataTypeLogPage = 2;
    public const int LogPageHealthInfo = 2;
    public const int HealthLogSize = 512;
    public const int ProtocolDataStructSize = 40;

    // Health log byte offsets
    public const int OffsetCompositeTemp = 1;
    public const int OffsetAvailableSpare = 3;
    public const int OffsetSpareThreshold = 4;
    public const int OffsetPercentageUsed = 5;
    public const int OffsetDataUnitsRead = 32;
    public const int OffsetDataUnitsWritten = 48;
    public const int OffsetControllerBusy = 96;
    public const int OffsetPowerCycles = 112;
    public const int OffsetPowerOnHours = 128;
    public const int OffsetUnsafeShutdowns = 144;
    public const int OffsetMediaErrors = 160;
    public const int OffsetTempSensor1 = 200;
    public const int OffsetTempSensor2 = 202;
    public const int KelvinOffset = 273;
}

public static class Ioctl
{
    public const uint SmartRcvDriveData = 0x0007C088;
    public const uint ScsiPassThrough = 0x0004D004;
    public const uint StorageQueryProperty = 0x002D1400;
}

public static class Sat
{
    public const byte OpCode = 0xA1;           // ATA PASS-THROUGH (12)
    public const byte ProtocolPioIn = 4 << 1;  // Protocol: PIO Data-In
    public const byte TransferFlags = 0x0E;    // T_DIR=1, BYT_BLOK=1, T_LENGTH=10
}

// ── Risk Assessment Thresholds ──

public static class RiskThresholds
{
    public const int ReallocatedCritical = 100;
    public const int WearCritical = 90;
    public const int WearWarning = 70;
    public const int TempCritical = 65;
    public const int TempWarning = 55;
    public const int CrcErrorWarning = 100;
    public const int NvmeUsedCritical = 95;
    public const int NvmeUsedWarning = 80;
}
