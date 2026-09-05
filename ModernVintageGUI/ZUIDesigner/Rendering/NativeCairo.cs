using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ModernVintageGUI.Designer.Rendering
{
    /// <summary>
    /// cairo-sharp P/Invokes into "libcairo-2", which ships inside the Vintage Story Lib folder
    /// and is therefore not on the default search path of a standalone executable.
    /// </summary>
    public static class NativeCairo
    {
        private static readonly object _gate = new object();
        private static bool _registered;

        /// <summary>
        /// Idempotent, unlike the harness version: SetDllImportResolver throws when it is
        /// called a second time for the same assembly, and a web host runs its startup path
        /// more than once per process during a hot reload.
        /// </summary>
        public static void Register()
        {
            lock (_gate)
            {
                if (_registered)
                    return;

                _registered = true;
            }

            string? gameDir = Environment.GetEnvironmentVariable("VINTAGE_STORY");
            if (string.IsNullOrWhiteSpace(gameDir))
            {
                throw new InvalidOperationException(
                    "VINTAGE_STORY is not set. It has to point at the Vintage Story installation " +
                    "directory so that Lib/libcairo-2.dll can be found.");
            }

            string libDir = Path.Combine(gameDir, "Lib");

            Assembly cairoSharp = typeof(Cairo.Context).Assembly;

            NativeLibrary.SetDllImportResolver(cairoSharp, (name, assembly, searchPath) =>
            {
                foreach (string candidate in CandidateFileNames(name))
                {
                    string full = Path.Combine(libDir, candidate);
                    if (File.Exists(full) && NativeLibrary.TryLoad(full, out IntPtr handle))
                    {
                        return handle;
                    }
                }

                return IntPtr.Zero; // fall back to the default resolver
            });
        }

        private static string[] CandidateFileNames(string name)
        {
            if (OperatingSystem.IsWindows())
            {
                return new[] { name + ".dll", name };
            }

            return new[] { "lib" + name + ".so", name + ".so", name };
        }
    }
}
