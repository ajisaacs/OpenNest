using ILGPU;
using ILGPU.Runtime;
using OpenNest.Engine.BestFit;
using System;
using System.Diagnostics;

namespace OpenNest.Gpu
{
    public static class GpuEvaluatorFactory
    {
        private static bool _probed;
        private static bool _gpuAvailable;
        private static string _deviceName;
        private static GpuSlideComputer _slideComputer;
        private static readonly object _slideLock = new object();

        public static bool GpuAvailable
        {
            get
            {
                if (!_probed) Probe();
                return _gpuAvailable;
            }
        }

        public static string DeviceName
        {
            get
            {
                if (!_probed) Probe();
                return _deviceName ?? "None";
            }
        }

        public static IPairEvaluator Create(Drawing drawing, double spacing)
        {
            if (!GpuAvailable)
                return null;

            try
            {
                return new GpuPairEvaluator(drawing, spacing);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GpuEvaluatorFactory] GPU evaluator failed: {ex.Message}");
                return null;
            }
        }

        public static ISlideComputer CreateSlideComputer()
        {
            if (!GpuAvailable)
                return null;

            lock (_slideLock)
            {
                if (_slideComputer != null)
                    return _slideComputer;

                try
                {
                    _slideComputer = new GpuSlideComputer();
                    return _slideComputer;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GpuEvaluatorFactory] GPU slide computer failed: {ex.Message}");
                    return null;
                }
            }
        }

        private static void Probe()
        {
            _probed = true;

            try
            {
                using var context = Context.CreateDefault();
                foreach (var device in context.Devices)
                {
                    if (device.AcceleratorType == AcceleratorType.Cuda ||
                        device.AcceleratorType == AcceleratorType.OpenCL)
                    {
                        _gpuAvailable = true;
                        _deviceName = device.Name;
                        Debug.WriteLine($"[GpuEvaluatorFactory] GPU found: {device.Name} ({device.AcceleratorType})");
                        return;
                    }
                }

                Debug.WriteLine("[GpuEvaluatorFactory] No GPU device found");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GpuEvaluatorFactory] GPU probe failed: {ex.Message}");
            }
        }
    }
}
