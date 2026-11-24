using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

sealed class ChildJob:IDisposable {
 IntPtr handle;
 [StructLayout(LayoutKind.Sequential)]struct Basic {public long PerProcessUserTimeLimit,PerJobUserTimeLimit;public uint LimitFlags;public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
 [StructLayout(LayoutKind.Sequential)]struct IO {public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}
 [StructLayout(LayoutKind.Sequential)]struct Limits {public Basic BasicLimitInformation;public IO IoInfo;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateJobObject(IntPtr security,string name);
 [DllImport("kernel32.dll")]static extern bool SetInformationJobObject(IntPtr job,int kind,ref Limits info,uint size);
 [DllImport("kernel32.dll")]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr value);
 public ChildJob(){handle=CreateJobObject(IntPtr.Zero,null);var info=new Limits();info.BasicLimitInformation.LimitFlags=0x2000;if(handle==IntPtr.Zero||!SetInformationJobObject(handle,9,ref info,(uint)Marshal.SizeOf(info)))throw new InvalidOperationException("无法建立后台任务管理");}
 public void Add(Process process){if(!AssignProcessToJobObject(handle,process.Handle)){process.Kill();throw new InvalidOperationException("无法管理后台任务");}}
 public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
}
