using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CIPeekShield.Components;
using CIPeekShield.Services;
using CIPeekShield.Views;

namespace CIPeekShield;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {

        PrepareForCleanUninstall();

        RegisterUninstallCleanup();

        services.AddComponent<PeekShieldComponent>();

        services.AddSettingsPage<SettingsPage>();

        services.AddNotificationProvider<PeekShieldNotificationProvider, PeekShieldNotificationSettingsControl>();

        EnsureNativeResolution();

        Dispatcher.UIThread.Post(() => PeekShieldEngine.Instance.Initialize());
    }

    private static void PrepareForCleanUninstall()
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attrs = File.GetAttributes(f);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(f, attrs & ~FileAttributes.ReadOnly);
                }
                catch
                {

                }
            }
        }
        catch
        {

        }
    }

    private static void EnsureNativeResolution()
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            if (string.IsNullOrEmpty(dir)) return;
            var alc = AssemblyLoadContext.GetLoadContext(typeof(Plugin).Assembly);
            if (alc == null) return;
            alc.ResolvingUnmanagedDll += (_, name) =>
            {
                var file = name.ToLowerInvariant() switch
                {
                    "dlibdotnetnative" => "DlibDotNetNative.dll",
                    "dlibdotnetnativednn" => "DlibDotNetNativeDnn.dll",
                    "opencvsharpextern" => "OpenCvSharpExtern.dll",
                    _ => null
                };
                if (file == null) return IntPtr.Zero;
                var path = Path.Combine(dir, file);
                return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
            };
        }
        catch
        {
        }
    }

    private static void RegisterUninstallCleanup()
    {
        try
        {
            var asm = typeof(Plugin).Assembly;
            var alc = AssemblyLoadContext.GetLoadContext(asm);
            if (alc == null) return;
            var dir = Path.GetDirectoryName(asm.Location);
            if (string.IsNullOrEmpty(dir)) return;

            alc.Unloading += _ =>
            {
                try
                {
                    if (!Directory.Exists(dir)) return;

                    if (!File.Exists(Path.Combine(dir, ".uninstall"))) return;
                    try { Directory.Delete(dir, true); }
                    catch
                    {

                    }
                }
                catch
                {

                }
            };
        }
        catch
        {

        }
    }
}
