using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using IdleDash.Core;

namespace IdleDash.Services;

public record SystemSnapshot(double? Cpu, double RamUsedGb, double RamTotalGb, double? Gpu, double? GpuTemp);

/// <summary>
/// Leest CPU, RAM en GPU uit. Read() is traag-ish, dus altijd vanaf een achtergrondthread aanroepen.
/// </summary>
public sealed class SystemMonitor
{
    private PerformanceCounter? _cpuCounter;
    private bool _cpuUnavailable;
    private bool _nvidiaSmiMissing;

    private record GpuReading(double Utilization, double Temperature);

    public SystemSnapshot Read()
    {
        double? cpu = ReadCpu();
        var (usedGb, totalGb) = ReadRam();
        var gpu = ReadNvidiaGpu();
        return new SystemSnapshot(cpu, usedGb, totalGb, gpu?.Utilization, gpu?.Temperature);
    }

    private double? ReadCpu()
    {
        if (_cpuUnavailable) return null;
        try
        {
            if (_cpuCounter == null)
            {
                // "% Processor Utility" is wat Taakbeheer ook laat zien; oudere teller als reserve
                try
                {
                    _cpuCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
                    _cpuCounter.NextValue();
                }
                catch
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue();
                }
            }
            return Math.Clamp(_cpuCounter.NextValue(), 0f, 100f);
        }
        catch
        {
            _cpuUnavailable = true;
            return null;
        }
    }

    private static (double UsedGb, double TotalGb) ReadRam()
    {
        var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref status)) return (0, 0);

        const double bytesPerGb = 1024d * 1024 * 1024;
        return ((status.ullTotalPhys - status.ullAvailPhys) / bytesPerGb, status.ullTotalPhys / bytesPerGb);
    }

    /// <summary>nvidia-smi wordt met de NVIDIA-driver meegeïnstalleerd en geeft gebruik én temperatuur.</summary>
    private GpuReading? ReadNvidiaGpu()
    {
        if (_nvidiaSmiMissing) return null;
        try
        {
            var startInfo = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            var parts = output.Split('\n')[0].Split(',');
            if (parts.Length >= 2
                && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double utilization)
                && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double temperature))
            {
                return new GpuReading(utilization, temperature);
            }
            return null;
        }
        catch (Win32Exception)
        {
            _nvidiaSmiMissing = true;   // nvidia-smi niet gevonden: niet meer proberen
            return null;
        }
        catch
        {
            return null;
        }
    }
}
