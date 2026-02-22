using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace PdfComposer.Api
{
    public class CustomAssemblyLoadContext : AssemblyLoadContext
    {
        public IntPtr LoadUnmanagedLibrary(string absolutePath)
        {
            return LoadUnmanagedDllFromPath(absolutePath);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            return IntPtr.Zero;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            return null;
        }
    }

    public class NativeLibraryLoader
    {
        public static void LoadWkhtmltox()
        {
            string archFolder = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : "x86";
            string dllName = "libwkhtmltox.dll";
            var dllPath = Path.Combine(Directory.GetCurrentDirectory(), "wkhtmltox", archFolder, dllName);

            if (!File.Exists(dllPath))
            {
                throw new FileNotFoundException($"Native library not found: {dllPath}");
            }

            var context = new CustomAssemblyLoadContext();
            var handle = context.LoadUnmanagedLibrary(dllPath);

            if (handle == IntPtr.Zero)
            {
                throw new DllNotFoundException($"Failed to load native library: {dllPath}");
            }
        }
    }
}