using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MediaToolkit.FFmpeg;

[SupportedOSPlatform(@"windows")]
internal static partial class WindowsJobObject
{
	private const string Kernel32 = @"kernel32.dll";
	private const int JobObjectExtendedLimitInformationClass = 9;
	private const uint JobObjectLimitKillOnJobClose = 0x2000;

	// 句柄在本进程存活期间一直不关闭，进程退出时由系统关闭，从而结束作业里的所有子进程
	private static readonly Lazy<SafeFileHandle> Job = new(Create);

	public static void Assign(Process process)
	{
		if (!AssignProcessToJobObject(Job.Value, process.SafeHandle))
		{
			throw new Win32Exception();
		}
	}

	public static bool Contains(Process process)
	{
		if (!IsProcessInJob(process.SafeHandle, Job.Value, out bool result))
		{
			throw new Win32Exception();
		}

		return result;
	}

	private static SafeFileHandle Create()
	{
		SafeFileHandle job = CreateJobObjectW(0, 0);

		if (job.IsInvalid)
		{
			throw new Win32Exception();
		}

		JobObjectExtendedLimitInformation information = new() { BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose } };

		if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, information, (uint)Unsafe.SizeOf<JobObjectExtendedLimitInformation>()))
		{
			Win32Exception exception = new();
			job.Dispose();
			throw exception;
		}

		return job;
	}

	[LibraryImport(Kernel32, SetLastError = true)]
	private static partial SafeFileHandle CreateJobObjectW(nint jobAttributes, nint name);

	[LibraryImport(Kernel32, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool SetInformationJobObject(SafeFileHandle job, int informationClass, in JobObjectExtendedLimitInformation information, uint length);

	[LibraryImport(Kernel32, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);

	[LibraryImport(Kernel32, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool IsProcessInJob(SafeProcessHandle process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool result);

	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectBasicLimitInformation
	{
		public long PerProcessUserTimeLimit;
		public long PerJobUserTimeLimit;
		public uint LimitFlags;
		public nuint MinimumWorkingSetSize;
		public nuint MaximumWorkingSetSize;
		public uint ActiveProcessLimit;
		public nuint Affinity;
		public uint PriorityClass;
		public uint SchedulingClass;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct IoCounters
	{
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectExtendedLimitInformation
	{
		public JobObjectBasicLimitInformation BasicLimitInformation;
		public IoCounters IoInfo;
		public nuint ProcessMemoryLimit;
		public nuint JobMemoryLimit;
		public nuint PeakProcessMemoryUsed;
		public nuint PeakJobMemoryUsed;
	}
}
