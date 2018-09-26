using CefSharp;
using CefSharp.OffScreen;
using SharpDX.Direct3D9;
using SharpDX.Mathematics.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GMWebExtension
{
    public static class GameMaker
    {
        private delegate void CreateAsyncEventWithDsMapDelegate(int ds_map, int event_type);
        private delegate int CreateDsMapDelegate(int num);
        private delegate void DsMapAddDoubleDelegate(int ds_map, string key, double value);
        private delegate void DsMapAddStringDelegate(int ds_map, string key, string value);

        private static CreateAsyncEventWithDsMapDelegate CreateAsyncEventWithDsMap;
        private static CreateDsMapDelegate CreateDsMap;
        private static DsMapAddDoubleDelegate DsMapAddDouble;
        private static DsMapAddStringDelegate DsMapAddString;

        public enum EventSubtypeAsync : int
        {
            Social = 70
        }

        [DllExport(CallingConvention.Cdecl)]
        public static void RegisterCallbacks(IntPtr CreateAsyncEventWithDsMapPtr, IntPtr CreateDsMapPtr, IntPtr DsMapAddDoublePtr, IntPtr DsMapAddStringPtr)
        {
            CreateAsyncEventWithDsMap = Marshal.GetDelegateForFunctionPointer<CreateAsyncEventWithDsMapDelegate>(CreateAsyncEventWithDsMapPtr);
            CreateDsMap = Marshal.GetDelegateForFunctionPointer<CreateDsMapDelegate>(CreateDsMapPtr);
            DsMapAddDouble = Marshal.GetDelegateForFunctionPointer<DsMapAddDoubleDelegate>(DsMapAddDoublePtr);
            DsMapAddString = Marshal.GetDelegateForFunctionPointer<DsMapAddStringDelegate>(DsMapAddStringPtr);
        }

        public class DsMap
        {
            public int ds_map_id { get; private set; }

            public DsMap(int num = 0)
            {
                if (CreateDsMap == null)
                    throw new InvalidOperationException("RegisterCallbacks was not called");
                ds_map_id = CreateDsMap(num);
            }

            public void Add(string key, double value)
            {
                if (DsMapAddDouble == null)
                    throw new InvalidOperationException("RegisterCallbacks was not called");
                DsMapAddDouble(ds_map_id, key, value);
            }

            public void Add(string key, string value)
            {
                if (DsMapAddString == null)
                    throw new InvalidOperationException("RegisterCallbacks was not called");
                DsMapAddString(ds_map_id, key, value);
            }
        }

        public static void SendAsyncEvent(EventSubtypeAsync event_type, DsMap ds_map)
        {
            if (CreateAsyncEventWithDsMap == null)
                throw new InvalidOperationException("RegisterCallbacks was not called");
            CreateAsyncEventWithDsMap(ds_map.ds_map_id, (int)event_type);
        }
    }

    public static class GMWebExtension
    {
        private static Device device;
        private static List<Browser> browsers = new List<Browser>();

        public static int ASYNC_EVENT_ID = 1337;

        public class Browser : IDisposable
        {
            public ChromiumWebBrowser browser;
            public Sprite sprite;
            public Texture texture;
            public Size textureSize;

            public Browser(ChromiumWebBrowser browser)
            {
                this.browser = browser;
            }

            public void Dispose()
            {
                browser?.Dispose();
                sprite?.Dispose();
                texture?.Dispose();
                browser = null;
                sprite = null;
                texture = null;
            }
        }

        [DllExport(CallingConvention.Cdecl)]
        public static double __webextension_native_init()
        {
            var settings = new CefSettings();
            settings.SetOffScreenRenderingBestPerformanceArgs();
            settings.CefCommandLineArgs.Add("autoplay-policy", "no-user-gesture-required");
            settings.CefCommandLineArgs.Remove("mute-audio");
            Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
            return 1;
        }

        [DllExport(CallingConvention.Cdecl)]
        public static double __webextension_native_exit()
        {
            Cef.Shutdown();
            return 1;
        }

        [DllExport(CallingConvention.Cdecl)]
        public static double __webextension_set_device(IntPtr device_pointer)
        {
            if (device != null && device.NativePointer == device_pointer)
                return 0;
            Debug.WriteLine("Device has changed!");
            foreach(var b in browsers)
            {
                b.texture?.Dispose();
                b.sprite?.Dispose();
                b.texture = null;
                b.sprite = null;
            }
            device = new Device(device_pointer);
            return 1;
        }

        [DllExport(CallingConvention.Cdecl)]
        public static double browser_create(string initialUrl)
        {
            int browserId = browsers.Count;
            ChromiumWebBrowser browser = new ChromiumWebBrowser(initialUrl);
            browser.FrameLoadEnd += (sender, e) =>
            {
                GameMaker.DsMap async_load = new GameMaker.DsMap();
                async_load.Add("id", ASYNC_EVENT_ID);
                async_load.Add("browser", browserId);
                async_load.Add("type", "frame_load_end");
                async_load.Add("url", e.Url);
                async_load.Add("is_main_frame", e.Frame.IsMain ? 1 : 0);
                GameMaker.SendAsyncEvent(GameMaker.EventSubtypeAsync.Social, async_load);
            };
            browser.LoadingStateChanged += (sender, e) =>
            {
                GameMaker.DsMap async_load = new GameMaker.DsMap();
                async_load.Add("id", ASYNC_EVENT_ID);
                async_load.Add("browser", browserId);
                async_load.Add("type", "loading_state_changed");
                async_load.Add("loading", e.IsLoading ? 1 : 0);
                GameMaker.SendAsyncEvent(GameMaker.EventSubtypeAsync.Social, async_load);
            };
            browser.BrowserInitialized += (sender, e) =>
            {
                GameMaker.DsMap async_load = new GameMaker.DsMap();
                async_load.Add("id", ASYNC_EVENT_ID);
                async_load.Add("browser", browserId);
                async_load.Add("type", "browser_initialized");
                GameMaker.SendAsyncEvent(GameMaker.EventSubtypeAsync.Social, async_load);
            };
            Browser browserObj = new Browser(browser);
            browsers.Add(browserObj);
            
            browser.Paint += (sender, e) =>
            {
                if (e.IsPopup)
                    return;
                
                //Debug.WriteLine("Paint " + e.DirtyRect.X + ";" + e.DirtyRect.Y + ";" + e.DirtyRect.Width + ";" + e.DirtyRect.Height);

                lock (device)
                {
                    if (browserObj.texture == null || browserObj.textureSize.Width != browserObj.browser.Size.Width || browserObj.textureSize.Height != browserObj.browser.Size.Height)
                    {
                        Debug.WriteLine("Recreate texture " + browserObj.browser.Size.Width + "x" + browserObj.browser.Size.Height);
                        browserObj.textureSize = new Size(browserObj.browser.Size.Width, browserObj.browser.Size.Height);
                        if (browserObj.texture != null)
                            browserObj.texture.Dispose();
                        browserObj.texture = new Texture(device, browserObj.textureSize.Width, browserObj.textureSize.Height, 1, Usage.Dynamic, Format.A8R8G8B8, Pool.Default);
                    }
                    var rect = browserObj.texture.LockRectangle(0, LockFlags.None);
                    unsafe
                    {
                        for (int y = 0; y < e.Height; y++)
                        {
                            Buffer.MemoryCopy((byte*)e.BufferHandle.ToPointer() + y * e.Width * 4, (byte*)rect.DataPointer.ToPointer() + y * rect.Pitch, (uint)(e.Width * 4), (uint)(e.Width * 4));
                        }
                    }
                    browserObj.texture.UnlockRectangle(0);
                }
            };

            return browserId;
        }

        [DllExport(CallingConvention.Cdecl)]
        public static double browser_destroy(double browserIdDouble)
        {
            int browserId = (int)browserIdDouble; // this is so stupid, GML...
            if (browserId < 0 || browserId >= browsers.Count || browsers[browserId] == null)
                return 0;
            browsers[browserId].Dispose();
            browsers[browserId] = null;
            return 1;
        }

        [DllExport(CallingConvention.Cdecl)]
        public static double browser_load(double browserIdDouble, string url)
        {
            int browserId = (int)browserIdDouble; // this is so stupid, GML...
            if (browserId < 0 || browserId >= browsers.Count || browsers[browserId] == null)
                return 0;
            browsers[browserId].browser.Load(url);
            return 1;
        }

        [DllExport(CallingConvention.Cdecl)]
        public static double browser_load_html(double browserIdDouble, string html)
        {
            int browserId = (int)browserIdDouble; // this is so stupid, GML...
            if (browserId < 0 || browserId >= browsers.Count || browsers[browserId] == null)
                return 0;
            browsers[browserId].browser.LoadHtml(html);
            return 1;
        }
        
        [DllExport(CallingConvention.Cdecl)]
        public static double browser_draw(double browserIdDouble, double x, double y)
        {
            int browserId = (int)browserIdDouble; // this is so stupid, GML...
            if (browserId < 0 || browserId >= browsers.Count || browsers[browserId] == null)
                return 0;

            //Debug.WriteLine("Draw");

            if (browsers[browserId].texture == null)
            {
                Debug.WriteLine("No texture yet!");
                return 0;
            }

            lock(device)
            {
                if (browsers[browserId].sprite == null)
                    browsers[browserId].sprite = new Sprite(device);
                browsers[browserId].sprite.Begin();
                browsers[browserId].sprite.Draw(browsers[browserId].texture, new RawColorBGRA(255, 255, 255, 255), null, null, new RawVector3((float)x, (float)y, 0.0f));
                browsers[browserId].sprite.End();
            }

            return 1;
        }

        [DllExport(CallingConvention.Cdecl)]
        public static double browser_resize(double browserIdDouble, double w, double h)
        {
            int browserId = (int)browserIdDouble; // this is so stupid, GML...
            if (browserId < 0 || browserId >= browsers.Count || browsers[browserId] == null)
                return 0;

            if (w != browsers[browserId].browser.Size.Width || h != browsers[browserId].browser.Size.Height)
            {
                browsers[browserId].browser.Size = new Size((int)w, (int)h);
            }

            return 1;
        }
    }
}
