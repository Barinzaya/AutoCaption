using System;
using System.IO;

namespace AutoCaption
{
    public interface ISpeechRecognizer : IDisposable
    {
        event EventHandler SpeechCancelled;
        event EventHandler<string> SpeechCompleted;
        event EventHandler<string> SpeechPartial;

        void Start(RecognitionConfig config);
        void ProcessData(byte[] data, int offset, int count);
        void Stop();
    }
}
