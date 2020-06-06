using OpenTK;
using SkiaSharp;

namespace AutoSub
{
    public class AppConfig
    {
        public RecognitionConfig Recognition { get; set; } = new RecognitionConfig();
        public TextConfig Text { get; set; } = new TextConfig();
        public WindowConfig Window { get; set; } = new WindowConfig();
    }

    public class RecognitionConfig
    {
        public float MinKeepConfidence { get; set; } = 0.1f;
        public float MinStartConfidence { get; set; } = 0.4f;
        public float MinUpdateConfidence { get; set; } = 0.2f;
    }

    public class TextConfig
    {
        public string Font { get; set; } = "OpenDyslexic-Regular.otf";
        public float Size { get; set; } = 24f;

        public SKColor FillColor { get; set; } = new SKColor(0xffffffff);
        public SKColor StrokeColor { get; set; } = new SKColor(0xff000000);

        public float BaseOpacity { get; set; } = 1f;
        public float OpacityDecay { get; set; } = 0.7f;

        public SKTextAlign Align { get; set; } = SKTextAlign.Left;

        public float LineSpacing { get; set; } = 1.1f;

        public SKStrokeCap StrokeCap { get; set; } = SKStrokeCap.Butt;
        public SKStrokeJoin StrokeJoin { get; set; } = SKStrokeJoin.Miter;
        public float StrokeMiter { get; set; } = 4f;
        public float StrokeWidth { get; set; } = 4f;

        public float SentenceMargin { get; set; } = 12f;

        public double FadeOutTime { get; set; } = 2;
        public double SustainTime { get; set; } = 8;
    }

    public class WindowConfig
    {
        public int? PosX  { get; set; } = null;
        public int? PosY  { get; set; } = null;

        public int SizeX { get; set; } = 800;
        public int SizeY { get; set; } = 300;

        public double FPS { get; set; } = 60;

        public VSyncMode VSync { get; set; } = VSyncMode.Adaptive;
    }
}
