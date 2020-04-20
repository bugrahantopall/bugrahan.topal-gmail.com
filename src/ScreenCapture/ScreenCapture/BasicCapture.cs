//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using Composition.WindowsRuntimeHelpers;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using System;
using System.Collections;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Composition;
using Windows.UI.Xaml;

namespace CaptureSampleCore
{
    public class BasicCapture : IDisposable
    {
        private GraphicsCaptureItem item;
        private Direct3D11CaptureFramePool framePool;
        private GraphicsCaptureSession session;
        private SizeInt32 lastSize;

        private CanvasDevice device;

        private CompositionGraphicsDevice d3dDevice;
        private SharpDX.DXGI.SwapChain1 swapChain;

        public BasicCapture(IDirect3DDevice d, GraphicsCaptureItem i)
        {
            item = i;
            //device = d;
            device = new CanvasDevice();

            d3dDevice = CanvasComposition.CreateCompositionGraphicsDevice(
                Window.Current.Compositor,
                device);

            var dxgiFactory = new SharpDX.DXGI.Factory2();
            var description = new SharpDX.DXGI.SwapChainDescription1()
            {
                Width = item.Size.Width,
                Height = item.Size.Height,
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SharpDX.DXGI.SampleDescription()
                {
                    Count = 1,
                    Quality = 0
                },
                Usage = SharpDX.DXGI.Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = SharpDX.DXGI.Scaling.Stretch,
                SwapEffect = SharpDX.DXGI.SwapEffect.FlipSequential,
                AlphaMode = SharpDX.DXGI.AlphaMode.Premultiplied,
                Flags = SharpDX.DXGI.SwapChainFlags.None
            };
            //swapChain = new SharpDX.DXGI.SwapChain1(dxgiFactory, d3dDevice, ref description);

            framePool = Direct3D11CaptureFramePool.Create(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                i.Size);
            session = framePool.CreateCaptureSession(i);
            lastSize = i.Size;

            framePool.FrameArrived += OnFrameArrived;

            
        }

        public void Dispose()
        {
            session?.Dispose();
            framePool?.Dispose();
            swapChain?.Dispose();
            d3dDevice?.Dispose();
        }

        public void StartCapture()
        {
            session.StartCapture();

            CreateFile();

            
        }

        private async void CreateFile()
        {
            var videoProps = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, 1024, 768);
            var videoDescriptor = new VideoStreamDescriptor(videoProps);

            //videoDescriptor.EncodingProperties.FrameRate.Numerator = frn;
            //videoDescriptor.EncodingProperties.FrameRate.Denominator = frd;
            //videoDescriptor.EncodingProperties.Bitrate = (frn / frd) * w * h * 4 * 8;
            var streamSource = new MediaStreamSource(videoDescriptor);
            streamSource.SampleRequested += StreamSource_SampleRequested;

            var tc = new MediaTranscoder();
            var prof = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            var tempFolder = await StorageFolder.GetFolderFromPathAsync("C:\\Videos");

            var file = await tempFolder.CreateFileAsync("out2.mp4", CreationCollisionOption.ReplaceExisting);
            var outputStream = await file.OpenAsync(FileAccessMode.ReadWrite);
            try
            {

                var result = await tc.PrepareMediaStreamSourceTranscodeAsync(streamSource, outputStream, prof);
                if (result.CanTranscode)
                {
                    //Debug.Print($"encoding");
                    var op = result.TranscodeAsync();
                    //op.Progress +=
                    //    new AsyncActionProgressHandler<double>(TranscodeProgress);
                    //op.Completed +=
                    //    new AsyncActionWithProgressCompletedHandler<double>(TranscodeComplete);
                    //Debug.WriteLine($"encoded");


                }
            }
            catch (Exception)
            {

            }
        }

        private void StreamSource_SampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            if (frames.Count == 0)
            {
                args.Request.Sample = null;
                return;

            }
            var videoFrame = frames.Dequeue();
            IBuffer buffer = new Windows.Storage.Streams.Buffer(1024);
            if (((VideoFrame)videoFrame).SoftwareBitmap == null)
            {
                args.Request.Sample = null;
                return;
            }
            ((VideoFrame)videoFrame).SoftwareBitmap.CopyToBuffer(buffer);
            var samp = MediaStreamSample.CreateFromBuffer(buffer, ((VideoFrame)videoFrame).SystemRelativeTime.Value);

            args.Request.Sample = samp;
        }

        public ICompositionSurface CreateSurface(Compositor compositor)
        {
            //return compositor.CreateCompositionSurfaceForSwapChain(swapChain);
            return d3dDevice.CreateDrawingSurface(
                new Size(400, 400),
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                DirectXAlphaMode.Premultiplied);
        }
        private Queue frames = new Queue(); 
        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            var newSize = false;

            using (var frame = sender.TryGetNextFrame())
            {

                



                if (frame.ContentSize.Width != lastSize.Width ||
                    frame.ContentSize.Height != lastSize.Height)
                {
                    // The thing we have been capturing has changed size.
                    // We need to resize the swap chain first, then blit the pixels.
                    // After we do that, retire the frame and then recreate the frame pool.
                    newSize = true;
                    lastSize = frame.ContentSize;
                    swapChain.ResizeBuffers(
                        2,
                        lastSize.Width,
                        lastSize.Height,
                        SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                        SharpDX.DXGI.SwapChainFlags.None);
                }
                CanvasBitmap canvasBitmap = CanvasBitmap.CreateFromDirect3D11Surface(
                    device,
                    frame.Surface);
                IBuffer buffer = new Windows.Storage.Streams.Buffer((uint)canvasBitmap.GetPixelBytes().Length);
                canvasBitmap.GetPixelBytes(buffer);

                var videoFrame = VideoFrame.CreateWithDirect3D11Surface(frame.Surface);
                frames.Enqueue(videoFrame);
                using (var backBuffer = swapChain.GetBackBuffer<SharpDX.Direct3D11.Texture2D>(0))
                using (var bitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface))
                {
                    //d3dDevice.ImmediateContext.CopyResource(bitmap, backBuffer);
                }

            } // Retire the frame.

            swapChain.Present(0, SharpDX.DXGI.PresentFlags.None);

            if (newSize)
            {
                framePool.Recreate(
                    device,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    lastSize);
            }
        }
    }
}
