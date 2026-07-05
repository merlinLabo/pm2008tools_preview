using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PM2008Tuner
{
    internal static class RawInputPatcher
    {
        private const uint CreateSuspended = 0x00000004;
        private const uint CreateUnicodeEnvironment = 0x00000400;
        private const uint PageExecuteReadWrite = 0x40;
        private const uint PageReadWrite = 0x04;
        private const uint MapVkToVsc = 0;

        internal static bool NeedsPatch(AppConfig config)
        {
            if (!config.EnableKeyboardRemap) return false;
            HashSet<int> sources = new HashSet<int>();
            foreach (KeyBindingConfig binding in config.Bindings)
            {
                if (binding.SourceKey != 0) sources.Add(binding.SourceKey);
                if (binding.SourceKey2 != 0) sources.Add(binding.SourceKey2);
            }
            foreach (KeyBindingConfig binding in config.Bindings)
            {
                int delay = Math.Max(0, config.GlobalInputDelayMs + binding.DelayMs);
                if (!sources.Contains(binding.TargetKey)) return true;
                if (binding.SourceKey != 0 && (binding.SourceKey != binding.TargetKey || delay > 0)) return true;
                if (binding.SourceKey2 != 0 && (binding.SourceKey2 != binding.TargetKey || delay > 0)) return true;
            }
            return false;
        }

        internal static string ValidateBindings(AppConfig config)
        {
            HashSet<int> sources = new HashSet<int>();
            foreach (KeyBindingConfig binding in config.Bindings)
            {
                if (binding.TargetKey == 0) return "按键映射中存在无效的默认键位。";
                int[] mappedKeys = { binding.SourceKey, binding.SourceKey2 };
                foreach (int source in mappedKeys)
                {
                    if (source == 0) continue;
                    if (!sources.Add(source))
                        return "映射键重复：" + KeyNames.Get(source) + "。一个按键只能对应一个 Control。";
                }
            }
            return null;
        }

        internal static Process Start(LaunchSpec spec, AppConfig config, out string status)
        {
            string bindingError = ValidateBindings(config);
            if (bindingError != null) throw new InvalidOperationException(bindingError);

            StartupInfo startup = new StartupInfo();
            startup.cb = Marshal.SizeOf(typeof(StartupInfo));
            ProcessInformation pi;
            IntPtr environment = BuildEnvironment(spec.Environment);
            StringBuilder command = new StringBuilder("\"" + spec.LauncherPath + "\" " + spec.Arguments);
            try
            {
                bool created = CreateProcessW(spec.LauncherPath, command, IntPtr.Zero, IntPtr.Zero, false,
                    CreateSuspended | CreateUnicodeEnvironment, environment, spec.WorkingDirectory,
                    ref startup, out pi);
                if (!created) throw new InvalidOperationException("CreateProcess 失败，Windows 错误：" + Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.FreeHGlobal(environment);
            }

            try
            {
                bool patched = NeedsPatch(config);
                if (patched)
                {
                    InstallAfterLoader(pi.hProcess, pi.hThread, pi.dwProcessId, spec.LauncherPath, config);
                }
                else
                {
                    uint resume = ResumeThread(pi.hThread);
                    if (resume == 0xFFFFFFFFU)
                        throw new InvalidOperationException("ResumeThread 失败，Windows 错误：" + Marshal.GetLastWin32Error());
                }

                Process process = Process.GetProcessById((int)pi.dwProcessId);
                status = patched
                    ? "游戏运行中；Raw Input 原地替换已启用，可交换原键。"
                    : "游戏运行中；当前键位无需替换。";
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
                return process;
            }
            catch
            {
                TerminateProcess(pi.hProcess, 1);
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
                throw;
            }
        }

        internal static int FindImportIatRva(string path, string importName)
        {
            return PeImports.FindIatRva(File.ReadAllBytes(path), importName);
        }

        private static void InstallAfterLoader(IntPtr process, IntPtr thread, uint processId,
            string launcherPath, AppConfig config)
        {
            IntPtr moduleBase = FindMainModuleBase(process, processId, launcherPath);
            if (moduleBase == IntPtr.Zero)
                throw new InvalidOperationException("无法取得 launcher 模块基址。Windows 错误：" + Marshal.GetLastWin32Error());

            int entryRva = PeImports.FindEntryPointRva(File.ReadAllBytes(launcherPath));
            if (entryRva <= 0) throw new InvalidOperationException("无法解析 launcher 入口点。版本可能不兼容。");
            IntPtr entryPoint = Add(moduleBase, entryRva);
            byte[] originalEntry = Read(process, entryPoint, 8);

            IntPtr gate = AllocateNear(process, moduleBase, 0x1000);
            if (gate == IntPtr.Zero)
                throw new InvalidOperationException("无法在 launcher 附近分配入口门控内存。Windows 错误：" + Marshal.GetLastWin32Error());
            IntPtr flags = new IntPtr(gate.ToInt64() + 0x100);
            byte[] gateCode = BuildEntryGate(flags.ToInt64(), entryPoint.ToInt64() + 8, originalEntry);
            Write(process, gate, gateCode);
            Write(process, flags, new byte[] { 0, 0 });

            long displacement = gate.ToInt64() - (entryPoint.ToInt64() + 5);
            if (displacement < Int32.MinValue || displacement > Int32.MaxValue)
                throw new InvalidOperationException("入口门控超出 x64 rel32 范围。");
            byte[] entryJump = new byte[8];
            entryJump[0] = 0xE9;
            Array.Copy(BitConverter.GetBytes((int)displacement), 0, entryJump, 1, 4);
            entryJump[5] = entryJump[6] = entryJump[7] = 0x90;

            uint entryProtect;
            if (!VirtualProtectEx(process, entryPoint, new UIntPtr(8), PageExecuteReadWrite, out entryProtect))
                throw new InvalidOperationException("修改入口保护失败，Windows 错误：" + Marshal.GetLastWin32Error());
            Write(process, entryPoint, entryJump);
            FlushInstructionCache(process, entryPoint, new UIntPtr(8));
            uint restoredProtect;
            VirtualProtectEx(process, entryPoint, new UIntPtr(8), entryProtect, out restoredProtect);

            uint resume = ResumeThread(thread);
            if (resume == 0xFFFFFFFFU)
                throw new InvalidOperationException("启动 loader 失败，Windows 错误：" + Marshal.GetLastWin32Error());

            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (Read(process, flags, 1)[0] == 0 && DateTime.UtcNow < deadline)
                System.Threading.Thread.Sleep(5);
            if (Read(process, flags, 1)[0] == 0)
                throw new InvalidOperationException("等待 launcher 完成 Windows loader 初始化超时。");

            if (!VirtualProtectEx(process, entryPoint, new UIntPtr(8), PageExecuteReadWrite, out entryProtect))
                throw new InvalidOperationException("恢复入口保护失败，Windows 错误：" + Marshal.GetLastWin32Error());
            Write(process, entryPoint, originalEntry);
            FlushInstructionCache(process, entryPoint, new UIntPtr(8));
            VirtualProtectEx(process, entryPoint, new UIntPtr(8), entryProtect, out restoredProtect);

            InstallResolvedIat(process, moduleBase, launcherPath, config);
            Write(process, new IntPtr(flags.ToInt64() + 1), new byte[] { 1 });
        }

        private static void InstallResolvedIat(IntPtr process, IntPtr moduleBase, string launcherPath, AppConfig config)
        {
            int rawInputIatRva = FindImportIatRva(launcherPath, "GetRawInputData");
            if (rawInputIatRva <= 0)
                throw new InvalidOperationException("launcher 没有导入 GetRawInputData，无法安装 Raw Input 替换器。版本可能不兼容。");

            IntPtr rawInputIat = Add(moduleBase, rawInputIatRva);
            long originalRawInput = ReadInt64(process, rawInputIat);
            if (originalRawInput == 0)
                throw new InvalidOperationException("GetRawInputData IAT 尚未解析。launcher 版本可能不兼容。");

            long sleepAddress = 0;
            int sleepIatRva = FindImportIatRva(launcherPath, "Sleep");
            if (sleepIatRva > 0) sleepAddress = ReadInt64(process, Add(moduleBase, sleepIatRva));

            byte[] hook = HookBuilder.Build(originalRawInput, sleepAddress, config);
            IntPtr remoteHook = VirtualAllocEx(process, IntPtr.Zero, new UIntPtr((uint)hook.Length),
                0x1000 | 0x2000, PageExecuteReadWrite);
            if (remoteHook == IntPtr.Zero)
                throw new InvalidOperationException("VirtualAllocEx 失败，Windows 错误：" + Marshal.GetLastWin32Error());

            Write(process, remoteHook, hook);
            uint oldProtect;
            if (!VirtualProtectEx(process, rawInputIat, new UIntPtr(8), PageReadWrite, out oldProtect))
                throw new InvalidOperationException("修改 IAT 保护失败，Windows 错误：" + Marshal.GetLastWin32Error());
            try
            {
                Write(process, rawInputIat, BitConverter.GetBytes(remoteHook.ToInt64()));
            }
            finally
            {
                uint ignored;
                VirtualProtectEx(process, rawInputIat, new UIntPtr(8), oldProtect, out ignored);
            }
            FlushInstructionCache(process, remoteHook, new UIntPtr((uint)hook.Length));
        }

        private static byte[] BuildEntryGate(long flags, long returnAddress, byte[] originalEntry)
        {
            List<byte> code = new List<byte>();
            code.AddRange(new byte[] { 0x48, 0xB8 });                         // mov rax,flags
            code.AddRange(BitConverter.GetBytes(flags));
            code.AddRange(new byte[] { 0xC6, 0x00, 0x01 });                   // mov byte [rax],1
            code.AddRange(new byte[] { 0x80, 0x78, 0x01, 0x01 });             // cmp byte [rax+1],1
            code.AddRange(new byte[] { 0x75, 0xFA });                         // jne loop
            code.AddRange(originalEntry);                                    // original first 8 bytes
            code.AddRange(new byte[] { 0x48, 0xB8 });                         // mov rax,entry+8
            code.AddRange(BitConverter.GetBytes(returnAddress));
            code.AddRange(new byte[] { 0xFF, 0xE0 });                         // jmp rax
            return code.ToArray();
        }

        private static IntPtr AllocateNear(IntPtr process, IntPtr moduleBase, int size)
        {
            long origin = moduleBase.ToInt64();
            for (long distance = 0x01000000; distance < 0x70000000; distance += 0x01000000)
            {
                long high = (origin + distance) & ~0xFFFFL;
                IntPtr allocated = VirtualAllocEx(process, new IntPtr(high), new UIntPtr((uint)size),
                    0x1000 | 0x2000, PageExecuteReadWrite);
                if (allocated != IntPtr.Zero) return allocated;
                if (origin > distance)
                {
                    long low = (origin - distance) & ~0xFFFFL;
                    allocated = VirtualAllocEx(process, new IntPtr(low), new UIntPtr((uint)size),
                        0x1000 | 0x2000, PageExecuteReadWrite);
                    if (allocated != IntPtr.Zero) return allocated;
                }
            }
            return IntPtr.Zero;
        }

        private static IntPtr BuildEnvironment(Dictionary<string, string> overrides)
        {
            SortedDictionary<string, string> values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry item in Environment.GetEnvironmentVariables())
                values[Convert.ToString(item.Key)] = Convert.ToString(item.Value);
            foreach (KeyValuePair<string, string> item in overrides) values[item.Key] = item.Value;
            StringBuilder block = new StringBuilder();
            foreach (KeyValuePair<string, string> item in values)
                block.Append(item.Key).Append('=').Append(item.Value).Append('\0');
            block.Append('\0');
            return Marshal.StringToHGlobalUni(block.ToString());
        }

        private static IntPtr FindMainModuleBase(IntPtr process, uint processId, string launcherPath)
        {
            IntPtr pebBase = FindImageBaseFromPeb(process);
            if (pebBase != IntPtr.Zero) return pebBase;

            string expected = Path.GetFileName(launcherPath);
            for (int attempt = 0; attempt < 50; attempt++)
            {
                IntPtr snapshot = CreateToolhelp32Snapshot(0x00000008 | 0x00000010, processId);
                if (snapshot != new IntPtr(-1))
                {
                    try
                    {
                        ModuleEntry32 entry = new ModuleEntry32();
                        entry.dwSize = (uint)Marshal.SizeOf(typeof(ModuleEntry32));
                        if (Module32FirstW(snapshot, ref entry))
                        {
                            do
                            {
                                if (String.Equals(entry.szModule, expected, StringComparison.OrdinalIgnoreCase))
                                    return entry.modBaseAddr;
                            } while (Module32NextW(snapshot, ref entry));
                        }
                    }
                    finally { CloseHandle(snapshot); }
                }
                System.Threading.Thread.Sleep(10);
            }
            return IntPtr.Zero;
        }

        private static IntPtr FindImageBaseFromPeb(IntPtr process)
        {
            ProcessBasicInformation info = new ProcessBasicInformation();
            int returned;
            int status = NtQueryInformationProcess(process, 0, ref info,
                Marshal.SizeOf(typeof(ProcessBasicInformation)), out returned);
            if (status != 0 || info.PebBaseAddress == IntPtr.Zero) return IntPtr.Zero;
            byte[] pointer = new byte[8];
            IntPtr read;
            IntPtr imageBaseAddressField = new IntPtr(info.PebBaseAddress.ToInt64() + 0x10);
            if (!ReadProcessMemory(process, imageBaseAddressField, pointer, pointer.Length, out read) || read.ToInt64() != 8)
                return IntPtr.Zero;
            return new IntPtr(BitConverter.ToInt64(pointer, 0));
        }

        private static long ReadInt64(IntPtr process, IntPtr address)
        {
            byte[] data = new byte[8];
            IntPtr read;
            if (!ReadProcessMemory(process, address, data, data.Length, out read) || read.ToInt64() != 8)
                throw new InvalidOperationException("ReadProcessMemory 失败，Windows 错误：" + Marshal.GetLastWin32Error());
            return BitConverter.ToInt64(data, 0);
        }

        private static byte[] Read(IntPtr process, IntPtr address, int size)
        {
            byte[] data = new byte[size];
            IntPtr read;
            if (!ReadProcessMemory(process, address, data, size, out read) || read.ToInt64() != size)
                throw new InvalidOperationException("ReadProcessMemory 失败，Windows 错误：" + Marshal.GetLastWin32Error());
            return data;
        }

        private static void Write(IntPtr process, IntPtr address, byte[] data)
        {
            IntPtr written;
            if (!WriteProcessMemory(process, address, data, data.Length, out written) || written.ToInt64() != data.Length)
                throw new InvalidOperationException("WriteProcessMemory 失败，Windows 错误：" + Marshal.GetLastWin32Error());
        }

        private static IntPtr Add(IntPtr pointer, int offset)
        {
            return new IntPtr(pointer.ToInt64() + offset);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessBasicInformation
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2_0;
            public IntPtr Reserved2_1;
            public IntPtr UniqueProcessId;
            public IntPtr Reserved3;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ModuleEntry32
        {
            public uint dwSize;
            public uint th32ModuleID;
            public uint th32ProcessID;
            public uint GlblcntUsage;
            public uint ProccntUsage;
            public IntPtr modBaseAddr;
            public uint modBaseSize;
            public IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine,
            IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
            IntPtr environment, string currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Module32FirstW(IntPtr snapshot, ref ModuleEntry32 entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Module32NextW(IntPtr snapshot, ref ModuleEntry32 entry);
        [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int informationClass,
            ref ProcessBasicInformation information, int informationLength, out int returnLength);

        private static class HookBuilder
        {
            private sealed class JumpFixup
            {
                internal int Displacement;
                internal JumpFixup(int displacement) { Displacement = displacement; }
            }

            internal static byte[] Build(long originalRawInput, long sleepAddress, AppConfig config)
            {
                List<byte> code = new List<byte>();
                List<JumpFixup> doneJumps = new List<JumpFixup>();
                Emit(code, 0x48, 0x83, 0xEC, 0x58);                         // sub rsp,58h
                Emit(code, 0x4C, 0x89, 0x44, 0x24, 0x30);                   // mov [rsp+30],r8
                Emit(code, 0x89, 0x54, 0x24, 0x38);                         // mov [rsp+38],edx
                Emit(code, 0x8B, 0x84, 0x24, 0x80, 0x00, 0x00, 0x00);       // mov eax,[rsp+80] original 5th arg
                Emit(code, 0x89, 0x44, 0x24, 0x20);                         // mov [rsp+20],eax cbSizeHeader
                Emit(code, 0x48, 0xB8); EmitInt64(code, originalRawInput);  // mov rax,original
                Emit(code, 0xFF, 0xD0);                                     // call rax
                Emit(code, 0x89, 0x44, 0x24, 0x40);                         // mov [rsp+40],eax
                Emit(code, 0x81, 0x7C, 0x24, 0x38); EmitInt32(code, 0x10000003); // cmp uiCommand,RID_INPUT
                doneJumps.Add(EmitNearJump(code, 0x85));                    // jne done
                Emit(code, 0x4C, 0x8B, 0x54, 0x24, 0x30);                   // mov r10,[rsp+30]
                Emit(code, 0x4D, 0x85, 0xD2);                               // test r10,r10
                doneJumps.Add(EmitNearJump(code, 0x84));                    // je done
                Emit(code, 0x41, 0x83, 0x3A, 0x01);                         // cmp dword [r10],1
                doneJumps.Add(EmitNearJump(code, 0x85));                    // jne done
                Emit(code, 0x41, 0x0F, 0xB7, 0x4A, 0x1E);                   // movzx ecx,word [r10+1e]

                HashSet<int> sources = new HashSet<int>();
                foreach (KeyBindingConfig binding in config.Bindings)
                {
                    int delay = Math.Max(0, Math.Min(1000, config.GlobalInputDelayMs + binding.DelayMs));
                    int[] mappedKeys = { binding.SourceKey, binding.SourceKey2 };
                    foreach (int source in mappedKeys)
                    {
                        if (source == 0 || !sources.Add(source)) continue;
                        if (source == binding.TargetKey && delay == 0) continue;

                        Emit(code, 0x66, 0x81, 0xF9); EmitUInt16(code, (ushort)source); // cmp cx,source
                        JumpFixup notThis = EmitNearJump(code, 0x85);          // jne next
                        Emit(code, 0x66, 0x41, 0xC7, 0x42, 0x1E); EmitUInt16(code, (ushort)binding.TargetKey);
                        ushort scan = (ushort)(MapVirtualKey((uint)binding.TargetKey, MapVkToVsc) & 0xFFFF);
                        Emit(code, 0x66, 0x41, 0xC7, 0x42, 0x18); EmitUInt16(code, scan);
                        Emit(code, 0x66, 0x41, 0x83, 0x62, 0x1A, 0x01);       // keep BREAK, clear E0/E1
                        if (delay > 0 && sleepAddress != 0)
                        {
                            Emit(code, 0xB9); EmitInt32(code, delay);          // mov ecx,delay
                            Emit(code, 0x48, 0xB8); EmitInt64(code, sleepAddress);
                            Emit(code, 0xFF, 0xD0);                            // call Sleep
                        }
                        doneJumps.Add(EmitShortOrNearDone(code));
                        Patch(code, notThis, code.Count);
                    }
                }

                // 独占替换：原 Launcher 键如果不再被任何功能选作“来源键”，就必须失效。
                // 例如 Blue 的来源由 S 改成 Q 后，Q→S，而物理 S→VK 0xFF（未映射）。
                HashSet<int> blockedOriginalKeys = new HashSet<int>();
                foreach (KeyBindingConfig binding in config.Bindings)
                {
                    int originalKey = binding.TargetKey;
                    if (sources.Contains(originalKey) || !blockedOriginalKeys.Add(originalKey)) continue;
                    Emit(code, 0x66, 0x81, 0xF9); EmitUInt16(code, (ushort)originalKey); // cmp cx,original
                    JumpFixup notBlocked = EmitNearJump(code, 0x85);                    // jne next
                    Emit(code, 0x66, 0x41, 0xC7, 0x42, 0x1E); EmitUInt16(code, 0x00FF); // VKey=reserved
                    Emit(code, 0x66, 0x41, 0xC7, 0x42, 0x18); EmitUInt16(code, 0);      // MakeCode=0
                    Emit(code, 0x66, 0x41, 0x83, 0x62, 0x1A, 0x01);                    // retain BREAK only
                    doneJumps.Add(EmitShortOrNearDone(code));
                    Patch(code, notBlocked, code.Count);
                }

                int done = code.Count;
                foreach (JumpFixup jump in doneJumps) Patch(code, jump, done);
                Emit(code, 0x8B, 0x44, 0x24, 0x40);                         // mov eax,[rsp+40]
                Emit(code, 0x48, 0x83, 0xC4, 0x58);                         // add rsp,58h
                Emit(code, 0xC3);                                           // ret
                return code.ToArray();
            }

            private static JumpFixup EmitNearJump(List<byte> code, byte condition)
            {
                Emit(code, 0x0F, condition);
                int displacement = code.Count;
                EmitInt32(code, 0);
                return new JumpFixup(displacement);
            }

            private static JumpFixup EmitShortOrNearDone(List<byte> code)
            {
                Emit(code, 0xE9);
                int displacement = code.Count;
                EmitInt32(code, 0);
                return new JumpFixup(displacement);
            }

            private static void Patch(List<byte> code, JumpFixup jump, int target)
            {
                byte[] value = BitConverter.GetBytes(target - (jump.Displacement + 4));
                for (int i = 0; i < 4; i++) code[jump.Displacement + i] = value[i];
            }

            private static void Emit(List<byte> code, params byte[] bytes) { code.AddRange(bytes); }
            private static void EmitUInt16(List<byte> code, ushort value) { code.AddRange(BitConverter.GetBytes(value)); }
            private static void EmitInt32(List<byte> code, int value) { code.AddRange(BitConverter.GetBytes(value)); }
            private static void EmitInt64(List<byte> code, long value) { code.AddRange(BitConverter.GetBytes(value)); }
        }

        private static class PeImports
        {
            internal static int FindEntryPointRva(byte[] image)
            {
                if (image.Length < 0x100 || image[0] != 'M' || image[1] != 'Z') return 0;
                int pe = ReadI32(image, 0x3C);
                if (pe < 0 || pe + 0x40 > image.Length || ReadU32(image, pe) != 0x00004550) return 0;
                return (int)ReadU32(image, pe + 24 + 16);
            }

            internal static int FindIatRva(byte[] image, string wanted)
            {
                if (image.Length < 0x100 || image[0] != 'M' || image[1] != 'Z') return 0;
                int pe = ReadI32(image, 0x3C);
                if (pe < 0 || pe + 0x108 > image.Length || ReadU32(image, pe) != 0x00004550) return 0;
                int sectionCount = ReadU16(image, pe + 6);
                int optionalSize = ReadU16(image, pe + 20);
                int optional = pe + 24;
                if (ReadU16(image, optional) != 0x20B) return 0;
                int importRva = (int)ReadU32(image, optional + 120);
                int sections = optional + optionalSize;
                int importOffset = RvaToOffset(image, sections, sectionCount, importRva);
                if (importOffset <= 0) return 0;

                for (int descriptor = importOffset; descriptor + 20 <= image.Length; descriptor += 20)
                {
                    int originalThunk = (int)ReadU32(image, descriptor);
                    int nameRva = (int)ReadU32(image, descriptor + 12);
                    int firstThunk = (int)ReadU32(image, descriptor + 16);
                    if (originalThunk == 0 && nameRva == 0 && firstThunk == 0) break;
                    int thunkRva = originalThunk != 0 ? originalThunk : firstThunk;
                    int thunkOffset = RvaToOffset(image, sections, sectionCount, thunkRva);
                    if (thunkOffset <= 0) continue;
                    for (int index = 0; thunkOffset + index * 8 + 8 <= image.Length; index++)
                    {
                        ulong entry = ReadU64(image, thunkOffset + index * 8);
                        if (entry == 0) break;
                        if ((entry & 0x8000000000000000UL) != 0) continue;
                        int nameOffset = RvaToOffset(image, sections, sectionCount, (int)entry);
                        if (nameOffset <= 0 || nameOffset + 2 >= image.Length) continue;
                        string name = ReadAscii(image, nameOffset + 2);
                        if (String.Equals(name, wanted, StringComparison.Ordinal)) return firstThunk + index * 8;
                    }
                }
                return 0;
            }

            private static int RvaToOffset(byte[] image, int sections, int count, int rva)
            {
                for (int i = 0; i < count; i++)
                {
                    int s = sections + i * 40;
                    if (s + 40 > image.Length) return 0;
                    int virtualSize = (int)ReadU32(image, s + 8);
                    int virtualAddress = (int)ReadU32(image, s + 12);
                    int rawSize = (int)ReadU32(image, s + 16);
                    int rawPointer = (int)ReadU32(image, s + 20);
                    int size = Math.Max(virtualSize, rawSize);
                    if (rva >= virtualAddress && rva < virtualAddress + size)
                        return rawPointer + rva - virtualAddress;
                }
                return 0;
            }

            private static string ReadAscii(byte[] data, int offset)
            {
                int end = offset;
                while (end < data.Length && data[end] != 0) end++;
                return Encoding.ASCII.GetString(data, offset, end - offset);
            }
            private static ushort ReadU16(byte[] b, int o) { return BitConverter.ToUInt16(b, o); }
            private static uint ReadU32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }
            private static int ReadI32(byte[] b, int o) { return BitConverter.ToInt32(b, o); }
            private static ulong ReadU64(byte[] b, int o) { return BitConverter.ToUInt64(b, o); }
        }
    }
}
