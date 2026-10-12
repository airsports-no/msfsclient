using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Networking.Connectivity;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace msfsclient.Services
{
    /// <summary>What only Windows can do: open links, the share and save dialogs, the photo picker, the connection state.</summary>
    public sealed class PlatformServices
    {
        private readonly Func<IntPtr> _windowHandle;

        public PlatformServices(Func<IntPtr> windowHandle)
        {
            _windowHandle = windowHandle;
        }

        public async void OpenUri(string uri)
        {
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri(uri)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not open {uri}: {ex.Message}"); }
        }

        /// <summary>The Windows date and time settings, where "Set time automatically" and "Sync now" are.</summary>
        public void OpenClockSettings() => OpenUri("ms-settings:dateandtime");

        /// <summary>Whether the PC has an internet connection; null when Windows cannot tell.</summary>
        public bool? Online()
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                return profile != null && profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Hands text (the live map link) to the Windows share dialog. Returns false when sharing is not available; the text
        /// is then on the clipboard instead.
        /// </summary>
        public bool ShareText(string title, string text)
        {
            try
            {
                var hwnd = _windowHandle();
                var manager = DataTransferManagerInterop.GetForWindow(hwnd);
                void OnRequested(DataTransferManager sender, DataRequestedEventArgs args)
                {
                    args.Request.Data.Properties.Title = title;
                    args.Request.Data.SetText(text);
                    sender.DataRequested -= OnRequested;
                }
                manager.DataRequested += OnRequested;
                DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
                return true;
            }
            catch (Exception)
            {
                CopyText(text);
                return false;
            }
        }

        public void CopyText(string text)
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }

        /// <summary>Asks where to save a GPX file and writes it. False when the pilot cancelled.</summary>
        public async Task<bool> SaveGpxAsync(string fileName, string gpx)
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = Path.GetFileNameWithoutExtension(fileName) };
            picker.FileTypeChoices.Add("GPX track", [".gpx"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle());
            var file = await picker.PickSaveFileAsync();
            if (file == null) return false;
            await FileIO.WriteTextAsync(file, gpx, UnicodeEncoding.Utf8);
            return true;
        }

        /// <summary>Lets the pilot pick a photo and returns it as a JPEG at most 1024 px on its longest side; null when cancelled.</summary>
        public async Task<byte[]?> PickProfilePhotoAsync()
        {
            var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail, SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            foreach (var type in new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".heic" }) picker.FileTypeFilter.Add(type);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle());
            var file = await picker.PickSingleFileAsync();
            if (file == null) return null;

            using var input = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(input);
            const double maxSide = 1024;
            var scale = Math.Min(1.0, maxSide / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Round(decoder.PixelWidth * scale),
                ScaledHeight = (uint)Math.Round(decoder.PixelHeight * scale),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform.ScaledWidth, transform.ScaledHeight, 96, 96, pixels.DetachPixelData());
            await encoder.FlushAsync();
            var bytes = new byte[output.Size];
            await output.ReadAsync(bytes.AsBuffer(), (uint)output.Size, InputStreamOptions.None);
            return bytes;
        }
    }
}
