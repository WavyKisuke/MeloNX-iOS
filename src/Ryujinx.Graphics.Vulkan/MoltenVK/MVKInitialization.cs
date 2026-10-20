using Silk.NET.Core.Loader;
using Silk.NET.Vulkan;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ryujinx.Graphics.Vulkan.MoltenVK
{
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("ios")]
    public static partial class MVKInitialization
    {
        private const string VulkanLib = "libvulkan.dylib";
        private const uint MaxActiveMetalCommandBuffersPerQueue = 32;

        [LibraryImport("libMoltenVK.dylib")]
        private static partial Result vkGetMoltenVKConfigurationMVK(nint unusedInstance, out MVKConfiguration config, in nint configSize);

        [LibraryImport("libMoltenVK.dylib")]
        private static partial Result vkSetMoltenVKConfigurationMVK(nint unusedInstance, in MVKConfiguration config, in nint configSize);

        public static void Initialize()
        {
            nint configSize = (nint)Marshal.SizeOf<MVKConfiguration>();

            vkGetMoltenVKConfigurationMVK(nint.Zero, out MVKConfiguration config, configSize);

            // Handled by Environment Variables.
            // config.UseMetalArgumentBuffers = true;
            // Fast math lets Metal assume there are no NaNs or infinities. Some post-processing (bloom in
            // Super Mario Odyssey, for one) produces them and then spreads them across the frame, so the
            // frontend can turn it off.
            config.FastMathEnabled = Environment.GetEnvironmentVariable("MELONX_METAL_FAST_MATH") != "0";

            config.SemaphoreSupportStyle = MVKVkSemaphoreSupportStyle.MVK_CONFIG_VK_SEMAPHORE_SUPPORT_STYLE_SINGLE_QUEUE;
            config.SynchronousQueueSubmits = false;
            config.PresentWithCommandBuffer = true;
            config.MaxActiveMetalCommandBuffersPerQueue = MaxActiveMetalCommandBuffersPerQueue;

            config.ResumeLostDevice = true;

            vkSetMoltenVKConfigurationMVK(nint.Zero, config, configSize);
        }

        private static string[] Resolver(string path)
        {
            if (path.EndsWith(VulkanLib))
            {
                path = path[..^VulkanLib.Length] + "libMoltenVK.dylib";
                return [path];
            }

            return [];
        }

        public static void InitializeResolver()
        {
            ((DefaultPathResolver)PathResolver.Default).Resolvers.Insert(0, Resolver);
        }
    }
}
