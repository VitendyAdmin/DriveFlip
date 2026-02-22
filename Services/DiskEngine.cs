using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DriveFlip.Models;
using Microsoft.Win32.SafeHandles;

namespace DriveFlip.Services;

[SupportedOSPlatform("windows")]
public class DiskEngine : IDisposable
{
    // P/Invoke for raw disk access
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
    private const uint FSCTL_LOCK_VOLUME = 0x00090018;
    private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;

    private const int SECTORS_PER_READ = 256; // 128 KB per read (at 512 bytes/sector)

    private bool _disposed;

    /// <summary>
    /// Perform a random surface check for a configurable duration, sampling sectors
    /// across the drive to look for read errors and data presence.
    /// </summary>
    public async Task<SurfaceCheckReport> RunSurfaceCheckAsync(
        PhysicalDrive drive,
        int durationMinutes = 15,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        var report = new SurfaceCheckReport
        {
            DriveNumber = drive.DeviceNumber,
            DriveModel = drive.Model,
            DriveSerial = drive.SerialNumber,
            DriveSizeBytes = drive.SizeBytes,
            TotalSectors = drive.TotalSectors,
            StartTime = DateTime.Now
        };

        Logger.Info($"Surface check starting: Disk {drive.DeviceNumber} ({drive.Model}), duration={durationMinutes}min");

        var sw = Stopwatch.StartNew();
        var totalDuration = TimeSpan.FromMinutes(durationMinutes);
        var bytesPerSector = drive.BytesPerSector;
        var bufferSize = bytesPerSector * SECTORS_PER_READ;
        var maxSectorStart = drive.TotalSectors - SECTORS_PER_READ;

        if (maxSectorStart <= 0)
        {
            report.EndTime = DateTime.Now;
            return report;
        }

        await Task.Run(() =>
        {
            using var handle = OpenDiskForRead(drive.DevicePath);
            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                throw new UnauthorizedAccessException(
                    $"Could not open Disk {drive.DeviceNumber} for reading (Win32 error {err}). Run as Administrator.");
            }

            using var stream = new FileStream(handle, FileAccess.Read, bufferSize, false);
            var buffer = new byte[bufferSize];
            var emptyBuffer = new byte[bufferSize];
            long operationsCompleted = 0;

            while (sw.Elapsed < totalDuration && !cancellation.IsCancellationRequested)
            {
                long randomSector = RandomLong(0, maxSectorStart);
                long byteOffset = randomSector * bytesPerSector;

                try
                {
                    stream.Seek(byteOffset, SeekOrigin.Begin);
                    int bytesRead = stream.Read(buffer, 0, bufferSize);

                    if (bytesRead > 0)
                    {
                        report.TotalSectorsSampled += bytesRead / bytesPerSector;

                        bool hasData = !buffer.AsSpan(0, bytesRead).SequenceEqual(
                            emptyBuffer.AsSpan(0, bytesRead));
                        if (hasData)
                            report.SectorsWithData += bytesRead / bytesPerSector;
                        else
                            report.SectorsEmpty += bytesRead / bytesPerSector;
                    }
                }
                catch (IOException)
                {
                    report.ReadErrors++;
                    report.BadSectors.Add(randomSector);
                    Logger.Warning($"Read error on Disk {drive.DeviceNumber} at sector {randomSector}");
                }
                catch (Exception ex)
                {
                    report.ReadErrors++;
                    Logger.Error($"Unexpected error reading Disk {drive.DeviceNumber} at sector {randomSector}", ex);
                }

                operationsCompleted++;

                if (operationsCompleted % 50 == 0)
                {
                    var pct = Math.Min(100.0, sw.Elapsed.TotalSeconds / totalDuration.TotalSeconds * 100);
                    var remaining = totalDuration - sw.Elapsed;
                    if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

                    var bytesProcessed = report.TotalSectorsSampled * bytesPerSector;
                    var speedMBps = sw.Elapsed.TotalSeconds > 0
                        ? bytesProcessed / 1_048_576.0 / sw.Elapsed.TotalSeconds
                        : 0;

                    progress?.Report(new OperationProgress
                    {
                        DriveNumber = drive.DeviceNumber,
                        Operation = OperationType.SurfaceCheck,
                        PercentComplete = pct,
                        SectorsProcessed = report.TotalSectorsSampled,
                        TotalSectorsToProcess = drive.TotalSectors,
                        ErrorCount = report.ReadErrors,
                        DataSectorsFound = report.SectorsWithData,
                        Elapsed = sw.Elapsed,
                        EstimatedRemaining = remaining,
                        SpeedMBps = speedMBps,
                        StatusMessage = $"Sampling... {report.TotalSectorsSampled:N0} sectors checked"
                    });
                }
            }
        }, cancellation);

