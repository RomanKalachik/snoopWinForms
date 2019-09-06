namespace Snoop
{
    using Snoop.Data;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.RegularExpressions;
    using System.Windows.Input;

    public class WindowInfo
    {
        private static readonly Dictionary<IntPtr, bool> windowHandleToValidityMap = new Dictionary<IntPtr, bool>();

        private static readonly Regex windowClassNameRegex = new Regex(@".*WindowsForms.*", RegexOptions.Compiled);

        private IList<NativeMethods.MODULEENTRY32> modules;
        private Process owningProcess;
        private bool? isOwningProcess64Bit;
        private bool? isOwningProcessElevated;
        private static readonly int snoopProcessId = Process.GetCurrentProcess().Id;

        public WindowInfo(IntPtr hwnd)
        {
            this.HWnd = hwnd;
        }

        public event EventHandler<AttachFailedEventArgs> AttachFailed;

        public static void ClearCachedWindowHandleInfo()
        {
            windowHandleToValidityMap.Clear();
        }

        public IList<NativeMethods.MODULEENTRY32> Modules => this.modules ?? (this.modules = this.GetModules().ToList());

        /// <summary>
        /// Similar to System.Diagnostics.WinProcessManager.GetModuleInfos,
        /// except that we include 32 bit modules when Snoop runs in 64 bit mode.
        /// See http://blogs.msdn.com/b/jasonz/archive/2007/05/11/code-sample-is-your-process-using-the-silverlight-clr.aspx
        /// </summary>
        private IEnumerable<NativeMethods.MODULEENTRY32> GetModules()
        {
            NativeMethods.GetWindowThreadProcessId(this.HWnd, out var processId);

            var me32 = new NativeMethods.MODULEENTRY32();
            var hModuleSnap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.SnapshotFlags.Module | NativeMethods.SnapshotFlags.Module32, processId);

            if (hModuleSnap.IsInvalid)
            {
                yield break;
            }

            using (hModuleSnap)
            {
                me32.dwSize = (uint)Marshal.SizeOf(me32);

                if (NativeMethods.Module32First(hModuleSnap, ref me32))
                {
                    do
                    {
                        yield return me32;
                    } while (NativeMethods.Module32Next(hModuleSnap, ref me32));
                }
            }
        }

        public bool IsValidProcess {
            get {
                var isValid = false;
                try
                {
                    if (this.HWnd == IntPtr.Zero)
                    {
                        return false;
                    }

                    if (windowHandleToValidityMap.TryGetValue(this.HWnd, out isValid))
                    {
                        return isValid;
                    }

                    var process = this.OwningProcess;
                    if (process == null)
                    {
                        return false;
                    }

                    if (process.Id == snoopProcessId)
                    {
                        isValid = false;

                    }
                    else
                    {
                        if (windowClassNameRegex.IsMatch(this.ClassName))
                        {
                            isValid = true;
                        }

                        if (isValid == false)
                        {
                            foreach (var module in this.Modules)
                            {
                                if (module.szModule.StartsWith("PresentationFramework", StringComparison.OrdinalIgnoreCase)
                                    || module.szModule.StartsWith("PresentationCore", StringComparison.OrdinalIgnoreCase)
                                    || module.szModule.StartsWith("wpfgfx", StringComparison.OrdinalIgnoreCase))
                                {
                                    isValid = true;
                                    break;
                                }
                            }
                        }
                    }

                    windowHandleToValidityMap[this.HWnd] = isValid;
                }
                catch
                {
                }

                return isValid;
            }
        }

        public Process OwningProcess => this.owningProcess ?? (this.owningProcess = NativeMethods.GetWindowThreadProcess(this.HWnd));

        public bool IsOwningProcess64Bit => (this.isOwningProcess64Bit ?? (this.isOwningProcess64Bit = IsProcess64Bit(this.OwningProcess))) == true;

        public bool IsOwningProcessElevated => (this.isOwningProcessElevated ?? (this.isOwningProcessElevated = IsProcessElevated(this.OwningProcess))) == true;

        public IntPtr HWnd { get; }

        public string Description {
            get {
                var process = this.OwningProcess;
                var windowTitle = NativeMethods.GetText(this.HWnd);

                if (string.IsNullOrEmpty(windowTitle))
                {
                    try
                    {
                        windowTitle = process.MainWindowTitle;
                    }
                    catch (InvalidOperationException)
                    {
                        return string.Empty;
                    }
                }

                return $"{windowTitle} - {process.ProcessName} [{process.Id}]";
            }
        }

        public string ClassName => NativeMethods.GetClassName(this.HWnd);

        public string TraceInfo => $"{this.Description} [{this.HWnd.ToInt64():X8}] {this.ClassName}";

        public override string ToString()
        {
            return this.Description;
        }

        public void Snoop()
        {
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                Injector.Launch(this, typeof(SnoopUI).Assembly, typeof(SnoopUI).FullName, "GoBabyGo", new TransientSettingsData() { Handle = this.HWnd.ToString() }.WriteToFile());
            }
            catch (Exception e)
            {
                this.OnFailedToAttach(e);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void OnFailedToAttach(Exception e)
        {
            this.AttachFailed?.Invoke(this, new AttachFailedEventArgs(e, this.Description));
        }

        public static bool IsProcess64Bit(Process process)
        {
            if (Environment.Is64BitOperatingSystem == false)
            {
                return false;
            }

            using (var processHandle = NativeMethods.OpenProcess(process, NativeMethods.ProcessAccessFlags.QueryLimitedInformation))
            {
                if (processHandle.IsInvalid)
                {
                    throw new Exception("Could not query process information.");
                }

                if (NativeMethods.IsWow64Process(processHandle.DangerousGetHandle(), out var isWow64) == false)
                {
                    throw new Win32Exception();
                }

                return isWow64 == false;
            }
        }

        private static bool IsProcessElevated(Process process)
        {
            using (var processHandle = NativeMethods.OpenProcess(process, NativeMethods.ProcessAccessFlags.QueryInformation))
            {
                if (processHandle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();

                    return error == NativeMethods.ERROR_ACCESS_DENIED;
                }

                return false;
            }
        }
    }
}
