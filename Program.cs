using NAudio.Wave;
using Nett;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace AutoCaption
{
    public class Program : IDisposable
    {
        private readonly char[] BREAK_CHARS = { ' ', '\n', '\r', '\t', '-', '.', ',' };

        private AppConfig _config;
        private TomlSettings _tomlSettings;

        private INativeWindow _tkWindow;
        private IGraphicsContext _tkContext;

        private GRGlInterface _skInterface;
        private GRContext _skContext;
        private GRBackendRenderTarget _skScreenRenderTarget;
        private SKSurface _skScreenSurface;

        private SKPaint _skFillPaint, _skStrokePaint;
        private SKTypeface _skFont;

        private ISpeechRecognizer _speechRecognizer;
        private WaveInEvent _waveIn;

        private ConcurrentQueue<Action> _actions;
        private List<Caption> _captions;
        private bool _running;
        private double _time;

        public static void Main(string[] args)
        {
            try
            {
                using(var program = new Program())
                {
                    program.Run();
                }
            }
            catch(Exception e)
            {
                Console.Error.WriteLine($"An error has occurred: {e.Message}");
#if DEBUG
                throw;
#else
                Environment.Exit(1);
#endif
            }
        }

        public Program()
        {
            Initialize();
        }

        public void Initialize()
        {
            _actions = new ConcurrentQueue<Action>();
            _captions = new List<Caption>();
            _running = true;

            InitConfig();
            InitWindow();
            InitSkia();
            InitAudio();
            InitRecognizer();

            _tkWindow.Visible = true;
        }

        private void InitAudio()
        {
            _waveIn = new WaveInEvent();

            _waveIn.BufferMilliseconds = 20;
            _waveIn.DataAvailable += OnAudioAvailable;
            _waveIn.WaveFormat = new WaveFormat(48000, 16, 1);

            _waveIn.StartRecording();
        }

        private void InitConfig()
        {
            if(_tomlSettings == null)
            {
                _tomlSettings = TomlSettings.Create(cfg => cfg
                    .ConfigureType<SKColor>(type => type
                        .WithConversionFor<TomlString>(conv => conv
                            .FromToml(s => SKColor.Parse(s.Value))
                            .ToToml(c => c.ToString()))));
            }

            try
            {
                _config = Toml.ReadFile<AppConfig>("config.toml", _tomlSettings);
                Console.WriteLine("Configuration loaded.");
            }
            catch(FileNotFoundException)
            {
                Console.WriteLine("Configuration not found. Using default configuration.");

                _config = new AppConfig();
                Toml.WriteFile(_config, "config.toml", _tomlSettings);
            }
        }

        private void InitSkia()
        {
            _skInterface = GRGlInterface.CreateNativeGlInterface();
            if(_skInterface == null)
            {
                throw new Exception($"Failed to create SkiaSharp OpenGL interface.");
            }

            _skContext = GRContext.Create(GRBackend.OpenGL, _skInterface);
            if(_skContext == null)
            {
                throw new Exception($"Failed to create SkiaSharp OpenGL context.");
            }

            ResizeScreen();

            var fontName = _config.Text.Font;
            _skFont = SKTypeface.FromFile(fontName) ?? SKTypeface.FromFamilyName(fontName);

            _skFillPaint = new SKPaint()
            {
                BlendMode = SKBlendMode.SrcOver,
                Color = _config.Text.FillColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                TextAlign = _config.Text.Align,
                TextSize = _config.Text.Size,
                Typeface = _skFont,
            };

            if(_config.Text.StrokeWidth > 0)
            {
                _skStrokePaint = new SKPaint()
                {
                    BlendMode = SKBlendMode.SrcOver,
                    Color = _config.Text.StrokeColor,
                    IsAntialias = true,
                    StrokeCap = _config.Text.StrokeCap,
                    StrokeJoin = _config.Text.StrokeJoin,
                    StrokeMiter = _config.Text.StrokeMiter,
                    StrokeWidth = _config.Text.StrokeWidth,
                    Style = SKPaintStyle.Stroke,
                    TextAlign = _config.Text.Align,
                    TextSize = _config.Text.Size,
                    Typeface = _skFont,
                };
            }
        }

        private void InitRecognizer()
        {
            var assembly = Assembly.GetExecutingAssembly();

            var engineName = _config.Recognition.Engine;
            var recognizerName = $"AutoCaption.Recognizers.{engineName}SpeechRecognizer";

            var recognizerType = assembly.GetType(recognizerName);
            if(recognizerType == null || !typeof(ISpeechRecognizer).IsAssignableFrom(recognizerType))
            {
                throw new ArgumentException($"No recognition engine found for configured engine \"{engineName}\".");
            }

            var constructor = recognizerType.GetConstructor(Type.EmptyTypes);
            if(constructor == null)
            {
                throw new ArgumentException($"Recognition engine for configured engine \"{engineName}\" does not have a suitable constructor.");
            }

            _speechRecognizer = (ISpeechRecognizer)constructor.Invoke(null);

            _speechRecognizer.SpeechCancelled += OnSpeechCancelled;
            _speechRecognizer.SpeechCompleted += OnSpeechCompleted;
            _speechRecognizer.SpeechPartial += OnSpeechPartial;

            _speechRecognizer.Start(_config.Recognition);
        }

        private void InitWindow()
        {
            var graphicsMode = new GraphicsMode(new ColorFormat(8, 8, 8, 8), 0, 0, 1);
            _tkWindow = new NativeWindow(_config.Window.SizeX, _config.Window.SizeY, "AutoCaption",
                                         GameWindowFlags.Default, graphicsMode, DisplayDevice.Default);
            _tkWindow.Closed += OnWindowClosed;
            _tkWindow.Resize += OnWindowResize;

            // TODO: Why does passing these sizes to the NativeWindow constructor result in the client size being larger?
            _tkWindow.ClientSize = new System.Drawing.Size(_config.Window.SizeX, _config.Window.SizeY);

            _tkContext = new GraphicsContext(graphicsMode, _tkWindow.WindowInfo, 3, 3, GraphicsContextFlags.ForwardCompatible)
            {
                ErrorChecking = true,
                SwapInterval = 1,
            };

            _tkContext.LoadAll();
        }

        public void Run()
        {
            var targetInterval = TimeSpan.FromSeconds(1 / _config.Window.FPS);

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            while(_running)
            {
                var startTime = stopwatch.Elapsed;

                _time = startTime.TotalSeconds;
                Update();

                var canvas = _skScreenSurface.Canvas;
                canvas.Clear();
                Draw(canvas);

                _skContext.Flush();
                _tkContext.SwapBuffers();

                _tkWindow.ProcessEvents();

                var endTime = stopwatch.Elapsed;

                var pauseTime = (startTime + targetInterval) - endTime;
                if(pauseTime > TimeSpan.Zero)
                {
                    Thread.Sleep(pauseTime);
                }
            }
        }

        private void Update()
        {
            Action action;
            while(_actions.TryDequeue(out action))
            {
                action();
            }

            var disappearTime = _time - _config.Text.SustainTime - _config.Text.FadeOutTime;

            int i;
            for(i = 0; i < _captions.Count && _captions[i].AppearTime <= disappearTime; i++);

            if(i > 0)
            {
                _captions.RemoveRange(0, i);
            }
        }

        private void Draw(SKCanvas canvas)
        {
            var tkScreenSize = _tkWindow.ClientSize;
            var skScreenSize = new SKSize(tkScreenSize.Width, tkScreenSize.Height);

            var metrics = _skFillPaint.FontMetrics;
            var margin = 0.5f * _config.Text.StrokeWidth;

            var x = margin;
            var y = skScreenSize.Height - metrics.Bottom - margin;

            var alpha = _config.Text.BaseOpacity;
            var dy = _config.Text.LineSpacing * _config.Text.Size;

            for(var i = _captions.Count - 1; i >= 0; i--)
            {
                var caption = _captions[i];
                if(caption.Lines == null)
                {
                    caption.Lines = BreakText(caption.Text, skScreenSize.Width - 2 * margin);
                }

                var age = Math.Max(0, _time - caption.AppearTime);

                double lineAlpha;
                if(age < _config.Text.SustainTime)
                {
                    lineAlpha = 1;
                }
                else
                {
                    age -= _config.Text.SustainTime;
                    lineAlpha = 1 - (age / _config.Text.FadeOutTime);
                }

                lineAlpha *= alpha;
                for(var j = caption.Lines.Length - 1; j >= 0; j--)
                {
                    var line = caption.Lines[j];

                    var alphaByte = (byte)Math.Round(lineAlpha * 255);
                    _skFillPaint.Color = _skFillPaint.Color.WithAlpha(alphaByte);
                    _skStrokePaint.Color = _skStrokePaint.Color.WithAlpha(alphaByte);

                    if(_skStrokePaint != null)
                        canvas.DrawText(line, x, y, _skStrokePaint);

                    canvas.DrawText(line, x, y, _skFillPaint);

                    y -= dy;
                }

                alpha *= _config.Text.OpacityDecay;
                y -= _config.Text.SentenceMargin;
            }
        }

        private string[] BreakText(string text, float width)
        {
            var lines = new List<string>();

            var remainder = text;
            while(remainder.Length > 0)
            {
                var lineMax = (int)_skFillPaint.BreakText(remainder, width);
                var lineMin = lineMax * 3 / 4;

                var lineBreak = lineMax;
                if(lineBreak < remainder.Length)
                {
                    lineBreak = remainder.LastIndexOfAny(BREAK_CHARS, lineMax - 1);
                    if(lineBreak < lineMin)
                    {
                        lineBreak = lineMax;
                    }
                    else
                    {
                        lineBreak++;
                    }
                }

                var line = remainder.Substring(0, lineBreak).Trim();
                remainder = remainder.Substring(lineBreak).Trim();

                lines.Add(line);
            }

            return lines.ToArray();
        }

        public Caption GetPartialCaption(bool create)
        {
            Caption caption = null;

            var count = _captions.Count;
            if(count > 0)
            {
                var lastCaption = _captions[count - 1];
                if(double.IsInfinity(lastCaption.AppearTime))
                {
                    caption = lastCaption;
                }
            }

            if(caption == null && create)
            {
                caption = new Caption();
                _captions.Add(caption);
            }

            return caption;
        }

        private void OnAudioAvailable(object sender, WaveInEventArgs e)
        {
            _speechRecognizer.ProcessData(e.Buffer, 0, e.BytesRecorded);
        }

        private void OnSpeechCancelled(object sender, EventArgs e)
        {
            _actions.Enqueue(() => {
                var caption = GetPartialCaption(false);
                if(caption != null)
                {
                    _captions.RemoveAt(_captions.Count - 1);
                }
            });
        }

        private void OnSpeechCompleted(object sender, string e)
        {
            _actions.Enqueue(() => {
                var caption = GetPartialCaption(true);
                caption.AppearTime = _time;
                caption.Text = TransformText(e);
                caption.Lines = null;
            });
        }

        private void OnSpeechPartial(object sender, string e)
        {
            _actions.Enqueue(() => {
                var caption = GetPartialCaption(true);
                caption.AppearTime = double.PositiveInfinity;
                caption.Text = TransformText(e);
                caption.Lines = null;
            });
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            _running = false;
        }

        private void OnWindowResize(object sender, EventArgs e)
        {
            ResizeScreen();
        }
        
        private void ResizeScreen()
        {
            _skScreenSurface?.Dispose();
            _skScreenSurface = null;

            _skScreenRenderTarget?.Dispose();
            _skScreenRenderTarget = null;

            var screenRenderTargetInfo = new GRGlFramebufferInfo()
            {
                Format = (int)PixelInternalFormat.Rgba8,
                FramebufferObjectId = 0,
            };

            var screenSize = _tkWindow.ClientSize;

            _skScreenRenderTarget = new GRBackendRenderTarget(screenSize.Width, screenSize.Height, 1, 0, screenRenderTargetInfo);
            if(_skScreenRenderTarget == null)
            {
                throw new Exception($"Failed to create SkiaSharp screen render target.");
            }

            _skScreenSurface = SKSurface.Create(_skContext, _skScreenRenderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
            if(_skScreenSurface == null)
            {
                throw new Exception($"Failed to create SkiaSharp screen surface.");
            }

            foreach(var caption in _captions)
            {
                caption.Lines = null;
            }
        }

        private string TransformText(string text)
        {
            text = text?.Trim();
            if(string.IsNullOrEmpty(text))
            {
                return text;
            }

            var buffer = new StringBuilder(text);

            buffer[0] = char.ToUpper(buffer[0]);

            return buffer.ToString();
        }

        protected virtual void Dispose(bool disposing)
        {
            if(disposing)
            {
                if(_config != null)
                {
                    Toml.WriteFile(_config, "config.toml", _tomlSettings);
                    _config = null;
                }

                _waveIn?.StopRecording();
                _waveIn?.Dispose();
                _waveIn = null;

                _speechRecognizer?.Dispose();
                _speechRecognizer = null;

                _skFont?.Dispose();
                _skFont = null;

                _skStrokePaint?.Dispose();
                _skStrokePaint = null;

                _skFillPaint?.Dispose();
                _skFillPaint = null;

                _skScreenSurface?.Dispose();
                _skScreenSurface = null;

                _skScreenRenderTarget?.Dispose();
                _skScreenRenderTarget = null;

                _skContext?.Dispose();
                _skContext = null;

                _skInterface?.Dispose();
                _skInterface = null;

                _tkContext?.Dispose();
                _tkContext = null;

                _tkWindow?.Dispose();
                _tkWindow = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