        report.EndTime = DateTime.Now;
        Logger.Info($"Surface check complete: Disk {drive.DeviceNumber} — {report.TotalSectorsSampled:N0} sectors sampled, {report.ReadErrors} errors, {report.DataPresencePercent:F1}% data");

        progress?.Report(new OperationProgress
        {
            DriveNumber = drive.DeviceNumber,
            Operation = OperationType.SurfaceCheck,
            PercentComplete = 100,
            IsComplete = true,
            WasCancelled = cancellation.IsCancellationRequested,
            SectorsProcessed = report.TotalSectorsSampled,
            TotalSectorsToProcess = drive.TotalSectors,
            ErrorCount = report.ReadErrors,
            DataSectorsFound = report.SectorsWithData,
            Elapsed = sw.Elapsed,
            StatusMessage = cancellation.IsCancellationRequested ? "Cancelled" : "Complete"
        });

        return report;
    }

    /// <summary>
    /// Smart Wipe: head + tail + random scatter. Much faster than full wipe
    /// while effectively destroying file system structures and making recovery impractical.
    /// </summary>
    public async Task<WipeReport> RunSmartWipeAsync(
        PhysicalDrive drive,
        WipeSettings settings,
        WipeMethod method,
        bool verifyAfterWipe,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        var report = new WipeReport
        {
            DriveNumber = drive.DeviceNumber,
            DriveModel = drive.Model,
            DriveSizeBytes = drive.SizeBytes,
            Method = method,
            Mode = WipeMode.SmartWipe,
            StartTime = DateTime.Now
        };

        Logger.Info($"Smart wipe starting: Disk {drive.DeviceNumber}, head/tail={settings.HeadTailSizeGB}GB, scatter={settings.ScatterDurationMinutes}min, passes={settings.NumberOfPasses}");

        var sw = Stopwatch.StartNew();
        var bytesPerSector = drive.BytesPerSector;
        var bufferSize = bytesPerSector * SECTORS_PER_READ;
        var totalSectors = drive.TotalSectors;

        // Calculate head/tail sector counts, clamped to half the drive each
        long headTailBytes = (long)settings.HeadTailSizeGB * 1_073_741_824L;
        long headSectors = Math.Min(headTailBytes / bytesPerSector, totalSectors / 2);
        long tailSectors = Math.Min(headTailBytes / bytesPerSector, totalSectors / 2);

        // For progress: estimate scatter work as equal to head+tail combined
        long scatterEstimate = headSectors + tailSectors;
        long totalWorkPerPass = headSectors + tailSectors + scatterEstimate;
        long totalWork = totalWorkPerPass * settings.NumberOfPasses;

        await Task.Run(() =>
        {
            using var handle = OpenDiskForWrite(drive.DevicePath);
            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                throw new UnauthorizedAccessException(
                    $"Could not open Disk {drive.DeviceNumber} for writing (Win32 error {err}). Run as Administrator.");
            }

            LockAndDismountVolumes(drive);

            using var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize, false);
            var buffer = new byte[bufferSize];

            for (int pass = 0; pass < settings.NumberOfPasses && !cancellation.IsCancellationRequested; pass++)
            {
                bool useRandom = method == WipeMethod.RandomFill ||
                    (method == WipeMethod.RandomThenZero && pass == 0);

                if (useRandom)
                    RandomNumberGenerator.Fill(buffer);
                else
                    Array.Clear(buffer);

                long passBase = pass * totalWorkPerPass;

                // ── Phase 1: Head wipe ──
                Logger.Info($"Disk {drive.DeviceNumber} pass {pass + 1}: Phase 1 — Head wipe ({headSectors} sectors)");
                WriteSequentialRegion(stream, 0, headSectors, buffer, bufferSize, bytesPerSector,
                    useRandom, report, drive.DeviceNumber, passBase, totalWork, sw, progress, cancellation,
                    $"Head wipe (Pass {pass + 1}/{settings.NumberOfPasses})");

                if (cancellation.IsCancellationRequested) break;

                // ── Phase 2: Tail wipe ──
                long tailStart = totalSectors - tailSectors;
                Logger.Info($"Disk {drive.DeviceNumber} pass {pass + 1}: Phase 2 — Tail wipe ({tailSectors} sectors from sector {tailStart})");
                WriteSequentialRegion(stream, tailStart, tailSectors, buffer, bufferSize, bytesPerSector,
                    useRandom, report, drive.DeviceNumber, passBase + headSectors, totalWork, sw, progress, cancellation,
                    $"Tail wipe (Pass {pass + 1}/{settings.NumberOfPasses})");

                if (cancellation.IsCancellationRequested) break;

                // ── Phase 3: Random scatter wipe ──
                long middleStart = headSectors;
                long middleEnd = totalSectors - tailSectors;
                if (middleEnd > middleStart)
                {
                    Logger.Info($"Disk {drive.DeviceNumber} pass {pass + 1}: Phase 3 — Random scatter ({settings.ScatterDurationMinutes} min)");
                    var scatterDuration = TimeSpan.FromMinutes(settings.ScatterDurationMinutes);
                    var scatterSw = Stopwatch.StartNew();
                    long scatterSectorsWritten = 0;

                    while (scatterSw.Elapsed < scatterDuration && !cancellation.IsCancellationRequested)
                    {
                        long randomSector = RandomLong(middleStart, middleEnd - SECTORS_PER_READ);
                        if (randomSector < middleStart) randomSector = middleStart;
                        long byteOffset = randomSector * bytesPerSector;

                        try
                        {
                            if (useRandom && scatterSectorsWritten % (SECTORS_PER_READ * 100) == 0)
                                RandomNumberGenerator.Fill(buffer);

                            stream.Seek(byteOffset, SeekOrigin.Begin);
                            stream.Write(buffer, 0, bufferSize);
                            scatterSectorsWritten += SECTORS_PER_READ;
                            report.SectorsWritten += SECTORS_PER_READ;
                        }
                        catch (IOException)
                        {
                            report.WriteErrors++;
                        }

                        if (scatterSectorsWritten % (SECTORS_PER_READ * 50) == 0)
                        {
                            var scatterFraction = Math.Min(1.0, scatterSw.Elapsed.TotalSeconds / scatterDuration.TotalSeconds);
                            var doneWork = passBase + headSectors + tailSectors + (long)(scatterEstimate * scatterFraction);
                            var pct = (double)doneWork / totalWork * 100;
                            var speed = sw.Elapsed.TotalSeconds > 0
                                ? (report.SectorsWritten * bytesPerSector / 1_048_576.0) / sw.Elapsed.TotalSeconds
                                : 0;

                            progress?.Report(new OperationProgress
                            {
                                DriveNumber = drive.DeviceNumber,
                                Operation = OperationType.SecureWipe,
                                PercentComplete = pct,
                                SectorsProcessed = report.SectorsWritten,
                                TotalSectorsToProcess = totalWork,
                                ErrorCount = report.WriteErrors,
                                Elapsed = sw.Elapsed,
                                SpeedMBps = speed,
                                StatusMessage = $"Scatter wipe (Pass {pass + 1}/{settings.NumberOfPasses})... {pct:F1}%"
                            });
                        }
                    }
                    Logger.Info($"Disk {drive.DeviceNumber} scatter complete: {scatterSectorsWritten:N0} sectors written");
                }

                stream.Flush();
            }

            report.Completed = !cancellation.IsCancellationRequested;
        }, cancellation);

        report.EndTime = DateTime.Now;
        Logger.Info($"Smart wipe {(report.Completed ? "complete" : "cancelled")}: Disk {drive.DeviceNumber} — {report.SectorsWritten:N0} sectors written, {report.WriteErrors} errors");

        progress?.Report(new OperationProgress
        {
            DriveNumber = drive.DeviceNumber,
            Operation = OperationType.SecureWipe,
            PercentComplete = 100,
            IsComplete = true,
            WasCancelled = cancellation.IsCancellationRequested,
            Elapsed = sw.Elapsed,
            StatusMessage = cancellation.IsCancellationRequested ? "Cancelled" : "Smart wipe complete"
        });

        return report;
    }

    /// <summary>
    /// Full sequential wipe of every sector on the drive.
    /// </summary>
    public async Task<WipeReport> RunFullWipeAsync(
        PhysicalDrive drive,
        WipeMethod method,
        int numberOfPasses,
        bool verifyAfterWipe,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        var report = new WipeReport
        {
            DriveNumber = drive.DeviceNumber,
            DriveModel = drive.Model,
            DriveSizeBytes = drive.SizeBytes,
            Method = method,
            Mode = WipeMode.FullWipe,
            StartTime = DateTime.Now
        };

        Logger.Info($"Full wipe starting: Disk {drive.DeviceNumber}, method={method}, passes={numberOfPasses}");

        var sw = Stopwatch.StartNew();
        var bytesPerSector = drive.BytesPerSector;
        var bufferSize = bytesPerSector * SECTORS_PER_READ;
        var totalSectors = drive.TotalSectors;
        var passes = method == WipeMethod.RandomThenZero ? 2 * numberOfPasses : numberOfPasses;

        await Task.Run(() =>
        {
            using var handle = OpenDiskForWrite(drive.DevicePath);
            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                throw new UnauthorizedAccessException(
                    $"Could not open Disk {drive.DeviceNumber} for writing (Win32 error {err}). Run as Administrator.");
            }

            LockAndDismountVolumes(drive);

            using var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize, false);
            var buffer = new byte[bufferSize];

            for (int pass = 0; pass < passes && !cancellation.IsCancellationRequested; pass++)
            {
                bool useRandom = method == WipeMethod.RandomFill ||
                    (method == WipeMethod.RandomThenZero && pass % 2 == 0);

                if (useRandom)
                    RandomNumberGenerator.Fill(buffer);
                else
                    Array.Clear(buffer);

                stream.Seek(0, SeekOrigin.Begin);
                long sectorsWritten = 0;

                while (sectorsWritten < totalSectors && !cancellation.IsCancellationRequested)
                {
                    var sectorsRemaining = totalSectors - sectorsWritten;
                    var sectorsThisBatch = (int)Math.Min(SECTORS_PER_READ, sectorsRemaining);
                    var bytesToWrite = sectorsThisBatch * bytesPerSector;

                    try
                    {
                        if (useRandom && sectorsWritten % (SECTORS_PER_READ * 100) == 0)
                            RandomNumberGenerator.Fill(buffer);

                        stream.Write(buffer, 0, bytesToWrite);
                        sectorsWritten += sectorsThisBatch;
                        report.SectorsWritten = sectorsWritten + (pass * totalSectors);
                    }
                    catch (IOException)
                    {
                        report.WriteErrors++;
                        sectorsWritten += sectorsThisBatch;
                        try { stream.Seek(sectorsWritten * bytesPerSector, SeekOrigin.Begin); }
                        catch { break; }
                    }

                    if (sectorsWritten % (SECTORS_PER_READ * 50) == 0)
                    {
                        var totalWork = totalSectors * passes;
                        var doneWork = sectorsWritten + (pass * totalSectors);
                        var pct = (double)doneWork / totalWork * 100;
                        var elapsed = sw.Elapsed;
                        var speed = elapsed.TotalSeconds > 0
                            ? (doneWork * bytesPerSector / 1_048_576.0) / elapsed.TotalSeconds
                            : 0;
                        var estRemaining = speed > 0
                            ? TimeSpan.FromSeconds((totalWork - doneWork) * bytesPerSector / 1_048_576.0 / speed)
                            : TimeSpan.Zero;

                        var passLabel = passes > 1 ? $" (Pass {pass + 1}/{passes})" : "";
                        progress?.Report(new OperationProgress
                        {
                            DriveNumber = drive.DeviceNumber,
                            Operation = OperationType.SecureWipe,
                            PercentComplete = pct,
                            SectorsProcessed = doneWork,
                            TotalSectorsToProcess = totalWork,
                            ErrorCount = report.WriteErrors,
                            Elapsed = elapsed,
                            EstimatedRemaining = estRemaining,
                            SpeedMBps = speed,
                            StatusMessage = $"Wiping{passLabel}... {pct:F1}%"
                        });
                    }
                }

                stream.Flush();
            }

            // Verification pass
            if (verifyAfterWipe && !cancellation.IsCancellationRequested)
            {
                RunVerification(stream, drive, report, bytesPerSector,
                    bufferSize, totalSectors, sw, progress, cancellation);
            }

            report.Completed = !cancellation.IsCancellationRequested;
        }, cancellation);

        report.EndTime = DateTime.Now;
        Logger.Info($"Full wipe {(report.Completed ? "complete" : "cancelled")}: Disk {drive.DeviceNumber} — {report.SectorsWritten:N0} sectors, {report.WriteErrors} errors");

        progress?.Report(new OperationProgress
        {
            DriveNumber = drive.DeviceNumber,
            Operation = OperationType.SecureWipe,
            PercentComplete = 100,
            IsComplete = true,
            WasCancelled = cancellation.IsCancellationRequested,
            Elapsed = sw.Elapsed,
            StatusMessage = cancellation.IsCancellationRequested ? "Cancelled" : "Wipe complete"
        });

        return report;
    }

    private void WriteSequentialRegion(
        FileStream stream,
        long startSector,
        long sectorCount,
        byte[] buffer,
        int bufferSize,
        int bytesPerSector,
        bool useRandom,
        WipeReport report,
        int driveNumber,
        long progressBase,
        long totalWork,
        Stopwatch sw,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellation,
        string phaseLabel)
    {
        stream.Seek(startSector * bytesPerSector, SeekOrigin.Begin);
        long sectorsWritten = 0;

        while (sectorsWritten < sectorCount && !cancellation.IsCancellationRequested)
        {
            var remaining = sectorCount - sectorsWritten;
            var batch = (int)Math.Min(SECTORS_PER_READ, remaining);
            var bytesToWrite = batch * bytesPerSector;

            try
            {
                if (useRandom && sectorsWritten % (SECTORS_PER_READ * 100) == 0)
                    RandomNumberGenerator.Fill(buffer);

                stream.Write(buffer, 0, bytesToWrite);
                sectorsWritten += batch;
                report.SectorsWritten += batch;
            }
            catch (IOException)
            {
                report.WriteErrors++;
                sectorsWritten += batch;
                try { stream.Seek((startSector + sectorsWritten) * bytesPerSector, SeekOrigin.Begin); }
                catch { break; }
            }

            if (sectorsWritten % (SECTORS_PER_READ * 50) == 0)
            {
                var doneWork = progressBase + sectorsWritten;
                var pct = (double)doneWork / totalWork * 100;
                var speed = sw.Elapsed.TotalSeconds > 0
                    ? (report.SectorsWritten * bytesPerSector / 1_048_576.0) / sw.Elapsed.TotalSeconds
                    : 0;

                progress?.Report(new OperationProgress
                {
                    DriveNumber = driveNumber,
                    Operation = OperationType.SecureWipe,
                    PercentComplete = pct,
                    SectorsProcessed = report.SectorsWritten,
                    TotalSectorsToProcess = totalWork,
                    ErrorCount = report.WriteErrors,
                    Elapsed = sw.Elapsed,
                    SpeedMBps = speed,
                    StatusMessage = $"{phaseLabel}... {pct:F1}%"
                });
            }
        }
    }

    private void RunVerification(
        FileStream stream,
        PhysicalDrive drive,
        WipeReport report,
        int bytesPerSector,
        int bufferSize,
        long totalSectors,
        Stopwatch sw,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellation)
    {
        Logger.Info($"Verification starting: Disk {drive.DeviceNumber}");
        var verifyBuffer = new byte[bufferSize];
        var emptyBuffer = new byte[bufferSize];
        Array.Clear(emptyBuffer);

        stream.Seek(0, SeekOrigin.Begin);
        long sectorsVerified = 0;

        while (sectorsVerified < totalSectors && !cancellation.IsCancellationRequested)
        {
            var sectorsRemaining = totalSectors - sectorsVerified;
            var sectorsThisBatch = (int)Math.Min(SECTORS_PER_READ, sectorsRemaining);
            var bytesToRead = sectorsThisBatch * bytesPerSector;

            try
            {
                int bytesRead = stream.Read(verifyBuffer, 0, bytesToRead);
                if (bytesRead > 0)
                {
                    bool isClean = verifyBuffer.AsSpan(0, bytesRead)
                        .SequenceEqual(emptyBuffer.AsSpan(0, bytesRead));
                    if (!isClean)
                        report.VerificationErrors++;
                }
                sectorsVerified += sectorsThisBatch;
            }
            catch (IOException)
            {
                report.VerificationErrors++;
                sectorsVerified += sectorsThisBatch;
                try { stream.Seek(sectorsVerified * bytesPerSector, SeekOrigin.Begin); }
                catch { break; }
            }

            if (sectorsVerified % (SECTORS_PER_READ * 50) == 0)
            {
                var pct = (double)sectorsVerified / totalSectors * 100;
                progress?.Report(new OperationProgress
                {
                    DriveNumber = drive.DeviceNumber,
                    Operation = OperationType.VerifyWipe,
                    PercentComplete = pct,
                    SectorsProcessed = sectorsVerified,
                    TotalSectorsToProcess = totalSectors,
                    ErrorCount = report.VerificationErrors,
                    Elapsed = sw.Elapsed,
                    StatusMessage = $"Verifying... {pct:F1}%"
                });
            }
        }

        report.VerificationPassed = report.VerificationErrors == 0;
        Logger.Info($"Verification complete: Disk {drive.DeviceNumber} — {(report.VerificationPassed ? "PASSED" : $"FAILED ({report.VerificationErrors} errors)")}");
    }

    private SafeFileHandle OpenDiskForRead(string path)
    {
        return CreateFile(
            path,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_NO_BUFFERING,
            IntPtr.Zero);
    }

    private SafeFileHandle OpenDiskForWrite(string path)
    {
        return CreateFile(
            path,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH,
            IntPtr.Zero);
    }

    private void LockAndDismountVolumes(PhysicalDrive drive)
    {
        foreach (var letter in drive.DriveLetters)
        {
            try
            {
                var volumePath = $"\\\\.\\{letter}";
                using var volHandle = CreateFile(
                    volumePath,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (!volHandle.IsInvalid)
                {
                    DeviceIoControl(volHandle, FSCTL_LOCK_VOLUME,
                        IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                    DeviceIoControl(volHandle, FSCTL_DISMOUNT_VOLUME,
                        IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                    Logger.Info($"Locked and dismounted volume {letter}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not lock volume {letter}: {ex.Message}");
            }
        }
    }

    private static long RandomLong(long min, long max)
    {
        if (max <= min) return min;
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        long raw = BitConverter.ToInt64(bytes) & long.MaxValue;
        return min + (raw % (max - min));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
